namespace CodePrintManager.Domain.Entities;

public class ProductNode
{
    public int Id { get; set; }
    public int? ParentId { get; set; }
    public string Name { get; set; } = string.Empty;
    public bool IsLeaf { get; set; }

    // Leaf-only fields
    public string? TemplateFile { get; set; }
    public string? PrinterCsvName { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    // Navigation properties
    public ProductNode? Parent { get; set; }
    public ICollection<ProductNode> Children { get; set; } = new List<ProductNode>();
    public ICollection<Code> Codes { get; set; } = new List<Code>();
    public ICollection<PrintJob> PrintJobs { get; set; } = new List<PrintJob>();
}
