using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using CodePrintManager.Data;
using CodePrintManager.Domain.Entities;
using CodePrintManager.Domain.Enums;
using CodePrintManager.Domain.Events;
using CodePrintManager.Domain.Interfaces;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;

namespace CodePrintManager.Desktop.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private const double ZoomDefault = 1.2;
    private const double ZoomMin = 0.7;
    private const double ZoomMax = 1.5;
    private const double ZoomStep = 0.1;
    private const string ZoomConfigKey = "ZoomLevel";

    private readonly IAlertService _alertService;
    private readonly AppDbContext _db;
    private readonly ILogger<MainViewModel> _logger;
    private readonly ILocalizationService _loc;

    [ObservableProperty]
    private ObservableObject? _currentView;

    [ObservableProperty]
    private string _currentViewName = "Dashboard";

    [ObservableProperty]
    private double _zoomLevel = ZoomDefault;

    public string ZoomPercent => $"{ZoomLevel * 100:0}%";

    public ObservableCollection<AlertItemViewModel> Alerts { get; } = new();

    // Language selector
    public List<LanguageOption> AvailableLanguages { get; private set; } = new();

    [ObservableProperty]
    private LanguageOption? _selectedLanguage;

    private readonly DashboardViewModel _dashboard;
    private readonly ProductsViewModel _products;
    private readonly PrintersViewModel _printers;
    private readonly JobsViewModel _jobs;
    private readonly NewJobViewModel _newJob;

    public MainViewModel(
        IAlertService alertService,
        AppDbContext db,
        DashboardViewModel dashboard,
        ProductsViewModel products,
        PrintersViewModel printers,
        JobsViewModel jobs,
        NewJobViewModel newJob,
        ILogger<MainViewModel> logger,
        ILocalizationService loc)
    {
        _alertService = alertService;
        _db = db;
        _logger = logger;
        _loc = loc;

        _logger.LogTrace("-> MainViewModel()");

        _dashboard = dashboard;
        _products = products;
        _printers = printers;
        _jobs = jobs;
        _newJob = newJob;

        LoadZoomLevel();
        LoadLanguageOptions();

        _alertService.AlertRaised += OnAlertRaised;
        _alertService.AlertDismissed += OnAlertDismissed;

        // Wire up navigation requests from child ViewModels
        _dashboard.NavigateToNewJobRequested += (_, _) => NavigateToNewJob();
        _dashboard.NavigateToJobRequested += (_, jobId) => NavigateToJobDetail(jobId);
        _products.NavigateToNewJobRequested += (_, productId) => NavigateToNewJob(productId: productId);
        _printers.NavigateToNewJobRequested += (_, printerId) => NavigateToNewJob(printerId: printerId);
        _jobs.NavigateToNewJobRequested += (_, _) => NavigateToNewJob();
        _newJob.NavigateBackRequested += (_, _) => NavigateTo("Dashboard");
        _newJob.NavigateToJobRequested += (_, jobId) => NavigateToJobDetail(jobId);

        CurrentView = _dashboard;

        _logger.LogTrace("<- MainViewModel()");
    }

    [RelayCommand]
    private void NavigateTo(string viewName)
    {
        _logger.LogTrace("-> NavigateTo(viewName={ViewName})", viewName);
        _logger.LogInformation("Navigate → {ViewName}", viewName);
        CurrentView = viewName switch
        {
            "Dashboard" => _dashboard,
            "Products" => _products,
            "Printers" => _printers,
            "Jobs" => _jobs,
            _ => _dashboard
        };
        CurrentViewName = viewName;
        _logger.LogTrace("<- NavigateTo(viewName={ViewName})", viewName);
    }

    private void NavigateToNewJob(int? productId = null, int? printerId = null)
    {
        _logger.LogTrace("-> NavigateToNewJob(productId={ProductId}, printerId={PrinterId})", productId, printerId);
        _logger.LogInformation("Navigate → NewJob (Product={ProductId}, Printer={PrinterId})", productId, printerId);
        _newJob.Reset(productId, printerId);
        CurrentView = _newJob;
        CurrentViewName = "NewJob";
        _logger.LogTrace("<- NavigateToNewJob()");
    }

    private void NavigateToJobDetail(int jobId)
    {
        _logger.LogTrace("-> NavigateToJobDetail(jobId={JobId})", jobId);
        _logger.LogInformation("Navigate → JobDetail (Job={JobId})", jobId);
        _jobs.SelectJobById(jobId);
        CurrentView = _jobs;
        CurrentViewName = "Jobs";
        _logger.LogTrace("<- NavigateToJobDetail(jobId={JobId})", jobId);
    }

    private void OnAlertRaised(object? sender, AlertRaisedEvent e)
    {
        _logger.LogTrace("-> OnAlertRaised(Id={AlertId}, Severity={Severity}, Message={Message})", e.Id, e.Severity, e.Message);
        System.Windows.Application.Current.Dispatcher.Invoke(() =>
        {
            Alerts.Insert(0, new AlertItemViewModel(e));
            if (Alerts.Count > 50)
                Alerts.RemoveAt(Alerts.Count - 1);
        });
        _logger.LogTrace("<- OnAlertRaised(Id={AlertId})", e.Id);
    }

    private void OnAlertDismissed(object? sender, Guid alertId)
    {
        _logger.LogTrace("-> OnAlertDismissed(alertId={AlertId})", alertId);
        System.Windows.Application.Current.Dispatcher.Invoke(() =>
        {
            var alert = Alerts.FirstOrDefault(a => a.Event.Id == alertId);
            if (alert != null)
                Alerts.Remove(alert);
        });
        _logger.LogTrace("<- OnAlertDismissed(alertId={AlertId})", alertId);
    }

    [RelayCommand]
    private void DismissAlert(AlertItemViewModel alert)
    {
        _logger.LogTrace("-> DismissAlert(alertId={AlertId})", alert.Event.Id);
        _logger.LogDebug("Alert dismissed: {AlertId}", alert.Event.Id);
        Alerts.Remove(alert);
        _alertService.Dismiss(alert.Event.Id);
        _logger.LogTrace("<- DismissAlert(alertId={AlertId})", alert.Event.Id);
    }

    // Zoom commands
    [RelayCommand]
    private void ZoomIn()
    {
        _logger.LogTrace("-> ZoomIn()");
        ZoomLevel = Math.Min(Math.Round(ZoomLevel + ZoomStep, 1), ZoomMax);
        OnPropertyChanged(nameof(ZoomPercent));
        _ = SaveZoomLevelAsync();
        _logger.LogTrace("<- ZoomIn() ZoomLevel={ZoomLevel}", ZoomLevel);
    }

    [RelayCommand]
    private void ZoomOut()
    {
        _logger.LogTrace("-> ZoomOut()");
        ZoomLevel = Math.Max(Math.Round(ZoomLevel - ZoomStep, 1), ZoomMin);
        OnPropertyChanged(nameof(ZoomPercent));
        _ = SaveZoomLevelAsync();
        _logger.LogTrace("<- ZoomOut() ZoomLevel={ZoomLevel}", ZoomLevel);
    }

    [RelayCommand]
    private void ZoomReset()
    {
        _logger.LogTrace("-> ZoomReset()");
        ZoomLevel = ZoomDefault;
        OnPropertyChanged(nameof(ZoomPercent));
        _ = SaveZoomLevelAsync();
        _logger.LogTrace("<- ZoomReset() ZoomLevel={ZoomLevel}", ZoomLevel);
    }

    private void LoadZoomLevel()
    {
        _logger.LogTrace("-> LoadZoomLevel()");
        try
        {
            var config = _db.AppConfig.Find(ZoomConfigKey);
            if (config != null
                && double.TryParse(config.Value, CultureInfo.InvariantCulture, out var saved)
                && saved >= ZoomMin && saved <= ZoomMax)
            {
                ZoomLevel = Math.Round(saved, 1);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load zoom level from config");
        }
        _logger.LogTrace("<- LoadZoomLevel() ZoomLevel={ZoomLevel}", ZoomLevel);
    }

    private async Task SaveZoomLevelAsync()
    {
        _logger.LogTrace("-> SaveZoomLevelAsync() ZoomLevel={ZoomLevel}", ZoomLevel);
        try
        {
            var config = await _db.AppConfig.FindAsync(ZoomConfigKey);
            if (config == null)
            {
                config = new AppConfig { Key = ZoomConfigKey, Value = ZoomLevel.ToString(CultureInfo.InvariantCulture) };
                _db.AppConfig.Add(config);
            }
            else
            {
                config.Value = ZoomLevel.ToString(CultureInfo.InvariantCulture);
            }
            await _db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to save zoom level to config");
        }
        _logger.LogTrace("<- SaveZoomLevelAsync()");
    }

    // ── Language ────────────────────────────────────────────
    private const string LanguageConfigKey = "Language";

    private void LoadLanguageOptions()
    {
        _logger.LogTrace("-> LoadLanguageOptions()");
        AvailableLanguages = _loc.AvailableLanguages
            .Select(code => new LanguageOption(code,
                _loc.LanguageDisplayNames.TryGetValue(code, out var name) ? name : code))
            .ToList();

        var savedLang = LoadSavedLanguage();
        if (savedLang != null && _loc.AvailableLanguages.Contains(savedLang))
            _loc.SetLanguage(savedLang);

        SelectedLanguage = AvailableLanguages.FirstOrDefault(l => l.Code == _loc.CurrentLanguage)
                        ?? AvailableLanguages.FirstOrDefault();
        _logger.LogTrace("<- LoadLanguageOptions() SelectedLanguage={Lang}", SelectedLanguage?.Code);
    }

    partial void OnSelectedLanguageChanged(LanguageOption? value)
    {
        _logger.LogTrace("-> OnSelectedLanguageChanged(value={Lang})", value?.Code);
        if (value == null || value.Code == _loc.CurrentLanguage)
        {
            _logger.LogTrace("<- OnSelectedLanguageChanged() [no change]");
            return;
        }
        _loc.SetLanguage(value.Code);
        _logger.LogInformation("Language changed to {Lang}", value.Code);
        _ = SaveLanguageAsync(value.Code);
        _logger.LogTrace("<- OnSelectedLanguageChanged()");
    }

    private string? LoadSavedLanguage()
    {
        _logger.LogTrace("-> LoadSavedLanguage()");
        try
        {
            var config = _db.AppConfig.Find(LanguageConfigKey);
            _logger.LogTrace("<- LoadSavedLanguage() result={Lang}", config?.Value);
            return config?.Value;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load language from config");
            _logger.LogTrace("<- LoadSavedLanguage() [failed]");
            return null;
        }
    }

    private async Task SaveLanguageAsync(string languageCode)
    {
        _logger.LogTrace("-> SaveLanguageAsync(languageCode={Lang})", languageCode);
        try
        {
            var config = await _db.AppConfig.FindAsync(LanguageConfigKey);
            if (config == null)
            {
                config = new AppConfig { Key = LanguageConfigKey, Value = languageCode };
                _db.AppConfig.Add(config);
            }
            else
            {
                config.Value = languageCode;
            }
            await _db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to save language to config");
        }
        _logger.LogTrace("<- SaveLanguageAsync()");
    }
}

public record LanguageOption(string Code, string DisplayName)
{
    public override string ToString() => DisplayName;
}

public partial class AlertItemViewModel : ObservableObject
{
    public AlertRaisedEvent Event { get; }
    public string Message => Event.Message;
    public string Source => Event.Source;
    public AlertSeverity Severity => Event.Severity;
    public DateTime Timestamp { get; } = DateTime.Now;

    public AlertItemViewModel(AlertRaisedEvent e) => Event = e;
}
