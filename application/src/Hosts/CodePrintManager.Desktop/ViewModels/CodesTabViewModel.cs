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
    private readonly ILocalizationService _loc;
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
        ILogger<CodesTabViewModel> logger, ILocalizationService loc)
    {
        _codeManagement = codeManagement;
        _productService = productService;
        _logger = logger;
        _loc = loc;
        _logger.LogTrace("-> CodesTabViewModel()");

        _searchDebounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
        _searchDebounce.Tick += (s, e) =>
        {
            _logger.LogTrace("-> SearchDebounce Tick handler");
            _searchDebounce.Stop();
            _ = LoadCodesAsync();
            _logger.LogTrace("<- SearchDebounce Tick handler");
        };
        _logger.LogTrace("<- CodesTabViewModel()");
    }

    public async Task LoadForProductAsync(int? productId)
    {
        _logger.LogTrace("-> LoadForProductAsync(productId={ProductId})", productId);
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
        _logger.LogTrace("<- LoadForProductAsync()");
    }

    private async Task LoadLeafProductsAsync()
    {
        _logger.LogTrace("-> LoadLeafProductsAsync()");
        LeafProducts.Clear();
        var roots = await _productService.GetTreeAsync();
        CollectLeafProducts(roots, ProductId);
        _logger.LogTrace("<- LoadLeafProductsAsync() LeafCount={Count}", LeafProducts.Count);
    }

    private void CollectLeafProducts(IEnumerable<ProductNode> nodes, int? excludeId)
    {
        _logger.LogTrace("-> CollectLeafProducts(excludeId={ExcludeId})", excludeId);
        foreach (var node in nodes)
        {
            if (node.IsLeaf && node.Id != excludeId)
                LeafProducts.Add(node);
            if (node.Children.Count > 0)
                CollectLeafProducts(node.Children, excludeId);
        }
        _logger.LogTrace("<- CollectLeafProducts()");
    }

    private CodeStatus? ParseStatusFilter()
    {
        _logger.LogTrace("-> ParseStatusFilter() SelectedStatusFilterText={Filter}", SelectedStatusFilterText);
        CodeStatus? result;
        if (string.IsNullOrEmpty(SelectedStatusFilterText) || SelectedStatusFilterText == "All")
            result = null;
        else
            result = Enum.TryParse<CodeStatus>(SelectedStatusFilterText, out var status) ? status : null;
        _logger.LogTrace("<- ParseStatusFilter() Result={Result}", result);
        return result;
    }

    [RelayCommand]
    private async Task LoadCodesAsync()
    {
        _logger.LogTrace("-> LoadCodesAsync() ProductId={ProductId}, Page={Page}, PageSize={PageSize}", ProductId, CurrentPage, SelectedPageSize);
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
        _logger.LogTrace("<- LoadCodesAsync() TotalCount={TotalCount}, CodesOnPage={CodesOnPage}", TotalCount, Codes.Count);
    }

    partial void OnSelectedStatusFilterTextChanged(string value)
    {
        _logger.LogTrace("-> OnSelectedStatusFilterTextChanged(value={Value})", value);
        CurrentPage = 1;
        _ = LoadCodesAsync();
        _logger.LogTrace("<- OnSelectedStatusFilterTextChanged()");
    }

    partial void OnSearchTextChanged(string value)
    {
        _logger.LogTrace("-> OnSearchTextChanged(value={Value})", value);
        _searchDebounce.Stop();
        _searchDebounce.Start();
        _logger.LogTrace("<- OnSearchTextChanged()");
    }

    partial void OnSelectedPageSizeChanged(int value)
    {
        _logger.LogTrace("-> OnSelectedPageSizeChanged(value={Value})", value);
        CurrentPage = 1;
        _ = LoadCodesAsync();
        _logger.LogTrace("<- OnSelectedPageSizeChanged()");
    }

    [RelayCommand]
    private async Task NextPageAsync()
    {
        _logger.LogTrace("-> NextPageAsync() CurrentPage={CurrentPage}", CurrentPage);
        if (!CanGoNext)
        {
            _logger.LogTrace("<- NextPageAsync() [already on last page]");
            return;
        }
        CurrentPage++;
        await LoadCodesAsync();
        _logger.LogTrace("<- NextPageAsync() NewPage={CurrentPage}", CurrentPage);
    }

    [RelayCommand]
    private async Task PreviousPageAsync()
    {
        _logger.LogTrace("-> PreviousPageAsync() CurrentPage={CurrentPage}", CurrentPage);
        if (!CanGoPrevious)
        {
            _logger.LogTrace("<- PreviousPageAsync() [already on first page]");
            return;
        }
        CurrentPage--;
        await LoadCodesAsync();
        _logger.LogTrace("<- PreviousPageAsync() NewPage={CurrentPage}", CurrentPage);
    }

    [RelayCommand]
    private void SelectAll()
    {
        _logger.LogTrace("-> SelectAll()");
        foreach (var code in Codes)
            if (!code.IsReserved)
                code.IsSelected = true;
        UpdateSelectionState();
        _logger.LogTrace("<- SelectAll() SelectedCount={Count}", SelectedCount);
    }

    [RelayCommand]
    private void DeselectAll()
    {
        _logger.LogTrace("-> DeselectAll()");
        foreach (var code in Codes)
            code.IsSelected = false;
        UpdateSelectionState();
        _logger.LogTrace("<- DeselectAll()");
    }

    private void UpdateSelectionState()
    {
        _logger.LogTrace("-> UpdateSelectionState()");
        var selected = Codes.Where(c => c.IsSelected).ToList();
        SelectedCount = selected.Count;
        HasSelection = SelectedCount > 0;
        _logger.LogTrace("<- UpdateSelectionState() SelectedCount={Count}, HasSelection={HasSelection}", SelectedCount, HasSelection);
    }

    // Called from UI when individual checkboxes change
    public void OnCodeSelectionChanged()
    {
        _logger.LogTrace("-> OnCodeSelectionChanged()");
        UpdateSelectionState();
        _logger.LogTrace("<- OnCodeSelectionChanged()");
    }

    private IReadOnlyList<int> GetSelectedCodeIds()
    {
        _logger.LogTrace("-> GetSelectedCodeIds()");
        var ids = Codes.Where(c => c.IsSelected).Select(c => c.Id).ToList();
        _logger.LogTrace("<- GetSelectedCodeIds() Count={Count}", ids.Count);
        return ids;
    }

    private bool IsRiskyTransition(CodeStatus from, CodeStatus to)
    {
        _logger.LogTrace("-> IsRiskyTransition(from={From}, to={To})", from, to);
        var result = to == CodeStatus.Available && from is CodeStatus.Printed or CodeStatus.Burned or CodeStatus.Quarantined;
        _logger.LogTrace("<- IsRiskyTransition() Result={Result}", result);
        return result;
    }

    [RelayCommand]
    private async Task ApplyStatusChangeAsync()
    {
        _logger.LogTrace("-> ApplyStatusChangeAsync() SelectedNewStatus={Status}", SelectedNewStatus);
        if (!HasSelection || SelectedNewStatus == null)
        {
            _logger.LogTrace("<- ApplyStatusChangeAsync() [no selection or no target status]");
            return;
        }
        if (!Enum.TryParse<CodeStatus>(SelectedNewStatus, out var newStatus))
        {
            _logger.LogTrace("<- ApplyStatusChangeAsync() [invalid status parse]");
            return;
        }

        var ids = GetSelectedCodeIds();
        var hasRisky = Codes.Where(c => c.IsSelected).Any(c => IsRiskyTransition(c.Status, newStatus));

        var message = hasRisky
            ? _loc.Format("Dialog_ConfirmStatusChangeWarning", ids.Count, newStatus)
            : _loc.Format("Dialog_ConfirmStatusChange", ids.Count, newStatus);

        var icon = hasRisky
            ? System.Windows.MessageBoxImage.Warning
            : System.Windows.MessageBoxImage.Question;

        var result = System.Windows.MessageBox.Show(message, _loc["DialogTitle_ConfirmStatusChange"],
            System.Windows.MessageBoxButton.YesNo, icon);
        if (result != System.Windows.MessageBoxResult.Yes)
        {
            _logger.LogTrace("<- ApplyStatusChangeAsync() [cancelled by user]");
            return;
        }

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
            System.Windows.MessageBox.Show(_loc.Format("Error_ChangeStatusFailed", ex.Message), _loc["DialogTitle_Error"],
                System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
        }
        _logger.LogTrace("<- ApplyStatusChangeAsync()");
    }

    [RelayCommand]
    private async Task ApplyBulkStatusChangeAsync()
    {
        _logger.LogTrace("-> ApplyBulkStatusChangeAsync() SelectedNewStatus={Status}", SelectedNewStatus);
        var statusFilter = ParseStatusFilter();
        if (statusFilter == null)
        {
            _logger.LogTrace("<- ApplyBulkStatusChangeAsync() [no status filter]");
            return; // Bulk requires a specific status filter
        }

        // Ask for target status
        var targetStatus = SelectedNewStatus;
        if (targetStatus == null || !Enum.TryParse<CodeStatus>(targetStatus, out var toStatus))
        {
            _logger.LogTrace("<- ApplyBulkStatusChangeAsync() [no target status or invalid parse]");
            return;
        }

        var hasRisky = IsRiskyTransition(statusFilter.Value, toStatus);
        var message = hasRisky
            ? _loc.Format("Dialog_ConfirmBulkStatusChangeWarning", TotalCount, statusFilter, toStatus)
            : _loc.Format("Dialog_ConfirmBulkStatusChange", TotalCount, statusFilter, toStatus);

        var result = System.Windows.MessageBox.Show(message, _loc["DialogTitle_ConfirmStatusChange"],
            System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Warning);
        if (result != System.Windows.MessageBoxResult.Yes)
        {
            _logger.LogTrace("<- ApplyBulkStatusChangeAsync() [cancelled by user]");
            return;
        }

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
            System.Windows.MessageBox.Show(_loc.Format("Error_ChangeStatusFailed", ex.Message), _loc["DialogTitle_Error"],
                System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
        }
        _logger.LogTrace("<- ApplyBulkStatusChangeAsync()");
    }

    [RelayCommand]
    private async Task MoveSelectedAsync()
    {
        _logger.LogTrace("-> MoveSelectedAsync() MoveTarget={Target}", MoveTargetProduct?.Name);
        if (!HasSelection || MoveTargetProduct == null)
        {
            _logger.LogTrace("<- MoveSelectedAsync() [no selection or no target]");
            return;
        }

        var ids = GetSelectedCodeIds();
        var result = System.Windows.MessageBox.Show(
            _loc.Format("Dialog_ConfirmMoveCodes", ids.Count, MoveTargetProduct.Name),
            _loc["DialogTitle_ConfirmMove"],
            System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Question);
        if (result != System.Windows.MessageBoxResult.Yes)
        {
            _logger.LogTrace("<- MoveSelectedAsync() [cancelled by user]");
            return;
        }

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
            System.Windows.MessageBox.Show(_loc.Format("Error_MoveCodesFailed", ex.Message), _loc["DialogTitle_Error"],
                System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
        }
        _logger.LogTrace("<- MoveSelectedAsync()");
    }

    [RelayCommand]
    private async Task MoveAllFilteredAsync()
    {
        _logger.LogTrace("-> MoveAllFilteredAsync() MoveTarget={Target}", MoveTargetProduct?.Name);
        var statusFilter = ParseStatusFilter();
        if (statusFilter == null || MoveTargetProduct == null)
        {
            _logger.LogTrace("<- MoveAllFilteredAsync() [no status filter or no target]");
            return;
        }

        var result = System.Windows.MessageBox.Show(
            _loc.Format("Dialog_ConfirmBulkMove", TotalCount, statusFilter, MoveTargetProduct.Name),
            _loc["DialogTitle_ConfirmBulkMove"],
            System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Warning);
        if (result != System.Windows.MessageBoxResult.Yes)
        {
            _logger.LogTrace("<- MoveAllFilteredAsync() [cancelled by user]");
            return;
        }

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
            System.Windows.MessageBox.Show(_loc.Format("Error_MoveCodesFailed", ex.Message), _loc["DialogTitle_Error"],
                System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
        }
        _logger.LogTrace("<- MoveAllFilteredAsync()");
    }

    [RelayCommand]
    private async Task ArchiveSelectedAsync()
    {
        _logger.LogTrace("-> ArchiveSelectedAsync()");
        if (!HasSelection)
        {
            _logger.LogTrace("<- ArchiveSelectedAsync() [no selection]");
            return;
        }

        var ids = GetSelectedCodeIds();
        var result = System.Windows.MessageBox.Show(
            _loc.Format("Dialog_ConfirmArchiveCodes", ids.Count),
            _loc["DialogTitle_ConfirmArchive"],
            System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Warning);
        if (result != System.Windows.MessageBoxResult.Yes)
        {
            _logger.LogTrace("<- ArchiveSelectedAsync() [cancelled by user]");
            return;
        }

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
            System.Windows.MessageBox.Show(_loc.Format("Error_ArchiveCodesFailed", ex.Message), _loc["DialogTitle_Error"],
                System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
        }
        _logger.LogTrace("<- ArchiveSelectedAsync()");
    }

    [RelayCommand]
    private async Task ArchiveAllFilteredAsync()
    {
        _logger.LogTrace("-> ArchiveAllFilteredAsync()");
        var statusFilter = ParseStatusFilter();
        if (statusFilter == null)
        {
            _logger.LogTrace("<- ArchiveAllFilteredAsync() [no status filter]");
            return;
        }

        var result = System.Windows.MessageBox.Show(
            _loc.Format("Dialog_ConfirmBulkArchive", TotalCount, statusFilter),
            _loc["DialogTitle_ConfirmBulkArchive"],
            System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Warning);
        if (result != System.Windows.MessageBoxResult.Yes)
        {
            _logger.LogTrace("<- ArchiveAllFilteredAsync() [cancelled by user]");
            return;
        }

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
            System.Windows.MessageBox.Show(_loc.Format("Error_ArchiveCodesFailed", ex.Message), _loc["DialogTitle_Error"],
                System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
        }
        _logger.LogTrace("<- ArchiveAllFilteredAsync()");
    }

    [RelayCommand]
    private async Task UndoAsync()
    {
        _logger.LogTrace("-> UndoAsync() StackCount={Count}", _undoStack.Count);
        if (_undoStack.Count == 0)
        {
            _logger.LogTrace("<- UndoAsync() [empty stack]");
            return;
        }

        var operation = _undoStack.Pop();
        try
        {
            var undoResult = await _codeManagement.UndoOperationAsync(operation);
            LastOperationSummary = undoResult.Message;
            CanUndo = _undoStack.Count > 0;

            if (_undoStack.Count > 0)
                LastOperationSummary += _loc.Format("Status_UndoMoreAvailable", _undoStack.Count);

            await LoadCodesAsync();
            CodesChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Undo failed");
            System.Windows.MessageBox.Show(_loc.Format("Error_UndoFailed", ex.Message), _loc["DialogTitle_Error"],
                System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
        }
        _logger.LogTrace("<- UndoAsync()");
    }

    private void PushUndo(CodeOperation operation)
    {
        _logger.LogTrace("-> PushUndo(operation={Description})", operation.Description);
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
        _logger.LogTrace("<- PushUndo() StackCount={Count}", _undoStack.Count);
    }
}
