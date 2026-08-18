using CodePrintManager.Domain.Entities;
using CodePrintManager.Domain.Enums;

namespace CodePrintManager.Domain.Interfaces;

public interface ICodeManagementService
{
    // Query (productId: null = unassigned codes)
    Task<CodePage> GetCodesPageAsync(int? productId, CodeStatus? statusFilter,
        string? searchText, int page, int pageSize);
    Task<int> GetUnassignedCountAsync();

    // Status transitions (returns operation record for undo)
    Task<CodeOperation> ChangeStatusAsync(IReadOnlyList<int> codeIds, CodeStatus newStatus);
    Task<CodeOperation> ChangeStatusBulkAsync(int? productId, CodeStatus fromStatus, CodeStatus toStatus);

    // Move between products
    Task<CodeOperation> MoveCodesAsync(IReadOnlyList<int> codeIds, int targetProductId);
    Task<CodeOperation> MoveCodesBulkAsync(int? sourceProductId, CodeStatus statusFilter, int targetProductId);

    // Archive (delete)
    Task<CodeOperation> ArchiveCodesAsync(IReadOnlyList<int> codeIds, string reason);
    Task<CodeOperation> ArchiveCodesBulkAsync(int? productId, CodeStatus statusFilter, string reason);

    // Unassign (used by product deletion — "keep codes")
    Task UnassignCodesAsync(int productId);

    // Undo
    Task<UndoResult> UndoOperationAsync(CodeOperation operation);
}

public record CodePage(
    IReadOnlyList<Code> Codes,
    int TotalCount,
    int Page,
    int PageSize,
    int TotalPages);

public record CodeOperation(
    string OperationType,
    IReadOnlyList<int> AffectedCodeIds,
    Dictionary<int, CodeStatus> PreviousStatuses,
    Dictionary<int, int?> PreviousProductIds,
    CodeStatus? NewStatus,
    int? TargetProductId,
    DateTime PerformedAt,
    string Description);

public record UndoResult(
    int Reverted,
    int Skipped,
    string Message);
