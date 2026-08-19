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
    private readonly ILocalizationService _loc;

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
        ILogger<NewJobViewModel> logger,
        ILocalizationService loc)
    {
        _printJobService = printJobService;
        _codePoolService = codePoolService;
        _db = db;
        _logger = logger;
        _loc = loc;
        _logger.LogTrace("-> NewJobViewModel()");
        _logger.LogTrace("<- NewJobViewModel()");
    }

    public void Reset(int? productId = null, int? printerId = null)
    {
        _logger.LogTrace("-> Reset(productId={ProductId}, printerId={PrinterId})", productId, printerId);
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
        _logger.LogTrace("<- Reset()");
    }

    [RelayCommand]
    private async Task LoadAsync()
    {
        _logger.LogTrace("-> LoadAsync()");
        try
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

            _logger.LogTrace("<- LoadAsync() — {ProductCount} products, {PrinterCount} printers",
                Products.Count, Printers.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "LoadAsync failed");
            _logger.LogTrace("<- LoadAsync() [exception]");
            throw;
        }
    }

    partial void OnSelectedProductChanged(ProductNode? value)
    {
        _logger.LogTrace("-> OnSelectedProductChanged(ProductId={ProductId}, Name={Name})",
            value?.Id, value?.Name);
        if (value != null)
        {
            _logger.LogInformation("NewJob: Product selected '{Name}' (Id={Id})", value.Name, value.Id);
            _ = RefreshAvailableAsync(value.Id);
        }
        _logger.LogTrace("<- OnSelectedProductChanged()");
    }

    private async Task RefreshAvailableAsync(int productId)
    {
        _logger.LogTrace("-> RefreshAvailableAsync(productId={ProductId})", productId);
        try
        {
            AvailableCodes = await _codePoolService.GetAvailableCountAsync(productId);
            _logger.LogTrace("<- RefreshAvailableAsync() — AvailableCodes={AvailableCodes}", AvailableCodes);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "RefreshAvailableAsync failed for ProductId={ProductId}", productId);
            _logger.LogTrace("<- RefreshAvailableAsync() [exception]");
            throw;
        }
    }

    [RelayCommand]
    private async Task PrepareAsync()
    {
        _logger.LogTrace("-> PrepareAsync(ProductId={ProductId}, PrinterId={PrinterId}, Qty={Qty})",
            SelectedProduct?.Id, SelectedPrinter?.Id, Quantity);
        if (SelectedProduct == null || SelectedPrinter == null || Quantity <= 0)
        {
            _logger.LogTrace("<- PrepareAsync() — skipped (invalid input)");
            return;
        }

        IsProcessing = true;
        ShowPrepProgress = false;
        PrepVerified = false;
        PrepCodesReserved = false;
        PrepDataUploaded = false;
        PrepTemplateLoaded = false;
        PrepComplete = false;
        PrepFailed = false;
        PrepErrorMessage = null;
        StatusMessage = _loc["Progress_CreatingJob"];

        try
        {
            _logger.LogInformation("NewJob: Prepare started (Product={ProductId}, Printer={PrinterId}, Qty={Qty})",
                SelectedProduct.Id, SelectedPrinter.Id, Quantity);

            // Step 1: Create job (validates quantity, available codes, etc.)
            var job = await _printJobService.CreateJobAsync(
                SelectedProduct.Id, SelectedPrinter.Id, Quantity);
            CreatedJobId = job.Id;
            ShowPrepProgress = true;
            StatusMessage = _loc["Progress_PreparingJob"];
            _logger.LogInformation("NewJob: Job #{JobId} created", job.Id);

            // Step 2: Prepare with step-by-step progress
            var progress = new Progress<string>(step =>
            {
                _logger.LogTrace("PrepareAsync progress step: {Step} for Job #{JobId}", step, job.Id);
                switch (step)
                {
                    case "checking_printer":
                        StatusMessage = _loc["Progress_CheckingPrinter"];
                        break;
                    case "printer_verified":
                        PrepVerified = true;
                        StatusMessage = _loc["Progress_ReservingCodes"];
                        break;
                    case "reserving_codes":
                        StatusMessage = _loc["Progress_ReservingCodes"];
                        break;
                    case "codes_reserved":
                        PrepCodesReserved = true;
                        StatusMessage = _loc["Progress_UploadingData"];
                        break;
                    case "uploading_data":
                        StatusMessage = _loc["Progress_UploadingData"];
                        break;
                    case "data_uploaded":
                        PrepDataUploaded = true;
                        StatusMessage = _loc["Progress_LoadingTemplate"];
                        break;
                    case "loading_template":
                        StatusMessage = _loc["Progress_LoadingTemplate"];
                        break;
                    case "template_loaded":
                        PrepTemplateLoaded = true;
                        break;
                    case "complete":
                        PrepComplete = true;
                        StatusMessage = _loc.Format("Status_JobReady", job.Id);
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
                StatusMessage = _loc.Format("Status_JobReady", job.Id);
            }
            _logger.LogInformation("NewJob: Job #{JobId} prepared successfully → Ready", job.Id);
            _logger.LogTrace("<- PrepareAsync()");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "NewJob: Preparation failed for Job #{JobId}", CreatedJobId);
            _logger.LogTrace("<- PrepareAsync() [exception]");
            PrepFailed = true;
            PrepErrorMessage = ex.Message;
            StatusMessage = _loc.Format("Error_PreparationFailed", ex.Message);
        }
        finally
        {
            IsProcessing = false;
        }
    }

    [RelayCommand]
    private async Task StartPrintAsync()
    {
        _logger.LogTrace("-> StartPrintAsync(CreatedJobId={JobId})", CreatedJobId);
        if (CreatedJobId == null)
        {
            _logger.LogTrace("<- StartPrintAsync() — skipped (no CreatedJobId)");
            return;
        }
        _logger.LogInformation("NewJob: Start print for Job #{JobId}", CreatedJobId.Value);

        try
        {
            await _printJobService.StartJobAsync(CreatedJobId.Value);
            NavigateToJobRequested?.Invoke(this, CreatedJobId.Value);
            _logger.LogTrace("<- StartPrintAsync()");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "NewJob: Start print failed for Job #{JobId}", CreatedJobId.Value);
            _logger.LogTrace("<- StartPrintAsync() [exception]");
            StatusMessage = _loc.Format("Error_StartPrintFailed", ex.Message);
        }
    }

    [RelayCommand]
    private void GoToJob()
    {
        _logger.LogTrace("-> GoToJob(CreatedJobId={JobId})", CreatedJobId);
        _logger.LogDebug("NewJob: Navigate to Job #{JobId}", CreatedJobId);
        if (CreatedJobId.HasValue)
            NavigateToJobRequested?.Invoke(this, CreatedJobId.Value);
        _logger.LogTrace("<- GoToJob()");
    }

    [RelayCommand]
    private async Task RetryPrepareAsync()
    {
        _logger.LogTrace("-> RetryPrepareAsync(previousJobId={JobId})", CreatedJobId);
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
        _logger.LogTrace("<- RetryPrepareAsync()");
    }

    [RelayCommand]
    private void GoBack()
    {
        _logger.LogTrace("-> GoBack()");
        _logger.LogDebug("NewJob: Navigate back");
        NavigateBackRequested?.Invoke(this, EventArgs.Empty);
        _logger.LogTrace("<- GoBack()");
    }
}
