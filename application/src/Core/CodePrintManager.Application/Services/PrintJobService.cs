using System.Diagnostics;
using CodePrintManager.Data;
using CodePrintManager.Domain.Entities;
using CodePrintManager.Domain.Enums;
using CodePrintManager.Domain.Events;
using CodePrintManager.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog.Context;

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

    private static readonly JobStatus[] ActiveStatuses =
    {
        JobStatus.Preparing, JobStatus.Ready, JobStatus.Printing, JobStatus.Paused
    };

    public async Task<PrintJob> CreateJobAsync(int productId, int printerId, int quantity)
    {
        _logger.LogTrace("-> CreateJobAsync(productId={ProductId}, printerId={PrinterId}, quantity={Quantity})",
            productId, printerId, quantity);
        var sw = Stopwatch.StartNew();

        if (quantity <= 0)
            throw new ArgumentOutOfRangeException(nameof(quantity), quantity,
                _loc["Error_QuantityMustBePositive"]);

        // Check for existing active job on this product
        var existingProductJob = await _db.PrintJobs
            .AnyAsync(j => j.ProductId == productId && ActiveStatuses.Contains(j.Status));
        if (existingProductJob)
            throw new InvalidOperationException(
                _loc["Error_ProductHasActiveJob"]);

        // Check for existing active job on this printer
        var existingPrinterJob = await _db.PrintJobs
            .AnyAsync(j => j.PrinterId == printerId && ActiveStatuses.Contains(j.Status));
        if (existingPrinterJob)
            throw new InvalidOperationException(
                _loc.Format("Error_PrinterHasActiveJobs", printerId));

        // Check available codes before creating the job
        var available = await _db.Codes
            .CountAsync(c => c.ProductId == productId && c.Status == CodeStatus.Available);
        _logger.LogDebug("CreateJobAsync: available codes for product {ProductId} = {Available} (requested {Qty})",
            productId, available, quantity);
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
        try
        {
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            // Detach the entity so it doesn't poison future SaveChangesAsync calls
            // on this long-lived DbContext.
            _db.Entry(job).State = EntityState.Detached;
            throw;
        }

        _logger.LogInformation("Job {JobId} created (Product={ProductId}, Printer={PrinterId}, Qty={Quantity}) in {ElapsedMs}ms",
            job.Id, productId, printerId, quantity, sw.ElapsedMilliseconds);

        await _audit.LogAsync("job_created", productId: productId, printerId: printerId, jobId: job.Id,
            details: new { quantity });

        _logger.LogTrace("<- CreateJobAsync = Job {JobId} ({ElapsedMs}ms)", job.Id, sw.ElapsedMilliseconds);
        return job;
    }

    public async Task PrepareJobAsync(int jobId, CancellationToken ct = default, IProgress<string>? progress = null)
    {
        _logger.LogTrace("-> PrepareJobAsync(jobId={JobId})", jobId);
        var totalSw = Stopwatch.StartNew();

        var job = await _db.PrintJobs
            .Include(j => j.Product)
            .Include(j => j.Printer)
            .FirstAsync(j => j.Id == jobId);

        using var _ = LogContext.PushProperty("JobId", jobId);
        using var __ = LogContext.PushProperty("PrinterId", job.PrinterId);
        _logger.LogInformation("Job {JobId} PREPARING (product='{Product}', printer='{Printer}', qty={Qty})",
            jobId, job.Product?.Name, job.Printer?.Name, job.Quantity);

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
            // Use filename only for comparison — printer stores filenames, not full paths
            var templateFileName = Path.GetFileName(templateName);
            _logger.LogDebug("Job {JobId}: checking template '{Template}' (filename='{FileName}')", jobId, templateName, templateFileName);
            var templates = await adapter.ListTemplatesAsync(ct);
            if (!templates.Contains(templateFileName))
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
            var activateOk = await adapter.ActivateTemplateAsync(templateFileName, ct);
            if (!activateOk)
                throw new InvalidOperationException(_loc["Error_TemplateActivationFailed"]);
            _logger.LogDebug("Job {JobId}: template activated", jobId);

            progress?.Report("template_loaded");

            // Record TotalBaseline during Prepare so Ready jobs have a
            // SPGGTP anchor for recovery inspection. The lifetime counter
            // won't change until actual printing occurs.
            // NOTE: Real Savema printers may drop the TCP connection after
            // template activation (SPLLTF). Retry with reconnect if needed.
            int totalBaseline;
            try
            {
                totalBaseline = await adapter.GetTotalCounterAsync(ct);
            }
            catch (Exception ex) when (ex is IOException or System.Net.Sockets.SocketException)
            {
                _logger.LogWarning("Job {JobId}: connection lost after template activation, waiting for reconnect...", jobId);
                // Wait for the printer to come back (up to 5 seconds)
                for (int attempt = 0; attempt < 10; attempt++)
                {
                    await Task.Delay(500, ct);
                    if (await _connectionManager.TryReconnectAsync(job.PrinterId, ct))
                    {
                        _logger.LogInformation("Job {JobId}: reconnected after template activation (attempt {Attempt})", jobId, attempt + 1);
                        adapter = _connectionManager.GetAdapter(job.PrinterId)
                            ?? throw new InvalidOperationException(_loc.Format("Error_PrinterNotConnected", job.Printer.Name));
                        break;
                    }
                    if (attempt == 9)
                        throw new InvalidOperationException(_loc.Format("Error_PrinterNotConnected", job.Printer.Name));
                }
                totalBaseline = await adapter.GetTotalCounterAsync(ct);
            }
            job.TotalBaseline = totalBaseline;
            // Also read SPGGCP baseline for the ReadyWatcher.
            // SPGGCP is cumulative on real hardware — the watcher needs this
            // baseline to detect external prints as counter > baseline.
            var spggcpBaseline = await adapter.GetCurrentCounterAsync(ct);
            _logger.LogDebug("Job {JobId}: TotalBaseline recorded during Prepare = {Baseline}, SPGGCP baseline = {CpBaseline}",
                jobId, job.TotalBaseline, spggcpBaseline);

            job.Status = JobStatus.Ready;
            await _db.SaveChangesAsync();
            totalSw.Stop();
            _logger.LogInformation("Job {JobId} prepared -> Ready (baseline={Baseline}) TOTAL: {ElapsedMs}ms",
                jobId, job.TotalBaseline, totalSw.ElapsedMilliseconds);
            progress?.Report("complete");

            // Start a ReadyWatcher to detect external print starts
            SpawnReadyWatcher(job, adapter, spggcpBaseline);
            _logger.LogTrace("<- PrepareJobAsync completed ({ElapsedMs}ms)", totalSw.ElapsedMilliseconds);
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("Job {JobId} preparation CANCELLED after {ElapsedMs}ms", jobId, totalSw.ElapsedMilliseconds);
            job.Status = JobStatus.Cancelled;
            await _db.SaveChangesAsync();
            _logger.LogTrace("<- PrepareJobAsync CANCELLED");
            throw;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Job {JobId} preparation FAILED after {ElapsedMs}ms", jobId, totalSw.ElapsedMilliseconds);
            // Clean up: return any reserved codes and mark job as Cancelled
            // so the partial unique index doesn't block retries
            job.Status = JobStatus.Cancelled;
            job.CompletedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();
            await _codePool.ReturnCodesToPoolAsync(jobId, 0, job.Quantity);
            _logger.LogTrace("<- PrepareJobAsync FAILED");
            throw;
        }
        finally
        {
            printerLock.Release();
        }
    }

    public async Task StartJobAsync(int jobId, CancellationToken ct = default)
    {
        _logger.LogTrace("-> StartJobAsync(jobId={JobId})", jobId);
        var sw = Stopwatch.StartNew();

        var job = await _db.PrintJobs
            .Include(j => j.Printer)
            .FirstAsync(j => j.Id == jobId);

        using var _ = LogContext.PushProperty("JobId", jobId);
        using var __ = LogContext.PushProperty("PrinterId", job.PrinterId);

        if (job.Status != JobStatus.Ready)
            throw new InvalidOperationException(_loc.Format("Error_JobNotReady", job.Status));

        // Stop the ReadyWatcher before acquiring the printer lock.
        // StopReadyWatcherAsync is safe to call without the lock (the registry is
        // concurrent-safe), and the watcher's StopAsync awaits its own loop — holding
        // the printer lock during that could deadlock if the watcher is mid-callback.
        await StopReadyWatcherAsync(jobId);

        var printerLock = _jobRegistry.GetPrinterLock(job.PrinterId);
        await printerLock.WaitAsync(ct);
        try
        {
            var adapter = _connectionManager.GetAdapter(job.PrinterId)
                ?? throw new InvalidOperationException(_loc["Error_PrinterNotConnectedShort"]);

            if (_connectionManager.HasSerialMismatch(job.PrinterId))
                throw new InvalidOperationException(_loc["Error_SerialMismatch"]);

            // Record fresh lifetime counter baseline for the active Printing session.
            // (Prepare already recorded a baseline for Ready-job recovery, but the
            // printer may have been used between Prepare and Start, so we refresh.)
            job.TotalBaseline = await adapter.GetTotalCounterAsync(ct);

            // Read SPGGCP baseline BEFORE starting print.
            // SPGGCP behavior varies by firmware: some reset on SPLLTF, some are cumulative.
            // We always record a baseline so the executor can compute session-only delta.
            var currentCounterBaseline = await adapter.GetCurrentCounterAsync(ct);
            _logger.LogInformation("Job {JobId}: SPGGCP baseline = {Baseline} (will be subtracted from poll readings)",
                jobId, currentCounterBaseline);

            // Set quantity and start
            await adapter.SetPrintQuantityAsync(job.Quantity, ct);
            await adapter.StartPrintAsync(ct);

            job.Status = JobStatus.Printing;
            job.StartedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();

            _logger.LogInformation("Job {JobId} started (SPGGTP baseline={TotalBaseline}, SPGGCP baseline={CounterBaseline}, qty={Quantity})",
                jobId, job.TotalBaseline, currentCounterBaseline, job.Quantity);

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

            var executor = new JobExecutor(job, adapter, executorCodePool, _alerts, executorDb, _logger, _loc,
                counterOffset: -currentCounterBaseline);
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
            _logger.LogDebug("Job {JobId} executor spawned (scopeFactory={HasScope}, counterOffset={Offset})",
                jobId, _scopeFactory != null, -currentCounterBaseline);
            executor.Start();
        }
        finally
        {
            printerLock.Release();
        }

        await _audit.LogAsync("job_started", printerId: job.PrinterId, jobId: jobId);
        _logger.LogTrace("<- StartJobAsync completed ({ElapsedMs}ms)", sw.ElapsedMilliseconds);
    }

    public async Task CancelJobAsync(int jobId)
    {
        _logger.LogTrace("-> CancelJobAsync(jobId={JobId})", jobId);
        var sw = Stopwatch.StartNew();

        var job = await _db.PrintJobs.Include(j => j.Printer).FirstAsync(j => j.Id == jobId);

        using var _ = LogContext.PushProperty("JobId", jobId);
        using var __ = LogContext.PushProperty("PrinterId", job.PrinterId);

        if (job.Status is JobStatus.Completed or JobStatus.Cancelled)
            throw new InvalidOperationException(
                _loc.Format("Error_CannotCancelJob", job.Status));

        _logger.LogInformation("Job {JobId} CANCELLING (status={Status}, confirmed={Confirmed})",
            jobId, job.Status, job.CodesConfirmed);

        var printerLock = _jobRegistry.GetPrinterLock(job.PrinterId);

        await printerLock.WaitAsync();
        try
        {
            // --- Printer I/O (outside transaction) ---
            var effectivePrinted = 0;
            var quarantineCount = 0;
            var hadActiveExecutor = false;
            var hasAdapter = false;

            if (job.Status == JobStatus.Printing && _jobRegistry.TryGet(jobId, out var executor) && executor != null)
            {
                hadActiveExecutor = true;
                await executor.StopAsync();
                _jobRegistry.TryRemove(jobId);

                var adapter = _connectionManager.GetAdapter(job.PrinterId);
                if (adapter != null)
                {
                    hasAdapter = true;
                    await adapter.StopPrintAsync();

                    // Use SPGGTP (lifetime counter) for authoritative print count.
                    // Raw SPGGCP may be cumulative and cannot be compared to CodesConfirmed
                    // (which is offset-adjusted by the executor). SPGGTP - TotalBaseline
                    // gives the true effective print count for this job.
                    var lifetimeCounter = await adapter.GetTotalCounterAsync();
                    effectivePrinted = job.TotalBaseline.HasValue
                        ? lifetimeCounter - job.TotalBaseline.Value
                        : job.CodesConfirmed; // fallback: trust last committed progress
                    // Cap to Quantity as a safety net
                    if (effectivePrinted > job.Quantity)
                        effectivePrinted = job.Quantity;
                    _logger.LogDebug("CancelJobAsync: SPGGTP={Lifetime}, TotalBaseline={Baseline}, effectivePrinted={Effective}, confirmed={Confirmed}",
                        lifetimeCounter, job.TotalBaseline, effectivePrinted, job.CodesConfirmed);

                    // Compute quarantine count from per-printer QuarantineMargin setting.
                    var margin = job.Printer.QuarantineMargin;
                    quarantineCount = Math.Min(margin, job.Quantity - effectivePrinted);
                }
            }

            // --- DB mutations (inside transaction) ---
            // using-dispose auto-rolls-back if CommitAsync is not reached.
            using var transaction = await _db.Database.BeginTransactionAsync();

            if (hadActiveExecutor && hasAdapter)
            {
                // Mark codes printed up to effective counter
                if (effectivePrinted > job.CodesConfirmed)
                    await _codePool.MarkCodesPrintedAsync(jobId, job.CodesConfirmed, effectivePrinted);

                // Quarantine boundary codes based on per-printer QuarantineMargin setting.
                // The margin covers uncertainty: the printer may have printed codes between
                // the counter read and the stop command. The operator can verify via the
                // Codes tab and move quarantined codes to Available or Printed.
                if (quarantineCount > 0)
                    await _codePool.QuarantineCodesAsync(jobId, effectivePrinted, quarantineCount);

                // Return remaining codes (no uncertainty — they were never sent to the printer).
                // startIndex=0 because MarkCodesPrintedAsync and QuarantineCodesAsync already moved
                // codes out of Reserved status; the remaining Reserved set IS the unprinted codes.
                var remaining = job.Quantity - effectivePrinted - quarantineCount;
                if (remaining > 0)
                    await _codePool.ReturnCodesToPoolAsync(jobId, 0, remaining);
            }
            else if (job.Status == JobStatus.Printing)
            {
                // Printing job but no executor (crash recovery / startup abort).
                // CodesConfirmed is the last persisted progress — treat it like a Paused job.
                // Return all remaining reserved codes.
                _logger.LogWarning("CancelJobAsync: Job {JobId} was Printing but had no executor (crash recovery). " +
                    "Returning {Remaining} reserved codes based on CodesConfirmed={Confirmed}",
                    jobId, job.Quantity - job.CodesConfirmed, job.CodesConfirmed);
                if (job.CodesConfirmed < job.Quantity)
                    await _codePool.ReturnCodesToPoolAsync(jobId, 0, job.Quantity - job.CodesConfirmed);
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
            await transaction.CommitAsync();

            _logger.LogInformation("Job {JobId} CANCELLED (confirmed={Confirmed}/{Total}) in {ElapsedMs}ms",
                jobId, job.CodesConfirmed, job.Quantity, sw.ElapsedMilliseconds);

            // Audit outside transaction — cancel is committed regardless of audit outcome
            await _audit.LogAsync("job_cancelled", jobId: jobId);
            _logger.LogTrace("<- CancelJobAsync completed ({ElapsedMs}ms)", sw.ElapsedMilliseconds);
        }
        finally
        {
            printerLock.Release();
        }
    }

    public async Task PauseJobAsync(int jobId)
    {
        _logger.LogTrace("-> PauseJobAsync(jobId={JobId})", jobId);
        var sw = Stopwatch.StartNew();

        var job = await _db.PrintJobs.FirstAsync(j => j.Id == jobId);

        using var _ = LogContext.PushProperty("JobId", jobId);
        using var __ = LogContext.PushProperty("PrinterId", job.PrinterId);

        if (job.Status != JobStatus.Printing)
            throw new InvalidOperationException(_loc.Format("Error_CannotPauseJob", job.Status));

        var printerLock = _jobRegistry.GetPrinterLock(job.PrinterId);
        await printerLock.WaitAsync();
        try
        {
            // --- Printer I/O (outside transaction) ---
            var effectivePrinted = job.CodesConfirmed;

            // Stop the executor polling loop (but don't destroy progress)
            if (_jobRegistry.TryGet(jobId, out var executor) && executor != null)
            {
                _logger.LogDebug("PauseJobAsync: stopping executor for Job {JobId}", jobId);
                await executor.StopAsync();
                _jobRegistry.TryRemove(jobId);
            }

            // Stop the printer and reconcile counter using SPGGTP (lifetime counter).
            // Raw SPGGCP may be cumulative and cannot be compared to CodesConfirmed
            // (which is offset-adjusted by the executor). SPGGTP - TotalBaseline
            // gives the true effective print count for this job.
            var adapter = _connectionManager.GetAdapter(job.PrinterId);
            if (adapter != null)
            {
                _logger.LogDebug("PauseJobAsync: sending StopPrint to printer for Job {JobId}", jobId);
                await adapter.StopPrintAsync();

                var lifetimeCounter = await adapter.GetTotalCounterAsync();
                effectivePrinted = job.TotalBaseline.HasValue
                    ? lifetimeCounter - job.TotalBaseline.Value
                    : job.CodesConfirmed; // fallback: trust executor's last commit
                // Cap to Quantity as a safety net
                if (effectivePrinted > job.Quantity)
                    effectivePrinted = job.Quantity;
                _logger.LogDebug("PauseJobAsync: SPGGTP={Lifetime}, TotalBaseline={Baseline}, effectivePrinted={Effective}, confirmed={Confirmed}",
                    lifetimeCounter, job.TotalBaseline, effectivePrinted, job.CodesConfirmed);
            }

            // --- DB mutations (inside transaction) ---
            using var transaction = await _db.Database.BeginTransactionAsync();

            if (effectivePrinted > job.CodesConfirmed)
            {
                _logger.LogDebug("PauseJobAsync: reconciling {Delta} additional prints",
                    effectivePrinted - job.CodesConfirmed);
                await _codePool.MarkCodesPrintedAsync(jobId, job.CodesConfirmed, effectivePrinted);
                job.CodesConfirmed = effectivePrinted;
            }

            job.Status = JobStatus.Paused;
            await _db.SaveChangesAsync();
            await transaction.CommitAsync();

            // Audit outside transaction — pause is committed regardless of audit outcome
            await _audit.LogAsync("job_paused", jobId: jobId, printerId: job.PrinterId);
            _logger.LogInformation("Job {JobId} PAUSED at {Confirmed}/{Total} in {ElapsedMs}ms",
                jobId, job.CodesConfirmed, job.Quantity, sw.ElapsedMilliseconds);
            _logger.LogTrace("<- PauseJobAsync completed ({ElapsedMs}ms)", sw.ElapsedMilliseconds);
        }
        finally
        {
            printerLock.Release();
        }
    }

    public async Task<List<PrintJob>> GetActiveJobsAsync()
    {
        _logger.LogTrace("-> GetActiveJobsAsync()");
        var result = await _db.PrintJobs
            .AsNoTracking()
            .Include(j => j.Product)
            .Include(j => j.Printer)
            .Where(j => j.Status == JobStatus.Preparing
                     || j.Status == JobStatus.Ready
                     || j.Status == JobStatus.Printing
                     || j.Status == JobStatus.Paused)
            .OrderByDescending(j => j.CreatedAt)
            .ToListAsync();
        _logger.LogTrace("<- GetActiveJobsAsync = {Count} jobs", result.Count);
        return result;
    }

    public async Task<List<PrintJob>> GetJobHistoryAsync(int? printerId = null, int? productId = null)
    {
        _logger.LogTrace("-> GetJobHistoryAsync(printerId={PrinterId}, productId={ProductId})", printerId, productId);
        var query = _db.PrintJobs
            .AsNoTracking()
            .Include(j => j.Product)
            .Include(j => j.Printer)
            .Where(j => j.Status == JobStatus.Completed || j.Status == JobStatus.Cancelled);

        if (printerId.HasValue)
            query = query.Where(j => j.PrinterId == printerId.Value);
        if (productId.HasValue)
            query = query.Where(j => j.ProductId == productId.Value);

        var result = await query.OrderByDescending(j => j.CompletedAt).ToListAsync();
        _logger.LogTrace("<- GetJobHistoryAsync = {Count} jobs", result.Count);
        return result;
    }

    public async Task<List<PrintJob>> GetStaleJobsAsync()
    {
        _logger.LogTrace("-> GetStaleJobsAsync()");
        var result = await _db.PrintJobs
            .AsNoTracking()
            .Include(j => j.Product)
            .Include(j => j.Printer)
            .Where(j => j.Status == JobStatus.Preparing
                     || j.Status == JobStatus.Ready
                     || j.Status == JobStatus.Printing
                     || j.Status == JobStatus.Paused)
            .ToListAsync();
        _logger.LogTrace("<- GetStaleJobsAsync = {Count} stale jobs", result.Count);
        return result;
    }

    public async Task ResumeJobAsync(int jobId, CancellationToken ct = default)
    {
        _logger.LogTrace("-> ResumeJobAsync(jobId={JobId})", jobId);
        var sw = Stopwatch.StartNew();

        var job = await _db.PrintJobs
            .Include(j => j.Product)
            .Include(j => j.Printer)
            .FirstAsync(j => j.Id == jobId);

        using var _ = LogContext.PushProperty("JobId", jobId);
        using var __ = LogContext.PushProperty("PrinterId", job.PrinterId);

        if (job.Status == JobStatus.Ready)
        {
            _logger.LogInformation("Job {JobId} resuming (status=Ready, delegating to StartJobAsync)", jobId);
            await StartJobAsync(jobId, ct);
            _logger.LogTrace("<- ResumeJobAsync delegated to StartJobAsync ({ElapsedMs}ms)", sw.ElapsedMilliseconds);
            return;
        }

        // Crash recovery: a Printing job with no running executor means the app
        // crashed while the job was active. Transition to Paused so the resume
        // procedure can handle it safely (CodesConfirmed is preserved).
        if (job.Status == JobStatus.Printing)
        {
            if (_jobRegistry.TryGet(jobId, out var existingExec) && existingExec != null)
                throw new InvalidOperationException(_loc["Error_JobStillRunning"]);
            _logger.LogWarning(
                "ResumeJobAsync: Job {JobId} was Printing but has no executor (crash recovery). " +
                "Transitioning to Paused.", jobId);
            job.Status = JobStatus.Paused;
            await _db.SaveChangesAsync(ct);
        }
        else if (job.Status != JobStatus.Paused)
        {
            throw new InvalidOperationException(_loc.Format("Error_CannotResumeJob", job.Status));
        }

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

            // Step 6: Reload template (reloads data buffer from new CSV)
            var activateOk = await adapter.ActivateTemplateAsync(templateFileName, ct);
            if (!activateOk)
                throw new InvalidOperationException(_loc["Error_TemplateActivationFailed"]);
            _logger.LogDebug("Job {JobId}: template reloaded", jobId);

            // Step 7: Record fresh lifetime baseline + SPGGCP baseline.
            // NOTE: Real Savema printers may drop the TCP connection after
            // template activation (SPLLTF). Retry with reconnect if needed.
            int totalBaseline;
            int currentCounterBaseline;
            try
            {
                totalBaseline = await adapter.GetTotalCounterAsync(ct);
                currentCounterBaseline = await adapter.GetCurrentCounterAsync(ct);
            }
            catch (Exception ex) when (ex is IOException or System.Net.Sockets.SocketException)
            {
                _logger.LogWarning("Job {JobId}: connection lost after template activation during resume, waiting for reconnect...", jobId);
                for (int attempt = 0; attempt < 10; attempt++)
                {
                    await Task.Delay(500, ct);
                    if (await _connectionManager.TryReconnectAsync(job.PrinterId, ct))
                    {
                        _logger.LogInformation("Job {JobId}: reconnected after template activation (attempt {Attempt})", jobId, attempt + 1);
                        adapter = _connectionManager.GetAdapter(job.PrinterId)
                            ?? throw new InvalidOperationException(_loc.Format("Error_PrinterNotConnected", job.Printer.Name));
                        break;
                    }
                    if (attempt == 9)
                        throw new InvalidOperationException(_loc.Format("Error_PrinterNotConnected", job.Printer.Name));
                }
                totalBaseline = await adapter.GetTotalCounterAsync(ct);
                currentCounterBaseline = await adapter.GetCurrentCounterAsync(ct);
            }
            job.TotalBaseline = totalBaseline;
            _logger.LogDebug("Job {JobId}: fresh TotalBaseline = {Baseline}, SPGGCP baseline = {CpBaseline}",
                jobId, job.TotalBaseline, currentCounterBaseline);

            // Step 8: Set print quantity
            await adapter.SetPrintQuantityAsync(remaining, ct);

            // Step 9: Start printing
            await adapter.StartPrintAsync(ct);

            job.Status = JobStatus.Printing;
            await _db.SaveChangesAsync();

            // Step 10: Spawn new JobExecutor with counter offset.
            // SPGGCP may or may not reset on SPLLTF depending on firmware version.
            // We always baseline it so the offset formula works either way:
            //   effective = raw_SPGGCP + offset = raw_SPGGCP + (CodesConfirmed - baseline)
            //             = CodesConfirmed + (raw_SPGGCP - baseline) = CodesConfirmed + session_delta
            var counterOffset = job.CodesConfirmed - currentCounterBaseline;

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
                "Job {JobId} RESUMED via full Resume Procedure at {Confirmed}/{Total} (new baseline={Baseline}) in {ElapsedMs}ms",
                jobId, job.CodesConfirmed, job.Quantity, job.TotalBaseline, sw.ElapsedMilliseconds);
            _logger.LogTrace("<- ResumeJobAsync completed ({ElapsedMs}ms)", sw.ElapsedMilliseconds);
        }
        finally
        {
            printerLock.Release();
        }
    }

    // --- ReadyWatcher helpers ---

    private void SpawnReadyWatcher(PrintJob job, IPrinterAdapter adapter, int spggcpBaseline = 0)
    {
        _logger.LogTrace("-> SpawnReadyWatcher(jobId={JobId}, spggcpBaseline={Baseline})", job.Id, spggcpBaseline);
        var watcher = new ReadyWatcher(job, adapter, _alerts, _logger, spggcpBaseline);
        watcher.ExternalPrintDetected += (_, jobId) =>
        {
            _logger.LogDebug("ReadyWatcher ExternalPrintDetected event fired for Job {JobId}", jobId);
            _jobRegistry.TryRemoveWatcher(jobId);
            _ = HandleExternalPrintDetectedAsync(jobId);
        };
        _jobRegistry.RegisterWatcher(job.Id, watcher);
        watcher.Start();
        _logger.LogDebug("ReadyWatcher spawned for Job {JobId}", job.Id);
        _logger.LogTrace("<- SpawnReadyWatcher completed");
    }

    private async Task StopReadyWatcherAsync(int jobId)
    {
        _logger.LogTrace("-> StopReadyWatcherAsync(jobId={JobId})", jobId);
        if (_jobRegistry.TryGetWatcher(jobId, out var watcher) && watcher != null)
        {
            await watcher.StopAsync();
            _jobRegistry.TryRemoveWatcher(jobId);
            _logger.LogDebug("ReadyWatcher stopped and removed for Job {JobId}", jobId);
        }
        else
        {
            _logger.LogTrace("   StopReadyWatcherAsync: no watcher found for Job {JobId}", jobId);
        }
        _logger.LogTrace("<- StopReadyWatcherAsync completed");
    }

    public async Task RespawnWatchersForPrinterAsync(int printerId, CancellationToken ct = default)
    {
        _logger.LogTrace("-> RespawnWatchersForPrinterAsync(printerId={PrinterId})", printerId);

        var adapter = _connectionManager.GetAdapter(printerId);
        if (adapter == null || !adapter.IsConnected)
        {
            _logger.LogDebug("RespawnWatchersForPrinterAsync: printer {PrinterId} not connected, skipping", printerId);
            _logger.LogTrace("<- RespawnWatchersForPrinterAsync (not connected)");
            return;
        }

        var readyJobs = await _db.PrintJobs
            .Include(j => j.Printer)
            .Where(j => j.PrinterId == printerId && j.Status == JobStatus.Ready)
            .ToListAsync(ct);

        foreach (var job in readyJobs)
        {
            // Skip if a watcher already exists (shouldn't happen, but be safe)
            if (_jobRegistry.TryGetWatcher(job.Id, out _))
            {
                _logger.LogDebug("RespawnWatchersForPrinterAsync: watcher already exists for Job {JobId}, skipping", job.Id);
                continue;
            }

            // Read fresh SPGGCP baseline from the new adapter
            var spggcpBaseline = await adapter.GetCurrentCounterAsync(ct);
            SpawnReadyWatcher(job, adapter, spggcpBaseline);
            _logger.LogInformation(
                "ReadyWatcher respawned for Job {JobId} on printer {PrinterId} after reconnect (SPGGCP baseline={Baseline})",
                job.Id, printerId, spggcpBaseline);
        }

        _logger.LogTrace("<- RespawnWatchersForPrinterAsync: respawned {Count} watcher(s)", readyJobs.Count);
    }

    /// <summary>
    /// Called when a ReadyWatcher detects external printing.
    /// Transitions the job from Ready to Printing and spawns a JobExecutor.
    /// </summary>
    private async Task HandleExternalPrintDetectedAsync(int jobId)
    {
        _logger.LogTrace("-> HandleExternalPrintDetectedAsync(jobId={JobId})", jobId);
        try
        {
            _logger.LogWarning(
                "EXTERNAL PRINT DETECTED for Job {JobId} - transitioning to Printing", jobId);
            await StartJobAsync(jobId);
            _logger.LogTrace("<- HandleExternalPrintDetectedAsync completed");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "FAILED to transition Job {JobId} to Printing after external print detection", jobId);
            _alerts.Raise(AlertSeverity.Error, "System",
                $"External printing detected on Job #{jobId} but failed to start tracking: {ex.Message}",
                jobId: jobId);
            _logger.LogTrace("<- HandleExternalPrintDetectedAsync FAILED");
        }
    }

}
