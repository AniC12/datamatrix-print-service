using System.Collections.ObjectModel;
using CodePrintManager.Data;
using CodePrintManager.Domain.Entities;
using CodePrintManager.Domain.Enums;
using CodePrintManager.Domain.Interfaces;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;

namespace CodePrintManager.Desktop.ViewModels;

public partial class ProductsViewModel : ObservableObject
{
    private readonly IProductService _productService;
    private readonly ICodePoolService _codePoolService;
    private readonly AppDbContext _db;

    public ObservableCollection<ProductNode> Products { get; } = new();
    public ObservableCollection<ImportHistoryItem> ImportHistory { get; } = new();

    [ObservableProperty]
    private ProductNode? _selectedProduct;

    [ObservableProperty]
    private int _availableCodesCount;

    [ObservableProperty]
    private int _printedCodesCount;

    [ObservableProperty]
    private int _burnedCodesCount;

    [ObservableProperty]
    private int _totalCodesCount;

    // Add folder/product dialog fields
    [ObservableProperty]
    private string _newNodeName = string.Empty;

    [ObservableProperty]
    private string _newProductTemplate = string.Empty;

    [ObservableProperty]
    private string _newProductCsvName = string.Empty;

    [ObservableProperty]
    private bool _isAddingFolder;

    [ObservableProperty]
    private bool _isAddingProduct;

    [ObservableProperty]
    private bool _canDeleteSelectedProduct;

    [ObservableProperty]
    private string _deleteBlockedReason = string.Empty;

    public event EventHandler<int>? NavigateToNewJobRequested;

