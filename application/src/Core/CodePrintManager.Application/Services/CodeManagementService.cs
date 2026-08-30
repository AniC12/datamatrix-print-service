using CodePrintManager.Data;
using CodePrintManager.Domain.Entities;
using CodePrintManager.Domain.Enums;
using CodePrintManager.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace CodePrintManager.Application.Services;

public class CodeManagementService : ICodeManagementService
{
    private readonly AppDbContext _db;
    private readonly IAuditService _audit;
    private readonly ILogger<CodeManagementService> _logger;
    private readonly ILocalizationService _loc;

    public CodeManagementService(AppDbContext db, IAuditService audit, ILogger<CodeManagementService> logger, ILocalizationService loc)
    {
        _db = db;
        _audit = audit;
        _logger = logger;
        _loc = loc;
    }

    public async Task<CodePage> GetCodesPageAsync(int? productId, CodeStatus? statusFilter,
        string? searchText, int page, int pageSize)
    {
        _logger.LogTrace("-> GetCodesPageAsync(productId={ProductId}, statusFilter={StatusFilter}, searchText={SearchText}, page={Page}, pageSize={PageSize})", productId, statusFilter, searchText, page, pageSize);
        var query = _db.Codes.AsNoTracking().AsQueryable();

        // Filter by product (null = unassigned)
        query = productId.HasValue
            ? query.Where(c => c.ProductId == productId.Value)
            : query.Where(c => c.ProductId == null);

        if (statusFilter.HasValue)
            query = query.Where(c => c.Status == statusFilter.Value);

        if (!string.IsNullOrWhiteSpace(searchText))
            query = query.Where(c => c.CodeText.Contains(searchText));

        var totalCount = await query.CountAsync();

        // pageSize == -1 means "All"
        if (pageSize <= 0)
        {
            var allCodes = await query
                .OrderBy(c => c.ImportOrder)
                .ToListAsync();
            var resultAll = new CodePage(allCodes, totalCount, 1, totalCount, 1);
            _logger.LogTrace("<- GetCodesPageAsync = CodePage(TotalCount={TotalCount}, Page=1, AllItems)", totalCount);
            return resultAll;
        }

        var totalPages = totalCount == 0 ? 1 : (int)Math.Ceiling((double)totalCount / pageSize);
        page = Math.Clamp(page, 1, totalPages);

        var codes = await query
            .OrderBy(c => c.ImportOrder)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        var result = new CodePage(codes, totalCount, page, pageSize, totalPages);
        _logger.LogTrace("<- GetCodesPageAsync = CodePage(TotalCount={TotalCount}, Page={Page}, PageSize={PageSize}, TotalPages={TotalPages})", totalCount, page, pageSize, totalPages);
        return result;
    }

    public async Task<int> GetUnassignedCountAsync()
    {
        _logger.LogTrace("-> GetUnassignedCountAsync()");
        var result = await _db.Codes.CountAsync(c => c.ProductId == null);
        _logger.LogTrace("<- GetUnassignedCountAsync = {Count}", result);
        return result;
    }

    public async Task<CodeOperation> ChangeStatusAsync(IReadOnlyList<int> codeIds, CodeStatus newStatus)
    {
        _logger.LogTrace("-> ChangeStatusAsync(codeIds=[{CodeIdCount} items], newStatus={NewStatus})", codeIds.Count, newStatus);

        if (newStatus == CodeStatus.Reserved)
            throw new InvalidOperationException(
                "Cannot manually set codes to Reserved status. Codes are reserved automatically by print jobs.");

        var codes = await _db.Codes
            .Where(c => codeIds.Contains(c.Id) && c.Status != CodeStatus.Reserved)
            .ToListAsync();

        var previousStatuses = codes.ToDictionary(c => c.Id, c => c.Status);
        var previousProductIds = codes.ToDictionary(c => c.Id, c => c.ProductId);
        var now = DateTime.UtcNow;

        foreach (var code in codes)
        {
            code.Status = newStatus;
            code.StatusChangedAt = now;
            // Clear job association when moving to Available or Returned
            if (newStatus is CodeStatus.Available or CodeStatus.Returned)
                code.JobId = null;
        }

        await _db.SaveChangesAsync();

        var description = _loc.Format("Status_ChangedCodes", codes.Count, newStatus);
        _logger.LogInformation("Admin status change: {Description}", description);
        await _audit.LogAsync("admin_status_change",
            productId: codes.FirstOrDefault()?.ProductId,
            details: new { codeCount = codes.Count, newStatus = newStatus.ToString(), codeIds = codes.Select(c => c.Id).ToList() });

        var result = new CodeOperation(
            "status_change", codes.Select(c => c.Id).ToList(),
            previousStatuses, previousProductIds,
            newStatus, null, now, description);
        _logger.LogTrace("<- ChangeStatusAsync = CodeOperation(affected={AffectedCount})", result.AffectedCodeIds.Count);
        return result;
    }

