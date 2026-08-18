using System.Collections.ObjectModel;
using CodePrintManager.Data;
using CodePrintManager.Domain.Entities;
using CodePrintManager.Domain.Enums;
using CodePrintManager.Domain.Interfaces;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace CodePrintManager.Desktop.ViewModels;

public partial class ProductsViewModel : ObservableObject
{
    private readonly IProductService _productService;
    private readonly ICodePoolService _codePoolService;
    private readonly ICodeManagementService _codeManagement;
    private readonly AppDbContext _db;
    private readonly ILogger<ProductsViewModel> _logger;

    public CodesTabViewModel CodesTab { get; }

    public ObservableCollection<ProductNode> Products { get; } = new();
    public ObservableCollection<ActivityHistoryItem> ActivityHistory { get; } = new();

    [ObservableProperty]
    private ProductNode? _selectedProduct;

    [ObservableProperty]
    private int _availableCodesCount;

    [ObservableProperty]
    private int _printedCodesCount;

    [ObservableProperty]
    private int _burnedCodesCount;

    [ObservableProperty]
    private int _quarantinedCodesCount;

    [ObservableProperty]
    private int _totalCodesCount;

    [ObservableProperty]
    private int _unassignedCodesCount;

    [ObservableProperty]
    private bool _isShowingUnassigned;

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

    /// <summary>Hint text showing where new nodes will be added.</summary>
    [ObservableProperty]
    private string _addTargetHint = "Root";

    /// <summary>True when the selected leaf has available codes to print.</summary>
    [ObservableProperty]
    private bool _canCreateNewJob;

    // Rename fields
    [ObservableProperty]
    private string _editName = string.Empty;

    [ObservableProperty]
    private bool _isRenaming;

    public event EventHandler<int>? NavigateToNewJobRequested;

    public ProductsViewModel(IProductService productService, ICodePoolService codePoolService,
        ICodeManagementService codeManagement, AppDbContext db,
        CodesTabViewModel codesTab, ILogger<ProductsViewModel> logger)
    {
        _productService = productService;
        _codePoolService = codePoolService;
        _codeManagement = codeManagement;
        _db = db;
        _logger = logger;
        CodesTab = codesTab;
        CodesTab.CodesChanged += async (_, _) =>
        {
            await RefreshCodeCountsAsync();
            await RefreshUnassignedCountAsync();
        };
    }

    [RelayCommand]
    private async Task LoadProductsAsync()
    {
        var roots = await _productService.GetRootsAsync();
        Products.Clear();
        foreach (var root in roots)
            Products.Add(root);
        _logger.LogInformation("Products page loaded: {Count} root nodes", roots.Count);
        await RefreshUnassignedCountAsync();
    }

    [RelayCommand]
    private void ShowAddFolder()
    {
        _logger.LogInformation("Products: Add Folder form opened");
        IsAddingFolder = true;
        IsAddingProduct = false;
        NewNodeName = string.Empty;
    }

