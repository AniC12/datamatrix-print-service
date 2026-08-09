using CodePrintManager.Application.Services;
using CodePrintManager.Data;
using CodePrintManager.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;
using PrinterEntity = CodePrintManager.Domain.Entities.Printer;

namespace CodePrintManager.TestHost.Endpoints;

public static class PrinterEndpoints
{
    public static void MapPrinterEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/printers");

        group.MapGet("/", async (AppDbContext db, PrinterConnectionManager connMgr) =>
        {
            var printers = await db.Printers.ToListAsync();
            return Results.Ok(printers.Select(p => new
            {
                p.Id, p.Name, p.IpAddress, p.Port, p.AdapterType,
                IsConnected = connMgr.GetAdapter(p.Id) != null
            }));
        });

        group.MapGet("/{id:int}", async (int id, AppDbContext db, PrinterConnectionManager connMgr) =>
        {
            var printer = await db.Printers.FindAsync(id);
            if (printer == null) return Results.NotFound();
            var adapter = connMgr.GetAdapter(id);
            return Results.Ok(new
            {
                printer.Id, printer.Name, printer.IpAddress, printer.Port, printer.AdapterType,
                IsConnected = adapter != null,
                Status = adapter != null ? (await adapter.GetStatusAsync()).ToString() : "Offline"
            });
        });

        group.MapPost("/", async (CreatePrinterRequest req, AppDbContext db) =>
        {
            var printer = new PrinterEntity
            {
                Name = req.Name,
                IpAddress = req.Ip,
                Port = req.Port ?? 9100,
                AdapterType = req.AdapterType ?? "mock"
            };
            db.Printers.Add(printer);
            await db.SaveChangesAsync();
            return Results.Created($"/api/printers/{printer.Id}", new { printer.Id, printer.Name });
        });

        group.MapDelete("/{id:int}", async (int id, AppDbContext db, PrinterConnectionManager connMgr) =>
        {
            var printer = await db.Printers.FindAsync(id);
            if (printer == null) return Results.NotFound();
            await connMgr.DisconnectAsync(id);
            db.Printers.Remove(printer);
            await db.SaveChangesAsync();
            return Results.NoContent();
        });

        group.MapPost("/{id:int}/connect", async (int id, AppDbContext db, PrinterConnectionManager connMgr) =>
        {
            var printer = await db.Printers.FindAsync(id);
            if (printer == null) return Results.NotFound();
            await connMgr.ConnectAsync(printer);
            return Results.Ok(new { Status = "Connected" });
        });

        group.MapPost("/{id:int}/disconnect", async (int id, PrinterConnectionManager connMgr) =>
        {
            await connMgr.DisconnectAsync(id);
            return Results.Ok(new { Status = "Disconnected" });
        });

        group.MapPost("/{id:int}/verify", async (int id, AppDbContext db, PrinterConnectionManager connMgr) =>
        {
            var adapter = connMgr.GetAdapter(id);
            if (adapter == null)
                return Results.Ok(new { Overall = "FAILED", Results = new[] { new { Check = "Connection", Status = "Fail", Details = "Not connected" } } });

            var activeJob = await db.PrintJobs
                .Include(j => j.Product)
                .Where(j => j.PrinterId == id &&
                    (j.Status == CodePrintManager.Domain.Enums.JobStatus.Printing ||
                     j.Status == CodePrintManager.Domain.Enums.JobStatus.Ready ||
                     j.Status == CodePrintManager.Domain.Enums.JobStatus.Paused))
                .OrderByDescending(j => j.CreatedAt)
                .FirstOrDefaultAsync();

            var results = new List<object>();

            // CSV check
            if (activeJob?.Product?.PrinterCsvName != null)
            {
                var csvExists = await adapter.VerifyCsvExistsAsync(activeJob.Product.PrinterCsvName);
                results.Add(new { Check = "CSV File", Status = csvExists ? "Pass" : "Warning", Details = csvExists ? "Present" : "Not found" });
            }

            // Template check
            var activeTemplate = await adapter.GetActiveTemplateAsync();
            results.Add(new { Check = "Active Template", Status = "Pass", Details = activeTemplate ?? "(none)" });

            // Counter check
            if (activeJob?.TotalBaseline != null)
            {
                var total = await adapter.GetTotalCounterAsync();
                var expected = activeJob.TotalBaseline.Value + activeJob.CodesConfirmed;
                var delta = total - expected;
                results.Add(new { Check = "Counter", Status = delta == 0 ? "Pass" : "Warning", Details = $"Printer={total}, Expected={expected}, Delta={delta}" });
            }

            var status = await adapter.GetStatusAsync();
            results.Add(new { Check = "Printer Status", Status = "Pass", Details = status.ToString() });

            var hasWarning = results.Any(r => ((dynamic)r).Status == "Warning");
            return Results.Ok(new { Overall = hasWarning ? "WARNINGS" : "ALL OK", Results = results });
        });

        group.MapGet("/{id:int}/storage", async (int id, AppDbContext db, PrinterConnectionManager connMgr) =>
        {
            var adapter = connMgr.GetAdapter(id);
            if (adapter == null) return Results.Ok(new { Templates = Array.Empty<string>(), CsvFiles = Array.Empty<string>() });

            var templates = await adapter.ListTemplatesAsync();
            var csvFiles = await adapter.ListCsvFilesAsync();
            return Results.Ok(new { Templates = templates, CsvFiles = csvFiles });
        });
    }
}

public record CreatePrinterRequest(string Name, string Ip, int? Port = 9100, string? AdapterType = "mock");
