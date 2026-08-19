using CodePrintManager.Data;
using CodePrintManager.Domain.Entities;
using CodePrintManager.Domain.Enums;
using CodePrintManager.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace CodePrintManager.Application.Services;

public class ProductService : IProductService
{
    private readonly AppDbContext _db;
    private readonly ILogger<ProductService> _logger;
    private readonly ILocalizationService _loc;

    public ProductService(AppDbContext db, ILogger<ProductService> logger, ILocalizationService loc)
    {
        _db = db;
        _logger = logger;
        _loc = loc;
    }

    public async Task<List<ProductNode>> GetTreeAsync()
    {
        _logger.LogTrace("-> GetTreeAsync()");
        var result = await _db.ProductNodes
            .Include(n => n.Children)
            .Where(n => n.ParentId == null)
            .OrderBy(n => n.Name)
            .ToListAsync();
        _logger.LogTrace("<- GetTreeAsync = {Count} roots", result.Count);
        return result;
    }

    public async Task<List<ProductNode>> GetRootsAsync()
    {
        _logger.LogTrace("-> GetRootsAsync()");
        var result = await _db.ProductNodes
            .Include(n => n.Children)
            .Where(n => n.ParentId == null)
            .OrderBy(n => n.Name)
            .ToListAsync();
        _logger.LogTrace("<- GetRootsAsync = {Count} roots", result.Count);
        return result;
    }

    public async Task<ProductNode?> GetByIdAsync(int id)
    {
        _logger.LogTrace("-> GetByIdAsync(id={Id})", id);
        var result = await _db.ProductNodes.FindAsync(id);
        _logger.LogTrace("<- GetByIdAsync = {Result}", result != null ? $"ProductNode(Id={result.Id}, Name={result.Name})" : "null");
        return result;
    }

    public async Task<ProductNode> CreateFolderAsync(string name, int? parentId)
    {
        _logger.LogTrace("-> CreateFolderAsync(name={Name}, parentId={ParentId})", name, parentId);
        var node = new ProductNode
        {
            Name = name,
            ParentId = parentId,
            IsLeaf = false,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        _db.ProductNodes.Add(node);
        await _db.SaveChangesAsync();
        _logger.LogInformation("Folder created: '{Name}' (Id={Id}, Parent={ParentId})", name, node.Id, parentId);
        _logger.LogTrace("<- CreateFolderAsync = ProductNode(Id={Id}, Name={Name})", node.Id, node.Name);
        return node;
    }

    public async Task<ProductNode> CreateProductAsync(string name, int? parentId, string templateFile, string printerCsvName)
    {
        _logger.LogTrace("-> CreateProductAsync(name={Name}, parentId={ParentId}, templateFile={TemplateFile}, printerCsvName={PrinterCsvName})", name, parentId, templateFile, printerCsvName);
        var node = new ProductNode
        {
            Name = name,
            ParentId = parentId,
            IsLeaf = true,
            TemplateFile = templateFile,
            PrinterCsvName = printerCsvName,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        _db.ProductNodes.Add(node);
        await _db.SaveChangesAsync();
        _logger.LogInformation("Product created: '{Name}' (Id={Id}, Template={Template}, CSV={Csv})",
            name, node.Id, templateFile, printerCsvName);
        _logger.LogTrace("<- CreateProductAsync = ProductNode(Id={Id}, Name={Name})", node.Id, node.Name);
        return node;
    }

    public async Task UpdateAsync(ProductNode product)
    {
        _logger.LogTrace("-> UpdateAsync(product.Id={Id}, product.Name={Name})", product.Id, product.Name);
        product.UpdatedAt = DateTime.UtcNow;
        _db.ProductNodes.Update(product);
        await _db.SaveChangesAsync();
        _logger.LogInformation("Product updated: Id={Id}", product.Id);
        _logger.LogTrace("<- UpdateAsync");
    }

    public async Task<bool> CanDeleteAsync(int id)
    {
        _logger.LogTrace("-> CanDeleteAsync(id={Id})", id);
        // Check for active jobs on this product
        var hasActiveJobs = await _db.PrintJobs
            .AnyAsync(j => j.ProductId == id &&
                (j.Status == JobStatus.Preparing || j.Status == JobStatus.Ready || j.Status == JobStatus.Printing || j.Status == JobStatus.Paused));
        if (hasActiveJobs)
        {
            _logger.LogDebug("Product {Id} cannot be deleted: has active jobs", id);
            _logger.LogTrace("<- CanDeleteAsync = false (active jobs)");
            return false;
        }

        // Check for reserved codes
        var hasReservedCodes = await _db.Codes
            .AnyAsync(c => c.ProductId == id && c.Status == CodeStatus.Reserved);
        if (hasReservedCodes)
        {
            _logger.LogDebug("Product {Id} cannot be deleted: has reserved codes", id);
            _logger.LogTrace("<- CanDeleteAsync = false (reserved codes)");
            return false;
        }

        _logger.LogTrace("<- CanDeleteAsync = true");
        return true;
    }

    public async Task<int> GetCodeCountAsync(int productId)
    {
        _logger.LogTrace("-> GetCodeCountAsync(productId={ProductId})", productId);
        var result = await _db.Codes.CountAsync(c => c.ProductId == productId);
        _logger.LogTrace("<- GetCodeCountAsync = {Count}", result);
        return result;
    }

    public async Task DeleteAsync(int id)
    {
        _logger.LogTrace("-> DeleteAsync(id={Id})", id);
        if (!await CanDeleteAsync(id))
        {
            _logger.LogTrace("<- DeleteAsync FAILED: cannot delete product {Id}", id);
            throw new InvalidOperationException(_loc["Error_CannotDeleteProduct"]);
        }

        var node = await _db.ProductNodes.FindAsync(id);
        if (node != null)
        {
            _db.ProductNodes.Remove(node);
            await _db.SaveChangesAsync();
            _logger.LogInformation("Product deleted: Id={Id}", id);
        }
        _logger.LogTrace("<- DeleteAsync");
    }
}
