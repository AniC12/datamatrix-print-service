using CodePrintManager.Data;
using Microsoft.EntityFrameworkCore;

namespace CodePrintManager.TestHost.Endpoints;

public static class AuditEndpoints
{
    public static void MapAuditEndpoints(this WebApplication app)
    {
        app.MapGet("/api/audit", async (int? jobId, string? eventType, int? limit, AppDbContext db) =>
        {
            var query = db.AuditLog.AsNoTracking().AsQueryable();

            if (jobId.HasValue)
                query = query.Where(a => a.JobId == jobId.Value);
            if (!string.IsNullOrEmpty(eventType))
                query = query.Where(a => a.EventType == eventType);

            var entries = await query
                .OrderByDescending(a => a.CreatedAt)
                .Take(limit ?? 100)
                .Select(a => new
                {
                    a.Id, a.EventType, a.ProductId, a.PrinterId,
                    a.JobId, a.Details, a.CreatedAt
                })
                .ToListAsync();

            return Results.Ok(entries);
        });
    }
}