    public async Task<CodeOperation> ChangeStatusBulkAsync(int? productId, CodeStatus fromStatus, CodeStatus toStatus)
    {
        _logger.LogTrace("-> ChangeStatusBulkAsync(productId={ProductId}, fromStatus={FromStatus}, toStatus={ToStatus})", productId, fromStatus, toStatus);

        if (toStatus == CodeStatus.Reserved)
            throw new InvalidOperationException(
                "Cannot manually set codes to Reserved status.");

        var query = productId.HasValue
            ? _db.Codes.Where(c => c.ProductId == productId.Value)
            : _db.Codes.Where(c => c.ProductId == null);

        var codes = await query
            .Where(c => c.Status == fromStatus && c.Status != CodeStatus.Reserved)
            .ToListAsync();

        var previousStatuses = codes.ToDictionary(c => c.Id, c => c.Status);
        var previousProductIds = codes.ToDictionary(c => c.Id, c => c.ProductId);
        var now = DateTime.UtcNow;

        foreach (var code in codes)
        {
            code.Status = toStatus;
            code.StatusChangedAt = now;
            if (toStatus is CodeStatus.Available or CodeStatus.Returned)
                code.JobId = null;
        }

        await _db.SaveChangesAsync();

        var description = _loc.Format("Status_ChangedAllCodes", codes.Count, fromStatus, toStatus);
        _logger.LogInformation("Admin bulk status change: {Description}", description);
        await _audit.LogAsync("admin_status_change",
            productId: productId,
            details: new { codeCount = codes.Count, fromStatus = fromStatus.ToString(), toStatus = toStatus.ToString() });

        var result = new CodeOperation(
            "status_change", codes.Select(c => c.Id).ToList(),
            previousStatuses, previousProductIds,
            toStatus, null, now, description);
        _logger.LogTrace("<- ChangeStatusBulkAsync = CodeOperation(affected={AffectedCount})", result.AffectedCodeIds.Count);
        return result;
    }

    public async Task<CodeOperation> MoveCodesAsync(IReadOnlyList<int> codeIds, int targetProductId)
    {
        _logger.LogTrace("-> MoveCodesAsync(codeIds=[{CodeIdCount} items], targetProductId={TargetProductId})", codeIds.Count, targetProductId);
        // Validate target is a leaf product
        var target = await _db.ProductNodes.FindAsync(targetProductId)
            ?? throw new InvalidOperationException(_loc.Format("Error_TargetProductNotFound", targetProductId));
        if (!target.IsLeaf)
        {
            _logger.LogTrace("<- MoveCodesAsync FAILED: target is not a leaf product");
            throw new InvalidOperationException(_loc["Error_CodesOnlyMoveToLeaf"]);
        }

        var codes = await _db.Codes
            .Where(c => codeIds.Contains(c.Id) && c.Status != CodeStatus.Reserved)
            .ToListAsync();

        var previousStatuses = codes.ToDictionary(c => c.Id, c => c.Status);
        var previousProductIds = codes.ToDictionary(c => c.Id, c => c.ProductId);
        var now = DateTime.UtcNow;

        foreach (var code in codes)
            code.ProductId = targetProductId;

        await _db.SaveChangesAsync();

        var description = _loc.Format("Status_MovedCodes", codes.Count, target.Name);
        _logger.LogInformation("Admin move: {Description}", description);
        await _audit.LogAsync("admin_move",
            productId: targetProductId,
            details: new { codeCount = codes.Count, targetProductId, targetName = target.Name, codeIds = codes.Select(c => c.Id).ToList() });

        var result = new CodeOperation(
            "move", codes.Select(c => c.Id).ToList(),
            previousStatuses, previousProductIds,
            null, targetProductId, now, description);
        _logger.LogTrace("<- MoveCodesAsync = CodeOperation(affected={AffectedCount})", result.AffectedCodeIds.Count);
        return result;
    }

