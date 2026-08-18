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

    public CodeManagementService(AppDbContext db, IAuditService audit, ILogger<CodeManagementService> logger)
    {
        _db = db;
        _audit = audit;
        _logger = logger;
    }

    public async Task<CodePage> GetCodesPageAsync(int? productId, CodeStatus? statusFilter,
        string? searchText, int page, int pageSize)
    {
        var query = _db.Codes.AsQueryable();

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
            return new CodePage(allCodes, totalCount, 1, totalCount, 1);
        }

        var totalPages = totalCount == 0 ? 1 : (int)Math.Ceiling((double)totalCount / pageSize);
        page = Math.Clamp(page, 1, totalPages);

        var codes = await query
            .OrderBy(c => c.ImportOrder)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return new CodePage(codes, totalCount, page, pageSize, totalPages);
    }

    public async Task<int> GetUnassignedCountAsync()
    {
        return await _db.Codes.CountAsync(c => c.ProductId == null);
    }

    public async Task<CodeOperation> ChangeStatusAsync(IReadOnlyList<int> codeIds, CodeStatus newStatus)
    {
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

        var description = $"Changed {codes.Count} code(s) to {newStatus}.";
        _logger.LogInformation("Admin status change: {Description}", description);
        await _audit.LogAsync("admin_status_change",
            productId: codes.FirstOrDefault()?.ProductId,
            details: new { codeCount = codes.Count, newStatus = newStatus.ToString(), codeIds = codes.Select(c => c.Id).ToList() });

        return new CodeOperation(
            "status_change", codes.Select(c => c.Id).ToList(),
            previousStatuses, previousProductIds,
            newStatus, null, now, description);
    }

    public async Task<CodeOperation> ChangeStatusBulkAsync(int? productId, CodeStatus fromStatus, CodeStatus toStatus)
    {
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

        var description = $"Changed all {codes.Count} {fromStatus} code(s) to {toStatus}.";
        _logger.LogInformation("Admin bulk status change: {Description}", description);
        await _audit.LogAsync("admin_status_change",
            productId: productId,
            details: new { codeCount = codes.Count, fromStatus = fromStatus.ToString(), toStatus = toStatus.ToString() });

        return new CodeOperation(
            "status_change", codes.Select(c => c.Id).ToList(),
            previousStatuses, previousProductIds,
            toStatus, null, now, description);
    }

    public async Task<CodeOperation> MoveCodesAsync(IReadOnlyList<int> codeIds, int targetProductId)
    {
        // Validate target is a leaf product
        var target = await _db.ProductNodes.FindAsync(targetProductId)
            ?? throw new InvalidOperationException($"Target product {targetProductId} not found.");
        if (!target.IsLeaf)
            throw new InvalidOperationException("Codes can only be moved to leaf products.");

        var codes = await _db.Codes
            .Where(c => codeIds.Contains(c.Id) && c.Status != CodeStatus.Reserved)
            .ToListAsync();

        var previousStatuses = codes.ToDictionary(c => c.Id, c => c.Status);
        var previousProductIds = codes.ToDictionary(c => c.Id, c => c.ProductId);
        var now = DateTime.UtcNow;

        foreach (var code in codes)
            code.ProductId = targetProductId;

        await _db.SaveChangesAsync();

        var description = $"Moved {codes.Count} code(s) to {target.Name}.";
        _logger.LogInformation("Admin move: {Description}", description);
        await _audit.LogAsync("admin_move",
            productId: targetProductId,
            details: new { codeCount = codes.Count, targetProductId, targetName = target.Name, codeIds = codes.Select(c => c.Id).ToList() });

        return new CodeOperation(
            "move", codes.Select(c => c.Id).ToList(),
            previousStatuses, previousProductIds,
            null, targetProductId, now, description);
    }

    public async Task<CodeOperation> MoveCodesBulkAsync(int? sourceProductId, CodeStatus statusFilter, int targetProductId)
    {
        var target = await _db.ProductNodes.FindAsync(targetProductId)
            ?? throw new InvalidOperationException($"Target product {targetProductId} not found.");
        if (!target.IsLeaf)
            throw new InvalidOperationException("Codes can only be moved to leaf products.");

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

        var description = $"Moved all {codes.Count} {statusFilter} code(s) to {target.Name}.";
        _logger.LogInformation("Admin bulk move: {Description}", description);
        await _audit.LogAsync("admin_move",
            productId: targetProductId,
            details: new { codeCount = codes.Count, sourceProductId, statusFilter = statusFilter.ToString(), targetProductId, targetName = target.Name });

        return new CodeOperation(
            "move", codes.Select(c => c.Id).ToList(),
            previousStatuses, previousProductIds,
            null, targetProductId, now, description);
    }

    public async Task<CodeOperation> ArchiveCodesAsync(IReadOnlyList<int> codeIds, string reason)
    {
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

        var description = $"Archived {codes.Count} code(s).";
        _logger.LogInformation("Admin archive: {Description}", description);
        await _audit.LogAsync("admin_archive",
            productId: codes.FirstOrDefault()?.ProductId,
            details: new { codeCount = codes.Count, reason, codeIds = codes.Select(c => c.Id).ToList() });

        return new CodeOperation(
            "archive", codes.Select(c => c.Id).ToList(),
            previousStatuses, previousProductIds,
            null, null, now, description);
    }

    public async Task<CodeOperation> ArchiveCodesBulkAsync(int? productId, CodeStatus statusFilter, string reason)
    {
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

        var description = $"Archived {codes.Count} code(s).";
        _logger.LogInformation("Admin bulk archive: {Description}", description);
        await _audit.LogAsync("admin_archive",
            productId: productId,
            details: new { codeCount = codes.Count, reason });

        return new CodeOperation(
            "archive", codes.Select(c => c.Id).ToList(),
            previousStatuses, previousProductIds,
            null, null, now, description);
    }

    public async Task UnassignCodesAsync(int productId)
    {
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
    }

    public async Task<UndoResult> UndoOperationAsync(CodeOperation operation)
    {
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
                        skipReasons.Add($"Code #{codeId}: no longer exists");
                        continue;
                    }

                    // Safety check: code must still be in the post-operation state
                    if (operation.NewStatus.HasValue && code.Status != operation.NewStatus.Value)
                    {
                        skipped++;
                        skipReasons.Add($"Code #{codeId}: status changed to {code.Status}");
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
                        skipReasons.Add($"Code #{codeId}: no longer exists");
                        continue;
                    }

                    // Safety check: code must still be at the target product
                    if (code.ProductId != operation.TargetProductId)
                    {
                        skipped++;
                        skipReasons.Add($"Code #{codeId}: moved again");
                        continue;
                    }

                    if (code.Status == CodeStatus.Reserved)
                    {
                        skipped++;
                        skipReasons.Add($"Code #{codeId}: now reserved by a job");
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
                        skipReasons.Add($"Code #{codeId}: archive record not found");
                        continue;
                    }

                    // Check if a code with the same text was re-imported
                    var conflicting = await _db.Codes.AnyAsync(c => c.CodeText == archived.CodeText);
                    if (conflicting)
                    {
                        skipped++;
                        skipReasons.Add($"Code '{archived.CodeText}': already re-imported");
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

        var message = $"Reverted {reverted} code(s).";
        if (skipped > 0)
            message += $" {skipped} skipped ({string.Join("; ", skipReasons)}).";

        _logger.LogInformation("Admin undo: {Message}", message);
        await _audit.LogAsync("admin_undo",
            details: new { operationType = operation.OperationType, reverted, skipped });

        return new UndoResult(reverted, skipped, message);
    }
}
