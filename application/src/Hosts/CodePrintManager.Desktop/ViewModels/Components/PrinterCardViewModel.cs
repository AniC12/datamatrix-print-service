using CodePrintManager.Application.Services;
using CodePrintManager.Domain.Entities;
using CodePrintManager.Domain.Enums;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PrinterEntity = CodePrintManager.Domain.Entities.Printer;

namespace CodePrintManager.Desktop.ViewModels.Components;

public partial class PrinterCardViewModel : ObservableObject
{
    private readonly PrinterConnectionManager _connectionManager;

    public int PrinterId { get; }
    public string Name { get; }
    public string IpAddress { get; }

    [ObservableProperty]
    private PrinterStatus _status = PrinterStatus.Offline;

    [ObservableProperty]
    private int _currentJobProgress;

    [ObservableProperty]
    private int _currentJobTotal;

    [ObservableProperty]
    private string? _currentJobProduct;

    public PrinterCardViewModel(PrinterEntity printer, PrinterConnectionManager connectionManager)
    {
        _connectionManager = connectionManager;
        PrinterId = printer.Id;
        Name = printer.Name;
        IpAddress = printer.IpAddress;
    }

    [RelayCommand]
    private async Task ConnectAsync()
    {
        // This would need the full Printer entity; simplified for now
        await Task.CompletedTask;
    }
}
