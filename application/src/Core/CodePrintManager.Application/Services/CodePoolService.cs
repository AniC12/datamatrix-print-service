using System.Diagnostics;
using CodePrintManager.Data;
using CodePrintManager.Domain.Entities;
using CodePrintManager.Domain.Enums;
using CodePrintManager.Domain.Interfaces;
using CodePrintManager.Domain.Validation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace CodePrintManager.Application.Services;

public class CodePoolService : ICodePoolService
{
    private readonly AppDbContext _db;
    private readonly IAuditService _audit;
    private readonly IAlertService _alerts;
    private readonly ILogger<CodePoolService> _logger;
    private readonly ILocalizationService _loc;

    private const int LowStockThreshold = 500;

    public CodePoolService(AppDbContext db, IAuditService audit, IAlertService alerts,
        ILogger<CodePoolService> logger, ILocalizationService loc)
    {
        _db = db;
        _audit = audit;
        _alerts = alerts;
        _logger = logger;
        _loc = loc;
    }

    public async Task<CsvImportResult> ImportCodesAsync(int productId, string batchName, IReadOnlyList<string> codes)
    {
        _logger.LogTrace("-> ImportCodesAsync(productId={ProductId}, batchName={BatchName}, codes.Count={Count})",
            productId, batchName, codes.Count);
        var sw = Stopwatch.StartNew();

        _logger.LogInformation("Importing {Count} codes for Product {ProductId} batch='{Batch}'",
            codes.Count, productId, batchName);

        var errors = new List<string>();
        var duplicates = 0;
        var imported = 0;
        var seenInBatch = new HashSet<string>(StringComparer.Ordinal);

        // Get current max import order for this product
        var maxOrder = await _db.Codes
            .Where(c => c.ProductId == productId)
            .MaxAsync(c => (int?)c.ImportOrder) ?? 0;

        for (int i = 0; i < codes.Count; i++)
        {
            var code = codes[i];

            // Validate code against SPPL forbidden sequences
            var validationError = CodeValidator.GetValidationError(code);
            if (validationError != null)
            {
                errors.Add($"Row {i + 1}: {validationError} — '{code}'");
                continue;
            }

            // Check within-batch uniqueness (before DB check to avoid
            // adding two identical entities that crash on SaveChangesAsync)
            if (!seenInBatch.Add(code))
            {
                duplicates++;
                continue;
            }

            // Check global uniqueness
            var exists = await _db.Codes.AnyAsync(c => c.CodeText == code);
            if (exists)
            {
                duplicates++;
                continue;
            }

            _db.Codes.Add(new Code
            {
                ProductId = productId,
                CodeText = code,
                Status = CodeStatus.Available,
                ImportOrder = ++maxOrder,
                ImportBatch = batchName,
                CreatedAt = DateTime.UtcNow
            });
            imported++;
        }

        await _db.SaveChangesAsync();

        sw.Stop();

        _logger.LogInformation("Import done: {Imported} imported, {Duplicates} duplicates, {Errors} errors",
            imported, duplicates, errors.Count);

        _logger.LogInformation("Import completed in {ElapsedMs}ms: {Imported} imported, {Duplicates} duplicates",
            sw.ElapsedMilliseconds, imported, duplicates);

        await _audit.LogAsync("import", productId: productId,
            details: new { batchName, imported, duplicates });

        var result = new CsvImportResult(imported, duplicates, errors);
        _logger.LogTrace("<- ImportCodesAsync = {{Imported={Imported}, Duplicates={Duplicates}, Errors={Errors}}}",
            result.Imported, result.Duplicates, result.Errors.Count);
        return result;
    }

