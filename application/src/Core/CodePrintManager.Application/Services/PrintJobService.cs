using CodePrintManager.Data;
using CodePrintManager.Domain.Entities;
using CodePrintManager.Domain.Enums;
using CodePrintManager.Domain.Events;
using CodePrintManager.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace CodePrintManager.Application.Services;

public class PrintJobService : IPrintJobService
{
    private readonly AppDbContext _db;
    private readonly ICodePoolService _codePool;
    private readonly PrinterConnectionManager _connectionManager;
    private readonly IAlertService _alerts;
    private readonly IAuditService _audit;
    private readonly ILogger<PrintJobService> _logger;
    private readonly ActiveJobRegistry _jobRegistry;
    private readonly JobEventBus _eventBus;
    private readonly IServiceScopeFactory? _scopeFactory;

    public event EventHandler<JobProgressChangedEvent>? JobProgressChanged;
    public event EventHandler<JobCompletedEvent>? JobCompleted;

    public PrintJobService(
        AppDbContext db,
        ICodePoolService codePool,
        PrinterConnectionManager connectionManager,
        IAlertService alerts,
        IAuditService audit,
        ILogger<PrintJobService> logger,
        ActiveJobRegistry jobRegistry,
        JobEventBus eventBus,
        IServiceScopeFactory? scopeFactory = null)
    {
        _db = db;
        _codePool = codePool;
        _connectionManager = connectionManager;
        _alerts = alerts;
        _audit = audit;
        _logger = logger;
        _jobRegistry = jobRegistry;
        _eventBus = eventBus;
        _scopeFactory = scopeFactory;
    }

    public async Task<PrintJob> CreateJobAsync(int productId, int printerId, int quantity)
    {
        if (quantity <= 0)
            throw new ArgumentOutOfRangeException(nameof(quantity), quantity,
                "Quantity must be greater than zero");

        var job = new PrintJob
        {
            ProductId = productId,
            PrinterId = printerId,
            Quantity = quantity,
            Status = JobStatus.Preparing,
            CreatedAt = DateTime.UtcNow
        };

        _db.PrintJobs.Add(job);
        await _db.SaveChangesAsync();

        _logger.LogInformation("Job {JobId} created (Product={ProductId}, Printer={PrinterId}, Qty={Quantity})",
            job.Id, productId, printerId, quantity);

        await _audit.LogAsync("job_created", productId: productId, printerId: printerId, jobId: job.Id,
            details: new { quantity });

        return job;
    }

