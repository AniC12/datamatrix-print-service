using System.Collections.ObjectModel;
using CodePrintManager.Application.Services;
using CodePrintManager.Data;
using CodePrintManager.Domain.Entities;
using CodePrintManager.Domain.Enums;
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
    private PrinterStatus _selectedPrinterStatus = PrinterStatus.Offline;

    // Configuration fields
    [ObservableProperty]
    private string _newPrinterName = string.Empty;

    [ObservableProperty]
    private string _newPrinterIp = string.Empty;

    [ObservableProperty]
    private int _newPrinterPort = 9100;

    [ObservableProperty]
    private string _newPrinterAdapterType = "savema_tto";

    [ObservableProperty]
    private bool _isAddingPrinter;

    // Storage tab
    public ObservableCollection<PrinterFileItem> TemplateFiles { get; } = new();
    public ObservableCollection<PrinterFileItem> CsvFiles { get; } = new();

    [ObservableProperty]
    private int _selectedDeleteCount;

    public event EventHandler<int>? NavigateToNewJobRequested;

    public PrintersViewModel(AppDbContext db, PrinterConnectionManager connectionManager)
    {
        _db = db;
        _connectionManager = connectionManager;
        _connectionManager.PrinterStatusChanged += (_, e) =>
        {
            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                if (SelectedPrinter?.Id == e.PrinterId)
                    SelectedPrinterStatus = e.NewStatus;
            });
        };
    }

    [RelayCommand]
    private async Task LoadPrintersAsync()
    {
        var printers = await _db.Printers.ToListAsync();
        Printers.Clear();
        foreach (var p in printers)
            Printers.Add(p);

        if (SelectedPrinter == null && Printers.Count > 0)
            SelectedPrinter = Printers[0];
    }

    partial void OnSelectedPrinterChanged(PrinterEntity? value)
    {
        if (value == null) return;
        // Update status from connection manager
        var adapter = _connectionManager.GetAdapter(value.Id);
        SelectedPrinterStatus = adapter != null ? PrinterStatus.Idle : PrinterStatus.Offline;
        _ = RefreshStorageAsync();
    }

    [RelayCommand]
    private void ShowAddPrinter()
    {
        IsAddingPrinter = true;
        NewPrinterName = string.Empty;
        NewPrinterIp = string.Empty;
        NewPrinterPort = 9100;
        NewPrinterAdapterType = "savema_tto";
    }

    [RelayCommand]
    private void CancelAddPrinter() => IsAddingPrinter = false;

    [RelayCommand]
    private async Task ConfirmAddPrinterAsync()
    {
        if (string.IsNullOrWhiteSpace(NewPrinterName) || string.IsNullOrWhiteSpace(NewPrinterIp))
            return;

        var printer = new PrinterEntity
        {
            Name = NewPrinterName.Trim(),
            IpAddress = NewPrinterIp.Trim(),
            Port = NewPrinterPort,
            AdapterType = NewPrinterAdapterType
        };

        _db.Printers.Add(printer);
        await _db.SaveChangesAsync();
        IsAddingPrinter = false;
        await LoadPrintersAsync();
        SelectedPrinter = Printers.FirstOrDefault(p => p.Id == printer.Id);
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
        SelectedPrinterStatus = PrinterStatus.Offline;
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

    [RelayCommand]
    private async Task RefreshStorageAsync()
    {
        TemplateFiles.Clear();
        CsvFiles.Clear();
        SelectedDeleteCount = 0;

        if (SelectedPrinter == null) return;

        var adapter = _connectionManager.GetAdapter(SelectedPrinter.Id);
        if (adapter == null) return;

        try
        {
            // Get templates from printer
            var templates = await adapter.ListTemplatesAsync();
            var products = await _db.ProductNodes.Where(p => p.IsLeaf).ToListAsync();

            foreach (var t in templates)
            {
                var mapped = products.FirstOrDefault(p => p.TemplateFile == t);
                var item = new PrinterFileItem(t, mapped?.Name);
                item.PropertyChanged += (_, _) => UpdateDeleteCount();
                TemplateFiles.Add(item);
            }

            // Get CSV/data files from printer
            var dataFiles = await adapter.ListCsvFilesAsync();
            foreach (var d in dataFiles)
            {
                var mapped = products.FirstOrDefault(p => p.PrinterCsvName == d);
                var item = new PrinterFileItem(d, mapped?.Name);
                // Pre-select orphaned files
                if (mapped == null) item.IsSelected = true;
                item.PropertyChanged += (_, _) => UpdateDeleteCount();
                CsvFiles.Add(item);
            }

            UpdateDeleteCount();
        }
        catch
        {
            // Printer may not be connected
        }
    }

    private void UpdateDeleteCount()
    {
        SelectedDeleteCount = TemplateFiles.Count(f => f.IsSelected) + CsvFiles.Count(f => f.IsSelected);
    }

    [RelayCommand]
    private async Task DeleteSelectedFilesAsync()
    {
        if (SelectedPrinter == null) return;
        var adapter = _connectionManager.GetAdapter(SelectedPrinter.Id);
        if (adapter == null) return;

        foreach (var f in CsvFiles.Where(f => f.IsSelected).ToList())
        {
            await adapter.DeleteCsvAsync(f.FileName);
        }

        // Templates can't easily be deleted via SPPL in many cases, but attempt it
        await RefreshStorageAsync();
    }

    [RelayCommand]
    private void NewJob()
    {
        if (SelectedPrinter != null)
            NavigateToNewJobRequested?.Invoke(this, SelectedPrinter.Id);
    }
}

public partial class PrinterFileItem : ObservableObject
{
    public string FileName { get; }
    public string? MappedProduct { get; }
    public bool IsMapped => MappedProduct != null;
    public string StatusText => IsMapped ? $"Used ({MappedProduct})" : "Not mapped to any product";

    [ObservableProperty]
    private bool _isSelected;

    public PrinterFileItem(string fileName, string? mappedProduct)
    {
        FileName = fileName;
        MappedProduct = mappedProduct;
    }
}
