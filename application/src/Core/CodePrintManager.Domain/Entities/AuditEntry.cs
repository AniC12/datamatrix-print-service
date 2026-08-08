namespace CodePrintManager.Domain.Entities;

public class AuditEntry
{
    public int Id { get; set; }
    public string EventType { get; set; } = string.Empty;
    public int? ProductId { get; set; }
    public int? PrinterId { get; set; }
    public int? JobId { get; set; }
    public string? Details { get; set; }
    public DateTime CreatedAt { get; set; }
}