    public async Task<CodeOperation> MoveCodesBulkAsync(int? sourceProductId, CodeStatus statusFilter, int targetProductId)
    {
        _logger.LogTrace("-> MoveCodesBulkAsync(sourceProductId={SourceProductId}, statusFilter={StatusFilter}, targetProductId={TargetProductId})", sourceProductId, statusFilter, targetProductId);
        var target = await _db.ProductNodes.FindAsync(targetProductId)
            ?? throw new InvalidOperationException(_loc.Format("Error_TargetProductNotFound", targetProductId));
        if (!target.IsLeaf)
        {
            _logger.LogTrace("<- MoveCodesBulkAsync FAILED: target is not a leaf product");
            throw new InvalidOperationException(_loc["Error_CodesOnlyMoveToLeaf"]);
        }

        var query = sourceProductId.HasValue
            ? _db.Codes.Where(c => c.ProductId == sourceProductId.Value)
            : _db.Codes.Where(c => c.ProductId == null);

        var codes = await query
            .Where(c => c.Status == statusFilter && c.Status != CodeStatus.Reserved)
            .ToListAsync();

        var previousStatuses = codes.ToDictionary(c => c.Id, c => c.Status);
        var previousProductIds = codes.ToDictionary(c => c.Id, c => c.ProductId);
        var now = DateTime.UtcNow;

        foreach (var code in codes)
            code.ProductId = targetProductId;

        await _db.SaveChangesAsync();

        var description = _loc.Format("Status_MovedAllCodes", codes.Count, statusFilter, target.Name);
        _logger.LogInformation("Admin bulk move: {Description}", description);
        await _audit.LogAsync("admin_move",
            productId: targetProductId,
            details: new { codeCount = codes.Count, sourceProductId, statusFilter = statusFilter.ToString(), targetProductId, targetName = target.Name });

        var result = new CodeOperation(
            "move", codes.Select(c => c.Id).ToList(),
            previousStatuses, previousProductIds,
            null, targetProductId, now, description);
        _logger.LogTrace("<- MoveCodesBulkAsync = CodeOperation(affected={AffectedCount})", result.AffectedCodeIds.Count);
        return result;
    }

    public async Task<CodeOperation> ArchiveCodesAsync(IReadOnlyList<int> codeIds, string reason)
    {
        _logger.LogTrace("-> ArchiveCodesAsync(codeIds=[{CodeIdCount} items], reason={Reason})", codeIds.Count, reason);
        var codes = await _db.Codes
            .Where(c => codeIds.Contains(c.Id) && c.Status != CodeStatus.Reserved)
            .ToListAsync();

        var previousStatuses = codes.ToDictionary(c => c.Id, c => c.Status);
        var previousProductIds = codes.ToDictionary(c => c.Id, c => c.ProductId);
        var now = DateTime.UtcNow;

        foreach (var code in codes)
        {
            _db.ArchivedCodes.Add(new ArchivedCode
            {
                OriginalCodeId = code.Id,
                ProductId = code.ProductId,
                CodeText = code.CodeText,
                Status = code.Status.ToString(),
                ImportOrder = code.ImportOrder,
                ImportBatch = code.ImportBatch,
                JobId = code.JobId,
                StatusChangedAt = code.StatusChangedAt,
                CreatedAt = code.CreatedAt,
                ArchivedAt = now,
                ArchivedReason = reason
            });
        }

        _db.Codes.RemoveRange(codes);
        await _db.SaveChangesAsync();

        var description = _loc.Format("Status_ArchivedCodes", codes.Count);
        _logger.LogInformation("Admin archive: {Description}", description);
        await _audit.LogAsync("admin_archive",
            productId: codes.FirstOrDefault()?.ProductId,
            details: new { codeCount = codes.Count, reason, codeIds = codes.Select(c => c.Id).ToList() });

        var result = new CodeOperation(
            "archive", codes.Select(c => c.Id).ToList(),
            previousStatuses, previousProductIds,
            null, null, now, description);
        _logger.LogTrace("<- ArchiveCodesAsync = CodeOperation(affected={AffectedCount})", result.AffectedCodeIds.Count);
        return result;
    }

