using System.Collections.ObjectModel;
using CodePrintManager.Domain.Entities;
using CodePrintManager.Domain.Interfaces;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CodePrintManager.Desktop.ViewModels;

public partial class RecoveryViewModel : ObservableObject
{
    private readonly IPrintJobService _printJobService;

    public ObservableCollection<PrintJob> StaleJobs { get; } = new();

    [ObservableProperty]
    private PrintJob? _selectedItem;

    [ObservableProperty]
    private bool _hasStaleJobs;

    public RecoveryViewModel(IPrintJobService printJobService)
    {
        _printJobService = printJobService;
    }

    [RelayCommand]
    private async Task LoadStaleJobsAsync()
    {
        var items = await _printJobService.GetStaleJobsAsync();
        StaleJobs.Clear();
        foreach (var item in items)
            StaleJobs.Add(item);
        HasStaleJobs = StaleJobs.Count > 0;
    }

    [RelayCommand]
    private async Task ResumeJobAsync()
    {
        if (SelectedItem == null) return;
        await _printJobService.ResumeJobAsync(SelectedItem.Id);
        StaleJobs.Remove(SelectedItem);
        HasStaleJobs = StaleJobs.Count > 0;
    }

    [RelayCommand]
    private async Task CancelJobAsync()
    {
        if (SelectedItem == null) return;
        await _printJobService.CancelJobAsync(SelectedItem.Id);
        StaleJobs.Remove(SelectedItem);
        HasStaleJobs = StaleJobs.Count > 0;
    }
}
