namespace CodePrintManager.Application.Models;

public record CsvImportResult(
    int TotalRows,
    int ImportedCount,
    int DuplicateCount,
    int InvalidCount,
    IReadOnlyList<string> Errors);
