using CodePrintManager.Application.Services;
using CodePrintManager.Data;
using CodePrintManager.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace CodePrintManager.TestHost.Endpoints;

public static class JobEndpoints
{
    public static void MapJobEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/jobs");

        group.MapGet("/active", async (IPrintJobService jobService) =>
        {
            var jobs = await jobService.GetActiveJobsAsync();
            return Results.Ok(jobs.Select(j => new
            {
                j.Id, j.ProductId, j.PrinterId, j.Quantity,
                Status = j.Status.ToString(),
                j.CodesConfirmed, j.TotalBaseline,
                j.CreatedAt, j.StartedAt, j.CompletedAt,
                ProductName = j.Product?.Name,
                PrinterName = j.Printer?.Name
            }));
        });

        group.MapGet("/history", async (int? printerId, int? productId, IPrintJobService jobService) =>
        {
            var jobs = await jobService.GetJobHistoryAsync(printerId, productId);
            return Results.Ok(jobs.Select(j => new
            {
                j.Id, j.ProductId, j.PrinterId, j.Quantity,
                Status = j.Status.ToString(),
                j.CodesConfirmed, j.CreatedAt, j.StartedAt, j.CompletedAt,
                ProductName = j.Product?.Name,
                PrinterName = j.Printer?.Name
            }));
        });

        group.MapGet("/{id:int}", async (int id, AppDbContext db) =>
        {
            var job = await db.PrintJobs
                .Include(j => j.Product)
                .Include(j => j.Printer)
                .FirstOrDefaultAsync(j => j.Id == id);
            if (job == null) return Results.NotFound();

            return Results.Ok(new
            {
                job.Id, job.ProductId, job.PrinterId, job.Quantity,
                Status = job.Status.ToString(),
                job.CodesConfirmed, job.TotalBaseline,
                job.CreatedAt, job.StartedAt, job.CompletedAt,
                ProductName = job.Product?.Name,
                PrinterName = job.Printer?.Name
            });
        });

        group.MapPost("/", async (CreateJobRequest req, IPrintJobService jobService,
            PrinterConnectionManager connMgr, AppDbContext db) =>
        {
            try
            {
                // Pre-load template onto mock printer so PrepareJobAsync doesn't need disk file
                var product = await db.ProductNodes.FindAsync(req.ProductId);
                if (product?.TemplateFile != null)
                {
                    var adapter = connMgr.GetAdapter(req.PrinterId);
                    if (adapter != null)
                    {
                        var templateName = Path.GetFileName(product.TemplateFile);
                        var templates = await adapter.ListTemplatesAsync();
                        if (!templates.Contains(templateName))
                            await adapter.UploadTemplateAsync(templateName, new byte[] { 0x00 });
                    }
                }

                var job = await jobService.CreateJobAsync(req.ProductId, req.PrinterId, req.Quantity);
                await jobService.PrepareJobAsync(job.Id);
                return Results.Created($"/api/jobs/{job.Id}", new
                {
                    job.Id, Status = job.Status.ToString(), job.Quantity
                });
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { Error = ex.Message });
            }
        });

        group.MapPost("/{id:int}/start", async (int id, IPrintJobService jobService) =>
        {
            try
            {
                await jobService.StartJobAsync(id);
                return Results.Ok(new { Status = "Printing" });
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { Error = ex.Message });
            }
        });

        group.MapPost("/{id:int}/pause", async (int id, IPrintJobService jobService) =>
        {
            try
            {
                await jobService.PauseJobAsync(id);
                return Results.Ok(new { Status = "Paused" });
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { Error = ex.Message });
            }
        });

        group.MapPost("/{id:int}/resume", async (int id, IPrintJobService jobService) =>
        {
            try
            {
                await jobService.ResumeJobAsync(id);
                return Results.Ok(new { Status = "Printing" });
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { Error = ex.Message });
            }
        });

        group.MapPost("/{id:int}/cancel", async (int id, IPrintJobService jobService) =>
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
    }
}

public record CreateJobRequest(int ProductId, int PrinterId, int Quantity);
