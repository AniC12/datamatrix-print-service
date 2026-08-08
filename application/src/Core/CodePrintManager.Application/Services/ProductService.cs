using CodePrintManager.Data;
using CodePrintManager.Domain.Entities;
using CodePrintManager.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace CodePrintManager.Application.Services;

public class ProductService : IProductService
{
    private readonly AppDbContext _db;

    public ProductService(AppDbContext db)
    {
        _db = db;
    }

    public async Task<List<ProductNode>> GetTreeAsync()
    {
        return await _db.ProductNodes
            .Include(n => n.Children)
            .Where(n => n.ParentId == null)
            .OrderBy(n => n.Name)
            .ToListAsync();
    }

    public async Task<List<ProductNode>> GetRootsAsync()
    {
        return await _db.ProductNodes
            .Include(n => n.Children)
            .Where(n => n.ParentId == null)
            .OrderBy(n => n.Name)
            .ToListAsync();
    }

    public async Task<ProductNode?> GetByIdAsync(int id)
    {
        return await _db.ProductNodes.FindAsync(id);
    }

    public async Task<ProductNode> CreateFolderAsync(string name, int? parentId)
    {
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
        return node;
    }

    public async Task<ProductNode> CreateProductAsync(string name, int? parentId, string templateFile, string printerCsvName)
    {
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
        return node;
    }

    public async Task UpdateAsync(ProductNode product)
    {
        product.UpdatedAt = DateTime.UtcNow;
        _db.ProductNodes.Update(product);
        await _db.SaveChangesAsync();
    }

    public async Task DeleteAsync(int id)
    {
        var node = await _db.ProductNodes.FindAsync(id);
        if (node != null)
        {
            _db.ProductNodes.Remove(node);
            await _db.SaveChangesAsync();
        }
    }
}
