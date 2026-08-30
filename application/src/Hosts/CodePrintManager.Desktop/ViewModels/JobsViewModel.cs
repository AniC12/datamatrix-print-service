using System.Collections.ObjectModel;
using CodePrintManager.Application.Services;
using CodePrintManager.Data;
using CodePrintManager.Domain.Entities;
using CodePrintManager.Domain.Enums;
using CodePrintManager.Domain.Events;
using CodePrintManager.Domain.Interfaces;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PrinterEntity = CodePrintManager.Domain.Entities.Printer;

namespace CodePrintManager.Desktop.ViewModels;

public partial class JobsViewModel : ObservableObject
{
    private readonly AppDbContext _db;
    private readonly IPrintJobService _printJobService;
    private readonly JobEventBus _eventBus;
    private readonly ILogger<JobsViewModel> _logger;
    private readonly ILocalizationService _loc;

    public ObservableCollection<PrintJob> ActiveJobs { get; } = new();
    public ObservableCollection<PrintJob> JobHistory { get; } = new();

    // Filter data for history tab
    public ObservableCollection<PrinterEntity> FilterPrinters { get; } = new();
    public ObservableCollection<ProductNode> FilterProducts { get; } = new();

    [ObservableProperty]
    private PrintJob? _selectedJob;

    [ObservableProperty]
    private PrintJob? _selectedHistoryJob;

    // Selected job detail fields
    [ObservableProperty]
    private string _jobDetailProduct = string.Empty;

    [ObservableProperty]
    private string _jobDetailPrinter = string.Empty;

    [ObservableProperty]
    private PrinterStatus _jobDetailPrinterStatus;

    [ObservableProperty]
    private int _jobDetailQuantity;

    [ObservableProperty]
    private int _jobDetailProgress;

    [ObservableProperty]
    private string _jobDetailProgressText = string.Empty;

    [ObservableProperty]
    private JobStatus? _jobDetailStatus;

    // Preparation checklist
    [ObservableProperty]
    private bool _prepTemplatePresent;

    [ObservableProperty]
    private bool _prepCodesReserved;

    [ObservableProperty]
    private bool _prepDataUploaded;

    [ObservableProperty]
    private bool _prepTemplateLoaded;

    // History filters
    [ObservableProperty]
    private PrinterEntity? _filterPrinter;

    [ObservableProperty]
    private ProductNode? _filterProduct;

    [ObservableProperty]
    private bool _hasActiveJobs;

    [ObservableProperty]
    private string _printerCountersText = string.Empty;

    // Cache last known counter text per job so it persists after job completes
    private readonly Dictionary<int, string> _countersCache = new();

    public event EventHandler? NavigateToNewJobRequested;

    public JobsViewModel(AppDbContext db, IPrintJobService printJobService, JobEventBus eventBus,
        ILogger<JobsViewModel> logger, ILocalizationService loc)
    {
        _db = db;
        _printJobService = printJobService;
        _eventBus = eventBus;
        _logger = logger;
        _loc = loc;

        _logger.LogTrace("-> JobsViewModel()");

        _eventBus.ProgressChanged += OnJobProgressChanged;
        _eventBus.Completed += OnJobCompleted;
        _eventBus.CountersUpdated += OnCountersUpdated;

        _logger.LogTrace("<- JobsViewModel()");
    }