    [RelayCommand]
    private void ShowAddProduct()
    {
        _logger.LogInformation("Products: Add Product form opened");
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
        _logger.LogInformation("Products: Creating folder '{Name}' under Parent={ParentId}", NewNodeName.Trim(), parentId);
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
        _logger.LogInformation("Products: Creating product '{Name}' (Template={Template}, CSV={Csv})",
            NewNodeName.Trim(), NewProductTemplate.Trim(), NewProductCsvName.Trim());
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

        var productId = SelectedProduct.Id;
        var productName = SelectedProduct.Name;

        try
        {
            var codeCount = await _productService.GetCodeCountAsync(productId);

            if (codeCount == 0)
            {
                // Simple confirmation for empty product
                var result = System.Windows.MessageBox.Show(
                    $"Are you sure you want to delete \"{productName}\"?\n\nThis action cannot be undone.",
                    "Confirm Delete",
                    System.Windows.MessageBoxButton.YesNo,
                    System.Windows.MessageBoxImage.Warning);

                if (result != System.Windows.MessageBoxResult.Yes) return;
            }
            else
            {
                // Three-button dialog for products with codes
                var result = System.Windows.MessageBox.Show(
                    $"Delete \"{productName}\"?\n\n" +
                    $"This product has {codeCount:N0} codes.\n\n" +
                    "• Yes = Keep Codes (move to Unassigned pool)\n" +
                    "• No = Delete Codes Too (archive them)\n" +
                    "• Cancel = Don't delete",
                    $"Delete \"{productName}\"",
                    System.Windows.MessageBoxButton.YesNoCancel,
                    System.Windows.MessageBoxImage.Warning);

                if (result == System.Windows.MessageBoxResult.Cancel) return;

                if (result == System.Windows.MessageBoxResult.Yes)
                {
                    // Keep codes — move to unassigned pool
                    _logger.LogInformation("Products: Keeping {Count} codes as unassigned before deleting Id={Id}", codeCount, productId);
                    await _codeManagement.UnassignCodesAsync(productId);
                }
                else // No = Delete codes too
                {
                    // Archive all codes
                    _logger.LogInformation("Products: Archiving {Count} codes before deleting Id={Id}", codeCount, productId);
                    await _codeManagement.ArchiveCodesBulkAsync(productId, default, "product_deleted");
                }
            }

            _logger.LogInformation("Products: Deleting Id={Id}", productId);
            await _productService.DeleteAsync(productId);
            SelectedProduct = null;
            await LoadProductsAsync();
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogError(ex, "Products: Delete failed for Id={Id}", SelectedProduct?.Id);
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
        _logger.LogInformation("Products: Importing CSV '{FileName}' ({LineCount} lines) for Product {Id}",
            System.IO.Path.GetFileName(filePath), codes.Count, SelectedProduct.Id);
        await _codePoolService.ImportCodesAsync(SelectedProduct.Id, batchName, codes);
        _logger.LogInformation("Products: Import complete for Product {Id}", SelectedProduct.Id);
        await RefreshCodeCountsAsync();
        await LoadActivityHistoryAsync();
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
        if (SelectedProduct?.IsLeaf == true && CanCreateNewJob)
            NavigateToNewJobRequested?.Invoke(this, SelectedProduct.Id);
    }

    [RelayCommand]
    private void ShowRename()
    {
        if (SelectedProduct == null) return;
        EditName = SelectedProduct.Name;
        IsRenaming = true;
        _logger.LogInformation("Products: Rename started for '{Name}' (Id={Id})", SelectedProduct.Name, SelectedProduct.Id);
    }

    [RelayCommand]
    private void CancelRename()
    {
        IsRenaming = false;
        EditName = string.Empty;
    }

    [RelayCommand]
    private async Task ConfirmRenameAsync()
    {
        if (SelectedProduct == null || string.IsNullOrWhiteSpace(EditName)) return;

        var trimmed = EditName.Trim();
        if (trimmed == SelectedProduct.Name)
        {
            IsRenaming = false;
            return;
        }

        _logger.LogInformation("Products: Renaming Id={Id} from '{Old}' to '{New}'",
            SelectedProduct.Id, SelectedProduct.Name, trimmed);
        SelectedProduct.Name = trimmed;
        SelectedProduct.UpdatedAt = DateTime.UtcNow;
        await _productService.UpdateAsync(SelectedProduct);
        IsRenaming = false;
        EditName = string.Empty;
        OnPropertyChanged(nameof(SelectedProduct));
        await LoadProductsAsync();
    }

    partial void OnSelectedProductChanged(ProductNode? value)
    {
        if (value != null)
            _logger.LogInformation("Product selected: '{Name}' (Id={Id}, IsLeaf={IsLeaf})", value.Name, value.Id, value.IsLeaf);

        // Clear unassigned mode when a product is selected
        IsShowingUnassigned = false;

        // Update the "adding to" hint
        if (value == null)
            AddTargetHint = "Root";
        else if (!value.IsLeaf)
            AddTargetHint = value.Name;
        else
            AddTargetHint = value.Parent?.Name ?? "Root";

        // Close any open rename form on selection change
        IsRenaming = false;
        EditName = string.Empty;

        _ = RefreshCodeCountsAsync();
        _ = LoadActivityHistoryAsync();
        _ = CheckCanDeleteAsync();

        // Load codes tab for leaf products
        if (value?.IsLeaf == true)
            _ = CodesTab.LoadForProductAsync(value.Id);
    }

    [RelayCommand]
    private async Task ShowUnassignedCodesAsync()
    {
        SelectedProduct = null;
        IsShowingUnassigned = true;
        await CodesTab.LoadForProductAsync(null);
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
            QuarantinedCodesCount = 0;
            TotalCodesCount = 0;
            CanCreateNewJob = false;
            return;
        }

        var stats = await _codePoolService.GetPoolStatsAsync(SelectedProduct.Id);
        AvailableCodesCount = stats.GetValueOrDefault(CodeStatus.Available, 0);
        PrintedCodesCount = stats.GetValueOrDefault(CodeStatus.Printed, 0);
        BurnedCodesCount = stats.GetValueOrDefault(CodeStatus.Burned, 0);
        QuarantinedCodesCount = stats.GetValueOrDefault(CodeStatus.Quarantined, 0);
        TotalCodesCount = stats.Values.Sum();
        CanCreateNewJob = AvailableCodesCount > 0;
        _logger.LogInformation("Product '{Name}' pool: Available={Avail}, Printed={Printed}, Burned={Burned}, Quarantined={Quarantined}, Total={Total}",
            SelectedProduct.Name, AvailableCodesCount, PrintedCodesCount, BurnedCodesCount, QuarantinedCodesCount, TotalCodesCount);
    }