    public async Task<List<Code>> ReserveCodesAsync(int productId, int quantity, int jobId)
    {
        _logger.LogTrace("-> ReserveCodesAsync(productId={ProductId}, quantity={Quantity}, jobId={JobId})",
            productId, quantity, jobId);
        var sw = Stopwatch.StartNew();

        var codes = await _db.Codes
            .Where(c => c.ProductId == productId && c.Status == CodeStatus.Available)
            .OrderBy(c => c.ImportOrder)
            .Take(quantity)
            .ToListAsync();

        if (codes.Count < quantity)
        {
            var ex = new InvalidOperationException(
                _loc.Format("Error_NotEnoughCodes", quantity, codes.Count));
            _logger.LogTrace("<- ReserveCodesAsync FAILED: {Error}", ex.Message);
            throw ex;
        }

        foreach (var code in codes)
        {
            code.Status = CodeStatus.Reserved;
            code.JobId = jobId;
            code.StatusChangedAt = DateTime.UtcNow;
        }

        await _db.SaveChangesAsync();

        sw.Stop();

        _logger.LogInformation("Reserved {Count} codes for Job {JobId} (Product {ProductId})",
            codes.Count, jobId, productId);

        _logger.LogInformation("ReserveCodes took {ElapsedMs}ms ({Count} codes for job {JobId})",
            sw.ElapsedMilliseconds, codes.Count, jobId);

        // Check remaining stock and raise warning if low
        var remaining = await _db.Codes
            .CountAsync(c => c.ProductId == productId && c.Status == CodeStatus.Available);
        if (remaining < LowStockThreshold && remaining >= 0)
        {
            _logger.LogWarning("Low stock: Product {ProductId} has {Remaining} codes remaining",
                productId, remaining);
            var product = await _db.ProductNodes.FindAsync(productId);
            var name = product?.Name ?? $"Product #{productId}";
            _alerts.Raise(AlertSeverity.Warning, _loc["Alert_CodePool"],
                _loc.Format("Alert_LowStock", name, remaining));
        }

        _logger.LogTrace("<- ReserveCodesAsync = {Count} codes reserved", codes.Count);
        return codes;
    }

    public async Task ReturnCodesToPoolAsync(int jobId, int startIndex, int count)
    {
        _logger.LogTrace("-> ReturnCodesToPoolAsync(jobId={JobId}, startIndex={StartIndex}, count={Count})",
            jobId, startIndex, count);

        _logger.LogInformation("Returning {Count} codes to pool: Job {JobId} (startIndex={Start})", count, jobId, startIndex);
        // Take from the front of the remaining Reserved set — previous operations
        // (MarkCodesPrinted, BurnCode) already removed earlier codes from Reserved.
        var codes = await _db.Codes
            .Where(c => c.JobId == jobId && c.Status == CodeStatus.Reserved)
            .OrderBy(c => c.ImportOrder)
            .Take(count)
            .ToListAsync();

        foreach (var code in codes)
        {
            code.Status = CodeStatus.Available;
            code.JobId = null;
            code.StatusChangedAt = DateTime.UtcNow;
        }

        await _db.SaveChangesAsync();

        _logger.LogTrace("<- ReturnCodesToPoolAsync = {Returned} codes returned to pool", codes.Count);
    }

    public async Task MarkCodesPrintedAsync(int jobId, int fromIndex, int toIndex)
    {
        _logger.LogTrace("-> MarkCodesPrintedAsync(jobId={JobId}, fromIndex={FromIndex}, toIndex={ToIndex})",
            jobId, fromIndex, toIndex);
        var sw = Stopwatch.StartNew();

        var count = toIndex - fromIndex;
        _logger.LogDebug("Codes printed: Job {JobId} [{From}..{To}) count={Count}", jobId, fromIndex, toIndex, count);
        // Take from the front of the remaining Reserved set.
        // Previous calls already moved earlier codes out of Reserved, so the
        // first N codes in this filtered set ARE the next ones to mark.
        var codes = await _db.Codes
            .Where(c => c.JobId == jobId && c.Status == CodeStatus.Reserved)
            .OrderBy(c => c.ImportOrder)
            .Take(count)
            .ToListAsync();

        if (codes.Count < count)
        {
            _logger.LogCritical(
                "MarkCodesPrintedAsync INTEGRITY VIOLATION: Job {JobId} expected {Expected} Reserved codes " +
                "but found only {Actual}. Data may be corrupt.",
                jobId, count, codes.Count);
            throw new InvalidOperationException(
                $"Code count mismatch for Job {jobId}: expected {count} Reserved codes, found {codes.Count}. " +
                $"This indicates data corruption — halting to prevent further damage.");
        }

        foreach (var code in codes)
        {
            code.Status = CodeStatus.Printed;
            code.StatusChangedAt = DateTime.UtcNow;
        }

        await _db.SaveChangesAsync();

        sw.Stop();
        _logger.LogTrace("<- MarkCodesPrintedAsync = {Marked} codes marked printed in {ElapsedMs}ms",
            codes.Count, sw.ElapsedMilliseconds);
    }

