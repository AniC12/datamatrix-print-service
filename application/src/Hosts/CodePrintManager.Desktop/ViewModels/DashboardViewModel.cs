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

namespace CodePrintManager.Desktop.ViewModels;

public partial class DashboardViewModel : ObservableObject
{
    private readonly PrinterConnectionManager _connectionManager;
    private readonly IPrintJobService _printJobService;
    private readonly JobEventBus _eventBus;
    private readonly AppDbContext _db;
    private readonly ILogger<DashboardViewModel> _logger;

    public ObservableCollection<Components.PrinterCardViewModel> PrinterCards { get; } = new();
    public ObservableCollection<AuditEntryViewModel> RecentActivity { get; } = new();

    public event EventHandler? NavigateToNewJobRequested;
    public event EventHandler<int>? NavigateToJobRequested;

    public DashboardViewModel(
        PrinterConnectionManager connectionManager,
        IPrintJobService printJobService,
        JobEventBus eventBus,
        AppDbContext db,
        ILogger<DashboardViewModel> logger)
    {
        _connectionManager = connectionManager;
        _printJobService = printJobService;
        _eventBus = eventBus;
        _db = db;
        _logger = logger;

        _connectionManager.PrinterStatusChanged += OnPrinterStatusChanged;
        _eventBus.ProgressChanged += OnJobProgressChanged;
        _eventBus.Completed += OnJobCompleted;
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        _logger.LogInformation("Dashboard loading");
        // Load printers with their most recent job
        var printers = await _db.Printers.ToListAsync();
        PrinterCards.Clear();

        foreach (var p in printers)
        {
            // Get the most recent job for this printer
            var latestJob = await _db.PrintJobs
                .Include(j => j.Product)
                .Include(j => j.Printer)
                .Where(j => j.PrinterId == p.Id)
                .OrderByDescending(j => j.CreatedAt)
                .FirstOrDefaultAsync();

            // Only show printers that have had at least one job (per spec)
            if (latestJob == null)
                continue;

            var card = new Components.PrinterCardViewModel(p, latestJob);
            card.StartPrintRequested += OnStartPrintRequested;
            card.CancelJobRequested += OnCancelJobRequested;
            card.PauseJobRequested += OnPauseJobRequested;
            card.ResumeJobRequested += OnResumeJobRequested;
            card.CardClicked += OnCardClicked;
            PrinterCards.Add(card);
        }

        // Sort: active jobs first (Printing, Paused, Error, Ready), then completed
        var sorted = PrinterCards.OrderBy(c => c.JobStatus switch
        {
            JobStatus.Printing => 0,
            JobStatus.Paused => 1,
            JobStatus.Error => 2,
            JobStatus.Ready => 3,
            JobStatus.Preparing => 4,
            _ => 5
        }).ToList();

        PrinterCards.Clear();
        foreach (var c in sorted)
            PrinterCards.Add(c);

        // Load recent activity
        var recentEntries = await _db.AuditLog
            .OrderByDescending(a => a.CreatedAt)
            .Take(20)
            .ToListAsync();

        RecentActivity.Clear();
        foreach (var entry in recentEntries)
            RecentActivity.Add(new AuditEntryViewModel(entry));

        _logger.LogInformation("Dashboard loaded: {CardCount} printer cards, {ActivityCount} recent entries",
            PrinterCards.Count, RecentActivity.Count);
    }

    [RelayCommand]
    private void NewJob()
    {
        _logger.LogInformation("Dashboard: New Job clicked");
        NavigateToNewJobRequested?.Invoke(this, EventArgs.Empty);
    }

    private async void OnStartPrintRequested(object? sender, int jobId)
    {
        _logger.LogInformation("Dashboard: Start print requested for Job {JobId}", jobId);
        try
        {
            await _printJobService.StartJobAsync(jobId);
            await RefreshAsync();
        }
        catch (Exception ex) { _logger.LogError(ex, "Dashboard: Start print failed for Job {JobId}", jobId); }
    }

    private async void OnCancelJobRequested(object? sender, int jobId)
    {
        _logger.LogInformation("Dashboard: Cancel requested for Job {JobId}", jobId);
        try
        {
            await _printJobService.CancelJobAsync(jobId);
            await RefreshAsync();
        }
        catch (Exception ex) { _logger.LogError(ex, "Dashboard: Cancel failed for Job {JobId}", jobId); }
    }

    private async void OnPauseJobRequested(object? sender, int jobId)
    {
        _logger.LogInformation("Dashboard: Pause requested for Job {JobId}", jobId);
        try
        {
            await _printJobService.PauseJobAsync(jobId);
            await RefreshAsync();
        }
        catch (Exception ex) { _logger.LogError(ex, "Dashboard: Pause failed for Job {JobId}", jobId); }
    }

    private async void OnResumeJobRequested(object? sender, int jobId)
    {
        _logger.LogInformation("Dashboard: Resume requested for Job {JobId}", jobId);
        try
        {
            await _printJobService.ResumeJobAsync(jobId);
            await RefreshAsync();
        }
        catch (Exception ex) { _logger.LogError(ex, "Dashboard: Resume failed for Job {JobId}", jobId); }
    }

    private void OnCardClicked(object? sender, int jobId)
    {
        _logger.LogDebug("Dashboard: Card clicked for Job {JobId}", jobId);
        NavigateToJobRequested?.Invoke(this, jobId);
    }

    private void OnPrinterStatusChanged(object? sender, PrinterStatusChangedEvent e)
    {
        System.Windows.Application.Current.Dispatcher.Invoke(() =>
        {
            var card = PrinterCards.FirstOrDefault(c => c.PrinterId == e.PrinterId);
            if (card != null)
                card.Status = e.NewStatus;
        });
    }

    private void OnJobProgressChanged(object? sender, JobProgressChangedEvent e)
    {
        System.Windows.Application.Current.Dispatcher.Invoke(() =>
        {
            var card = PrinterCards.FirstOrDefault(c => c.JobId == e.JobId);
            if (card != null)
            {
                card.CurrentJobProgress = e.Confirmed;
                card.CurrentJobTotal = e.Total;
                card.JobStatus = JobStatus.Printing;
            }
        });
    }

    private void OnJobCompleted(object? sender, JobCompletedEvent e)
    {
        _logger.LogInformation("Dashboard: Job {JobId} completed", e.JobId);
        System.Windows.Application.Current.Dispatcher.Invoke(async () =>
        {
            await RefreshAsync();
        });
    }
}

public class AuditEntryViewModel
{
    public AuditEntry Entry { get; }
    public string Time => Entry.CreatedAt.ToLocalTime().ToString("HH:mm");
    public string Description { get; }

    public AuditEntryViewModel(AuditEntry entry)
    {
        Entry = entry;
        Description = $"{entry.EventType.Replace('_', ' ')}";
        if (entry.Details != null)
            Description += $" — {entry.Details}";
    }
}
