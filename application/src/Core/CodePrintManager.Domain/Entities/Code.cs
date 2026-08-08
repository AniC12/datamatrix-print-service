using CodePrintManager.Domain.Enums;

namespace CodePrintManager.Domain.Entities;

public class Code
{
    public int Id { get; set; }
    public int ProductId { get; set; }
    public string CodeText { get; set; } = string.Empty;
    public CodeStatus Status { get; set; } = CodeStatus.Available;
    public int ImportOrder { get; set; }
    public string? ImportBatch { get; set; }
    public int? JobId { get; set; }
    public DateTime? StatusChangedAt { get; set; }
    public DateTime CreatedAt { get; set; }

    // Navigation properties
    public ProductNode Product { get; set; } = null!;
    public PrintJob? Job { get; set; }
}
