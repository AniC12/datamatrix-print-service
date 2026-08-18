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

    public event EventHandler? NavigateToNewJobRequested;

    public JobsViewModel(AppDbContext db, IPrintJobService printJobService, JobEventBus eventBus,
        ILogger<JobsViewModel> logger, ILocalizationService loc)
    {
        _db = db;
        _printJobService = printJobService;
        _eventBus = eventBus;
        _logger = logger;
        _loc = loc;

        _eventBus.ProgressChanged += OnJobProgressChanged;
        _eventBus.Completed += OnJobCompleted;
    }

    [RelayCommand]
    private async Task LoadJobsAsync()
    {
        var active = await _printJobService.GetActiveJobsAsync();

        ActiveJobs.Clear();
        foreach (var j in active)
            ActiveJobs.Add(j);

        HasActiveJobs = ActiveJobs.Count > 0;

        await LoadHistoryAsync();
        await LoadFiltersAsync();

        _logger.LogInformation("Jobs page loaded: {ActiveCount} active, {HistoryCount} history",
            ActiveJobs.Count, JobHistory.Count);
    }

    private async Task LoadFiltersAsync()
    {
        var printers = await _db.Printers.ToListAsync();
        FilterPrinters.Clear();
        foreach (var p in printers)
            FilterPrinters.Add(p);

        var products = await _db.ProductNodes.Where(p => p.IsLeaf).ToListAsync();
        FilterProducts.Clear();
        foreach (var p in products)
            FilterProducts.Add(p);
    }

    private async Task LoadHistoryAsync()
    {
        var history = await _printJobService.GetJobHistoryAsync(
            FilterPrinter?.Id, FilterProduct?.Id);

        JobHistory.Clear();
        foreach (var j in history)
            JobHistory.Add(j);
    }

    partial void OnFilterPrinterChanged(PrinterEntity? value) => _ = LoadHistoryAsync();
    partial void OnFilterProductChanged(ProductNode? value) => _ = LoadHistoryAsync();

    partial void OnSelectedJobChanged(PrintJob? value)
    {
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
    }

    public void SelectJobById(int jobId)
    {
        _ = LoadJobsAsync().ContinueWith(_ =>
        {
            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                SelectedJob = ActiveJobs.FirstOrDefault(j => j.Id == jobId)
                    ?? JobHistory.FirstOrDefault(j => j.Id == jobId);
            });
        });
    }

    [RelayCommand]
    private async Task StartPrintAsync()
    {
        if (SelectedJob == null || SelectedJob.Status != JobStatus.Ready) return;
        _logger.LogInformation("Jobs: Start print for Job {JobId}", SelectedJob.Id);
        await _printJobService.StartJobAsync(SelectedJob.Id);
        await LoadJobsAsync();
    }

    [RelayCommand]
    private async Task CancelJobAsync()
    {
        if (SelectedJob == null) return;
        _logger.LogInformation("Jobs: Cancel for Job {JobId}", SelectedJob.Id);
        await _printJobService.CancelJobAsync(SelectedJob.Id);
        await LoadJobsAsync();
    }

    [RelayCommand]
    private async Task PauseJobAsync()
    {
        if (SelectedJob == null || SelectedJob.Status != JobStatus.Printing) return;
        _logger.LogInformation("Jobs: Pause for Job {JobId}", SelectedJob.Id);
        await _printJobService.PauseJobAsync(SelectedJob.Id);
        await LoadJobsAsync();
    }

    [RelayCommand]
    private async Task ResumeJobAsync()
    {
        if (SelectedJob == null || SelectedJob.Status != JobStatus.Paused) return;
        _logger.LogInformation("Jobs: Resume for Job {JobId}", SelectedJob.Id);
        await _printJobService.ResumeJobAsync(SelectedJob.Id);
        await LoadJobsAsync();
    }

    [RelayCommand]
    private void NewJob()
    {
        _logger.LogInformation("Jobs: New Job clicked");
        NavigateToNewJobRequested?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void ClearFilters()
    {
        _logger.LogDebug("Jobs: Filters cleared");
        FilterPrinter = null;
        FilterProduct = null;
    }

    private void OnJobProgressChanged(object? sender, JobProgressChangedEvent e)
    {
        System.Windows.Application.Current.Dispatcher.Invoke(() =>
        {
            // Update the active job in the list and re-insert to trigger binding refresh
            var index = -1;
            for (int i = 0; i < ActiveJobs.Count; i++)
            {
                if (ActiveJobs[i].Id == e.JobId)
                {
                    index = i;
                    break;
                }
            }
            if (index >= 0)
            {
                var job = ActiveJobs[index];
                job.CodesConfirmed = e.Confirmed;
                ActiveJobs[index] = job; // triggers CollectionChanged → re-binds list item
            }

            // Update detail view if this is the selected job
            if (SelectedJob?.Id == e.JobId)
            {
                JobDetailProgress = e.Confirmed;
                var pct = e.Total > 0 ? (int)(100.0 * e.Confirmed / e.Total) : 0;
                JobDetailProgressText = _loc.Format("Status_ProgressDisplay", e.Confirmed, e.Total, pct);
            }
        });
    }

    private void OnJobCompleted(object? sender, JobCompletedEvent e)
    {
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
                    ActiveJobs[i] = job; // triggers CollectionChanged → re-binds list item

                    if (SelectedJob?.Id == e.JobId)
                    {
                        SelectedJob = job; // triggers OnSelectedJobChanged → updates detail pane
                    }
                    break;
                }
            }
        });
    }
}
