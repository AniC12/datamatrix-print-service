using CodePrintManager.Application.Services;
using CodePrintManager.Data;
using CodePrintManager.Domain.Enums;
using CodePrintManager.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace CodePrintManager.TestHost.Endpoints;

public static class RecoveryEndpoints
{
    public static void MapRecoveryEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/recovery");

        // Inspect stale jobs (equivalent to App.RunStartupRecoveryAsync inspection)
        group.MapPost("/inspect", async (IPrintJobService jobService,
            PrinterConnectionManager connMgr) =>
        {
            var staleJobs = await jobService.GetStaleJobsAsync();
            var items = new List<object>();

            foreach (var job in staleJobs)
            {
                if (job.Status == JobStatus.Preparing)
                {
                    // Auto-cancel Preparing jobs per safety rules
                    await jobService.CancelJobAsync(job.Id);
                    items.Add(new
                    {
                        job.Id,
                        Status = job.Status.ToString(),
                        Action = "AutoCancelled",
                        Reason = "Stale Preparing job"
                    });
                    continue;
                }

                // Ready, Printing, Paused → inspect printer
                var adapter = connMgr.GetAdapter(job.PrinterId);
                if (adapter == null || !adapter.IsConnected)
                {
                    items.Add(new
                    {
                        job.Id,
                        Status = job.Status.ToString(),
                        Action = "NeedsAttention",
                        Reason = "Printer offline",
                        job.CodesConfirmed,
                        job.TotalBaseline,
                        PrinterOffline = true
                    });
                    continue;
                }

                // Read inspection values
                try
                {
                    var status = await adapter.GetStatusAsync();
                    var currentCounter = await adapter.GetCurrentCounterAsync();
                    var lifetimeCounter = await adapter.GetTotalCounterAsync();
                    var activeTemplate = await adapter.GetActiveTemplateAsync();
                    var csvFiles = await adapter.ListCsvFilesAsync();
                    var serialNumber = await adapter.GetSerialNumberAsync();

                    var printerConfirmed = job.TotalBaseline.HasValue
                        ? lifetimeCounter - job.TotalBaseline.Value
                        : job.CodesConfirmed;
                    var discrepancy = printerConfirmed - job.CodesConfirmed;

                    var expectedTemplate = job.Product?.TemplateFile;
                    var expectedTemplateName = expectedTemplate != null
                        ? Path.GetFileName(expectedTemplate) : null;
                    var templateMatch = expectedTemplateName == null
                        || string.Equals(expectedTemplateName, activeTemplate, StringComparison.OrdinalIgnoreCase);

                    var expectedCsv = job.Product?.PrinterCsvName;
                    var csvPresent = expectedCsv == null || csvFiles.Contains(expectedCsv);

                    var serialMismatch = !string.IsNullOrEmpty(job.Printer?.SerialNumber)
                        && !string.IsNullOrEmpty(serialNumber)
                        && !string.Equals(job.Printer.SerialNumber, serialNumber, StringComparison.Ordinal);

                    var powerCycled = (currentCounter == 0 || !csvPresent)
                        && job.TotalBaseline.HasValue
                        && (job.Status == JobStatus.Printing || job.Status == JobStatus.Ready);

                    items.Add(new
                    {
                        job.Id,
                        Status = job.Status.ToString(),
                        Action = "NeedsDecision",
                        job.CodesConfirmed,
                        job.TotalBaseline,
                        PrinterConfirmed = printerConfirmed,
                        Discrepancy = discrepancy,
                        PrinterStatus = status.ToString(),
                        CurrentCounter = currentCounter,
                        LifetimeCounter = lifetimeCounter,
                        ActiveTemplate = activeTemplate,
                        TemplateMatch = templateMatch,
                        CsvPresent = csvPresent,
                        PowerCycleDetected = powerCycled,
                        SerialMismatch = serialMismatch
                    });
                }
                catch (Exception ex)
                {
                    items.Add(new
                    {
                        job.Id,
                        Status = job.Status.ToString(),
                        Action = "NeedsAttention",
                        Reason = $"Inspection failed: {ex.Message}",
                        job.CodesConfirmed,
                        job.TotalBaseline,
                        PrinterOffline = false
                    });
                }
            }

            return Results.Ok(items);
        });

        // Cancel a stale job (with quarantine for ambiguous codes)
        group.MapPost("/cancel/{id:int}", async (int id, IPrintJobService jobService) =>
        {
            try
            {
                await jobService.CancelJobAsync(id);
                return Results.Ok(new { Status = "Cancelled" });
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { Error = ex.Message });
            }
        });

        // Resume a stale job (pre-loads template on fresh mock adapter if needed)
        group.MapPost("/resume/{id:int}", async (int id, IPrintJobService jobService,
            AppDbContext db, PrinterConnectionManager connMgr) =>
        {
            try
            {
                // Pre-load template for mock adapters (fresh adapter after crash has empty storage)
                var job = await db.PrintJobs.Include(j => j.Product).FirstOrDefaultAsync(j => j.Id == id);
                if (job != null)
                {
                    var adapter = connMgr.GetAdapter(job.PrinterId) as CodePrintManager.Printer.Mock.MockPrinterAdapter;
                    if (adapter != null && job.Product?.TemplateFile != null)
                    {
                        var templateName = Path.GetFileName(job.Product.TemplateFile);
                        if (!adapter.StoredTemplates.Contains(templateName))
                        {
                            await adapter.UploadTemplateAsync(templateName, Array.Empty<byte>());
                        }
                    }
                }

                await jobService.ResumeJobAsync(id);
                return Results.Ok(new { Status = "Resumed" });
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { Error = ex.Message });
            }
        });
    }
}
