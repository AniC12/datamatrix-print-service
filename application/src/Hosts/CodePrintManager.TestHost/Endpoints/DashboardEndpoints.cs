using CodePrintManager.Application.Services;
using CodePrintManager.Data;
using CodePrintManager.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace CodePrintManager.TestHost.Endpoints;

public static class DashboardEndpoints
{
    public static void MapDashboardEndpoints(this WebApplication app)
    {
        app.MapGet("/api/dashboard", async (
            AppDbContext db,
            IPrintJobService jobService,
            PrinterConnectionManager connMgr,
            IAlertService alertService) =>
        {
            var activeJobs = await jobService.GetActiveJobsAsync();
            var totalAvailable = await db.Codes
                .Where(c => c.Status == CodePrintManager.Domain.Enums.CodeStatus.Available)
                .CountAsync();
            var printedToday = await db.Codes
                .Where(c => c.Status == CodePrintManager.Domain.Enums.CodeStatus.Printed &&
                    c.StatusChangedAt != null && c.StatusChangedAt.Value.Date == DateTime.UtcNow.Date)
                .CountAsync();

            var recentActivity = await db.AuditLog
                .OrderByDescending(a => a.CreatedAt)
                .Take(20)
                .Select(a => new { a.CreatedAt, a.EventType, a.Details })
                .ToListAsync();

            return Results.Ok(new
            {
                ActiveJobCount = activeJobs.Count,
                TotalAvailableCodes = totalAvailable,
                PrintedToday = printedToday,
                ActiveJobs = activeJobs.Select(j => new
                {
                    j.Id, j.PrinterId, j.ProductId, j.Quantity,
                    Status = j.Status.ToString(),
                    j.CodesConfirmed,
                    ProductName = j.Product?.Name,
                    PrinterName = j.Printer?.Name
                }),
                RecentActivity = recentActivity
            });
        });
    }
}