    public async Task PrepareJobAsync(int jobId, CancellationToken ct = default, IProgress<string>? progress = null)
    {
        var job = await _db.PrintJobs
            .Include(j => j.Product)
            .Include(j => j.Printer)
            .FirstAsync(j => j.Id == jobId);

        _logger.LogInformation("Job {JobId} preparing", jobId);

        var printerLock = _jobRegistry.GetPrinterLock(job.PrinterId);
        await printerLock.WaitAsync(ct);
        try
        {
            var adapter = _connectionManager.GetAdapter(job.PrinterId)
                ?? throw new InvalidOperationException($"Printer {job.Printer.Name} is not connected");

            // Step 1: Check printer state
            progress?.Report("checking_printer");
            var status = await adapter.GetStatusAsync(ct);
            _logger.LogDebug("Job {JobId}: printer status = {Status}", jobId, status);
            if (status != PrinterStatus.Idle)
                throw new InvalidOperationException($"Printer is not idle. Current state: {status}");
            progress?.Report("printer_verified");

            // Step 2: Reserve codes
            progress?.Report("reserving_codes");
            _logger.LogDebug("Job {JobId}: reserving {Qty} codes", jobId, job.Quantity);
            var codes = await _codePool.ReserveCodesAsync(job.ProductId, job.Quantity, job.Id);
            _logger.LogDebug("Job {JobId}: codes reserved", jobId);
            progress?.Report("codes_reserved");

            ct.ThrowIfCancellationRequested();

            // Step 3: Upload CSV
            progress?.Report("uploading_data");
            var csvFilename = job.Product.PrinterCsvName
                ?? throw new InvalidOperationException("Product has no CSV filename configured");
            _logger.LogDebug("Job {JobId}: uploading CSV '{Filename}' ({Count} codes)", jobId, csvFilename, codes.Count);
            await adapter.DeleteCsvAsync(csvFilename, ct);

            ct.ThrowIfCancellationRequested();

            var codeTexts = codes.Select(c => c.CodeText).ToList();
            var uploadOk = await adapter.UploadCsvAsync(csvFilename, codeTexts, ct);
            if (!uploadOk)
                throw new InvalidOperationException("CSV upload failed: SPLCDF returned FAIL");

            ct.ThrowIfCancellationRequested();

            // Verify upload
            var exists = await adapter.VerifyCsvExistsAsync(csvFilename, ct);
            if (!exists)
                throw new InvalidOperationException("CSV verification failed: file not found on printer");
            _logger.LogDebug("Job {JobId}: CSV uploaded and verified", jobId);
            progress?.Report("data_uploaded");

            // Step 4: Check template
            progress?.Report("loading_template");
            var templateName = job.Product.TemplateFile
                ?? throw new InvalidOperationException("Product has no template configured");
            _logger.LogDebug("Job {JobId}: checking template '{Template}'", jobId, templateName);
            var templates = await adapter.ListTemplatesAsync(ct);
            if (!templates.Contains(templateName))
            {
                // Attempt to upload .rox file from disk
                if (File.Exists(templateName))
                {
                    var roxBytes = await File.ReadAllBytesAsync(templateName, ct);
                    var uploadTemplateOk = await adapter.UploadTemplateAsync(
                        Path.GetFileName(templateName), roxBytes, ct);
                    if (!uploadTemplateOk)
                    {
                        _alerts.Raise(AlertSeverity.Error, job.Printer.Name,
                            "Template upload failed. Load it manually via Sayasis.",
                            printerId: job.PrinterId, jobId: job.Id);
                        throw new InvalidOperationException("Template upload failed: SPLRTF returned FAIL");
                    }
                    _logger.LogInformation("Job {JobId}: template '{Template}' uploaded from disk", jobId, templateName);
                    // Use the filename (without path) for activation
                    templateName = Path.GetFileName(templateName);
                }
                else
                {
                    throw new InvalidOperationException(
                        $"Template '{templateName}' not found on printer and .rox file not found on disk.");
                }
            }

            // Activate template (resets counter, loads CSV buffer)
            ct.ThrowIfCancellationRequested();
            var activateOk = await adapter.ActivateTemplateAsync(templateName, ct);
            if (!activateOk)
                throw new InvalidOperationException("Template activation failed: SPLLTF returned FAIL");
            _logger.LogDebug("Job {JobId}: template activated", jobId);

            progress?.Report("template_loaded");

            job.Status = JobStatus.Ready;
            await _db.SaveChangesAsync();
            _logger.LogInformation("Job {JobId} prepared → Ready", jobId);
            progress?.Report("complete");
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("Job {JobId} preparation cancelled", jobId);
            job.Status = JobStatus.Cancelled;
            await _db.SaveChangesAsync();
            throw;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Job {JobId} preparation failed", jobId);
            throw;
        }
        finally
        {
            printerLock.Release();
        }
    }

    public async Task StartJobAsync(int jobId, CancellationToken ct = default)
    {
        var job = await _db.PrintJobs
            .Include(j => j.Printer)
            .FirstAsync(j => j.Id == jobId);

        if (job.Status != JobStatus.Ready)
            throw new InvalidOperationException($"Job is not ready. Current status: {job.Status}");

        var adapter = _connectionManager.GetAdapter(job.PrinterId)
            ?? throw new InvalidOperationException("Printer not connected");

        // Record lifetime counter baseline
        job.TotalBaseline = await adapter.GetTotalCounterAsync(ct);

        // Set quantity and start
        await adapter.SetPrintQuantityAsync(job.Quantity, ct);
        await adapter.StartPrintAsync(ct);

        job.Status = JobStatus.Printing;
        job.StartedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        _logger.LogInformation("Job {JobId} started (baseline={Baseline}, qty={Quantity})",
            jobId, job.TotalBaseline, job.Quantity);

        // Spawn job executor with its own DbContext scope (outlives the calling scope)
        IServiceScope? executorScope = null;
        AppDbContext executorDb;
        ICodePoolService executorCodePool;

        if (_scopeFactory != null)
        {
            executorScope = _scopeFactory.CreateScope();
            executorDb = executorScope.ServiceProvider.GetRequiredService<AppDbContext>();
            executorCodePool = executorScope.ServiceProvider.GetRequiredService<ICodePoolService>();
            // Re-attach the job entity to the new context
            var executorJob = await executorDb.PrintJobs
                .Include(j => j.Printer)
                .FirstAsync(j => j.Id == jobId);
            job = executorJob;
        }
        else
        {
            // Fallback: use caller's context (WPF host where scope is long-lived)
            executorDb = _db;
            executorCodePool = _codePool;
        }

        var executor = new JobExecutor(job, adapter, executorCodePool, _alerts, executorDb, _logger);
        executor.ProgressChanged += (_, e) =>
        {
            _eventBus.RaiseProgressChanged(this, e);
            JobProgressChanged?.Invoke(this, e);
        };
        executor.Completed += (_, e) =>
        {
            _jobRegistry.TryRemove(jobId);
            executorScope?.Dispose();
            _eventBus.RaiseCompleted(this, e);
            JobCompleted?.Invoke(this, e);
        };
        _jobRegistry.Register(jobId, executor);
        _logger.LogDebug("Job {JobId} executor spawned (scopeFactory={HasScope})", jobId, _scopeFactory != null);
        executor.Start();

        await _audit.LogAsync("job_started", printerId: job.PrinterId, jobId: jobId);
    }

