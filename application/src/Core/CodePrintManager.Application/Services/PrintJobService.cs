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
    private readonly ILocalizationService _loc;
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
        ILocalizationService loc,
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
        _loc = loc;
        _jobRegistry = jobRegistry;
        _eventBus = eventBus;
        _scopeFactory = scopeFactory;
    }

    public async Task<PrintJob> CreateJobAsync(int productId, int printerId, int quantity)
    {
        if (quantity <= 0)
            throw new ArgumentOutOfRangeException(nameof(quantity), quantity,
                _loc["Error_QuantityMustBePositive"]);

        // Check available codes before creating the job
        var available = await _db.Codes
            .CountAsync(c => c.ProductId == productId && c.Status == CodeStatus.Available);
        if (available < quantity)
            throw new InvalidOperationException(
                _loc.Format("Error_NotEnoughCodes", quantity, available));

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
                ?? throw new InvalidOperationException(_loc.Format("Error_PrinterNotConnected", job.Printer.Name));

            if (_connectionManager.HasSerialMismatch(job.PrinterId))
                throw new InvalidOperationException(_loc["Error_SerialMismatch"]);

            // Step 1: Check printer state
            progress?.Report("checking_printer");
            var status = await adapter.GetStatusAsync(ct);
            _logger.LogDebug("Job {JobId}: printer status = {Status}", jobId, status);
            if (status != PrinterStatus.Idle)
                throw new InvalidOperationException(_loc.Format("Error_PrinterNotIdle", status));
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
                ?? throw new InvalidOperationException(_loc["Error_NoCsvFilename"]);
            _logger.LogDebug("Job {JobId}: uploading CSV '{Filename}' ({Count} codes)", jobId, csvFilename, codes.Count);
            await adapter.DeleteCsvAsync(csvFilename, ct);

            ct.ThrowIfCancellationRequested();

            var codeTexts = codes.Select(c => c.CodeText).ToList();
            var uploadOk = await adapter.UploadCsvAsync(csvFilename, codeTexts, ct);
            if (!uploadOk)
                throw new InvalidOperationException(_loc["Error_CsvUploadFailed"]);

            ct.ThrowIfCancellationRequested();

            // Verify upload
            var exists = await adapter.VerifyCsvExistsAsync(csvFilename, ct);
            if (!exists)
                throw new InvalidOperationException(_loc["Error_CsvVerificationFailed"]);
            _logger.LogDebug("Job {JobId}: CSV uploaded and verified", jobId);
            progress?.Report("data_uploaded");

            // Step 4: Check template
            progress?.Report("loading_template");
            var templateName = job.Product.TemplateFile
                ?? throw new InvalidOperationException(_loc["Error_NoTemplateConfigured"]);
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
                            _loc["Alert_TemplateUploadFailed"],
                            printerId: job.PrinterId, jobId: job.Id);
                        throw new InvalidOperationException(_loc["Error_TemplateUploadFailed"]);
                    }
                    _logger.LogInformation("Job {JobId}: template '{Template}' uploaded from disk", jobId, templateName);
                    // Use the filename (without path) for activation
                    templateName = Path.GetFileName(templateName);
                }
                else
                {
                    throw new InvalidOperationException(
                        _loc.Format("Error_TemplateNotFound", templateName));
                }
            }

            // Activate template (resets counter, loads CSV buffer)
            ct.ThrowIfCancellationRequested();
            var activateOk = await adapter.ActivateTemplateAsync(templateName, ct);
            if (!activateOk)
                throw new InvalidOperationException(_loc["Error_TemplateActivationFailed"]);
            _logger.LogDebug("Job {JobId}: template activated", jobId);

            progress?.Report("template_loaded");

            // Record TotalBaseline during Prepare so Ready jobs have a
            // SPGGTP anchor for recovery inspection. The lifetime counter
            // won't change until actual printing occurs.
            job.TotalBaseline = await adapter.GetTotalCounterAsync(ct);
            _logger.LogDebug("Job {JobId}: TotalBaseline recorded during Prepare = {Baseline}",
                jobId, job.TotalBaseline);

            job.Status = JobStatus.Ready;
            await _db.SaveChangesAsync();
            _logger.LogInformation("Job {JobId} prepared → Ready (baseline={Baseline})",
                jobId, job.TotalBaseline);
            progress?.Report("complete");

            // Start a ReadyWatcher to detect external print starts
            SpawnReadyWatcher(job, adapter);
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
            // Clean up: return any reserved codes and mark job as Cancelled
            // so the partial unique index doesn't block retries
            job.Status = JobStatus.Cancelled;
            job.CompletedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();
            await _codePool.ReturnCodesToPoolAsync(jobId, 0, job.Quantity);
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
            throw new InvalidOperationException(_loc.Format("Error_JobNotReady", job.Status));

        // Stop the ReadyWatcher before transitioning to Printing
        await StopReadyWatcherAsync(jobId);

        var adapter = _connectionManager.GetAdapter(job.PrinterId)
            ?? throw new InvalidOperationException(_loc["Error_PrinterNotConnectedShort"]);

        if (_connectionManager.HasSerialMismatch(job.PrinterId))
            throw new InvalidOperationException(_loc["Error_SerialMismatch"]);

        // Record fresh lifetime counter baseline for the active Printing session.
        // (Prepare already recorded a baseline for Ready-job recovery, but the
        // printer may have been used between Prepare and Start, so we refresh.)
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

        var executor = new JobExecutor(job, adapter, executorCodePool, _alerts, executorDb, _logger, _loc);
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
                _loc.Format("Error_CannotCancelJob", job.Status));

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

                    // Quarantine the code at finalCounter because there is genuine uncertainty:
                    // the printer may have printed it between the counter read and the stop command.
                    // The operator can verify via the Codes tab and move it to Available or Printed.
                    if (finalCounter < job.Quantity)
                        await _codePool.QuarantineCodeAsync(jobId, finalCounter);

                    // Return remaining codes (no uncertainty — they were never sent to the printer).
                    // startIndex=0 because MarkCodesPrintedAsync and QuarantineCodeAsync already moved
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
                // Stop ReadyWatcher if one is running for this job
                await StopReadyWatcherAsync(jobId);
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
            throw new InvalidOperationException(_loc.Format("Error_CannotPauseJob", job.Status));

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
            .Include(j => j.Product)
            .Include(j => j.Printer)
            .FirstAsync(j => j.Id == jobId);

        if (job.Status == JobStatus.Ready)
        {
            _logger.LogInformation("Job {JobId} resuming (status=Ready, delegating to StartJobAsync)", jobId);
            await StartJobAsync(jobId, ct);
            return;
        }

        if (job.Status != JobStatus.Paused)
            throw new InvalidOperationException(_loc.Format("Error_CannotResumeJob", job.Status));

        var printerLock = _jobRegistry.GetPrinterLock(job.PrinterId);
        await printerLock.WaitAsync(ct);
        try
        {
            var adapter = _connectionManager.GetAdapter(job.PrinterId)
                ?? throw new InvalidOperationException(_loc["Error_PrinterNotConnectedShort"]);

            if (_connectionManager.HasSerialMismatch(job.PrinterId))
                throw new InvalidOperationException(_loc["Error_SerialMismatch"]);

            // --- Full Resume Procedure (Section 10 of connection-recovery-deep-dive.md) ---

            // Step 1: Determine remaining codes
            var remainingCodes = await _db.Codes
                .Where(c => c.JobId == jobId && c.Status == CodeStatus.Reserved)
                .OrderBy(c => c.ImportOrder)
                .Select(c => c.CodeText)
                .ToListAsync(ct);

            var remaining = remainingCodes.Count;
            if (remaining == 0)
                throw new InvalidOperationException(
                    _loc.Format("Error_NoRemainingCodes", jobId));

            _logger.LogInformation(
                "Job {JobId} resuming: confirmed={Confirmed}, remaining={Remaining}",
                jobId, job.CodesConfirmed, remaining);

            // Step 2: Delete old CSV from printer
            var csvFilename = job.Product.PrinterCsvName
                ?? throw new InvalidOperationException(_loc["Error_NoCsvFilename"]);
            await adapter.DeleteCsvAsync(csvFilename, ct);
            _logger.LogDebug("Job {JobId}: old CSV '{Csv}' deleted (or not found)", jobId, csvFilename);

            ct.ThrowIfCancellationRequested();

            // Step 3: Upload NEW CSV with ONLY remaining codes
            var uploadOk = await adapter.UploadCsvAsync(csvFilename, remainingCodes, ct);
            if (!uploadOk)
                throw new InvalidOperationException(_loc["Error_CsvUploadFailed"]);
            _logger.LogDebug("Job {JobId}: new CSV uploaded ({Count} remaining codes)", jobId, remaining);

            ct.ThrowIfCancellationRequested();

            // Step 4: Verify upload
            var exists = await adapter.VerifyCsvExistsAsync(csvFilename, ct);
            if (!exists)
                throw new InvalidOperationException(_loc["Error_CsvVerificationFailed"]);

            // Step 5: Check template is still in storage
            var templateName = job.Product.TemplateFile
                ?? throw new InvalidOperationException(_loc["Error_NoTemplateConfigured"]);
            // Use filename only (template may be stored as full path)
            var templateFileName = Path.GetFileName(templateName);
            var templates = await adapter.ListTemplatesAsync(ct);
            if (!templates.Contains(templateFileName))
            {
                // Re-upload from disk if available
                if (File.Exists(templateName))
                {
                    var roxBytes = await File.ReadAllBytesAsync(templateName, ct);
                    var uploadTemplateOk = await adapter.UploadTemplateAsync(templateFileName, roxBytes, ct);
                    if (!uploadTemplateOk)
                        throw new InvalidOperationException(_loc["Error_TemplateUploadFailed"]);
                    _logger.LogInformation("Job {JobId}: template '{Template}' re-uploaded from disk",
                        jobId, templateFileName);
                }
                else
                {
                    throw new InvalidOperationException(
                        _loc.Format("Error_TemplateNotFound", templateFileName));
                }
            }

            ct.ThrowIfCancellationRequested();

            // Step 6: Reload template (resets SPGGCP to 0, reloads data buffer from new CSV)
            var activateOk = await adapter.ActivateTemplateAsync(templateFileName, ct);
            if (!activateOk)
                throw new InvalidOperationException(_loc["Error_TemplateActivationFailed"]);
            _logger.LogDebug("Job {JobId}: template reloaded, SPGGCP reset to 0", jobId);

            // Step 7: Record fresh lifetime baseline
            job.TotalBaseline = await adapter.GetTotalCounterAsync(ct);
            _logger.LogDebug("Job {JobId}: fresh TotalBaseline = {Baseline}", jobId, job.TotalBaseline);

            // Step 8: Set print quantity
            await adapter.SetPrintQuantityAsync(remaining, ct);

            // Step 9: Start printing
            await adapter.StartPrintAsync(ct);

            job.Status = JobStatus.Printing;
            await _db.SaveChangesAsync();

            // Step 10: Spawn new JobExecutor with counter offset.
            // After the full Resume Procedure, SPGGCP was reset to 0 by template reload.
            // The executor needs to add this offset to raw counter values to map back to
            // the job-level CodesConfirmed/Quantity space.
            var counterOffset = job.CodesConfirmed;

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

            var executor = new JobExecutor(job, adapter, executorCodePool, _alerts, executorDb, _logger, _loc,
                counterOffset: counterOffset);
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

            await _audit.LogAsync("job_resumed", jobId: jobId, printerId: job.PrinterId,
                details: new { remaining, newBaseline = job.TotalBaseline });
            _logger.LogInformation(
                "Job {JobId} resumed via full Resume Procedure at {Confirmed}/{Total} (new baseline={Baseline})",
                jobId, job.CodesConfirmed, job.Quantity, job.TotalBaseline);
        }
        finally
        {
            printerLock.Release();
        }
    }

    // --- ReadyWatcher helpers ---

    private void SpawnReadyWatcher(PrintJob job, IPrinterAdapter adapter)
    {
        var watcher = new ReadyWatcher(job, adapter, _alerts, _logger);
        watcher.ExternalPrintDetected += (_, jobId) =>
        {
            _jobRegistry.TryRemoveWatcher(jobId);
            _ = HandleExternalPrintDetectedAsync(jobId);
        };
        _jobRegistry.RegisterWatcher(job.Id, watcher);
        watcher.Start();
        _logger.LogDebug("ReadyWatcher spawned for Job {JobId}", job.Id);
    }

    private async Task StopReadyWatcherAsync(int jobId)
    {
        if (_jobRegistry.TryGetWatcher(jobId, out var watcher) && watcher != null)
        {
            await watcher.StopAsync();
            _jobRegistry.TryRemoveWatcher(jobId);
            _logger.LogDebug("ReadyWatcher stopped for Job {JobId}", jobId);
        }
    }

    /// <summary>
    /// Called when a ReadyWatcher detects external printing.
    /// Transitions the job from Ready to Printing and spawns a JobExecutor.
    /// </summary>
    private async Task HandleExternalPrintDetectedAsync(int jobId)
    {
        try
        {
            _logger.LogWarning(
                "External print detected for Job {JobId} — transitioning to Printing", jobId);
            await StartJobAsync(jobId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to transition Job {JobId} to Printing after external print detection", jobId);
            _alerts.Raise(AlertSeverity.Error, "System",
                $"External printing detected on Job #{jobId} but failed to start tracking: {ex.Message}",
                jobId: jobId);
        }
    }
}