    private async Task RefreshUnassignedCountAsync()
    {
        UnassignedCodesCount = await _codeManagement.GetUnassignedCountAsync();
    }

    private async Task LoadActivityHistoryAsync()
    {
        ActivityHistory.Clear();
        if (SelectedProduct == null || !SelectedProduct.IsLeaf) return;

        // Import events from audit log
        var imports = await _db.AuditLog
            .Where(a => a.EventType == "import" && a.ProductId == SelectedProduct.Id)
            .OrderByDescending(a => a.CreatedAt)
            .Take(20)
            .Select(a => new ActivityHistoryItem
            {
                Date = a.CreatedAt,
                Type = ActivityType.Import,
                Description = a.Details ?? "Imported codes"
            })
            .ToListAsync();

        // Jobs that actually printed at least one code
        var jobs = await _db.PrintJobs
            .Where(j => j.ProductId == SelectedProduct.Id &&
                (j.Status == JobStatus.Completed || j.Status == JobStatus.Cancelled || j.Status == JobStatus.Error) &&
                j.CodesConfirmed > 0)
            .OrderByDescending(j => j.CompletedAt ?? j.CreatedAt)
            .Take(20)
            .Select(j => new ActivityHistoryItem
            {
                Date = j.CompletedAt ?? j.CreatedAt,
                Type = j.Status == JobStatus.Completed ? ActivityType.JobCompleted :
                       j.Status == JobStatus.Cancelled ? ActivityType.JobCancelled :
                       ActivityType.JobError,
                Description = $"Job #{j.Id} {j.Status.ToString().ToLower()} \u2014 {j.CodesConfirmed}/{j.Quantity} printed"
            })
            .ToListAsync();

        // Merge and sort by date descending, take 20
        var merged = imports.Concat(jobs)
            .OrderByDescending(h => h.Date)
            .Take(20);

        foreach (var item in merged)
            ActivityHistory.Add(item);
    }
}

public enum ActivityType { Import, JobCompleted, JobCancelled, JobError }

public class ActivityHistoryItem
{
    public DateTime Date { get; init; }
    public ActivityType Type { get; init; }
    public string Description { get; init; } = string.Empty;

    public string DateText => Date.ToLocalTime().ToString("MMM dd HH:mm");

    public System.Windows.Media.Brush TypeBrush => Type switch
    {
        ActivityType.Import => new System.Windows.Media.SolidColorBrush(
            (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#3182CE")),
        ActivityType.JobCompleted => new System.Windows.Media.SolidColorBrush(
            (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#38A169")),
        ActivityType.JobCancelled => new System.Windows.Media.SolidColorBrush(
            (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#DD6B20")),
        ActivityType.JobError => new System.Windows.Media.SolidColorBrush(
            (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#E53E3E")),
        _ => new System.Windows.Media.SolidColorBrush(
            (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#718096"))
    };
}
