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
        _logger.LogTrace("-> RecoveryViewModel()");
        _logger.LogTrace("<- RecoveryViewModel()");
    }

    public void LoadItems(IEnumerable<RecoveryItem> recoveryItems)
    {
        _logger.LogTrace("-> LoadItems()");
        Items.Clear();
        foreach (var item in recoveryItems)
            Items.Add(new RecoveryItemViewModel(item, _loc));
        HasItems = Items.Count > 0;
        AllResolved = !HasItems;

        _logger.LogInformation("Recovery: {Count} stale jobs found", Items.Count);
        foreach (var vm in Items)
        {
            _logger.LogInformation(
                "Recovery: Job #{JobId} (Product='{Product}', Printer='{PrinterName}', AppConfirmed={App}, PrinterConfirmed={PrinterConfirmed}, Delta={Delta})",
                vm.JobId, vm.ProductName, vm.PrinterName, vm.AppConfirmed, vm.PrinterConfirmed, vm.Delta);
        }
        _logger.LogTrace("<- LoadItems() Count={Count}", Items.Count);
    }

    partial void OnSelectedItemChanged(RecoveryItemViewModel? value)
    {
        _logger.LogTrace("-> OnSelectedItemChanged(JobId={JobId})", value?.JobId);
        _logger.LogTrace("<- OnSelectedItemChanged()");
    }

    [RelayCommand]
    private async Task ResumeAsync()
    {
        _logger.LogTrace("-> ResumeAsync()");
        if (SelectedItem == null)
        {
            _logger.LogTrace("<- ResumeAsync() [no selection]");
            return;
        }
        try
        {
            _logger.LogInformation("Recovery: Resuming Job #{JobId}", SelectedItem.Item.Job.Id);
            await _printJobService.ResumeJobAsync(SelectedItem.Item.Job.Id);
            SelectedItem.Status = _loc["Status_Resumed"];
            Items.Remove(SelectedItem);
            SelectedItem = null;
            HasItems = Items.Count > 0;
            AllResolved = !HasItems;
            if (AllResolved) _logger.LogInformation("Recovery: All stale jobs resolved");
            _logger.LogTrace("<- ResumeAsync() RemainingItems={Count}", Items.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Recovery: failed to resume Job #{JobId}", SelectedItem?.Item.Job.Id);
            if (SelectedItem != null)
                SelectedItem.Status = _loc.Format("Recovery_Error", ex.Message);
        }
    }

    [RelayCommand]
    private async Task AbortAsync()
    {
        _logger.LogTrace("-> AbortAsync()");
        if (SelectedItem == null)
        {
            _logger.LogTrace("<- AbortAsync() [no selection]");
            return;
        }
        try
        {
            _logger.LogInformation("Recovery: Aborting Job #{JobId}", SelectedItem.Item.Job.Id);
            await _printJobService.CancelJobAsync(SelectedItem.Item.Job.Id);
            SelectedItem.Status = _loc["Status_Aborted"];
            Items.Remove(SelectedItem);
            SelectedItem = null;
            HasItems = Items.Count > 0;
            AllResolved = !HasItems;
            if (AllResolved) _logger.LogInformation("Recovery: All stale jobs resolved");
            _logger.LogTrace("<- AbortAsync() RemainingItems={Count}", Items.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Recovery: failed to abort Job #{JobId}", SelectedItem?.Item.Job.Id);
            if (SelectedItem != null)
                SelectedItem.Status = _loc.Format("Recovery_Error", ex.Message);
        }
    }
}

public partial class RecoveryItemViewModel : ObservableObject
{
    private readonly ILocalizationService _loc;
    public RecoveryItem Item { get; }

    public int JobId => Item.Job.Id;
    public string JobStatus => Item.Job.Status.ToString();
    public string ProductName => Item.Job.Product?.Name ?? _loc["Common_Unknown"];
    public string PrinterName => Item.Job.Printer?.Name ?? _loc["Common_Unknown"];
    public int AppConfirmed => Item.ConfirmedByApp;
    public int PrinterConfirmed => Item.ConfirmedByPrinter;
    public int Delta => Item.Discrepancy;
    public string DeltaDisplay => Item.ConfirmedByPrinter >= 0
        ? $"{Item.Discrepancy:+#;-#;0}"
        : _loc["Status_Offline"];

    public string Recommendation => Item.RecommendedAction ?? "";
    public bool PowerCycled => Item.PowerCycleDetected;
    public bool TemplateMismatch => !Item.TemplateMatch;
    public bool CsvMissing => !Item.CsvPresent;
    public bool PrinterOffline => Item.PrinterOffline;
    public bool SerialMismatch => Item.SerialMismatch;

    /// <summary>Summary of warnings for this item (shown in the Flags column).</summary>
    public string Flags
    {
        get
        {
            var flags = new List<string>();
            if (Item.PrinterOffline) flags.Add(_loc["Recovery_Flag_Offline"]);
            if (Item.SerialMismatch) flags.Add(_loc["Recovery_Flag_SerialMismatch"]);
            if (Item.PowerCycleDetected) flags.Add(_loc["Recovery_Flag_PowerCycle"]);
            if (!Item.TemplateMatch) flags.Add(_loc["Recovery_Flag_TemplateMismatch"]);
            if (!Item.CsvPresent) flags.Add(_loc["Recovery_Flag_CsvMissing"]);
            return flags.Count > 0 ? string.Join(", ", flags) : "OK";
        }
    }

    [ObservableProperty]
    private string _status;

    public RecoveryItemViewModel(RecoveryItem item, ILocalizationService loc)
    {
        _loc = loc;
        Item = item;
        _status = _loc["Status_Pending"];
    }
}
