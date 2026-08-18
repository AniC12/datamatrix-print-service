namespace CodePrintManager.Domain.Entities;

public class ArchivedCode
{
    public int Id { get; set; }
    public int OriginalCodeId { get; set; }
    public int? ProductId { get; set; }
    public string CodeText { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public int ImportOrder { get; set; }
    public string? ImportBatch { get; set; }
    public int? JobId { get; set; }
    public DateTime? StatusChangedAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime ArchivedAt { get; set; }
    public string? ArchivedReason { get; set; }
}
