using System.Collections.ObjectModel;
using CodePrintManager.Application.Services;
using CodePrintManager.Data;
using CodePrintManager.Domain.Entities;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using PrinterEntity = CodePrintManager.Domain.Entities.Printer;

namespace CodePrintManager.Desktop.ViewModels;

public partial class PrintersViewModel : ObservableObject
{
    private readonly AppDbContext _db;
    private readonly PrinterConnectionManager _connectionManager;

    public ObservableCollection<PrinterEntity> Printers { get; } = new();

    [ObservableProperty]
    private PrinterEntity? _selectedPrinter;

    [ObservableProperty]
    private string _newPrinterName = string.Empty;

    [ObservableProperty]
    private string _newPrinterIp = string.Empty;

    [ObservableProperty]
    private int _newPrinterPort = 9100;

    [ObservableProperty]
    private string _newPrinterAdapterType = "savema_tto";

    public PrintersViewModel(AppDbContext db, PrinterConnectionManager connectionManager)
    {
        _db = db;
        _connectionManager = connectionManager;
    }

    [RelayCommand]
    private async Task LoadPrintersAsync()
    {
        var printers = await _db.Printers.ToListAsync();
        Printers.Clear();
        foreach (var p in printers)
            Printers.Add(p);
    }

    [RelayCommand]
    private async Task AddPrinterAsync()
    {
        var printer = new PrinterEntity
        {
            Name = NewPrinterName,
            IpAddress = NewPrinterIp,
            Port = NewPrinterPort,
            AdapterType = NewPrinterAdapterType
        };

        _db.Printers.Add(printer);
        await _db.SaveChangesAsync();

        NewPrinterName = string.Empty;
        NewPrinterIp = string.Empty;
        NewPrinterPort = 9100;

        await LoadPrintersAsync();
    }

    [RelayCommand]
    private async Task ConnectPrinterAsync()
    {
        if (SelectedPrinter == null) return;
        await _connectionManager.ConnectAsync(SelectedPrinter);
    }

    [RelayCommand]
    private async Task DisconnectPrinterAsync()
    {
        if (SelectedPrinter == null) return;
        await _connectionManager.DisconnectAsync(SelectedPrinter.Id);
    }

    [RelayCommand]
    private async Task DeletePrinterAsync()
    {
        if (SelectedPrinter == null) return;
        await _connectionManager.DisconnectAsync(SelectedPrinter.Id);
        _db.Printers.Remove(SelectedPrinter);
        await _db.SaveChangesAsync();
        SelectedPrinter = null;
        await LoadPrintersAsync();
    }
}