    [RelayCommand]
    private async Task LoadJobsAsync()
    {
        _logger.LogTrace("-> LoadJobsAsync()");
        try
        {
            var active = await _printJobService.GetActiveJobsAsync();

            ActiveJobs.Clear();
            foreach (var j in active)
                ActiveJobs.Add(j);

            HasActiveJobs = ActiveJobs.Count > 0;

            // Auto-select when there is exactly one active job so the user
            // sees job details immediately without having to click.
            if (ActiveJobs.Count == 1 && SelectedJob == null)
                SelectedJob = ActiveJobs[0];

            await LoadHistoryAsync();
            await LoadFiltersAsync();

            _logger.LogInformation("Jobs page loaded: {ActiveCount} active, {HistoryCount} history",
                ActiveJobs.Count, JobHistory.Count);
            _logger.LogTrace("<- LoadJobsAsync()");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "LoadJobsAsync failed");
            _logger.LogTrace("<- LoadJobsAsync() [exception]");
            throw;
        }
    }

    private async Task LoadFiltersAsync()
    {
        _logger.LogTrace("-> LoadFiltersAsync()");
        try
        {
            var printers = await _db.Printers.AsNoTracking().ToListAsync();
            FilterPrinters.Clear();
            foreach (var p in printers)
                FilterPrinters.Add(p);

            var products = await _db.ProductNodes.AsNoTracking().Where(p => p.IsLeaf).ToListAsync();
            FilterProducts.Clear();
            foreach (var p in products)
                FilterProducts.Add(p);

            _logger.LogTrace("<- LoadFiltersAsync()");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "LoadFiltersAsync failed");
            _logger.LogTrace("<- LoadFiltersAsync() [exception]");
            throw;
        }
    }

    private async Task LoadHistoryAsync()
    {
        _logger.LogTrace("-> LoadHistoryAsync(PrinterId={PrinterId}, ProductId={ProductId})",
            FilterPrinter?.Id, FilterProduct?.Id);
        try
        {
            var history = await _printJobService.GetJobHistoryAsync(
                FilterPrinter?.Id, FilterProduct?.Id);

            JobHistory.Clear();
            foreach (var j in history)
                JobHistory.Add(j);

            _logger.LogTrace("<- LoadHistoryAsync() — {Count} items", JobHistory.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "LoadHistoryAsync failed");
            _logger.LogTrace("<- LoadHistoryAsync() [exception]");
            throw;
        }
    }

    partial void OnFilterPrinterChanged(PrinterEntity? value)
    {
        _logger.LogTrace("-> OnFilterPrinterChanged(PrinterId={PrinterId})", value?.Id);
        _ = LoadHistoryAsync();
        _logger.LogTrace("<- OnFilterPrinterChanged()");
    }

    partial void OnFilterProductChanged(ProductNode? value)
    {
        _logger.LogTrace("-> OnFilterProductChanged(ProductId={ProductId})", value?.Id);
        _ = LoadHistoryAsync();
        _logger.LogTrace("<- OnFilterProductChanged()");
    }

    partial void OnSelectedJobChanged(PrintJob? value)
    {
        _logger.LogTrace("-> OnSelectedJobChanged(JobId={JobId})", value?.Id);
        if (value != null)
            _logger.LogInformation("Job selected: #{JobId} (Product='{Product}', Printer='{Printer}', Status={Status}, Progress={Confirmed}/{Total})",
                value.Id, value.Product?.Name, value.Printer?.Name, value.Status, value.CodesConfirmed, value.Quantity);
        if (value == null)
        {
            JobDetailProduct = string.Empty;
            JobDetailPrinter = string.Empty;
            JobDetailQuantity = 0;
            JobDetailProgress = 0;
            JobDetailProgressText = string.Empty;
            JobDetailStatus = null;
            PrinterCountersText = string.Empty;
            _logger.LogTrace("<- OnSelectedJobChanged() — cleared details");
            return;
        }

        JobDetailProduct = value.Product?.Name ?? _loc["Common_Unknown"];
        JobDetailPrinter = value.Printer != null
            ? _loc.Format("Status_PrinterDisplay", value.Printer.Name, value.Printer.IpAddress)
            : _loc["Common_Unknown"];
        JobDetailQuantity = value.Quantity;
        JobDetailProgress = value.CodesConfirmed;
        JobDetailStatus = value.Status;

        var pct = value.Quantity > 0 ? (int)(100.0 * value.CodesConfirmed / value.Quantity) : 0;
        JobDetailProgressText = _loc.Format("Status_ProgressDisplay", value.CodesConfirmed, value.Quantity, pct);

        // Preparation checklist — if job is past Preparing, all prep steps were completed
        var prepared = value.Status is not JobStatus.Preparing;
        PrepTemplatePresent = prepared;
        PrepCodesReserved = prepared;
        PrepDataUploaded = prepared;
        PrepTemplateLoaded = prepared;

        // Restore cached counters if available, otherwise clear
        PrinterCountersText = _countersCache.GetValueOrDefault(value.Id, string.Empty);
        _logger.LogTrace("<- OnSelectedJobChanged()");
    }

    public void SelectJobById(int jobId)
    {
        _logger.LogTrace("-> SelectJobById(jobId={JobId})", jobId);
        _ = LoadJobsAsync().ContinueWith(_ =>
        {
            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                SelectedJob = ActiveJobs.FirstOrDefault(j => j.Id == jobId)
                    ?? JobHistory.FirstOrDefault(j => j.Id == jobId);
            });
        });
        _logger.LogTrace("<- SelectJobById()");
    }

    [RelayCommand]
    private async Task StartPrintAsync()
    {
        _logger.LogTrace("-> StartPrintAsync(SelectedJobId={JobId})", SelectedJob?.Id);
        if (SelectedJob == null || SelectedJob.Status != JobStatus.Ready)
        {
            _logger.LogTrace("<- StartPrintAsync() — skipped (no selection or not Ready)");
            return;
        }
        try
        {
            _logger.LogInformation("Jobs: Start print for Job {JobId}", SelectedJob.Id);
            await _printJobService.StartJobAsync(SelectedJob.Id);
            await LoadJobsAsync();
            _logger.LogTrace("<- StartPrintAsync()");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "StartPrintAsync failed for Job {JobId}", SelectedJob?.Id);
            _logger.LogTrace("<- StartPrintAsync() [exception]");
            throw;
        }
    }

    [RelayCommand]
    private async Task CancelJobAsync()
    {
        _logger.LogTrace("-> CancelJobAsync(SelectedJobId={JobId})", SelectedJob?.Id);
        if (SelectedJob == null)
        {
            _logger.LogTrace("<- CancelJobAsync() — skipped (no selection)");
            return;
        }
        try
        {
            _logger.LogInformation("Jobs: Cancel for Job {JobId}", SelectedJob.Id);
            await _printJobService.CancelJobAsync(SelectedJob.Id);
            await LoadJobsAsync();
            _logger.LogTrace("<- CancelJobAsync()");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "CancelJobAsync failed for Job {JobId}", SelectedJob?.Id);
            _logger.LogTrace("<- CancelJobAsync() [exception]");
            throw;
        }
    }

    [RelayCommand]
    private async Task PauseJobAsync()
    {
        _logger.LogTrace("-> PauseJobAsync(SelectedJobId={JobId})", SelectedJob?.Id);
        if (SelectedJob == null || SelectedJob.Status != JobStatus.Printing)
        {
            _logger.LogTrace("<- PauseJobAsync() — skipped (no selection or not Printing)");
            return;
        }
        try
        {
            _logger.LogInformation("Jobs: Pause for Job {JobId}", SelectedJob.Id);
            await _printJobService.PauseJobAsync(SelectedJob.Id);
            await LoadJobsAsync();
            _logger.LogTrace("<- PauseJobAsync()");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "PauseJobAsync failed for Job {JobId}", SelectedJob?.Id);
            _logger.LogTrace("<- PauseJobAsync() [exception]");
            throw;
        }
    }

    [RelayCommand]
    private async Task ResumeJobAsync()
    {
        _logger.LogTrace("-> ResumeJobAsync(SelectedJobId={JobId})", SelectedJob?.Id);
        if (SelectedJob == null || SelectedJob.Status != JobStatus.Paused)
        {
            _logger.LogTrace("<- ResumeJobAsync() — skipped (no selection or not Paused)");
            return;
        }
        try
        {
            _logger.LogInformation("Jobs: Resume for Job {JobId}", SelectedJob.Id);
            await _printJobService.ResumeJobAsync(SelectedJob.Id);
            await LoadJobsAsync();
            _logger.LogTrace("<- ResumeJobAsync()");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ResumeJobAsync failed for Job {JobId}", SelectedJob?.Id);
            _logger.LogTrace("<- ResumeJobAsync() [exception]");
            throw;
        }
    }

    [RelayCommand]
    private void NewJob()
    {
        _logger.LogTrace("-> NewJob()");
        _logger.LogInformation("Jobs: New Job clicked");
        NavigateToNewJobRequested?.Invoke(this, EventArgs.Empty);
        _logger.LogTrace("<- NewJob()");
    }

    [RelayCommand]
    private void ClearFilters()
    {
        _logger.LogTrace("-> ClearFilters()");
        _logger.LogDebug("Jobs: Filters cleared");
        FilterPrinter = null;
        FilterProduct = null;
        _logger.LogTrace("<- ClearFilters()");
    }

    private void OnJobProgressChanged(object? sender, JobProgressChangedEvent e)
    {
        _logger.LogTrace("-> OnJobProgressChanged(JobId={JobId}, Confirmed={Confirmed}, Total={Total})",
            e.JobId, e.Confirmed, e.Total);
        System.Windows.Application.Current.Dispatcher.Invoke(() =>
        {
            // Update the job entity in-place (for data integrity).
            // Do NOT replace the item in the collection (ActiveJobs[i] = job) because
            // that fires CollectionChanged/Replace which causes WPF ListBox to deselect
            // the item, hiding the detail pane while printing.
            for (int i = 0; i < ActiveJobs.Count; i++)
            {
                if (ActiveJobs[i].Id == e.JobId)
                {
                    ActiveJobs[i].CodesConfirmed = e.Confirmed;
                    break;
                }
            }

            // Update detail view if this is the selected job
            if (SelectedJob?.Id == e.JobId)
            {
                JobDetailProgress = e.Confirmed;
                var pct = e.Total > 0 ? (int)(100.0 * e.Confirmed / e.Total) : 0;
                JobDetailProgressText = _loc.Format("Status_ProgressDisplay", e.Confirmed, e.Total, pct);
            }
        });
        _logger.LogTrace("<- OnJobProgressChanged()");
    }

    private void OnJobCompleted(object? sender, JobCompletedEvent e)
    {
        _logger.LogTrace("-> OnJobCompleted(JobId={JobId}, FinalStatus={FinalStatus})",
            e.JobId, e.FinalStatus);
        _logger.LogInformation("Jobs: Job {JobId} completed (live)", e.JobId);
        System.Windows.Application.Current.Dispatcher.Invoke(() =>
        {
            // Update the completed job in-place (no async DB query — avoids
            // DbContext concurrency issues and the async-void Dispatcher pitfall)
            for (int i = 0; i < ActiveJobs.Count; i++)
            {
                if (ActiveJobs[i].Id == e.JobId)
                {
                    var job = ActiveJobs[i];
                    job.Status = e.FinalStatus;
                    job.CompletedAt = DateTime.UtcNow;
                    var wasSelected = SelectedJob?.Id == e.JobId;

                    // RemoveAt/Insert forces WPF to fully re-bind the list item.
                    // A plain ActiveJobs[i] = job reuses the same reference and
                    // WPF may skip re-reading property values (e.g. CodesConfirmed).
                    ActiveJobs.RemoveAt(i);
                    ActiveJobs.Insert(i, job);

                    if (wasSelected)
                    {
                        SelectedJob = job; // triggers OnSelectedJobChanged → updates detail pane
                    }
                    break;
                }
            }

            _ = LoadHistoryAsync();
        });
        _logger.LogTrace("<- OnJobCompleted()");
    }

    private void OnCountersUpdated(object? sender, JobCountersUpdatedEvent e)
    {
        System.Windows.Application.Current.Dispatcher.Invoke(() =>
        {
            var parts = new List<string>();
            parts.Add($"{_loc["Jobs_Counter_Current"]} - {e.CurrentCounter}");
            if (e.LifetimeCounter.HasValue)
                parts.Add($"{_loc["Jobs_Counter_Lifetime"]} - {e.LifetimeCounter.Value}");
            parts.Add($"{_loc["Jobs_Counter_Effective"]} - {e.EffectiveCounter}");
            var text = string.Join(",  ", parts);

            _countersCache[e.JobId] = text;

            if (SelectedJob?.Id == e.JobId)
                PrinterCountersText = text;
        });
    }
}