    public async Task<CodeOperation> ArchiveCodesBulkAsync(int? productId, CodeStatus statusFilter, string reason)
    {
        _logger.LogTrace("-> ArchiveCodesBulkAsync(productId={ProductId}, statusFilter={StatusFilter}, reason={Reason})", productId, statusFilter, reason);
        var query = productId.HasValue
            ? _db.Codes.Where(c => c.ProductId == productId.Value)
            : _db.Codes.Where(c => c.ProductId == null);

        // When statusFilter is not specified (called from product deletion), archive all non-Reserved
        var codes = await query
            .Where(c => c.Status != CodeStatus.Reserved)
            .Where(c => c.Status == statusFilter || statusFilter == default)
            .ToListAsync();

        // For product deletion, we want ALL non-Reserved codes regardless of status
        if (reason == "product_deleted")
        {
            codes = await query
                .Where(c => c.Status != CodeStatus.Reserved)
                .ToListAsync();
        }

        var previousStatuses = codes.ToDictionary(c => c.Id, c => c.Status);
        var previousProductIds = codes.ToDictionary(c => c.Id, c => c.ProductId);
        var now = DateTime.UtcNow;

        foreach (var code in codes)
        {
            _db.ArchivedCodes.Add(new ArchivedCode
            {
                OriginalCodeId = code.Id,
                ProductId = code.ProductId,
                CodeText = code.CodeText,
                Status = code.Status.ToString(),
                ImportOrder = code.ImportOrder,
                ImportBatch = code.ImportBatch,
                JobId = code.JobId,
                StatusChangedAt = code.StatusChangedAt,
                CreatedAt = code.CreatedAt,
                ArchivedAt = now,
                ArchivedReason = reason
            });
        }

        _db.Codes.RemoveRange(codes);
        await _db.SaveChangesAsync();

        var description = _loc.Format("Status_ArchivedCodes", codes.Count);
        _logger.LogInformation("Admin bulk archive: {Description}", description);
        await _audit.LogAsync("admin_archive",
            productId: productId,
            details: new { codeCount = codes.Count, reason });

        var result = new CodeOperation(
            "archive", codes.Select(c => c.Id).ToList(),
            previousStatuses, previousProductIds,
            null, null, now, description);
        _logger.LogTrace("<- ArchiveCodesBulkAsync = CodeOperation(affected={AffectedCount})", result.AffectedCodeIds.Count);
        return result;
    }

    public async Task UnassignCodesAsync(int productId)
    {
        _logger.LogTrace("-> UnassignCodesAsync(productId={ProductId})", productId);
        var codes = await _db.Codes
            .Where(c => c.ProductId == productId && c.Status != CodeStatus.Reserved)
            .ToListAsync();

        foreach (var code in codes)
            code.ProductId = null;

        await _db.SaveChangesAsync();

        _logger.LogInformation("Unassigned {Count} codes from Product {ProductId}", codes.Count, productId);
        await _audit.LogAsync("admin_unassign",
            productId: productId,
            details: new { codeCount = codes.Count });
        _logger.LogTrace("<- UnassignCodesAsync (unassigned {Count} codes)", codes.Count);
    }