    public ProductsViewModel(IProductService productService, ICodePoolService codePoolService, AppDbContext db)
    {
        _productService = productService;
        _codePoolService = codePoolService;
        _db = db;
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
    private void ShowAddFolder()
    {
        IsAddingFolder = true;
        IsAddingProduct = false;
        NewNodeName = string.Empty;
    }

    [RelayCommand]
    private void ShowAddProduct()
    {
        IsAddingProduct = true;
        IsAddingFolder = false;
        NewNodeName = string.Empty;
        NewProductTemplate = string.Empty;
        NewProductCsvName = string.Empty;
    }

    [RelayCommand]
    private void CancelAdd()
    {
        IsAddingFolder = false;
        IsAddingProduct = false;
    }

    [RelayCommand]
    private async Task ConfirmAddFolderAsync()
    {
        if (string.IsNullOrWhiteSpace(NewNodeName)) return;
        var parentId = SelectedProduct?.IsLeaf == false ? SelectedProduct.Id : SelectedProduct?.ParentId;
        await _productService.CreateFolderAsync(NewNodeName.Trim(), parentId);
        IsAddingFolder = false;
        NewNodeName = string.Empty;
        await LoadProductsAsync();
    }

    [RelayCommand]
    private void BrowseNewTemplate()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "Template Files|*.rox|All Files|*.*",
            Title = "Select Template File (.rox)"
        };
        if (dialog.ShowDialog() == true)
            NewProductTemplate = dialog.FileName;
    }

    [RelayCommand]
    private async Task ConfirmAddProductAsync()
    {
        if (string.IsNullOrWhiteSpace(NewNodeName)) return;
        var parentId = SelectedProduct?.IsLeaf == false ? SelectedProduct.Id : SelectedProduct?.ParentId;
        await _productService.CreateProductAsync(
            NewNodeName.Trim(), parentId,
            NewProductTemplate.Trim(), NewProductCsvName.Trim());
        IsAddingProduct = false;
        NewNodeName = string.Empty;
        NewProductTemplate = string.Empty;
        NewProductCsvName = string.Empty;
        await LoadProductsAsync();
    }

    [RelayCommand]
    private async Task DeleteProductAsync()
    {
        if (SelectedProduct == null) return;

        var result = System.Windows.MessageBox.Show(
            $"Are you sure you want to delete \"{SelectedProduct.Name}\"?\n\nThis action cannot be undone.",
            "Confirm Delete",
            System.Windows.MessageBoxButton.YesNo,
            System.Windows.MessageBoxImage.Warning);

        if (result != System.Windows.MessageBoxResult.Yes) return;

        try
        {
            await _productService.DeleteAsync(SelectedProduct.Id);
            SelectedProduct = null;
            await LoadProductsAsync();
        }
        catch (InvalidOperationException ex)
        {
            System.Windows.MessageBox.Show(
                ex.Message,
                "Cannot Delete",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Error);
        }
    }

    [RelayCommand]
    private async Task ImportCsvAsync()
    {
        if (SelectedProduct == null || !SelectedProduct.IsLeaf) return;

        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "CSV Files|*.csv|All Files|*.*",
            Title = "Import Codes CSV"
        };

        if (dialog.ShowDialog() != true) return;

        var filePath = dialog.FileName;
        var lines = await System.IO.File.ReadAllLinesAsync(filePath);
        var codes = lines.Where(l => !string.IsNullOrWhiteSpace(l)).ToList();
        var batchName = System.IO.Path.GetFileNameWithoutExtension(filePath);
        await _codePoolService.ImportCodesAsync(SelectedProduct.Id, batchName, codes);
        await RefreshCodeCountsAsync();
        await LoadImportHistoryAsync();
    }

    [RelayCommand]
    private async Task ChangeTemplateAsync()
    {
        if (SelectedProduct == null || !SelectedProduct.IsLeaf) return;

        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "Template Files|*.rox|All Files|*.*",
            Title = "Select Template File (.rox)"
        };

        if (dialog.ShowDialog() != true) return;

        SelectedProduct.TemplateFile = dialog.FileName;
        await _db.SaveChangesAsync();
        OnPropertyChanged(nameof(SelectedProduct));
    }

    [RelayCommand]
    private async Task SaveCsvNameAsync()
    {
        if (SelectedProduct == null || !SelectedProduct.IsLeaf) return;
        await _db.SaveChangesAsync();
    }

    [RelayCommand]
    private void NewJob()
    {
        if (SelectedProduct?.IsLeaf == true)
            NavigateToNewJobRequested?.Invoke(this, SelectedProduct.Id);
    }

    partial void OnSelectedProductChanged(ProductNode? value)
    {
        _ = RefreshCodeCountsAsync();
        _ = LoadImportHistoryAsync();
        _ = CheckCanDeleteAsync();
    }

    private async Task CheckCanDeleteAsync()
    {
        if (SelectedProduct == null)
        {
            CanDeleteSelectedProduct = false;
            DeleteBlockedReason = string.Empty;
            return;
        }

        var canDelete = await _productService.CanDeleteAsync(SelectedProduct.Id);
        CanDeleteSelectedProduct = canDelete;
        DeleteBlockedReason = canDelete ? string.Empty : "Has active jobs or reserved codes";
    }

    private async Task RefreshCodeCountsAsync()
    {
        if (SelectedProduct == null || !SelectedProduct.IsLeaf)
        {
            AvailableCodesCount = 0;
            PrintedCodesCount = 0;
            BurnedCodesCount = 0;
            TotalCodesCount = 0;
            return;
        }

        var stats = await _codePoolService.GetPoolStatsAsync(SelectedProduct.Id);
        AvailableCodesCount = stats.GetValueOrDefault(CodeStatus.Available, 0);
        PrintedCodesCount = stats.GetValueOrDefault(CodeStatus.Printed, 0);
        BurnedCodesCount = stats.GetValueOrDefault(CodeStatus.Burned, 0);
        TotalCodesCount = stats.Values.Sum();
    }

    private async Task LoadImportHistoryAsync()
    {
        ImportHistory.Clear();
        if (SelectedProduct == null || !SelectedProduct.IsLeaf) return;

        var imports = await _db.AuditLog
            .Where(a => a.EventType == "import" && a.ProductId == SelectedProduct.Id)
            .OrderByDescending(a => a.CreatedAt)
            .Take(20)
            .ToListAsync();

        foreach (var entry in imports)
            ImportHistory.Add(new ImportHistoryItem(entry));
    }
}

public class ImportHistoryItem
{
    public string Date { get; }
    public string Details { get; }

    public ImportHistoryItem(AuditEntry entry)
    {
        Date = entry.CreatedAt.ToLocalTime().ToString("yyyy-MM-dd");
        Details = entry.Details ?? "imported codes";
    }
}
