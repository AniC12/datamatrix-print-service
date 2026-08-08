using System.Collections.ObjectModel;
using CodePrintManager.Domain.Entities;
using CodePrintManager.Domain.Interfaces;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CodePrintManager.Desktop.ViewModels;

public partial class ProductsViewModel : ObservableObject
{
    private readonly IProductService _productService;
    private readonly ICodePoolService _codePoolService;

    public ObservableCollection<ProductNode> Products { get; } = new();

    [ObservableProperty]
    private ProductNode? _selectedProduct;

    [ObservableProperty]
    private int _availableCodesCount;

    [ObservableProperty]
    private int _totalCodesCount;

    public ProductsViewModel(IProductService productService, ICodePoolService codePoolService)
    {
        _productService = productService;
        _codePoolService = codePoolService;
    }

    [RelayCommand]
    private async Task LoadProductsAsync()
    {
        var roots = await _productService.GetRootsAsync();
        Products.Clear();
        foreach (var root in roots)
            Products.Add(root);
    }

    [RelayCommand]
    private async Task AddProductAsync(string name)
    {
        var parentId = SelectedProduct?.Id;
        await _productService.CreateFolderAsync(name, parentId);
        await LoadProductsAsync();
    }

    [RelayCommand]
    private async Task DeleteProductAsync()
    {
        if (SelectedProduct == null) return;
        await _productService.DeleteAsync(SelectedProduct.Id);
        SelectedProduct = null;
        await LoadProductsAsync();
    }

    [RelayCommand]
    private async Task ImportCsvAsync(string filePath)
    {
        if (SelectedProduct == null) return;
        var lines = await System.IO.File.ReadAllLinesAsync(filePath);
        var codes = lines.Where(l => !string.IsNullOrWhiteSpace(l)).ToList();
        var batchName = System.IO.Path.GetFileNameWithoutExtension(filePath);
        await _codePoolService.ImportCodesAsync(SelectedProduct.Id, batchName, codes);
        await RefreshCodeCountsAsync();
    }

    partial void OnSelectedProductChanged(ProductNode? value)
    {
        _ = RefreshCodeCountsAsync();
    }

    private async Task RefreshCodeCountsAsync()
    {
        if (SelectedProduct == null)
        {
            AvailableCodesCount = 0;
            TotalCodesCount = 0;
            return;
        }
        var counts = await _codePoolService.GetCodeCountsAsync(SelectedProduct.Id);
        AvailableCodesCount = counts.Available;
        TotalCodesCount = counts.Total;
    }
}