    public async Task BurnCodeAsync(int jobId, int index)
    {
        _logger.LogTrace("-> BurnCodeAsync(jobId={JobId}, index={Index})", jobId, index);

        _logger.LogWarning("Code burned: Job {JobId} index={Index}", jobId, index);
        var code = await _db.Codes
            .Where(c => c.JobId == jobId && c.Status == CodeStatus.Reserved)
            .OrderBy(c => c.ImportOrder)
            .Skip(index)
            .FirstOrDefaultAsync();

        if (code != null)
        {
            code.Status = CodeStatus.Burned;
            code.StatusChangedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();
        }

        _logger.LogTrace("<- BurnCodeAsync = code {Result}", code != null ? "burned" : "not found");
    }

    public async Task QuarantineCodeAsync(int jobId, int index)
    {
        _logger.LogTrace("-> QuarantineCodeAsync(jobId={JobId}, index={Index})", jobId, index);

        _logger.LogWarning("Code quarantined: Job {JobId} index={Index}", jobId, index);
        var code = await _db.Codes
            .Where(c => c.JobId == jobId && c.Status == CodeStatus.Reserved)
            .OrderBy(c => c.ImportOrder)
            .Skip(index)
            .FirstOrDefaultAsync();

        if (code != null)
        {
            code.Status = CodeStatus.Quarantined;
            code.StatusChangedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();
        }

        _logger.LogTrace("<- QuarantineCodeAsync = code {Result}", code != null ? "quarantined" : "not found");
    }

    public async Task QuarantineCodesAsync(int jobId, int fromIndex, int count)
    {
        _logger.LogTrace("-> QuarantineCodesAsync(jobId={JobId}, fromIndex={FromIndex}, count={Count})",
            jobId, fromIndex, count);

        if (count <= 0)
        {
            _logger.LogTrace("<- QuarantineCodesAsync = skipped (count <= 0)");
            return;
        }
        _logger.LogWarning("Codes quarantined: Job {JobId} fromIndex={From} count={Count}", jobId, fromIndex, count);

        var codes = await _db.Codes
            .Where(c => c.JobId == jobId && c.Status == CodeStatus.Reserved)
            .OrderBy(c => c.ImportOrder)
            .Take(count)
            .ToListAsync();

        foreach (var code in codes)
        {
            code.Status = CodeStatus.Quarantined;
            code.StatusChangedAt = DateTime.UtcNow;
        }

        await _db.SaveChangesAsync();

        _logger.LogTrace("<- QuarantineCodesAsync = {Quarantined} codes quarantined", codes.Count);
    }

    public async Task<int> GetAvailableCountAsync(int productId)
    {
        _logger.LogTrace("-> GetAvailableCountAsync(productId={ProductId})", productId);

        var result = await _db.Codes
            .CountAsync(c => c.ProductId == productId && c.Status == CodeStatus.Available);

        _logger.LogTrace("<- GetAvailableCountAsync = {Result}", result);
        return result;
    }

    public async Task<(int Available, int Total)> GetCodeCountsAsync(int productId)
    {
        _logger.LogTrace("-> GetCodeCountsAsync(productId={ProductId})", productId);

        var available = await _db.Codes
            .CountAsync(c => c.ProductId == productId && c.Status == CodeStatus.Available);
        var total = await _db.Codes
            .CountAsync(c => c.ProductId == productId);

        _logger.LogTrace("<- GetCodeCountsAsync = (Available={Available}, Total={Total})", available, total);
        return (available, total);
    }

    public async Task<Dictionary<CodeStatus, int>> GetPoolStatsAsync(int productId)
    {
        _logger.LogTrace("-> GetPoolStatsAsync(productId={ProductId})", productId);

        var result = await _db.Codes
            .Where(c => c.ProductId == productId)
            .GroupBy(c => c.Status)
            .ToDictionaryAsync(g => g.Key, g => g.Count());

        _logger.LogTrace("<- GetPoolStatsAsync = {Count} status groups", result.Count);
        return result;
    }
}
