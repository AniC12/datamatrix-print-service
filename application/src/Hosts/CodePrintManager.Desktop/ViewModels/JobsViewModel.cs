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
using PrinterEntity = CodePrintManager.Domain.Entities.Printer;

namespace CodePrintManager.Desktop.ViewModels;

public partial class JobsViewModel : ObservableObject
{
    private readonly AppDbContext _db;
    private readonly IPrintJobService _printJobService;
    private readonly JobEventBus _eventBus;

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

    public JobsViewModel(AppDbContext db, IPrintJobService printJobService, JobEventBus eventBus)
    {
        _db = db;
        _printJobService = printJobService;
        _eventBus = eventBus;

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

        JobDetailProduct = value.Product?.Name ?? "Unknown";
        JobDetailPrinter = value.Printer != null
            ? $"{value.Printer.Name} ({value.Printer.IpAddress})"
            : "Unknown";
        JobDetailQuantity = value.Quantity;
        JobDetailProgress = value.CodesConfirmed;
        JobDetailStatus = value.Status;

        var pct = value.Quantity > 0 ? (int)(100.0 * value.CodesConfirmed / value.Quantity) : 0;
        JobDetailProgressText = $"{value.CodesConfirmed} / {value.Quantity}  ({pct}%)";

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
        await _printJobService.StartJobAsync(SelectedJob.Id);
        await LoadJobsAsync();
    }

    [RelayCommand]
    private async Task CancelJobAsync()
    {
        if (SelectedJob == null) return;
        await _printJobService.CancelJobAsync(SelectedJob.Id);
        await LoadJobsAsync();
    }

    [RelayCommand]
    private void PauseJob()
    {
        // Placeholder — will be wired in E6-3 (Pause/Resume support)
    }

    [RelayCommand]
    private void NewJob() => NavigateToNewJobRequested?.Invoke(this, EventArgs.Empty);

    [RelayCommand]
    private void ClearFilters()
    {
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
                JobDetailProgressText = $"{e.Confirmed} / {e.Total}  ({pct}%)";
            }
        });
    }

    private void OnJobCompleted(object? sender, JobCompletedEvent e)
    {
        System.Windows.Application.Current.Dispatcher.Invoke(async () =>
        {
            // Update the completed job in-place so it stays displayed
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
                // Reload the job from DB to get final status and timestamps
                var freshJob = await _db.PrintJobs
                    .Include(j => j.Product)
                    .Include(j => j.Printer)
                    .FirstOrDefaultAsync(j => j.Id == e.JobId);
                if (freshJob != null)
                {
                    ActiveJobs[index] = freshJob;
                    if (SelectedJob?.Id == e.JobId)
                    {
                        SelectedJob = freshJob;
                    }
                }
            }

            // Also refresh the history tab
            await LoadHistoryAsync();
        });
    }
}
