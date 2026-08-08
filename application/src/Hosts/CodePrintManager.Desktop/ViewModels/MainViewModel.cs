using System.Collections.ObjectModel;
using System.Windows;
using CodePrintManager.Domain.Enums;
using CodePrintManager.Domain.Events;
using CodePrintManager.Domain.Interfaces;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CodePrintManager.Desktop.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly IAlertService _alertService;

    [ObservableProperty]
    private ObservableObject? _currentView;

    [ObservableProperty]
    private string _currentViewName = "Dashboard";

    public ObservableCollection<AlertItemViewModel> Alerts { get; } = new();

    private readonly DashboardViewModel _dashboard;
    private readonly ProductsViewModel _products;
    private readonly PrintersViewModel _printers;
    private readonly JobsViewModel _jobs;
    private readonly NewJobViewModel _newJob;

    public MainViewModel(
        IAlertService alertService,
        DashboardViewModel dashboard,
        ProductsViewModel products,
        PrintersViewModel printers,
        JobsViewModel jobs,
        NewJobViewModel newJob)
    {
        _alertService = alertService;
        _dashboard = dashboard;
        _products = products;
        _printers = printers;
        _jobs = jobs;
        _newJob = newJob;

        _alertService.AlertRaised += OnAlertRaised;
        CurrentView = _dashboard;
    }

    [RelayCommand]
    private void NavigateTo(string viewName)
    {
        CurrentView = viewName switch
        {
            "Dashboard" => _dashboard,
            "Products" => _products,
            "Printers" => _printers,
            "Jobs" => _jobs,
            "NewJob" => _newJob,
            _ => _dashboard
        };
        CurrentViewName = viewName;
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

    [RelayCommand]
    private void DismissAlert(AlertItemViewModel alert) => Alerts.Remove(alert);
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
