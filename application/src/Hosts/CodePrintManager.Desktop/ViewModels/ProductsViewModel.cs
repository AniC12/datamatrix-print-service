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
    private readonly ILocalizationService _loc;

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
    private string _addTargetHint = string.Empty;

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
        CodesTabViewModel codesTab, ILogger<ProductsViewModel> logger,
        ILocalizationService loc)
    {
        _productService = productService;
        _codePoolService = codePoolService;
        _codeManagement = codeManagement;
        _db = db;
        _logger = logger;
        _loc = loc;
        _logger.LogTrace("-> ProductsViewModel()");
        _addTargetHint = _loc["Label_Root"];
        CodesTab = codesTab;
        CodesTab.CodesChanged += async (_, _) =>
        {
            _logger.LogTrace("-> CodesChanged event handler");
            await RefreshCodeCountsAsync();
            await RefreshUnassignedCountAsync();
            _logger.LogTrace("<- CodesChanged event handler");
        };
        _logger.LogTrace("<- ProductsViewModel()");
    }

    [RelayCommand]
    private async Task LoadProductsAsync()
    {
        _logger.LogTrace("-> LoadProductsAsync()");
        var roots = await _productService.GetRootsAsync();
        Products.Clear();
        foreach (var root in roots)
            Products.Add(root);
        _logger.LogInformation("Products page loaded: {Count} root nodes", roots.Count);
        await RefreshUnassignedCountAsync();
        _logger.LogTrace("<- LoadProductsAsync()");
    }

    [RelayCommand]
    private void ShowAddFolder()
    {
        _logger.LogTrace("-> ShowAddFolder()");
        _logger.LogInformation("Products: Add Folder form opened");
        IsAddingFolder = true;
        IsAddingProduct = false;
        NewNodeName = string.Empty;
        _logger.LogTrace("<- ShowAddFolder()");
    }

    [RelayCommand]
    private void ShowAddProduct()
    {
        _logger.LogTrace("-> ShowAddProduct()");
        _logger.LogInformation("Products: Add Product form opened");
        IsAddingProduct = true;
        IsAddingFolder = false;
        NewNodeName = string.Empty;
        NewProductTemplate = string.Empty;
        NewProductCsvName = string.Empty;
        _logger.LogTrace("<- ShowAddProduct()");
    }

    [RelayCommand]
    private void CancelAdd()
    {
        _logger.LogTrace("-> CancelAdd()");
        IsAddingFolder = false;
        IsAddingProduct = false;
        _logger.LogTrace("<- CancelAdd()");
    }

    [RelayCommand]
    private async Task ConfirmAddFolderAsync()
    {
        _logger.LogTrace("-> ConfirmAddFolderAsync()");
        if (string.IsNullOrWhiteSpace(NewNodeName))
        {
            _logger.LogTrace("<- ConfirmAddFolderAsync() [empty name]");
            return;
        }
        var parentId = SelectedProduct?.IsLeaf == false ? SelectedProduct.Id : SelectedProduct?.ParentId;
        _logger.LogInformation("Products: Creating folder '{Name}' under Parent={ParentId}", NewNodeName.Trim(), parentId);
        await _productService.CreateFolderAsync(NewNodeName.Trim(), parentId);
        IsAddingFolder = false;
        NewNodeName = string.Empty;
        await LoadProductsAsync();
        _logger.LogTrace("<- ConfirmAddFolderAsync()");
    }

    [RelayCommand]
    private void BrowseNewTemplate()
    {
        _logger.LogTrace("-> BrowseNewTemplate()");
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Filter = _loc["Filter_TemplateFiles"],
            Title = _loc["DialogTitle_SelectTemplate"]
        };
        if (dialog.ShowDialog() == true)
            NewProductTemplate = dialog.FileName;
        _logger.LogTrace("<- BrowseNewTemplate() Template={Template}", NewProductTemplate);
    }

    [RelayCommand]
    private async Task ConfirmAddProductAsync()
    {
        _logger.LogTrace("-> ConfirmAddProductAsync()");
        if (string.IsNullOrWhiteSpace(NewNodeName))
        {
            _logger.LogTrace("<- ConfirmAddProductAsync() [empty name]");
            return;
        }
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
        _logger.LogTrace("<- ConfirmAddProductAsync()");
    }

    [RelayCommand]
    private async Task DeleteProductAsync()
    {
        _logger.LogTrace("-> DeleteProductAsync()");
        if (SelectedProduct == null)
        {
            _logger.LogTrace("<- DeleteProductAsync() [no selection]");
            return;
        }

        var productId = SelectedProduct.Id;
        var productName = SelectedProduct.Name;

        try
        {
            var codeCount = await _productService.GetCodeCountAsync(productId);

            if (codeCount == 0)
            {
                // Simple confirmation for empty product
                var result = System.Windows.MessageBox.Show(
                    _loc.Format("Dialog_ConfirmDeleteProduct", productName),
                    _loc.Format("DialogTitle_DeleteProduct", productName),
                    System.Windows.MessageBoxButton.YesNo,
                    System.Windows.MessageBoxImage.Warning);

                if (result != System.Windows.MessageBoxResult.Yes)
                {
                    _logger.LogTrace("<- DeleteProductAsync() [cancelled by user]");
                    return;
                }
            }
            else
            {
                // Three-button dialog for products with codes
                var result = System.Windows.MessageBox.Show(
                    _loc.Format("Dialog_DeleteProductWithCodes", productName, codeCount),
                    _loc.Format("DialogTitle_DeleteProduct", productName),
                    System.Windows.MessageBoxButton.YesNoCancel,
                    System.Windows.MessageBoxImage.Warning);

                if (result == System.Windows.MessageBoxResult.Cancel)
                {
                    _logger.LogTrace("<- DeleteProductAsync() [cancelled by user]");
                    return;
                }

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
                _loc.Format("Error_CannotDeleteReason", productName, ex.Message),
                _loc["DialogTitle_CannotDelete"],
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Error);
        }
        _logger.LogTrace("<- DeleteProductAsync()");
    }

    [RelayCommand]
    private async Task DeleteFolderAsync()
    {
        _logger.LogTrace("-> DeleteFolderAsync()");
        if (SelectedProduct == null || SelectedProduct.IsLeaf)
        {
            _logger.LogTrace("<- DeleteFolderAsync() [no selection or is leaf]");
            return;
        }

        var folderName = SelectedProduct.Name;
        var folderId = SelectedProduct.Id;

        // Check if folder has children
        var hasChildren = await _db.ProductNodes.AnyAsync(n => n.ParentId == folderId);
        if (hasChildren)
        {
            System.Windows.MessageBox.Show(
                _loc.Format("Error_CannotDeleteNonEmptyFolder", folderName),
                _loc["DialogTitle_CannotDelete"],
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Error);
            _logger.LogTrace("<- DeleteFolderAsync() [folder not empty]");
            return;
        }

        var result = System.Windows.MessageBox.Show(
            _loc.Format("Dialog_ConfirmDeleteFolder", folderName),
            _loc.Format("DialogTitle_DeleteFolder", folderName),
            System.Windows.MessageBoxButton.YesNo,
            System.Windows.MessageBoxImage.Warning);

        if (result != System.Windows.MessageBoxResult.Yes)
        {
            _logger.LogTrace("<- DeleteFolderAsync() [cancelled by user]");
            return;
        }

        _logger.LogInformation("Products: Deleting folder Id={Id}", folderId);
        await _productService.DeleteAsync(folderId);
        SelectedProduct = null;
        await LoadProductsAsync();
        _logger.LogTrace("<- DeleteFolderAsync()");
    }

    [RelayCommand]
    private async Task ImportCsvAsync()
    {
        _logger.LogTrace("-> ImportCsvAsync()");
        if (SelectedProduct == null || !SelectedProduct.IsLeaf)
        {
            _logger.LogTrace("<- ImportCsvAsync() [no leaf selected]");
            return;
        }

        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Filter = _loc["Filter_CsvFiles"],
            Title = _loc["DialogTitle_ImportCodes"]
        };

        if (dialog.ShowDialog() != true)
        {
            _logger.LogTrace("<- ImportCsvAsync() [dialog cancelled]");
            return;
        }

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
        _logger.LogTrace("<- ImportCsvAsync()");
    }

    [RelayCommand]
    private async Task ChangeTemplateAsync()
    {
        _logger.LogTrace("-> ChangeTemplateAsync()");
        if (SelectedProduct == null || !SelectedProduct.IsLeaf)
        {
            _logger.LogTrace("<- ChangeTemplateAsync() [no leaf selected]");
            return;
        }

        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Filter = _loc["Filter_TemplateFiles"],
            Title = _loc["DialogTitle_SelectTemplate"]
        };

        if (dialog.ShowDialog() != true)
        {
            _logger.LogTrace("<- ChangeTemplateAsync() [dialog cancelled]");
            return;
        }

        SelectedProduct.TemplateFile = dialog.FileName;
        await _db.SaveChangesAsync();
        OnPropertyChanged(nameof(SelectedProduct));
        _logger.LogTrace("<- ChangeTemplateAsync() Template={Template}", dialog.FileName);
    }

    [RelayCommand]
    private async Task SaveCsvNameAsync()
    {
        _logger.LogTrace("-> SaveCsvNameAsync()");
        if (SelectedProduct == null || !SelectedProduct.IsLeaf)
        {
            _logger.LogTrace("<- SaveCsvNameAsync() [no leaf selected]");
            return;
        }
        await _db.SaveChangesAsync();
        _logger.LogTrace("<- SaveCsvNameAsync()");
    }

    [RelayCommand]
    private void NewJob()
    {
        _logger.LogTrace("-> NewJob() ProductId={ProductId}", SelectedProduct?.Id);
        if (SelectedProduct?.IsLeaf == true && CanCreateNewJob)
            NavigateToNewJobRequested?.Invoke(this, SelectedProduct.Id);
        _logger.LogTrace("<- NewJob()");
    }

    [RelayCommand]
    private void ShowRename()
    {
        _logger.LogTrace("-> ShowRename()");
        if (SelectedProduct == null)
        {
            _logger.LogTrace("<- ShowRename() [no selection]");
            return;
        }
        EditName = SelectedProduct.Name;
        IsRenaming = true;
        _logger.LogInformation("Products: Rename started for '{Name}' (Id={Id})", SelectedProduct.Name, SelectedProduct.Id);
        _logger.LogTrace("<- ShowRename()");
    }

    [RelayCommand]
    private void CancelRename()
    {
        _logger.LogTrace("-> CancelRename()");
        IsRenaming = false;
        EditName = string.Empty;
        _logger.LogTrace("<- CancelRename()");
    }

    [RelayCommand]
    private async Task ConfirmRenameAsync()
    {
        _logger.LogTrace("-> ConfirmRenameAsync()");
        if (SelectedProduct == null || string.IsNullOrWhiteSpace(EditName))
        {
            _logger.LogTrace("<- ConfirmRenameAsync() [no selection or empty name]");
            return;
        }

        var trimmed = EditName.Trim();
        if (trimmed == SelectedProduct.Name)
        {
            IsRenaming = false;
            _logger.LogTrace("<- ConfirmRenameAsync() [name unchanged]");
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
        _logger.LogTrace("<- ConfirmRenameAsync()");
    }

    partial void OnSelectedProductChanged(ProductNode? value)
    {
        _logger.LogTrace("-> OnSelectedProductChanged(Id={Id}, Name={Name})", value?.Id, value?.Name);
        if (value != null)
            _logger.LogInformation("Product selected: '{Name}' (Id={Id}, IsLeaf={IsLeaf})", value.Name, value.Id, value.IsLeaf);

        // Clear unassigned mode when a product is selected
        IsShowingUnassigned = false;

        // Update the "adding to" hint
        if (value == null)
            AddTargetHint = _loc["Label_Root"];
        else if (!value.IsLeaf)
            AddTargetHint = value.Name;
        else
            AddTargetHint = value.Parent?.Name ?? _loc["Label_Root"];

        // Close any open rename form on selection change
        IsRenaming = false;
        EditName = string.Empty;

        _ = RefreshCodeCountsAsync();
        _ = LoadActivityHistoryAsync();
        _ = CheckCanDeleteAsync();

        // Load codes tab for leaf products
        if (value?.IsLeaf == true)
            _ = CodesTab.LoadForProductAsync(value.Id);
        _logger.LogTrace("<- OnSelectedProductChanged()");
    }

    [RelayCommand]
    private async Task ShowUnassignedCodesAsync()
    {
        _logger.LogTrace("-> ShowUnassignedCodesAsync()");
        SelectedProduct = null;
        IsShowingUnassigned = true;
        await CodesTab.LoadForProductAsync(null);
        _logger.LogTrace("<- ShowUnassignedCodesAsync()");
    }

    private async Task CheckCanDeleteAsync()
    {
        _logger.LogTrace("-> CheckCanDeleteAsync()");
        if (SelectedProduct == null)
        {
            CanDeleteSelectedProduct = false;
            DeleteBlockedReason = string.Empty;
            _logger.LogTrace("<- CheckCanDeleteAsync() [no selection]");
            return;
        }

        var canDelete = await _productService.CanDeleteAsync(SelectedProduct.Id);
        CanDeleteSelectedProduct = canDelete;
        DeleteBlockedReason = canDelete ? string.Empty : _loc["Error_DeleteBlocked"];
        _logger.LogTrace("<- CheckCanDeleteAsync() CanDelete={CanDelete}", canDelete);
    }

    private async Task RefreshCodeCountsAsync()
    {
        _logger.LogTrace("-> RefreshCodeCountsAsync()");
        if (SelectedProduct == null || !SelectedProduct.IsLeaf)
        {
            AvailableCodesCount = 0;
            PrintedCodesCount = 0;
            BurnedCodesCount = 0;
            QuarantinedCodesCount = 0;
            TotalCodesCount = 0;
            CanCreateNewJob = false;
            _logger.LogTrace("<- RefreshCodeCountsAsync() [no leaf selected]");
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
        _logger.LogTrace("<- RefreshCodeCountsAsync()");
    }

    private async Task RefreshUnassignedCountAsync()
    {
        _logger.LogTrace("-> RefreshUnassignedCountAsync()");
        UnassignedCodesCount = await _codeManagement.GetUnassignedCountAsync();
        _logger.LogTrace("<- RefreshUnassignedCountAsync() Count={Count}", UnassignedCodesCount);
    }

    private async Task LoadActivityHistoryAsync()
    {
        _logger.LogTrace("-> LoadActivityHistoryAsync()");
        ActivityHistory.Clear();
        if (SelectedProduct == null || !SelectedProduct.IsLeaf)
        {
            _logger.LogTrace("<- LoadActivityHistoryAsync() [no leaf selected]");
            return;
        }

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
        _logger.LogTrace("<- LoadActivityHistoryAsync() Items={Count}", ActivityHistory.Count);
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
