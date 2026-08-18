using System.Collections.ObjectModel;
using System.Windows.Threading;
using CodePrintManager.Domain.Entities;
using CodePrintManager.Domain.Enums;
using CodePrintManager.Domain.Interfaces;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;

namespace CodePrintManager.Desktop.ViewModels;

public record PageSizeOption(string Label, int Value);

public partial class CodeItemViewModel : ObservableObject
{
    public int Id { get; init; }
    public string CodeText { get; init; } = string.Empty;
    public CodeStatus Status { get; init; }
    public string? ImportBatch { get; init; }
    public int? JobId { get; init; }
    public DateTime? StatusChangedAt { get; init; }
    public bool IsReserved => Status == CodeStatus.Reserved;

    [ObservableProperty]
    private bool _isSelected;
}

public partial class CodesTabViewModel : ObservableObject
{
    private readonly ICodeManagementService _codeManagement;
    private readonly IProductService _productService;
    private readonly ILogger<CodesTabViewModel> _logger;
    private readonly DispatcherTimer _searchDebounce;
    private readonly Stack<CodeOperation> _undoStack = new();

    public ObservableCollection<CodeItemViewModel> Codes { get; } = new();
    public ObservableCollection<PageSizeOption> PageSizeOptions { get; } = new()
    {
        new("100", 100),
        new("250", 250),
        new("500", 500),
        new("1000", 1000),
        new("All", -1)
    };

    public ObservableCollection<ProductNode> LeafProducts { get; } = new();

    // Status filter options for ComboBox
    public ObservableCollection<string> StatusFilterOptions { get; } = new()
    {
        "All", "Available", "Reserved", "Printed", "Returned", "Burned", "Quarantined"
    };

    // Status change options (excludes Reserved — you cannot manually set a code to Reserved)
    public ObservableCollection<string> StatusChangeOptions { get; } = new()
    {
        "Available", "Printed", "Returned", "Burned", "Quarantined"
    };

    // Context
    [ObservableProperty]
    private int? _productId;

    public bool IsUnassignedMode => ProductId == null;

    // Data
    [ObservableProperty]
    private int _totalCount;

    [ObservableProperty]
    private int _currentPage = 1;

    [ObservableProperty]
    private int _totalPages = 1;

    [ObservableProperty]
    private bool _canGoNext;

    [ObservableProperty]
    private bool _canGoPrevious;

    // Filters
    [ObservableProperty]
    private string _selectedStatusFilterText = "All";

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private int _selectedPageSize = 100;

    [ObservableProperty]
    private bool _isLoading;

    // Selection
    [ObservableProperty]
    private bool _hasSelection;

    [ObservableProperty]
    private int _selectedCount;

    // Actions
    [ObservableProperty]
    private string? _selectedNewStatus;

    [ObservableProperty]
    private ProductNode? _moveTargetProduct;

    // Undo
    [ObservableProperty]
    private bool _canUndo;

    [ObservableProperty]
    private string? _lastOperationSummary;

    public event EventHandler? CodesChanged;

    public CodesTabViewModel(ICodeManagementService codeManagement, IProductService productService,
        ILogger<CodesTabViewModel> logger)
    {
        _codeManagement = codeManagement;
        _productService = productService;
        _logger = logger;

        _searchDebounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
        _searchDebounce.Tick += (s, e) =>
        {
            _searchDebounce.Stop();
            _ = LoadCodesAsync();
        };
    }

    public async Task LoadForProductAsync(int? productId)
    {
        ProductId = productId;
        OnPropertyChanged(nameof(IsUnassignedMode));
        SelectedStatusFilterText = "All";
        SearchText = string.Empty;
        CurrentPage = 1;
        SelectedPageSize = 100;
        SelectedNewStatus = null;
        MoveTargetProduct = null;
        _undoStack.Clear();
        CanUndo = false;
        LastOperationSummary = null;

        await LoadLeafProductsAsync();
        await LoadCodesAsync();
    }

    private async Task LoadLeafProductsAsync()
    {
        LeafProducts.Clear();
        var roots = await _productService.GetTreeAsync();
        CollectLeafProducts(roots, ProductId);
    }

    private void CollectLeafProducts(IEnumerable<ProductNode> nodes, int? excludeId)
    {
        foreach (var node in nodes)
        {
            if (node.IsLeaf && node.Id != excludeId)
                LeafProducts.Add(node);
            if (node.Children.Count > 0)
                CollectLeafProducts(node.Children, excludeId);
        }
    }

    private CodeStatus? ParseStatusFilter()
    {
        if (string.IsNullOrEmpty(SelectedStatusFilterText) || SelectedStatusFilterText == "All")
            return null;
        return Enum.TryParse<CodeStatus>(SelectedStatusFilterText, out var status) ? status : null;
    }

