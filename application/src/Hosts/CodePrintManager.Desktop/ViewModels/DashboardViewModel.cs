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

namespace CodePrintManager.Desktop.ViewModels;

public partial class DashboardViewModel : ObservableObject
{
    private readonly PrinterConnectionManager _connectionManager;
    private readonly IPrintJobService _printJobService;
    private readonly AppDbContext _db;

    public ObservableCollection<Components.PrinterCardViewModel> PrinterCards { get; } = new();
    public ObservableCollection<AuditEntryViewModel> RecentActivity { get; } = new();

    public event EventHandler? NavigateToNewJobRequested;
    public event EventHandler<int>? NavigateToJobRequested;

    public DashboardViewModel(
        PrinterConnectionManager connectionManager,
        IPrintJobService printJobService,
        AppDbContext db)
    {
        _connectionManager = connectionManager;
        _printJobService = printJobService;
        _db = db;

        _connectionManager.PrinterStatusChanged += OnPrinterStatusChanged;
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
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
            card.CardClicked += OnCardClicked;
            PrinterCards.Add(card);
        }

        // Sort: active jobs first (Printing, Error, Ready), then completed
        var sorted = PrinterCards.OrderBy(c => c.JobStatus switch
        {
            JobStatus.Printing => 0,
            JobStatus.Error => 1,
            JobStatus.Ready => 2,
            JobStatus.Preparing => 3,
            _ => 4
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
    }

    [RelayCommand]
    private void NewJob() => NavigateToNewJobRequested?.Invoke(this, EventArgs.Empty);

    private async void OnStartPrintRequested(object? sender, int jobId)
    {
        try
        {
            await _printJobService.StartJobAsync(jobId);
            await RefreshAsync();
        }
        catch { /* Alert service will handle errors */ }
    }

    private async void OnCancelJobRequested(object? sender, int jobId)
    {
        try
        {
            await _printJobService.CancelJobAsync(jobId);
            await RefreshAsync();
        }
        catch { /* Alert service will handle errors */ }
    }

    private void OnCardClicked(object? sender, int jobId)
    {
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
