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

    [ObservableProperty]
    private bool _showPrepProgress;

    // Preparation progress steps
    [ObservableProperty]
    private bool _prepVerified;

    [ObservableProperty]
    private bool _prepCodesReserved;

    [ObservableProperty]
    private bool _prepDataUploaded;

    [ObservableProperty]
    private bool _prepTemplateLoaded;

    [ObservableProperty]
    private bool _prepComplete;

    [ObservableProperty]
    private bool _prepFailed;

    [ObservableProperty]
    private string? _prepErrorMessage;

    [ObservableProperty]
    private int? _createdJobId;

    public event EventHandler? NavigateBackRequested;
    public event EventHandler<int>? NavigateToJobRequested;

    // Preselection IDs (set before Load)
    private int? _preselectProductId;
    private int? _preselectPrinterId;

    public NewJobViewModel(
        IPrintJobService printJobService,
        ICodePoolService codePoolService,
        AppDbContext db)
    {
        _printJobService = printJobService;
        _codePoolService = codePoolService;
        _db = db;
    }

    public void Reset(int? productId = null, int? printerId = null)
    {
        _preselectProductId = productId;
        _preselectPrinterId = printerId;
        SelectedProduct = null;
        SelectedPrinter = null;
        Quantity = 0;
        AvailableCodes = 0;
        IsProcessing = false;
        StatusMessage = null;
        ShowPrepProgress = false;
        PrepVerified = false;
        PrepCodesReserved = false;
        PrepDataUploaded = false;
        PrepTemplateLoaded = false;
        PrepComplete = false;
        PrepFailed = false;
        PrepErrorMessage = null;
        CreatedJobId = null;
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

        // Apply preselection
        if (_preselectProductId.HasValue)
            SelectedProduct = Products.FirstOrDefault(p => p.Id == _preselectProductId.Value);
        if (_preselectPrinterId.HasValue)
            SelectedPrinter = Printers.FirstOrDefault(p => p.Id == _preselectPrinterId.Value);
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
    private async Task PrepareAsync()
    {
        if (SelectedProduct == null || SelectedPrinter == null || Quantity <= 0)
            return;

        IsProcessing = true;
        ShowPrepProgress = true;
        PrepVerified = false;
        PrepCodesReserved = false;
        PrepDataUploaded = false;
        PrepTemplateLoaded = false;
        PrepComplete = false;
        PrepFailed = false;
        PrepErrorMessage = null;
        StatusMessage = "Preparing job...";

        try
        {
            // Step 1: Create job
            var job = await _printJobService.CreateJobAsync(
                SelectedProduct.Id, SelectedPrinter.Id, Quantity);
            CreatedJobId = job.Id;

            PrepVerified = true;
            StatusMessage = "Reserving codes...";

            // Step 2: Prepare (reserves codes, uploads CSV, activates template)
            await _printJobService.PrepareJobAsync(job.Id);

            PrepCodesReserved = true;
            PrepDataUploaded = true;
            PrepTemplateLoaded = true;
            PrepComplete = true;
            StatusMessage = $"Job #{job.Id} is ready to print.";
        }
        catch (Exception ex)
        {
            PrepFailed = true;
            PrepErrorMessage = ex.Message;
            StatusMessage = $"Preparation failed: {ex.Message}";
        }
        finally
        {
            IsProcessing = false;
        }
    }

    [RelayCommand]
    private async Task StartPrintAsync()
    {
        if (CreatedJobId == null) return;

        try
        {
            await _printJobService.StartJobAsync(CreatedJobId.Value);
            NavigateToJobRequested?.Invoke(this, CreatedJobId.Value);
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error starting print: {ex.Message}";
        }
    }

    [RelayCommand]
    private void GoToJob()
    {
        if (CreatedJobId.HasValue)
            NavigateToJobRequested?.Invoke(this, CreatedJobId.Value);
    }

    [RelayCommand]
    private async Task RetryPrepareAsync()
    {
        // Reset prep state and try again
        PrepFailed = false;
        PrepErrorMessage = null;
        await PrepareAsync();
    }

    [RelayCommand]
    private void GoBack() => NavigateBackRequested?.Invoke(this, EventArgs.Empty);
}
