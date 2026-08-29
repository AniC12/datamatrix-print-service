using System.Collections.ObjectModel;
using CodePrintManager.Application.Services;
using CodePrintManager.Data;
using CodePrintManager.Domain.Entities;
using CodePrintManager.Domain.Enums;
using CodePrintManager.Domain.Interfaces;
using CodePrintManager.Printer.Mock;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PrinterEntity = CodePrintManager.Domain.Entities.Printer;

namespace CodePrintManager.Desktop.ViewModels;

public partial class PrintersViewModel : ObservableObject
{
    private readonly AppDbContext _db;
    private readonly PrinterConnectionManager _connectionManager;
    private readonly IPrintJobService _printJobService;
    private readonly IAuditService _audit;
    private readonly IPrinterAdapterFactory _adapterFactory;
    private readonly IDialogService _dialog;
    private readonly ILogger<PrintersViewModel> _logger;
    private readonly ILocalizationService _loc;

    public ObservableCollection<PrinterEntity> Printers { get; } = new();

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConnectPrinterCommand))]
    [NotifyCanExecuteChangedFor(nameof(DisconnectPrinterCommand))]
    [NotifyCanExecuteChangedFor(nameof(RefreshStorageCommand))]
    [NotifyCanExecuteChangedFor(nameof(DeleteSelectedFilesCommand))]
    [NotifyCanExecuteChangedFor(nameof(VerifyPrinterCommand))]
    [NotifyCanExecuteChangedFor(nameof(NewJobCommand))]
    private PrinterEntity? _selectedPrinter;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConnectPrinterCommand))]
    [NotifyCanExecuteChangedFor(nameof(DisconnectPrinterCommand))]
    [NotifyCanExecuteChangedFor(nameof(RefreshStorageCommand))]
    [NotifyCanExecuteChangedFor(nameof(DeleteSelectedFilesCommand))]
    [NotifyCanExecuteChangedFor(nameof(NewJobCommand))]
    private PrinterStatus _selectedPrinterStatus = PrinterStatus.Offline;

    // Configuration fields
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConfirmAddPrinterCommand))]
    private string _newPrinterName = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConfirmAddPrinterCommand))]
    private string _newPrinterIp = string.Empty;

    [ObservableProperty]
    private int _newPrinterPort = 9100;

    [ObservableProperty]
    private string _newPrinterAdapterType = "savema_tto";

    public List<string> AvailableAdapterTypes { get; private set; } = new() { "savema_tto" };

    [ObservableProperty]
    private bool _isAddingPrinter;

    // Edit mode fields
    [ObservableProperty]
    private bool _isEditingPrinter;

    [ObservableProperty]
    private string _editPrinterName = string.Empty;

    [ObservableProperty]
    private string _editPrinterIp = string.Empty;

    [ObservableProperty]
    private int _editPrinterPort = 9100;

    [ObservableProperty]
    private int _editQuarantineMargin;

    // Storage tab
    public ObservableCollection<PrinterFileItem> TemplateFiles { get; } = new();
    public ObservableCollection<PrinterFileItem> CsvFiles { get; } = new();

    [ObservableProperty]
    private int _selectedDeleteCount;

    // Verify tab
    public ObservableCollection<VerifyResultItem> VerifyResults { get; } = new();

    [ObservableProperty]
    private bool _isVerifying;

    [ObservableProperty]
    private bool _hasVerifyResults;

    [ObservableProperty]
    private string _verifyOverallStatus = string.Empty;

    public event EventHandler<int>? NavigateToNewJobRequested;

    public PrintersViewModel(AppDbContext db, PrinterConnectionManager connectionManager, IPrintJobService printJobService,
        IAuditService audit, IPrinterAdapterFactory adapterFactory, IDialogService dialog,
        ILogger<PrintersViewModel> logger, ILocalizationService loc)
    {
        _db = db;
        _connectionManager = connectionManager;
        _printJobService = printJobService;
        _audit = audit;
        _adapterFactory = adapterFactory;
        _dialog = dialog;
        _logger = logger;
        _loc = loc;

        _logger.LogTrace("-> PrintersViewModel(.ctor)");

        // Build available adapter types from registered factory
        AvailableAdapterTypes = adapterFactory is MockPrinterAdapterFactory
            ? new List<string> { "mock", "savema_tto" }
            : new List<string> { "savema_tto", "mock" };

        _connectionManager.PrinterStatusChanged += (_, e) =>
        {
            _logger.LogTrace("-> PrinterStatusChanged(PrinterId={PrinterId}, NewStatus={NewStatus})", e.PrinterId, e.NewStatus);
            var app = System.Windows.Application.Current;
            if (app != null)
            {
                app.Dispatcher.Invoke(() =>
                {
                    if (SelectedPrinter?.Id == e.PrinterId)
                        SelectedPrinterStatus = e.NewStatus;
                });
            }
            else
            {
                if (SelectedPrinter?.Id == e.PrinterId)
                    SelectedPrinterStatus = e.NewStatus;
            }
            _logger.LogTrace("<- PrinterStatusChanged");
        };

        _logger.LogTrace("<- PrintersViewModel(.ctor)");
    }

    [RelayCommand]
    private async Task LoadPrintersAsync()
    {
        _logger.LogTrace("-> LoadPrintersAsync()");
        var printers = await _db.Printers.ToListAsync();
        Printers.Clear();
        foreach (var p in printers)
            Printers.Add(p);

        _logger.LogInformation("Printers page loaded: {Count} printers", printers.Count);

        if (SelectedPrinter == null && Printers.Count > 0)
            SelectedPrinter = Printers[0];
        _logger.LogTrace("<- LoadPrintersAsync");
    }

    partial void OnSelectedPrinterChanged(PrinterEntity? value)
    {
        _logger.LogTrace("-> OnSelectedPrinterChanged(value={PrinterName}, Id={PrinterId})", value?.Name, value?.Id);
        if (value == null)
        {
            _logger.LogTrace("<- OnSelectedPrinterChanged (value is null)");
            return;
        }
        _ = OnPrinterSelectedAsync(value);
        _logger.LogTrace("<- OnSelectedPrinterChanged");
    }

    private async Task OnPrinterSelectedAsync(PrinterEntity printer)
    {
        _logger.LogTrace("-> OnPrinterSelectedAsync(printer={Name}, Id={Id})", printer.Name, printer.Id);
        try
        {
            // Query actual printer status instead of assuming Idle
            var adapter = _connectionManager.GetAdapter(printer.Id);
            if (adapter != null)
            {
                try
                {
                    SelectedPrinterStatus = await adapter.GetStatusAsync();
                }
                catch
                {
                    SelectedPrinterStatus = PrinterStatus.Idle;
                }
            }
            else
            {
                SelectedPrinterStatus = PrinterStatus.Offline;
            }

            _logger.LogInformation("Printer selected: '{Name}' (Id={Id}, Status={Status})",
                printer.Name, printer.Id, SelectedPrinterStatus);
            await RefreshStorageAsync();
            _logger.LogTrace("<- OnPrinterSelectedAsync");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "OnPrinterSelectedAsync failed for printer '{Name}' (Id={Id})", printer.Name, printer.Id);
            _logger.LogTrace("<- OnPrinterSelectedAsync (exception)");
        }
    }

    [RelayCommand]
    private void ShowAddPrinter()
    {
        _logger.LogTrace("-> ShowAddPrinter()");
        IsAddingPrinter = true;
        SelectedPrinter = null;
        NewPrinterName = string.Empty;
        NewPrinterIp = string.Empty;
        NewPrinterPort = 9100;
        NewPrinterAdapterType = _adapterFactory is MockPrinterAdapterFactory ? "mock" : "savema_tto";
        _logger.LogInformation("Printers: Add Printer form opened (default adapter={AdapterType})", NewPrinterAdapterType);
        _logger.LogTrace("<- ShowAddPrinter");
    }

    [RelayCommand]
    private void CancelAddPrinter()
    {
        _logger.LogTrace("-> CancelAddPrinter()");
        _logger.LogDebug("Printers: Add Printer cancelled");
        IsAddingPrinter = false;
        if (SelectedPrinter == null && Printers.Count > 0)
            SelectedPrinter = Printers[0];
        _logger.LogTrace("<- CancelAddPrinter");
    }

    private bool CanConfirmAddPrinter()
    {
        _logger.LogTrace("-> CanConfirmAddPrinter()");
        var result = !string.IsNullOrWhiteSpace(NewPrinterName) && !string.IsNullOrWhiteSpace(NewPrinterIp);
        _logger.LogTrace("<- CanConfirmAddPrinter = {Result}", result);
        return result;
    }

    [RelayCommand(CanExecute = nameof(CanConfirmAddPrinter))]
    private async Task ConfirmAddPrinterAsync()
    {
        _logger.LogTrace("-> ConfirmAddPrinterAsync(Name={Name}, Ip={Ip}, Port={Port}, AdapterType={AdapterType})",
            NewPrinterName, NewPrinterIp, NewPrinterPort, NewPrinterAdapterType);
        var printer = new PrinterEntity
        {
            Name = NewPrinterName.Trim(),
            IpAddress = NewPrinterIp.Trim(),
            Port = NewPrinterPort,
            AdapterType = NewPrinterAdapterType
        };

        _db.Printers.Add(printer);
        await _db.SaveChangesAsync();
        _logger.LogInformation("Printers: Printer added '{Name}' ({Ip}:{Port}, adapter={Type})",
            printer.Name, printer.IpAddress, printer.Port, printer.AdapterType);
        IsAddingPrinter = false;
        await LoadPrintersAsync();
        SelectedPrinter = Printers.FirstOrDefault(p => p.Id == printer.Id);

        // Auto-connect the newly added printer
        _ = _connectionManager.ConnectAsync(printer);
        _logger.LogTrace("<- ConfirmAddPrinterAsync");
    }

    private bool CanConnectPrinter()
    {
        _logger.LogTrace("-> CanConnectPrinter()");
        var result = SelectedPrinter != null && SelectedPrinterStatus == PrinterStatus.Offline;
        _logger.LogTrace("<- CanConnectPrinter = {Result}", result);
        return result;
    }

    [RelayCommand(CanExecute = nameof(CanConnectPrinter))]
    private async Task ConnectPrinterAsync()
    {
        _logger.LogTrace("-> ConnectPrinterAsync(Printer={Name}, Id={Id})", SelectedPrinter?.Name, SelectedPrinter?.Id);
        if (SelectedPrinter == null)
        {
            _logger.LogTrace("<- ConnectPrinterAsync (SelectedPrinter is null)");
            return;
        }
        _logger.LogInformation("Printers: Connect requested for '{Name}' (Id={Id})", SelectedPrinter.Name, SelectedPrinter.Id);
        await _connectionManager.ConnectAsync(SelectedPrinter);

        // Respawn ReadyWatchers for any Ready jobs on this printer.
        // After a manual disconnect/reconnect the old watchers were stopped
        // because they held a reference to the now-disposed adapter.
        await _printJobService.RespawnWatchersForPrinterAsync(SelectedPrinter.Id);
        _logger.LogTrace("<- ConnectPrinterAsync");
    }

    private bool CanDisconnectPrinter()
    {
        _logger.LogTrace("-> CanDisconnectPrinter()");
        var result = SelectedPrinter != null && SelectedPrinterStatus != PrinterStatus.Offline;
        _logger.LogTrace("<- CanDisconnectPrinter = {Result}", result);
        return result;
    }

    [RelayCommand(CanExecute = nameof(CanDisconnectPrinter))]
    private async Task DisconnectPrinterAsync()
    {
        _logger.LogTrace("-> DisconnectPrinterAsync(Printer={Name}, Id={Id})", SelectedPrinter?.Name, SelectedPrinter?.Id);
        if (SelectedPrinter == null)
        {
            _logger.LogTrace("<- DisconnectPrinterAsync (SelectedPrinter is null)");
            return;
        }

        // Warn if printer has active jobs
        var activeStatuses = new[] { JobStatus.Preparing, JobStatus.Ready, JobStatus.Printing, JobStatus.Paused };
        var hasActiveJobs = await _db.PrintJobs
            .AnyAsync(j => j.PrinterId == SelectedPrinter.Id && activeStatuses.Contains(j.Status));
        if (hasActiveJobs)
        {
            if (!_dialog.Confirm(
                _loc.Format("Dialog_ConfirmDisconnectActiveJobs", SelectedPrinter.Name),
                _loc["DialogTitle_ActiveJobsWarning"]))
            {
                _logger.LogTrace("<- DisconnectPrinterAsync (user cancelled active-jobs warning)");
                return;
            }
        }

        _logger.LogInformation("Printers: Disconnect requested for '{Name}' (Id={Id})", SelectedPrinter.Name, SelectedPrinter.Id);
        await _connectionManager.DisconnectAsync(SelectedPrinter.Id);
        SelectedPrinterStatus = PrinterStatus.Offline;
        _logger.LogTrace("<- DisconnectPrinterAsync");
    }

    [RelayCommand]
    private void EditPrinter()
    {
        _logger.LogTrace("-> EditPrinter()");
        if (SelectedPrinter == null)
        {
            _logger.LogTrace("<- EditPrinter (SelectedPrinter is null)");
            return;
        }
        EditPrinterName = SelectedPrinter.Name;
        EditPrinterIp = SelectedPrinter.IpAddress;
        EditPrinterPort = SelectedPrinter.Port;
        EditQuarantineMargin = SelectedPrinter.QuarantineMargin;
        IsEditingPrinter = true;
        _logger.LogDebug("Printers: Edit mode opened for '{Name}' (Id={Id})", SelectedPrinter.Name, SelectedPrinter.Id);
        _logger.LogTrace("<- EditPrinter");
    }

    [RelayCommand]
    private void CancelEditPrinter()
    {
        _logger.LogTrace("-> CancelEditPrinter()");
        IsEditingPrinter = false;
        _logger.LogDebug("Printers: Edit mode cancelled");
        _logger.LogTrace("<- CancelEditPrinter");
    }

    [RelayCommand]
    private async Task SaveEditPrinterAsync()
    {
        _logger.LogTrace("-> SaveEditPrinterAsync(EditName={Name}, EditIp={Ip}, EditPort={Port})",
            EditPrinterName, EditPrinterIp, EditPrinterPort);
        if (SelectedPrinter == null)
        {
            _logger.LogTrace("<- SaveEditPrinterAsync (SelectedPrinter is null)");
            return;
        }
        if (string.IsNullOrWhiteSpace(EditPrinterName) || string.IsNullOrWhiteSpace(EditPrinterIp))
        {
            _logger.LogTrace("<- SaveEditPrinterAsync (validation failed: empty name or IP)");
            return;
        }

        var oldName = SelectedPrinter.Name;
        SelectedPrinter.Name = EditPrinterName.Trim();
        SelectedPrinter.IpAddress = EditPrinterIp.Trim();
        SelectedPrinter.Port = EditPrinterPort;
        SelectedPrinter.QuarantineMargin = Math.Max(0, EditQuarantineMargin);
        SelectedPrinter.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        _logger.LogInformation("Printers: Printer updated '{OldName}' → '{NewName}' ({Ip}:{Port})",
            oldName, SelectedPrinter.Name, SelectedPrinter.IpAddress, SelectedPrinter.Port);
        await _audit.LogAsync("printer_updated",
            printerId: SelectedPrinter.Id,
            details: $"Updated printer: \"{SelectedPrinter.Name}\" ({SelectedPrinter.IpAddress}:{SelectedPrinter.Port})");

        IsEditingPrinter = false;

        // Refresh the list to reflect updated name in the dropdown
        var selectedId = SelectedPrinter.Id;
        await LoadPrintersAsync();
        SelectedPrinter = Printers.FirstOrDefault(p => p.Id == selectedId);
        _logger.LogTrace("<- SaveEditPrinterAsync");
    }

    [RelayCommand]
    private async Task DeletePrinterAsync()
    {
        _logger.LogTrace("-> DeletePrinterAsync(Printer={Name}, Id={Id})", SelectedPrinter?.Name, SelectedPrinter?.Id);
        if (SelectedPrinter == null)
        {
            _logger.LogTrace("<- DeletePrinterAsync (SelectedPrinter is null)");
            return;
        }

        // Block deletion if printer has active jobs
        var activeStatuses = new[] { JobStatus.Preparing, JobStatus.Ready, JobStatus.Printing, JobStatus.Paused };
        var hasActiveJobs = await _db.PrintJobs
            .AnyAsync(j => j.PrinterId == SelectedPrinter.Id && activeStatuses.Contains(j.Status));
        if (hasActiveJobs)
        {
            _dialog.ShowWarning(
                _loc.Format("Error_PrinterHasActiveJobs", SelectedPrinter.Name),
                _loc["DialogTitle_PrinterInUse"]);
            _logger.LogTrace("<- DeletePrinterAsync (blocked: printer has active jobs)");
            return;
        }

        if (!_dialog.Confirm(
            _loc.Format("Dialog_ConfirmDeletePrinter", SelectedPrinter.Name),
            _loc["DialogTitle_ConfirmDelete"]))
        {
            _logger.LogTrace("<- DeletePrinterAsync (user cancelled confirmation)");
            return;
        }

        _logger.LogInformation("Printers: Printer deleted '{Name}' (Id={Id})", SelectedPrinter.Name, SelectedPrinter.Id);
        await _connectionManager.DisconnectAsync(SelectedPrinter.Id);
        _db.Printers.Remove(SelectedPrinter);
        await _db.SaveChangesAsync();
        await _audit.LogAsync("printer_deleted",
            printerId: SelectedPrinter.Id,
            details: $"Deleted printer \"{SelectedPrinter.Name}\" ({SelectedPrinter.IpAddress}:{SelectedPrinter.Port})");
        SelectedPrinter = null;
        await LoadPrintersAsync();
        _logger.LogTrace("<- DeletePrinterAsync");
    }

    private bool CanRefreshStorage()
    {
        _logger.LogTrace("-> CanRefreshStorage()");
        var result = SelectedPrinter != null && SelectedPrinterStatus != PrinterStatus.Offline;
        _logger.LogTrace("<- CanRefreshStorage = {Result}", result);
        return result;
    }

    [RelayCommand(CanExecute = nameof(CanRefreshStorage))]
    private async Task RefreshStorageAsync()
    {
        _logger.LogTrace("-> RefreshStorageAsync(Printer={Name}, Id={Id})", SelectedPrinter?.Name, SelectedPrinter?.Id);
        TemplateFiles.Clear();
        CsvFiles.Clear();
        SelectedDeleteCount = 0;

        if (SelectedPrinter == null)
        {
            _logger.LogTrace("<- RefreshStorageAsync (SelectedPrinter is null)");
            return;
        }

        var adapter = _connectionManager.GetAdapter(SelectedPrinter.Id);
        if (adapter == null)
        {
            _logger.LogTrace("<- RefreshStorageAsync (adapter is null)");
            return;
        }

        try
        {
            // Get templates from printer + the currently active template
            var templates = await adapter.ListTemplatesAsync();
            var activeTemplateName = await adapter.GetActiveTemplateAsync();
            var products = await _db.ProductNodes.AsNoTracking().Where(p => p.IsLeaf).ToListAsync();

            foreach (var t in templates)
            {
                // Compare by filename only — TemplateFile may be a full path
                var mapped = products.FirstOrDefault(p =>
                    !string.IsNullOrEmpty(p.TemplateFile) &&
                    string.Equals(System.IO.Path.GetFileName(p.TemplateFile), t, StringComparison.OrdinalIgnoreCase));
                var isActive = activeTemplateName != null &&
                    string.Equals(activeTemplateName, t, StringComparison.OrdinalIgnoreCase);
                var item = new PrinterFileItem(t, mapped?.Name, isActiveOnPrinter: isActive, loc: _loc);
                if (!item.IsProtected) item.IsSelected = true;
                item.PropertyChanged += (_, _) => UpdateDeleteCount();
                TemplateFiles.Add(item);
            }

            // Get CSV/data files from printer
            var dataFiles = await adapter.ListCsvFilesAsync();
            foreach (var d in dataFiles)
            {
                var mapped = products.FirstOrDefault(p =>
                    string.Equals(p.PrinterCsvName, d, StringComparison.OrdinalIgnoreCase));
                var item = new PrinterFileItem(d, mapped?.Name, loc: _loc);
                if (mapped == null) item.IsSelected = true;
                item.PropertyChanged += (_, _) => UpdateDeleteCount();
                CsvFiles.Add(item);
            }

            _logger.LogDebug("Printers: Storage refreshed: {TemplateCount} templates, {CsvCount} CSV files",
                TemplateFiles.Count, CsvFiles.Count);
            UpdateDeleteCount();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Printers: Storage refresh failed for Printer {Id}", SelectedPrinter?.Id);
            _logger.LogTrace("<- RefreshStorageAsync (exception)");
            return;
        }
        _logger.LogTrace("<- RefreshStorageAsync");
    }

    private void UpdateDeleteCount()
    {
        _logger.LogTrace("-> UpdateDeleteCount()");
        SelectedDeleteCount = TemplateFiles.Count(f => f.IsSelected) + CsvFiles.Count(f => f.IsSelected);
        _logger.LogTrace("<- UpdateDeleteCount = {Count}", SelectedDeleteCount);
    }

    private bool CanDeleteSelectedFiles()
    {
        _logger.LogTrace("-> CanDeleteSelectedFiles()");
        var result = SelectedPrinter != null && SelectedPrinterStatus != PrinterStatus.Offline;
        _logger.LogTrace("<- CanDeleteSelectedFiles = {Result}", result);
        return result;
    }

    [RelayCommand(CanExecute = nameof(CanDeleteSelectedFiles))]
    private async Task DeleteSelectedFilesAsync()
    {
        _logger.LogTrace("-> DeleteSelectedFilesAsync(Printer={Name}, Id={Id})", SelectedPrinter?.Name, SelectedPrinter?.Id);
        if (SelectedPrinter == null)
        {
            _logger.LogTrace("<- DeleteSelectedFilesAsync (SelectedPrinter is null)");
            return;
        }
        var adapter = _connectionManager.GetAdapter(SelectedPrinter.Id);
        if (adapter == null)
        {
            _logger.LogTrace("<- DeleteSelectedFilesAsync (adapter is null)");
            return;
        }

        var deleteCount = TemplateFiles.Count(f => f.IsSelected && !f.IsProtected)
                        + CsvFiles.Count(f => f.IsSelected && !f.IsProtected);
        if (deleteCount == 0)
        {
            _logger.LogTrace("<- DeleteSelectedFilesAsync (no files selected for deletion)");
            return;
        }

        if (!_dialog.Confirm(
            _loc.Format("Dialog_ConfirmDeleteFiles", deleteCount, SelectedPrinter.Name),
            _loc["DialogTitle_ConfirmDelete"]))
        {
            _logger.LogTrace("<- DeleteSelectedFilesAsync (user cancelled confirmation)");
            return;
        }

        var deletedFiles = new List<string>();

        foreach (var f in TemplateFiles.Where(f => f.IsSelected && !f.IsProtected).ToList())
        {
            if (await adapter.DeleteTemplateAsync(f.FileName))
                deletedFiles.Add($"template:{f.FileName}");
        }

        foreach (var f in CsvFiles.Where(f => f.IsSelected && !f.IsProtected).ToList())
        {
            if (await adapter.DeleteCsvAsync(f.FileName))
                deletedFiles.Add($"csv:{f.FileName}");
        }

        if (deletedFiles.Count > 0)
        {
            _logger.LogInformation("Printers: Deleted {Count} files from Printer {Id}: {FileList}",
                deletedFiles.Count, SelectedPrinter.Id, string.Join(", ", deletedFiles));
            await _audit.LogAsync("printer_files_deleted",
                printerId: SelectedPrinter.Id,
                details: $"Deleted {deletedFiles.Count} file(s): {string.Join(", ", deletedFiles)}");
        }

        await RefreshStorageAsync();
        _logger.LogTrace("<- DeleteSelectedFilesAsync");
    }

    private bool CanVerifyPrinter()
    {
        _logger.LogTrace("-> CanVerifyPrinter()");
        var result = SelectedPrinter != null;
        _logger.LogTrace("<- CanVerifyPrinter = {Result}", result);
        return result;
    }

    [RelayCommand(CanExecute = nameof(CanVerifyPrinter))]
    private async Task VerifyPrinterAsync()
    {
        _logger.LogTrace("-> VerifyPrinterAsync(Printer={Name}, Id={Id})", SelectedPrinter?.Name, SelectedPrinter?.Id);
        if (SelectedPrinter == null)
        {
            _logger.LogTrace("<- VerifyPrinterAsync (SelectedPrinter is null)");
            return;
        }
        _logger.LogInformation("Printers: Verify started for '{Name}' (Id={Id})", SelectedPrinter.Name, SelectedPrinter.Id);

        var adapter = _connectionManager.GetAdapter(SelectedPrinter.Id);
        if (adapter == null)
        {
            VerifyResults.Clear();
            VerifyResults.Add(new VerifyResultItem(_loc["Verify_Connection"], VerifyStatus.Fail, _loc["Verify_PrinterNotConnected"]));
            HasVerifyResults = true;
            VerifyOverallStatus = _loc["Verify_Failed"];
            _logger.LogTrace("<- VerifyPrinterAsync (adapter is null)");
            return;
        }

        IsVerifying = true;
        VerifyResults.Clear();
        HasVerifyResults = false;

        try
        {
            // Get the active job for this printer (if any)
            var activeJob = await _db.PrintJobs
                .AsNoTracking()
                .Include(j => j.Product)
                .Where(j => j.PrinterId == SelectedPrinter.Id &&
                    (j.Status == JobStatus.Printing || j.Status == JobStatus.Ready || j.Status == JobStatus.Preparing || j.Status == JobStatus.Paused))
                .OrderByDescending(j => j.CreatedAt)
                .FirstOrDefaultAsync();

            // 1. Check stored CSV file
            if (activeJob?.Product?.PrinterCsvName != null)
            {
                var csvExists = await adapter.VerifyCsvExistsAsync(activeJob.Product.PrinterCsvName);
                VerifyResults.Add(csvExists
                    ? new VerifyResultItem(_loc["Verify_CsvFile"], VerifyStatus.Pass,
                        _loc.Format("Verify_CsvPresent", activeJob.Product.PrinterCsvName))
                    : new VerifyResultItem(_loc["Verify_CsvFile"], VerifyStatus.Warning,
                        _loc.Format("Verify_CsvNotFound", activeJob.Product.PrinterCsvName)));
            }
            else if (activeJob != null)
            {
                VerifyResults.Add(new VerifyResultItem(_loc["Verify_CsvFile"], VerifyStatus.Warning,
                    _loc["Verify_NoCsvConfigured"]));
            }
            else
            {
                VerifyResults.Add(new VerifyResultItem(_loc["Verify_CsvFile"], VerifyStatus.Pass,
                    _loc["Verify_NoActiveJobNoCsv"]));
            }

            // 2. Check active template
            var activeTemplate = await adapter.GetActiveTemplateAsync();
            if (activeJob?.Product?.TemplateFile != null)
            {
                var expectedName = System.IO.Path.GetFileName(activeJob.Product.TemplateFile);
                var matches = activeTemplate != null &&
                    string.Equals(activeTemplate, expectedName, StringComparison.OrdinalIgnoreCase);
                VerifyResults.Add(matches
                    ? new VerifyResultItem(_loc["Verify_ActiveTemplate"], VerifyStatus.Pass,
                        _loc.Format("Verify_TemplateMatches", activeTemplate!))
                    : new VerifyResultItem(_loc["Verify_ActiveTemplate"], VerifyStatus.Warning,
                        _loc.Format("Verify_TemplateMismatch", activeTemplate ?? _loc["Verify_None"], expectedName)));
            }
            else if (activeJob != null)
            {
                VerifyResults.Add(new VerifyResultItem(_loc["Verify_ActiveTemplate"], VerifyStatus.Warning,
                    _loc.Format("Verify_NoTemplateConfigured", activeTemplate ?? _loc["Verify_None"])));
            }
            else
            {
                VerifyResults.Add(new VerifyResultItem(_loc["Verify_ActiveTemplate"], VerifyStatus.Pass,
                    _loc.Format("Verify_NoActiveJobTemplate", activeTemplate ?? _loc["Verify_None"])));
            }

            // 3. Check counters (only meaningful with an active/printing job)
            if (activeJob != null && activeJob.TotalBaseline.HasValue)
            {
                var totalCounter = await adapter.GetTotalCounterAsync();
                var expectedTotal = activeJob.TotalBaseline.Value + activeJob.CodesConfirmed;
                var delta = totalCounter - expectedTotal;

                if (delta == 0)
                {
                    VerifyResults.Add(new VerifyResultItem(_loc["Verify_Counter"], VerifyStatus.Pass,
                        _loc.Format("Verify_CounterConsistent", totalCounter, expectedTotal)));
                }
                else if (delta > 0)
                {
                    VerifyResults.Add(new VerifyResultItem(_loc["Verify_Counter"], VerifyStatus.Warning,
                        _loc.Format("Verify_CounterAhead", totalCounter, expectedTotal, delta)));
                }
                else
                {
                    VerifyResults.Add(new VerifyResultItem(_loc["Verify_Counter"], VerifyStatus.Fail,
                        _loc.Format("Verify_CounterBehind", totalCounter, expectedTotal, delta)));
                }
            }
            else if (activeJob != null)
            {
                VerifyResults.Add(new VerifyResultItem(_loc["Verify_Counter"], VerifyStatus.Warning,
                    _loc["Verify_NoBaseline"]));
            }
            else
            {
                var totalCounter = await adapter.GetTotalCounterAsync();
                VerifyResults.Add(new VerifyResultItem(_loc["Verify_Counter"], VerifyStatus.Pass,
                    _loc.Format("Verify_LifetimeCounter", totalCounter)));
            }

            // 4. Printer status
            var status = await adapter.GetStatusAsync();
            var statusResult = status switch
            {
                PrinterStatus.Error => new VerifyResultItem(_loc["Verify_PrinterStatus"], VerifyStatus.Fail,
                    _loc["Verify_PrinterError"]),
                PrinterStatus.Blocked => new VerifyResultItem(_loc["Verify_PrinterStatus"], VerifyStatus.Warning,
                    _loc["Verify_PrinterBlocked"]),
                _ => new VerifyResultItem(_loc["Verify_PrinterStatus"], VerifyStatus.Pass,
                    _loc.Format("Verify_PrinterState", status))
            };
            VerifyResults.Add(statusResult);

            // Overall
            var hasFailure = VerifyResults.Any(r => r.Status == VerifyStatus.Fail);
            var hasWarning = VerifyResults.Any(r => r.Status == VerifyStatus.Warning);
            VerifyOverallStatus = hasFailure ? _loc["Verify_IssuesFound"] : hasWarning ? _loc["Verify_Warnings"] : _loc["Verify_AllOk"];
            _logger.LogInformation("Printers: Verify result for '{Name}': {OverallStatus} ({ResultCount} checks)",
                SelectedPrinter.Name, VerifyOverallStatus, VerifyResults.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Printers: Verify failed for Printer {Id}", SelectedPrinter.Id);
            _logger.LogTrace("<- VerifyPrinterAsync (exception)");
            VerifyResults.Add(new VerifyResultItem(_loc["DialogTitle_Error"], VerifyStatus.Fail,
                _loc.Format("Error_VerificationFailed", ex.Message)));
            VerifyOverallStatus = _loc["DialogTitle_Error"];
        }
        finally
        {
            IsVerifying = false;
            HasVerifyResults = true;
        }
        _logger.LogTrace("<- VerifyPrinterAsync");
    }

    private bool CanNewJob()
    {
        _logger.LogTrace("-> CanNewJob()");
        var result = SelectedPrinter != null && SelectedPrinterStatus != PrinterStatus.Offline;
        _logger.LogTrace("<- CanNewJob = {Result}", result);
        return result;
    }

    [RelayCommand(CanExecute = nameof(CanNewJob))]
    private void NewJob()
    {
        _logger.LogTrace("-> NewJob(Printer={Name}, Id={Id})", SelectedPrinter?.Name, SelectedPrinter?.Id);
        if (SelectedPrinter != null)
            NavigateToNewJobRequested?.Invoke(this, SelectedPrinter.Id);
        _logger.LogTrace("<- NewJob");
    }
}