    [RelayCommand]
    private async Task LoadCodesAsync()
    {
        IsLoading = true;
        try
        {
            var statusFilter = ParseStatusFilter();
            var search = string.IsNullOrWhiteSpace(SearchText) ? null : SearchText.Trim();

            var page = await _codeManagement.GetCodesPageAsync(
                ProductId, statusFilter, search, CurrentPage, SelectedPageSize);

            Codes.Clear();
            foreach (var code in page.Codes)
            {
                Codes.Add(new CodeItemViewModel
                {
                    Id = code.Id,
                    CodeText = code.CodeText,
                    Status = code.Status,
                    ImportBatch = code.ImportBatch,
                    JobId = code.JobId,
                    StatusChangedAt = code.StatusChangedAt
                });
            }

            TotalCount = page.TotalCount;
            CurrentPage = page.Page;
            TotalPages = page.TotalPages;
            CanGoNext = CurrentPage < TotalPages;
            CanGoPrevious = CurrentPage > 1;
            UpdateSelectionState();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load codes page");
        }
        finally
        {
            IsLoading = false;
        }
    }

    partial void OnSelectedStatusFilterTextChanged(string value)
    {
        CurrentPage = 1;
        _ = LoadCodesAsync();
    }

    partial void OnSearchTextChanged(string value)
    {
        _searchDebounce.Stop();
        _searchDebounce.Start();
    }

    partial void OnSelectedPageSizeChanged(int value)
    {
        CurrentPage = 1;
        _ = LoadCodesAsync();
    }

    [RelayCommand]
    private async Task NextPageAsync()
    {
        if (!CanGoNext) return;
        CurrentPage++;
        await LoadCodesAsync();
    }

    [RelayCommand]
    private async Task PreviousPageAsync()
    {
        if (!CanGoPrevious) return;
        CurrentPage--;
        await LoadCodesAsync();
    }

    [RelayCommand]
    private void SelectAll()
    {
        foreach (var code in Codes)
            if (!code.IsReserved)
                code.IsSelected = true;
        UpdateSelectionState();
    }

    [RelayCommand]
    private void DeselectAll()
    {
        foreach (var code in Codes)
            code.IsSelected = false;
        UpdateSelectionState();
    }

    private void UpdateSelectionState()
    {
        var selected = Codes.Where(c => c.IsSelected).ToList();
        SelectedCount = selected.Count;
        HasSelection = SelectedCount > 0;
    }

    // Called from UI when individual checkboxes change
    public void OnCodeSelectionChanged() => UpdateSelectionState();

    private IReadOnlyList<int> GetSelectedCodeIds() =>
        Codes.Where(c => c.IsSelected).Select(c => c.Id).ToList();

    private bool IsRiskyTransition(CodeStatus from, CodeStatus to) =>
        to == CodeStatus.Available && from is CodeStatus.Printed or CodeStatus.Burned or CodeStatus.Quarantined;

