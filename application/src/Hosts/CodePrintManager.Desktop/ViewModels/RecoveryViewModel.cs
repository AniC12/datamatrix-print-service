using System.Collections.ObjectModel;
using CodePrintManager.Application.Models;
using CodePrintManager.Domain.Enums;
using CodePrintManager.Domain.Interfaces;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;

namespace CodePrintManager.Desktop.ViewModels;

public partial class RecoveryViewModel : ObservableObject
{
    private readonly IPrintJobService _printJobService;
    private readonly ILogger<RecoveryViewModel> _logger;
    private readonly ILocalizationService _loc;

    public ObservableCollection<RecoveryItemViewModel> Items { get; } = new();

    [ObservableProperty]
    private RecoveryItemViewModel? _selectedItem;

    [ObservableProperty]
    private bool _hasItems;

    /// <summary>
    /// Set to true when all items have been resolved (Resume or Abort).
    /// The dialog should close when this becomes true.
    /// </summary>
    [ObservableProperty]
    private bool _allResolved;

    public RecoveryViewModel(IPrintJobService printJobService, ILogger<RecoveryViewModel> logger,
        ILocalizationService loc)
    {
        _printJobService = printJobService;
        _logger = logger;
        _loc = loc;
    }

    public void LoadItems(IEnumerable<RecoveryItem> recoveryItems)
    {
        Items.Clear();
        foreach (var item in recoveryItems)
            Items.Add(new RecoveryItemViewModel(item, _loc));
        HasItems = Items.Count > 0;
        AllResolved = !HasItems;

        _logger.LogInformation("Recovery: {Count} stale jobs found", Items.Count);
        foreach (var vm in Items)
        {
            _logger.LogInformation(
                "Recovery: Job #{JobId} (Product='{Product}', Printer='{Printer}', AppConfirmed={App}, PrinterConfirmed={Printer}, Delta={Delta})",
                vm.JobId, vm.ProductName, vm.PrinterName, vm.AppConfirmed, vm.PrinterConfirmed, vm.Delta);
        }
    }

    [RelayCommand]
    private async Task ResumeAsync()
    {
        if (SelectedItem == null) return;
        _logger.LogInformation("Recovery: Resuming Job #{JobId}", SelectedItem.Item.Job.Id);
        await _printJobService.ResumeJobAsync(SelectedItem.Item.Job.Id);
        SelectedItem.Status = _loc["Status_Resumed"];
        Items.Remove(SelectedItem);
        SelectedItem = null;
        HasItems = Items.Count > 0;
        AllResolved = !HasItems;
        if (AllResolved) _logger.LogInformation("Recovery: All stale jobs resolved");
    }

    [RelayCommand]
    private async Task AbortAsync()
    {
        if (SelectedItem == null) return;
        _logger.LogInformation("Recovery: Aborting Job #{JobId}", SelectedItem.Item.Job.Id);
        await _printJobService.CancelJobAsync(SelectedItem.Item.Job.Id);
        SelectedItem.Status = _loc["Status_Aborted"];
        Items.Remove(SelectedItem);
        SelectedItem = null;
        HasItems = Items.Count > 0;
        AllResolved = !HasItems;
        if (AllResolved) _logger.LogInformation("Recovery: All stale jobs resolved");
    }
}

public partial class RecoveryItemViewModel : ObservableObject
{
    private readonly ILocalizationService _loc;
    public RecoveryItem Item { get; }

    public int JobId => Item.Job.Id;
    public string ProductName => Item.Job.Product?.Name ?? _loc["Common_Unknown"];
    public string PrinterName => Item.Job.Printer?.Name ?? _loc["Common_Unknown"];
    public int AppConfirmed => Item.ConfirmedByApp;
    public int PrinterConfirmed => Item.ConfirmedByPrinter;
    public int Delta => Item.Discrepancy;
    public string DeltaDisplay => Item.ConfirmedByPrinter >= 0
        ? $"{Item.Discrepancy:+#;-#;0}"
        : _loc["Status_Offline"];

    [ObservableProperty]
    private string _status;

    public RecoveryItemViewModel(RecoveryItem item, ILocalizationService loc)
    {
        _loc = loc;
        Item = item;
        _status = _loc["Status_Pending"];
    }
}
