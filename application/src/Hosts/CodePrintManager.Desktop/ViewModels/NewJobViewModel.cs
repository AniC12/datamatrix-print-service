using System.Collections.ObjectModel;
using CodePrintManager.Data;
using CodePrintManager.Domain.Entities;
using CodePrintManager.Domain.Enums;
using CodePrintManager.Domain.Interfaces;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PrinterEntity = CodePrintManager.Domain.Entities.Printer;

namespace CodePrintManager.Desktop.ViewModels;

public partial class NewJobViewModel : ObservableObject
{
    private readonly IPrintJobService _printJobService;
    private readonly ICodePoolService _codePoolService;
    private readonly AppDbContext _db;
    private readonly ILogger<NewJobViewModel> _logger;

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
        AppDbContext db,
        ILogger<NewJobViewModel> logger)
    {
        _printJobService = printJobService;
        _codePoolService = codePoolService;
        _db = db;
        _logger = logger;
    }

    public void Reset(int? productId = null, int? printerId = null)
    {
        _logger.LogInformation("NewJob: Wizard opened (preselect Product={ProductId}, Printer={PrinterId})",
            productId, printerId);
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
        {
            _logger.LogInformation("NewJob: Product selected '{Name}' (Id={Id})", value.Name, value.Id);
            _ = RefreshAvailableAsync(value.Id);
        }
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
        ShowPrepProgress = false;
        PrepVerified = false;
        PrepCodesReserved = false;
        PrepDataUploaded = false;
        PrepTemplateLoaded = false;
        PrepComplete = false;
        PrepFailed = false;
        PrepErrorMessage = null;
        StatusMessage = "Creating job...";

        try
        {
            _logger.LogInformation("NewJob: Prepare started (Product={ProductId}, Printer={PrinterId}, Qty={Qty})",
                SelectedProduct.Id, SelectedPrinter.Id, Quantity);

            // Step 1: Create job (validates quantity, available codes, etc.)
            var job = await _printJobService.CreateJobAsync(
                SelectedProduct.Id, SelectedPrinter.Id, Quantity);
            CreatedJobId = job.Id;
            ShowPrepProgress = true;
            StatusMessage = "Preparing job...";
            _logger.LogInformation("NewJob: Job #{JobId} created", job.Id);

            // Step 2: Prepare with step-by-step progress
            var progress = new Progress<string>(step =>
            {
                switch (step)
                {
                    case "checking_printer":
                        StatusMessage = "Checking printer state...";
                        break;
                    case "printer_verified":
                        PrepVerified = true;
                        StatusMessage = "Reserving codes...";
                        break;
                    case "reserving_codes":
                        StatusMessage = "Reserving codes...";
                        break;
                    case "codes_reserved":
                        PrepCodesReserved = true;
                        StatusMessage = "Uploading data file...";
                        break;
                    case "uploading_data":
                        StatusMessage = "Uploading data file...";
                        break;
                    case "data_uploaded":
                        PrepDataUploaded = true;
                        StatusMessage = "Loading template...";
                        break;
                    case "loading_template":
                        StatusMessage = "Loading template...";
                        break;
                    case "template_loaded":
                        PrepTemplateLoaded = true;
                        break;
                    case "complete":
                        PrepComplete = true;
                        StatusMessage = $"Job #{job.Id} is ready to print.";
                        break;
                }
            });

            await _printJobService.PrepareJobAsync(job.Id, progress: progress);

            // Ensure final state is set even if progress callbacks were missed
            if (!PrepComplete)
            {
                PrepVerified = true;
                PrepCodesReserved = true;
                PrepDataUploaded = true;
                PrepTemplateLoaded = true;
                PrepComplete = true;
                StatusMessage = $"Job #{job.Id} is ready to print.";
            }
            _logger.LogInformation("NewJob: Job #{JobId} prepared successfully → Ready", job.Id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "NewJob: Preparation failed for Job #{JobId}", CreatedJobId);
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
        _logger.LogInformation("NewJob: Start print for Job #{JobId}", CreatedJobId.Value);

        try
        {
            await _printJobService.StartJobAsync(CreatedJobId.Value);
            NavigateToJobRequested?.Invoke(this, CreatedJobId.Value);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "NewJob: Start print failed for Job #{JobId}", CreatedJobId.Value);
            StatusMessage = $"Error starting print: {ex.Message}";
        }
    }

    [RelayCommand]
    private void GoToJob()
    {
        _logger.LogDebug("NewJob: Navigate to Job #{JobId}", CreatedJobId);
        if (CreatedJobId.HasValue)
            NavigateToJobRequested?.Invoke(this, CreatedJobId.Value);
    }

    [RelayCommand]
    private async Task RetryPrepareAsync()
    {
        _logger.LogInformation("NewJob: Retry preparation (previous Job #{JobId} failed)", CreatedJobId);
        // Reset all prep state — failed job was already cancelled by PrepareJobAsync
        CreatedJobId = null;
        PrepFailed = false;
        PrepErrorMessage = null;
        PrepVerified = false;
        PrepCodesReserved = false;
        PrepDataUploaded = false;
        PrepTemplateLoaded = false;
        PrepComplete = false;
        await PrepareAsync();
    }

    [RelayCommand]
    private void GoBack()
    {
        _logger.LogDebug("NewJob: Navigate back");
        NavigateBackRequested?.Invoke(this, EventArgs.Empty);
    }
}