    [RelayCommand]
    private async Task ApplyStatusChangeAsync()
    {
        if (!HasSelection || SelectedNewStatus == null) return;
        if (!Enum.TryParse<CodeStatus>(SelectedNewStatus, out var newStatus)) return;

        var ids = GetSelectedCodeIds();
        var hasRisky = Codes.Where(c => c.IsSelected).Any(c => IsRiskyTransition(c.Status, newStatus));

        var message = hasRisky
            ? $"WARNING: Changing {ids.Count} code(s) to {newStatus}.\n\n" +
              "If any of these codes were physically printed on a product, " +
              "marking them Available will allow them to be printed again, " +
              "creating a DUPLICATE.\n\nContinue?"
            : $"Change {ids.Count} code(s) to {newStatus}?";

        var icon = hasRisky
            ? System.Windows.MessageBoxImage.Warning
            : System.Windows.MessageBoxImage.Question;

        var result = System.Windows.MessageBox.Show(message, "Confirm Status Change",
            System.Windows.MessageBoxButton.YesNo, icon);
        if (result != System.Windows.MessageBoxResult.Yes) return;

        try
        {
            var op = await _codeManagement.ChangeStatusAsync(ids, newStatus);
            PushUndo(op);
            await LoadCodesAsync();
            CodesChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Status change failed");
            System.Windows.MessageBox.Show($"Operation failed: {ex.Message}", "Error",
                System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
        }
    }

    [RelayCommand]
    private async Task ApplyBulkStatusChangeAsync()
    {
        var statusFilter = ParseStatusFilter();
        if (statusFilter == null) return; // Bulk requires a specific status filter

        // Ask for target status
        var targetStatus = SelectedNewStatus;
        if (targetStatus == null || !Enum.TryParse<CodeStatus>(targetStatus, out var toStatus)) return;

        var hasRisky = IsRiskyTransition(statusFilter.Value, toStatus);
        var message = hasRisky
            ? $"WARNING: Changing ALL {TotalCount} {statusFilter} code(s) to {toStatus}.\n\n" +
              "If any of these codes were physically printed, marking them Available " +
              "will allow DUPLICATES.\n\nThis cannot be easily undone for large batches.\n\nContinue?"
            : $"Change ALL {TotalCount} {statusFilter} code(s) to {toStatus}?";

        var result = System.Windows.MessageBox.Show(message, "Confirm Bulk Status Change",
            System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Warning);
        if (result != System.Windows.MessageBoxResult.Yes) return;

        try
        {
            var op = await _codeManagement.ChangeStatusBulkAsync(ProductId, statusFilter.Value, toStatus);
            PushUndo(op);
            await LoadCodesAsync();
            CodesChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Bulk status change failed");
            System.Windows.MessageBox.Show($"Operation failed: {ex.Message}", "Error",
                System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
        }
    }

    [RelayCommand]
    private async Task MoveSelectedAsync()
    {
        if (!HasSelection || MoveTargetProduct == null) return;

        var ids = GetSelectedCodeIds();
        var result = System.Windows.MessageBox.Show(
            $"Move {ids.Count} code(s) to \"{MoveTargetProduct.Name}\"?",
            "Confirm Move",
            System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Question);
        if (result != System.Windows.MessageBoxResult.Yes) return;

        try
        {
            var op = await _codeManagement.MoveCodesAsync(ids, MoveTargetProduct.Id);
            PushUndo(op);
            await LoadCodesAsync();
            CodesChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Move failed");
            System.Windows.MessageBox.Show($"Operation failed: {ex.Message}", "Error",
                System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
        }
    }

    [RelayCommand]
    private async Task MoveAllFilteredAsync()
    {
        var statusFilter = ParseStatusFilter();
        if (statusFilter == null || MoveTargetProduct == null) return;

        var result = System.Windows.MessageBox.Show(
            $"Move ALL {TotalCount} {statusFilter} code(s) to \"{MoveTargetProduct.Name}\"?\n\n" +
            "This affects every matching code, not just the current page.",
            "Confirm Bulk Move",
            System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Warning);
        if (result != System.Windows.MessageBoxResult.Yes) return;

        try
        {
            var op = await _codeManagement.MoveCodesBulkAsync(ProductId, statusFilter.Value, MoveTargetProduct.Id);
            PushUndo(op);
            await LoadCodesAsync();
            CodesChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Bulk move failed");
            System.Windows.MessageBox.Show($"Operation failed: {ex.Message}", "Error",
                System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
        }
    }

    [RelayCommand]
    private async Task ArchiveSelectedAsync()
    {
        if (!HasSelection) return;

        var ids = GetSelectedCodeIds();
        var result = System.Windows.MessageBox.Show(
            $"Archive {ids.Count} code(s)?\n\n" +
            "Archived codes are removed from the active pool. They can be re-imported later, " +
            "but their current status and job associations will be moved to the archive.",
            "Confirm Archive",
            System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Warning);
        if (result != System.Windows.MessageBoxResult.Yes) return;

        try
        {
            var op = await _codeManagement.ArchiveCodesAsync(ids, "admin_delete");
            PushUndo(op);
            await LoadCodesAsync();
            CodesChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Archive failed");
            System.Windows.MessageBox.Show($"Operation failed: {ex.Message}", "Error",
                System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
        }
    }

    [RelayCommand]
    private async Task ArchiveAllFilteredAsync()
    {
        var statusFilter = ParseStatusFilter();
        if (statusFilter == null) return;

        var result = System.Windows.MessageBox.Show(
            $"Archive ALL {TotalCount} {statusFilter} code(s)?\n\n" +
            "This permanently archives every matching code — not just the current page.\n" +
            "Archived codes can be re-imported later.",
            "Confirm Bulk Archive",
            System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Warning);
        if (result != System.Windows.MessageBoxResult.Yes) return;

        try
        {
            var op = await _codeManagement.ArchiveCodesBulkAsync(ProductId, statusFilter.Value, "admin_delete");
            PushUndo(op);
            await LoadCodesAsync();
            CodesChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Bulk archive failed");
            System.Windows.MessageBox.Show($"Operation failed: {ex.Message}", "Error",
                System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
        }
    }

    [RelayCommand]
    private async Task UndoAsync()
    {
        if (_undoStack.Count == 0) return;

        var operation = _undoStack.Pop();
        try
        {
            var undoResult = await _codeManagement.UndoOperationAsync(operation);
            LastOperationSummary = undoResult.Message;
            CanUndo = _undoStack.Count > 0;

            if (_undoStack.Count > 0)
                LastOperationSummary += $" ({_undoStack.Count} more undo(s) available)";

            await LoadCodesAsync();
            CodesChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Undo failed");
            System.Windows.MessageBox.Show($"Undo failed: {ex.Message}", "Error",
                System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
        }
    }

    private void PushUndo(CodeOperation operation)
    {
        _undoStack.Push(operation);
        // Limit to 10 entries
        if (_undoStack.Count > 10)
        {
            var temp = _undoStack.ToArray().Take(10).ToArray();
            _undoStack.Clear();
            foreach (var item in temp.Reverse())
                _undoStack.Push(item);
        }
        CanUndo = true;
        LastOperationSummary = operation.Description;
    }
}