public partial class PrinterFileItem : ObservableObject
{
    private readonly ILocalizationService? _loc;
    public string FileName { get; }
    public string? MappedProduct { get; }
    public bool IsActiveOnPrinter { get; }
    public bool IsMapped => MappedProduct != null;
    public bool IsProtected => IsMapped || IsActiveOnPrinter;
    public string StatusText => IsActiveOnPrinter
        ? (_loc?["Storage_ActiveOnPrinter"] ?? "Active on printer")
        : IsMapped ? (_loc != null ? _loc.Format("Storage_UsedByProduct", MappedProduct!) : $"Used ({MappedProduct})") : (_loc?["Storage_NotMapped"] ?? "Not mapped to any product");

    [ObservableProperty]
    private bool _isSelected;

    public PrinterFileItem(string fileName, string? mappedProduct, bool isActiveOnPrinter = false, ILocalizationService? loc = null)
    {
        FileName = fileName;
        MappedProduct = mappedProduct;
        IsActiveOnPrinter = isActiveOnPrinter;
        _loc = loc;
    }
}

public enum VerifyStatus { Pass, Warning, Fail }

public class VerifyResultItem
{
    public string CheckName { get; }
    public VerifyStatus Status { get; }
    public string Details { get; }
    public string StatusIcon => Status switch
    {
        VerifyStatus.Pass => "\u2705",    // green checkmark
        VerifyStatus.Warning => "\u26A0", // warning triangle
        VerifyStatus.Fail => "\u274C",    // red X
        _ => "?"
    };

    public VerifyResultItem(string checkName, VerifyStatus status, string details)
    {
        CheckName = checkName;
        Status = status;
        Details = details;
    }
}