    public async Task CancelJobAsync(int jobId)
    {
        var job = await _db.PrintJobs.FirstAsync(j => j.Id == jobId);

        if (job.Status is JobStatus.Completed or JobStatus.Cancelled)
            throw new InvalidOperationException(
                $"Cannot cancel job in {job.Status} state");

        _logger.LogInformation("Job {JobId} cancelling (status={Status}, confirmed={Confirmed})",
            jobId, job.Status, job.CodesConfirmed);

        var printerLock = _jobRegistry.GetPrinterLock(job.PrinterId);

        await printerLock.WaitAsync();
        try
        {
            if (job.Status == JobStatus.Printing && _jobRegistry.TryGet(jobId, out var executor) && executor != null)
            {
                await executor.StopAsync();
                _jobRegistry.TryRemove(jobId);

                var adapter = _connectionManager.GetAdapter(job.PrinterId);
                if (adapter != null)
                {
                    var finalCounter = await adapter.GetCurrentCounterAsync();
                    await adapter.StopPrintAsync();

                    // Mark codes printed up to counter
                    if (finalCounter > job.CodesConfirmed)
                        await _codePool.MarkCodesPrintedAsync(jobId, job.CodesConfirmed, finalCounter);

                    // Burn the code at finalCounter ONLY because there is genuine uncertainty:
                    // the printer may have printed it between the counter read and the stop command.
                    if (finalCounter < job.Quantity)
                        await _codePool.BurnCodeAsync(jobId, finalCounter);

                    // Return remaining codes (no uncertainty — they were never sent to the printer).
                    // startIndex=0 because MarkCodesPrintedAsync and BurnCodeAsync already moved
                    // codes out of Reserved status; the remaining Reserved set IS the unprinted codes.
                    var remaining = job.Quantity - finalCounter - 1;
                    if (remaining > 0)
                        await _codePool.ReturnCodesToPoolAsync(jobId, 0, remaining);
                }
            }
            else if (job.Status == JobStatus.Paused)
            {
                // Paused job: pause already reconciled the counter (printer stopped, counter
                // read after stop). CodesConfirmed is accurate — no uncertainty, no burn needed.
                // startIndex=0 because MarkCodesPrintedAsync already moved confirmed codes out
                // of Reserved status, so the remaining Reserved set IS the unprinted codes.
                if (job.CodesConfirmed < job.Quantity)
                    await _codePool.ReturnCodesToPoolAsync(jobId, 0, job.Quantity - job.CodesConfirmed);
            }
            else if (job.Status is JobStatus.Preparing or JobStatus.Ready)
            {
                await _codePool.ReturnCodesToPoolAsync(jobId, 0, job.Quantity);
            }

            job.Status = JobStatus.Cancelled;
            job.CompletedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();

            _logger.LogInformation("Job {JobId} cancelled (confirmed={Confirmed}/{Total})",
                jobId, job.CodesConfirmed, job.Quantity);

            await _audit.LogAsync("job_cancelled", jobId: jobId);
        }
        finally
        {
            printerLock.Release();
        }
    }

    public async Task PauseJobAsync(int jobId)
    {
        var job = await _db.PrintJobs.FirstAsync(j => j.Id == jobId);

        if (job.Status != JobStatus.Printing)
            throw new InvalidOperationException($"Only printing jobs can be paused. Current status: {job.Status}");

        var printerLock = _jobRegistry.GetPrinterLock(job.PrinterId);
        await printerLock.WaitAsync();
        try
        {
            // Stop the executor polling loop (but don't destroy progress)
            if (_jobRegistry.TryGet(jobId, out var executor) && executor != null)
            {
                await executor.StopAsync();
                _jobRegistry.TryRemove(jobId);
            }

            // Stop the printer and reconcile counter
            var adapter = _connectionManager.GetAdapter(job.PrinterId);
            if (adapter != null)
            {
                await adapter.StopPrintAsync();

                // Reconcile: the printer may have advanced past what the executor committed
                var finalCounter = await adapter.GetCurrentCounterAsync();
                if (finalCounter > job.CodesConfirmed)
                {
                    await _codePool.MarkCodesPrintedAsync(jobId, job.CodesConfirmed, finalCounter);
                    job.CodesConfirmed = finalCounter;
                }
            }

            job.Status = JobStatus.Paused;
            await _db.SaveChangesAsync();

            await _audit.LogAsync("job_paused", jobId: jobId, printerId: job.PrinterId);
            _logger.LogInformation("Job {JobId} paused at {Confirmed}/{Total}", jobId, job.CodesConfirmed, job.Quantity);
        }
        finally
        {
            printerLock.Release();
        }
    }

