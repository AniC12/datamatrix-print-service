using System.Text.Json;
using CodePrintManager.Data;
using CodePrintManager.Domain.Entities;
using CodePrintManager.Domain.Interfaces;

namespace CodePrintManager.Application.Services;

public class AuditService : IAuditService
{
    private readonly AppDbContext _db;

    public AuditService(AppDbContext db)
    {
        _db = db;
    }

    public async Task LogAsync(string eventType, int? productId = null, int? printerId = null,
                               int? jobId = null, object? details = null)
    {
        var entry = new AuditEntry
        {
            EventType = eventType,
            ProductId = productId,
            PrinterId = printerId,
            JobId = jobId,
            Details = details != null ? JsonSerializer.Serialize(details) : null,
            CreatedAt = DateTime.UtcNow
        };

        _db.AuditLog.Add(entry);
        await _db.SaveChangesAsync();
    }
}
