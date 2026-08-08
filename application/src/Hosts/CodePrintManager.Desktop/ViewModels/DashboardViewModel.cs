using System.Collections.ObjectModel;
using CodePrintManager.Application.Services;
using CodePrintManager.Data;
using CodePrintManager.Domain.Enums;
using CodePrintManager.Domain.Events;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;

namespace CodePrintManager.Desktop.ViewModels;

public partial class DashboardViewModel : ObservableObject
{
    private readonly PrinterConnectionManager _connectionManager;
    private readonly AppDbContext _db;

    public ObservableCollection<Components.PrinterCardViewModel> PrinterCards { get; } = new();

    [ObservableProperty]
    private int _totalActiveJobs;

    [ObservableProperty]
    private int _totalCodesAvailable;

    [ObservableProperty]
    private int _totalCodesPrintedToday;

    public DashboardViewModel(
        PrinterConnectionManager connectionManager,
        AppDbContext db)
    {
        _connectionManager = connectionManager;
        _db = db;

        _connectionManager.PrinterStatusChanged += OnPrinterStatusChanged;
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        var printers = await _db.Printers.ToListAsync();
        PrinterCards.Clear();
        foreach (var p in printers)
        {
            PrinterCards.Add(new Components.PrinterCardViewModel(p, _connectionManager));
        }

        TotalActiveJobs = await _db.PrintJobs
            .CountAsync(j => j.Status == JobStatus.Printing || j.Status == JobStatus.Ready);

        TotalCodesAvailable = await _db.Codes
            .CountAsync(c => c.Status == CodeStatus.Available);

        var today = DateTime.UtcNow.Date;
        TotalCodesPrintedToday = await _db.Codes
            .CountAsync(c => c.Status == CodeStatus.Printed && c.StatusChangedAt >= today);
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