    public async Task<List<PrintJob>> GetActiveJobsAsync()
    {
        return await _db.PrintJobs
            .Include(j => j.Product)
            .Include(j => j.Printer)
            .Where(j => j.Status == JobStatus.Preparing
                     || j.Status == JobStatus.Ready
                     || j.Status == JobStatus.Printing
                     || j.Status == JobStatus.Paused)
            .OrderByDescending(j => j.CreatedAt)
            .ToListAsync();
    }

    public async Task<List<PrintJob>> GetJobHistoryAsync(int? printerId = null, int? productId = null)
    {
        var query = _db.PrintJobs
            .Include(j => j.Product)
            .Include(j => j.Printer)
            .Where(j => j.Status == JobStatus.Completed || j.Status == JobStatus.Cancelled);

        if (printerId.HasValue)
            query = query.Where(j => j.PrinterId == printerId.Value);
        if (productId.HasValue)
            query = query.Where(j => j.ProductId == productId.Value);

        return await query.OrderByDescending(j => j.CompletedAt).ToListAsync();
    }

    public async Task<List<PrintJob>> GetStaleJobsAsync()
    {
        return await _db.PrintJobs
            .Include(j => j.Product)
            .Include(j => j.Printer)
            .Where(j => j.Status == JobStatus.Preparing
                     || j.Status == JobStatus.Ready
                     || j.Status == JobStatus.Printing
                     || j.Status == JobStatus.Paused)
            .ToListAsync();
    }

    public async Task ResumeJobAsync(int jobId, CancellationToken ct = default)
    {
        var job = await _db.PrintJobs
            .Include(j => j.Printer)
            .FirstAsync(j => j.Id == jobId);

        if (job.Status == JobStatus.Ready)
        {
            _logger.LogInformation("Job {JobId} resuming (status=Ready, delegating to StartJobAsync)", jobId);
            await StartJobAsync(jobId, ct);
            return;
        }

        if (job.Status != JobStatus.Paused)
            throw new InvalidOperationException($"Only paused or ready jobs can be resumed. Current status: {job.Status}");

        var printerLock = _jobRegistry.GetPrinterLock(job.PrinterId);
        await printerLock.WaitAsync(ct);
        try
        {
            var adapter = _connectionManager.GetAdapter(job.PrinterId)
                ?? throw new InvalidOperationException("Printer not connected");

            // Set remaining quantity and restart printer
            var remaining = job.Quantity - job.CodesConfirmed;
            _logger.LogInformation("Job {JobId} resuming (confirmed={Confirmed}, remaining={Remaining})",
                jobId, job.CodesConfirmed, remaining);
            await adapter.SetPrintQuantityAsync(remaining, ct);
            await adapter.StartPrintAsync(ct);

            job.Status = JobStatus.Printing;
            await _db.SaveChangesAsync();

            // Spawn a new executor with its own scope
            IServiceScope? executorScope = null;
            AppDbContext executorDb;
            ICodePoolService executorCodePool;

            if (_scopeFactory != null)
            {
                executorScope = _scopeFactory.CreateScope();
                executorDb = executorScope.ServiceProvider.GetRequiredService<AppDbContext>();
                executorCodePool = executorScope.ServiceProvider.GetRequiredService<ICodePoolService>();
                job = await executorDb.PrintJobs.Include(j => j.Printer).FirstAsync(j => j.Id == jobId);
            }
            else
            {
                executorDb = _db;
                executorCodePool = _codePool;
            }

            var executor = new JobExecutor(job, adapter, executorCodePool, _alerts, executorDb, _logger);
            executor.ProgressChanged += (_, e) =>
            {
                _eventBus.RaiseProgressChanged(this, e);
                JobProgressChanged?.Invoke(this, e);
            };
            executor.Completed += (_, e) =>
            {
                _jobRegistry.TryRemove(jobId);
                executorScope?.Dispose();
                _eventBus.RaiseCompleted(this, e);
                JobCompleted?.Invoke(this, e);
            };
            _jobRegistry.Register(jobId, executor);
            executor.Start();

            await _audit.LogAsync("job_resumed", jobId: jobId, printerId: job.PrinterId);
            _logger.LogInformation("Job {JobId} resumed at {Confirmed}/{Total}", jobId, job.CodesConfirmed, job.Quantity);
        }
        finally
        {
            printerLock.Release();
        }
    }
}