    public async Task<UndoResult> UndoOperationAsync(CodeOperation operation)
    {
        _logger.LogTrace("-> UndoOperationAsync(operationType={OperationType}, affectedCodes={AffectedCount})", operation.OperationType, operation.AffectedCodeIds.Count);
        var reverted = 0;
        var skipped = 0;
        var skipReasons = new List<string>();

        switch (operation.OperationType)
        {
            case "status_change":
                foreach (var codeId in operation.AffectedCodeIds)
                {
                    var code = await _db.Codes.FindAsync(codeId);
                    if (code == null)
                    {
                        skipped++;
                        skipReasons.Add(_loc.Format("Undo_CodeNotFound", codeId));
                        continue;
                    }

                    // Safety check: code must still be in the post-operation state
                    if (operation.NewStatus.HasValue && code.Status != operation.NewStatus.Value)
                    {
                        skipped++;
                        skipReasons.Add(_loc.Format("Undo_CodeStatusChanged", codeId, code.Status));
                        continue;
                    }

                    if (operation.PreviousStatuses.TryGetValue(codeId, out var prevStatus))
                    {
                        code.Status = prevStatus;
                        code.StatusChangedAt = DateTime.UtcNow;
                        reverted++;
                    }
                }
                break;

            case "move":
                foreach (var codeId in operation.AffectedCodeIds)
                {
                    var code = await _db.Codes.FindAsync(codeId);
                    if (code == null)
                    {
                        skipped++;
                        skipReasons.Add(_loc.Format("Undo_CodeNotFound", codeId));
                        continue;
                    }

                    // Safety check: code must still be at the target product
                    if (code.ProductId != operation.TargetProductId)
                    {
                        skipped++;
                        skipReasons.Add(_loc.Format("Undo_CodeMovedAgain", codeId));
                        continue;
                    }

                    if (code.Status == CodeStatus.Reserved)
                    {
                        skipped++;
                        skipReasons.Add(_loc.Format("Undo_CodeReserved", codeId));
                        continue;
                    }

                    if (operation.PreviousProductIds.TryGetValue(codeId, out var prevProductId))
                    {
                        code.ProductId = prevProductId;
                        reverted++;
                    }
                }
                break;

            case "archive":
                foreach (var codeId in operation.AffectedCodeIds)
                {
                    // Find the archived copy
                    var archived = await _db.ArchivedCodes
                        .FirstOrDefaultAsync(a => a.OriginalCodeId == codeId);
                    if (archived == null)
                    {
                        skipped++;
                        skipReasons.Add(_loc.Format("Undo_ArchiveNotFound", codeId));
                        continue;
                    }

                    // Check if a code with the same text was re-imported
                    var conflicting = await _db.Codes.AnyAsync(c => c.CodeText == archived.CodeText);
                    if (conflicting)
                    {
                        skipped++;
                        skipReasons.Add(_loc.Format("Undo_AlreadyReimported", archived.CodeText));
                        continue;
                    }

                    // Restore the code
                    if (!Enum.TryParse<CodeStatus>(archived.Status, out var restoredStatus))
                        restoredStatus = CodeStatus.Available;

                    _db.Codes.Add(new Code
                    {
                        ProductId = archived.ProductId,
                        CodeText = archived.CodeText,
                        Status = restoredStatus,
                        ImportOrder = archived.ImportOrder,
                        ImportBatch = archived.ImportBatch,
                        JobId = archived.JobId,
                        StatusChangedAt = DateTime.UtcNow,
                        CreatedAt = archived.CreatedAt
                    });

                    _db.ArchivedCodes.Remove(archived);
                    reverted++;
                }
                break;
        }

        await _db.SaveChangesAsync();

        var message = _loc.Format("Status_RevertedCodes", reverted);
        if (skipReasons.Count > 0)
            message += $" {_loc.Format("Status_SkippedCount", skipReasons.Count)} ({string.Join("; ", skipReasons)}).";

        _logger.LogInformation("Admin undo: {Message}", message);
        await _audit.LogAsync("admin_undo",
            details: new { operationType = operation.OperationType, reverted, skipped });

        var result = new UndoResult(reverted, skipped, message);
        _logger.LogTrace("<- UndoOperationAsync = UndoResult(reverted={Reverted}, skipped={Skipped})", result.Reverted, result.Skipped);
        return result;
    }
}
