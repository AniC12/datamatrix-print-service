namespace CodePrintManager.Domain.Interfaces;

public interface IAuditService
{
    Task LogAsync(string eventType, int? productId = null, int? printerId = null,
                  int? jobId = null, object? details = null);
}
