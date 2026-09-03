using CodePrintManager.Domain.Enums;

namespace CodePrintManager.Domain.Entities;

public class PrintJob
{
    public int Id { get; set; }
    public int ProductId { get; set; }
    public int PrinterId { get; set; }
    public int Quantity { get; set; }
    public JobStatus Status { get; set; } = JobStatus.Preparing;
    public int? TotalBaseline { get; set; }
    public int CodesConfirmed { get; set; }

    // Executor state persisted for crash recovery.
    // Allows exact restoration of the polling executor after app restart.
    public int? CounterOffset { get; set; }
    public int? PreviousCounter { get; set; }
    public int? LastKnownLifetime { get; set; }

    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public DateTime CreatedAt { get; set; }

    // Navigation properties
    public ProductNode Product { get; set; } = null!;
    public Printer Printer { get; set; } = null!;
    public ICollection<Code> Codes { get; set; } = new List<Code>();
}
