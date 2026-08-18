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
    private double _zoomLevel = 1.0;

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
    }

    [RelayCommand]
    private void NavigateTo(string viewName)
    {
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
    }

    private void NavigateToNewJob(int? productId = null, int? printerId = null)
    {
        _logger.LogInformation("Navigate → NewJob (Product={ProductId}, Printer={PrinterId})", productId, printerId);
        _newJob.Reset(productId, printerId);
        CurrentView = _newJob;
        CurrentViewName = "NewJob";
    }

    private void NavigateToJobDetail(int jobId)
    {
        _logger.LogInformation("Navigate → JobDetail (Job={JobId})", jobId);
        _jobs.SelectJobById(jobId);
        CurrentView = _jobs;
        CurrentViewName = "Jobs";
    }

    private void OnAlertRaised(object? sender, AlertRaisedEvent e)
    {
        System.Windows.Application.Current.Dispatcher.Invoke(() =>
        {
            Alerts.Insert(0, new AlertItemViewModel(e));
            if (Alerts.Count > 50)
                Alerts.RemoveAt(Alerts.Count - 1);
        });
    }

    private void OnAlertDismissed(object? sender, Guid alertId)
    {
        System.Windows.Application.Current.Dispatcher.Invoke(() =>
        {
            var alert = Alerts.FirstOrDefault(a => a.Event.Id == alertId);
            if (alert != null)
                Alerts.Remove(alert);
        });
    }

    [RelayCommand]
    private void DismissAlert(AlertItemViewModel alert)
    {
        _logger.LogDebug("Alert dismissed: {AlertId}", alert.Event.Id);
        Alerts.Remove(alert);
        _alertService.Dismiss(alert.Event.Id);
    }

    // Zoom commands
    [RelayCommand]
    private void ZoomIn()
    {
        ZoomLevel = Math.Min(Math.Round(ZoomLevel + ZoomStep, 1), ZoomMax);
        OnPropertyChanged(nameof(ZoomPercent));
        _ = SaveZoomLevelAsync();
    }

    [RelayCommand]
    private void ZoomOut()
    {
        ZoomLevel = Math.Max(Math.Round(ZoomLevel - ZoomStep, 1), ZoomMin);
        OnPropertyChanged(nameof(ZoomPercent));
        _ = SaveZoomLevelAsync();
    }

    [RelayCommand]
    private void ZoomReset()
    {
        ZoomLevel = 1.0;
        OnPropertyChanged(nameof(ZoomPercent));
        _ = SaveZoomLevelAsync();
    }

    private void LoadZoomLevel()
    {
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
    }

    private async Task SaveZoomLevelAsync()
    {
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
    }

    // ── Language ────────────────────────────────────────────
    private const string LanguageConfigKey = "Language";

    private void LoadLanguageOptions()
    {
        AvailableLanguages = _loc.AvailableLanguages
            .Select(code => new LanguageOption(code,
                _loc.LanguageDisplayNames.TryGetValue(code, out var name) ? name : code))
            .ToList();

        var savedLang = LoadSavedLanguage();
        if (savedLang != null && _loc.AvailableLanguages.Contains(savedLang))
            _loc.SetLanguage(savedLang);

        SelectedLanguage = AvailableLanguages.FirstOrDefault(l => l.Code == _loc.CurrentLanguage)
                        ?? AvailableLanguages.FirstOrDefault();
    }

    partial void OnSelectedLanguageChanged(LanguageOption? value)
    {
        if (value == null || value.Code == _loc.CurrentLanguage) return;
        _loc.SetLanguage(value.Code);
        _logger.LogInformation("Language changed to {Lang}", value.Code);
        _ = SaveLanguageAsync(value.Code);
    }

    private string? LoadSavedLanguage()
    {
        try
        {
            var config = _db.AppConfig.Find(LanguageConfigKey);
            return config?.Value;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load language from config");
            return null;
        }
    }

    private async Task SaveLanguageAsync(string languageCode)
    {
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
