using CodePrintManager.Data;
using CodePrintManager.Domain.Entities;
using CodePrintManager.Domain.Enums;
using CodePrintManager.Domain.Interfaces;
using CodePrintManager.Domain.Validation;
using Microsoft.EntityFrameworkCore;

namespace CodePrintManager.Application.Services;

public class CodePoolService : ICodePoolService
{
    private readonly AppDbContext _db;
    private readonly IAuditService _audit;

    public CodePoolService(AppDbContext db, IAuditService audit)
    {
        _db = db;
        _audit = audit;
    }

    public async Task<CsvImportResult> ImportCodesAsync(int productId, string batchName, IReadOnlyList<string> codes)
    {
        var errors = new List<string>();
        var duplicates = 0;
        var imported = 0;

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

        await _audit.LogAsync("import", productId: productId,
            details: new { batchName, imported, duplicates });

        return new CsvImportResult(imported, duplicates, errors);
    }

    public async Task<List<Code>> ReserveCodesAsync(int productId, int quantity, int jobId)
    {
        var codes = await _db.Codes
            .Where(c => c.ProductId == productId && c.Status == CodeStatus.Available)
            .OrderBy(c => c.ImportOrder)
            .Take(quantity)
            .ToListAsync();

        if (codes.Count < quantity)
            throw new InvalidOperationException(
                $"Not enough codes available. Requested: {quantity}, Available: {codes.Count}");

        foreach (var code in codes)
        {
            code.Status = CodeStatus.Reserved;
            code.JobId = jobId;
            code.StatusChangedAt = DateTime.UtcNow;
        }

        await _db.SaveChangesAsync();
        return codes;
    }

    public async Task ReturnCodesToPoolAsync(int jobId, int startIndex, int count)
    {
        var codes = await _db.Codes
            .Where(c => c.JobId == jobId && c.Status == CodeStatus.Reserved)
            .OrderBy(c => c.ImportOrder)
            .Skip(startIndex)
            .Take(count)
            .ToListAsync();

        foreach (var code in codes)
        {
            code.Status = CodeStatus.Available;
            code.JobId = null;
            code.StatusChangedAt = DateTime.UtcNow;
        }

        await _db.SaveChangesAsync();
    }

    public async Task MarkCodesPrintedAsync(int jobId, int fromIndex, int toIndex)
    {
        var codes = await _db.Codes
            .Where(c => c.JobId == jobId && c.Status == CodeStatus.Reserved)
            .OrderBy(c => c.ImportOrder)
            .Skip(fromIndex)
            .Take(toIndex - fromIndex)
            .ToListAsync();

        foreach (var code in codes)
        {
            code.Status = CodeStatus.Printed;
            code.StatusChangedAt = DateTime.UtcNow;
        }

        await _db.SaveChangesAsync();
    }

    public async Task BurnCodeAsync(int jobId, int index)
    {
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
    }

    public async Task<int> GetAvailableCountAsync(int productId)
    {
        return await _db.Codes
            .CountAsync(c => c.ProductId == productId && c.Status == CodeStatus.Available);
    }

    public async Task<(int Available, int Total)> GetCodeCountsAsync(int productId)
    {
        var available = await _db.Codes
            .CountAsync(c => c.ProductId == productId && c.Status == CodeStatus.Available);
        var total = await _db.Codes
            .CountAsync(c => c.ProductId == productId);
        return (available, total);
    }

    public async Task<Dictionary<CodeStatus, int>> GetPoolStatsAsync(int productId)
    {
        return await _db.Codes
            .Where(c => c.ProductId == productId)
            .GroupBy(c => c.Status)
            .ToDictionaryAsync(g => g.Key, g => g.Count());
    }
}
