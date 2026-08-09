using CodePrintManager.Data;
using CodePrintManager.Domain.Entities;
using CodePrintManager.Domain.Enums;
using CodePrintManager.Domain.Events;
using CodePrintManager.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;
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
        JobEventBus eventBus)
    {
        _db = db;
        _codePool = codePool;
        _connectionManager = connectionManager;
        _alerts = alerts;
        _audit = audit;
        _logger = logger;
        _jobRegistry = jobRegistry;
        _eventBus = eventBus;
    }

    public async Task<PrintJob> CreateJobAsync(int productId, int printerId, int quantity)
    {
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

        await _audit.LogAsync("job_created", productId: productId, printerId: printerId, jobId: job.Id,
            details: new { quantity });

        return job;
    }

    public async Task PrepareJobAsync(int jobId, CancellationToken ct = default)
    {
        var job = await _db.PrintJobs
            .Include(j => j.Product)
            .Include(j => j.Printer)
            .FirstAsync(j => j.Id == jobId);

        var printerLock = _jobRegistry.GetPrinterLock(job.PrinterId);
        await printerLock.WaitAsync(ct);
        try
        {
            var adapter = _connectionManager.GetAdapter(job.PrinterId)
                ?? throw new InvalidOperationException($"Printer {job.Printer.Name} is not connected");

            // Check printer state
            var status = await adapter.GetStatusAsync(ct);
            if (status != PrinterStatus.Idle)
                throw new InvalidOperationException($"Printer is not idle. Current state: {status}");

            // Reserve codes
            var codes = await _codePool.ReserveCodesAsync(job.ProductId, job.Quantity, job.Id);

            ct.ThrowIfCancellationRequested();

            // Upload CSV
            var csvFilename = job.Product.PrinterCsvName
                ?? throw new InvalidOperationException("Product has no CSV filename configured");
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

            // Check template
            var templateName = job.Product.TemplateFile
                ?? throw new InvalidOperationException("Product has no template configured");
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

            job.Status = JobStatus.Ready;
            await _db.SaveChangesAsync();
        }
        catch (OperationCanceledException)
        {
            job.Status = JobStatus.Cancelled;
            await _db.SaveChangesAsync();
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

        // Spawn job executor
        var executor = new JobExecutor(job, adapter, _codePool, _alerts, _db, _logger);
        executor.ProgressChanged += (_, e) =>
        {
            _eventBus.RaiseProgressChanged(this, e);
            JobProgressChanged?.Invoke(this, e);
        };
        executor.Completed += (_, e) =>
        {
            _jobRegistry.TryRemove(jobId);
            _eventBus.RaiseCompleted(this, e);
            JobCompleted?.Invoke(this, e);
        };
        _jobRegistry.Register(jobId, executor);
        executor.Start();

        await _audit.LogAsync("job_started", printerId: job.PrinterId, jobId: jobId);
    }

    public async Task CancelJobAsync(int jobId)
    {
        var job = await _db.PrintJobs.FirstAsync(j => j.Id == jobId);
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

                    // Mark codes: printed up to counter, burn +1, return rest
                    if (finalCounter > job.CodesConfirmed)
                        await _codePool.MarkCodesPrintedAsync(jobId, job.CodesConfirmed, finalCounter);

                    if (finalCounter < job.Quantity)
                        await _codePool.BurnCodeAsync(jobId, finalCounter);

                    if (finalCounter + 1 < job.Quantity)
                        await _codePool.ReturnCodesToPoolAsync(jobId, finalCounter + 1, job.Quantity - finalCounter - 1);
                }
            }
            else if (job.Status is JobStatus.Preparing or JobStatus.Ready)
            {
                await _codePool.ReturnCodesToPoolAsync(jobId, 0, job.Quantity);
            }

            job.Status = JobStatus.Cancelled;
            job.CompletedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();

            await _audit.LogAsync("job_cancelled", jobId: jobId);
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
                     || j.Status == JobStatus.Printing)
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
                     || j.Status == JobStatus.Printing)
            .ToListAsync();
    }

    public async Task ResumeJobAsync(int jobId, CancellationToken ct = default)
    {
        var job = await _db.PrintJobs.FindAsync(jobId)
            ?? throw new InvalidOperationException($"Job {jobId} not found");

        if (job.Status == JobStatus.Printing || job.Status == JobStatus.Ready)
        {
            await StartJobAsync(jobId, ct);
        }
    }
}
