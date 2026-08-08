using CodePrintManager.Domain.Entities;
using CodePrintManager.Domain.Enums;

namespace CodePrintManager.Domain.Interfaces;

public interface ICodePoolService
{
    Task<CsvImportResult> ImportCodesAsync(int productId, string batchName, IReadOnlyList<string> codes);
    Task<List<Code>> ReserveCodesAsync(int productId, int quantity, int jobId);
    Task ReturnCodesToPoolAsync(int jobId, int startIndex, int count);
    Task MarkCodesPrintedAsync(int jobId, int fromIndex, int toIndex);
    Task BurnCodeAsync(int jobId, int index);
    Task<int> GetAvailableCountAsync(int productId);
    Task<(int Available, int Total)> GetCodeCountsAsync(int productId);
    Task<Dictionary<CodeStatus, int>> GetPoolStatsAsync(int productId);
}

public record CsvImportResult(int Imported, int Duplicates, List<string> Errors);
