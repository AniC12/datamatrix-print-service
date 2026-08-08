using System.Collections.ObjectModel;
using CodePrintManager.Data;
using CodePrintManager.Domain.Entities;
using CodePrintManager.Domain.Enums;
using CodePrintManager.Domain.Interfaces;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using PrinterEntity = CodePrintManager.Domain.Entities.Printer;

namespace CodePrintManager.Desktop.ViewModels;

public partial class NewJobViewModel : ObservableObject
{
    private readonly IPrintJobService _printJobService;
    private readonly ICodePoolService _codePoolService;
    private readonly AppDbContext _db;

    public ObservableCollection<ProductNode> Products { get; } = new();
    public ObservableCollection<PrinterEntity> Printers { get; } = new();

    [ObservableProperty]
    private ProductNode? _selectedProduct;

    [ObservableProperty]
    private PrinterEntity? _selectedPrinter;

    [ObservableProperty]
    private int _quantity;

    [ObservableProperty]
    private int _availableCodes;

    [ObservableProperty]
    private bool _isProcessing;

    [ObservableProperty]
    private string? _statusMessage;

    public NewJobViewModel(
        IPrintJobService printJobService,
        ICodePoolService codePoolService,
        AppDbContext db)
    {
        _printJobService = printJobService;
        _codePoolService = codePoolService;
        _db = db;
    }

    [RelayCommand]
    private async Task LoadAsync()
    {
        var products = await _db.ProductNodes.Where(p => p.IsLeaf).ToListAsync();
        Products.Clear();
        foreach (var p in products)
            Products.Add(p);

        var printers = await _db.Printers.ToListAsync();
        Printers.Clear();
        foreach (var p in printers)
            Printers.Add(p);
    }

    partial void OnSelectedProductChanged(ProductNode? value)
    {
        if (value != null)
            _ = RefreshAvailableAsync(value.Id);
    }

    private async Task RefreshAvailableAsync(int productId)
    {
        AvailableCodes = await _codePoolService.GetAvailableCountAsync(productId);
    }

    [RelayCommand]
    private async Task StartJobAsync()
    {
        if (SelectedProduct == null || SelectedPrinter == null || Quantity <= 0)
            return;

        IsProcessing = true;
        StatusMessage = "Preparing job...";

        try
        {
            var job = await _printJobService.CreateJobAsync(
                SelectedProduct.Id, SelectedPrinter.Id, Quantity);
            await _printJobService.PrepareJobAsync(job.Id);
            await _printJobService.StartJobAsync(job.Id);
            StatusMessage = $"Job #{job.Id} started successfully.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error: {ex.Message}";
        }
        finally
        {
            IsProcessing = false;
        }
    }
}
