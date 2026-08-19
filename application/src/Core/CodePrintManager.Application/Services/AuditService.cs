using System.Text.Json;
using CodePrintManager.Data;
using CodePrintManager.Domain.Entities;
using CodePrintManager.Domain.Interfaces;
using Microsoft.Extensions.Logging;

namespace CodePrintManager.Application.Services;

public class AuditService : IAuditService
{
    private readonly AppDbContext _db;
    private readonly ILogger<AuditService> _logger;

    public AuditService(AppDbContext db, ILogger<AuditService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task LogAsync(string eventType, int? productId = null, int? printerId = null,
                               int? jobId = null, object? details = null)
    {
        _logger.LogTrace("-> LogAsync(eventType={EventType}, productId={ProductId}, printerId={PrinterId}, jobId={JobId}, details={Details})",
            eventType, productId, printerId, jobId, details);

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

        _logger.LogTrace("<- LogAsync (entryId={EntryId})", entry.Id);
    }
}
