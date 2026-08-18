using CodePrintManager.Domain.Entities;

namespace CodePrintManager.Domain.Interfaces;

public interface IProductService
{
    Task<List<ProductNode>> GetTreeAsync();
    Task<List<ProductNode>> GetRootsAsync();
    Task<ProductNode?> GetByIdAsync(int id);
    Task<ProductNode> CreateFolderAsync(string name, int? parentId);
    Task<ProductNode> CreateProductAsync(string name, int? parentId, string templateFile, string printerCsvName);
    Task UpdateAsync(ProductNode product);
    Task<bool> CanDeleteAsync(int id);
    Task<int> GetCodeCountAsync(int productId);
    Task DeleteAsync(int id);
}
