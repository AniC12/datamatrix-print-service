using System.Collections.ObjectModel;
using CodePrintManager.Data;
using CodePrintManager.Domain.Entities;
using CodePrintManager.Domain.Enums;
using CodePrintManager.Domain.Interfaces;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;

namespace CodePrintManager.Desktop.ViewModels;

public partial class JobsViewModel : ObservableObject
{
    private readonly AppDbContext _db;
    private readonly IPrintJobService _printJobService;

    public ObservableCollection<PrintJob> ActiveJobs { get; } = new();
    public ObservableCollection<PrintJob> JobHistory { get; } = new();

    [ObservableProperty]
    private PrintJob? _selectedJob;

    public JobsViewModel(AppDbContext db, IPrintJobService printJobService)
    {
        _db = db;
        _printJobService = printJobService;
    }

    [RelayCommand]
    private async Task LoadJobsAsync()
    {
        var active = await _db.PrintJobs
            .Include(j => j.Product)
            .Include(j => j.Printer)
            .Where(j => j.Status == JobStatus.Printing || j.Status == JobStatus.Ready || j.Status == JobStatus.Preparing)
            .OrderByDescending(j => j.CreatedAt)
            .ToListAsync();

        ActiveJobs.Clear();
        foreach (var j in active)
            ActiveJobs.Add(j);

        var history = await _db.PrintJobs
            .Include(j => j.Product)
            .Include(j => j.Printer)
            .Where(j => j.Status == JobStatus.Completed || j.Status == JobStatus.Cancelled || j.Status == JobStatus.Error)
            .OrderByDescending(j => j.CompletedAt)
            .Take(100)
            .ToListAsync();

        JobHistory.Clear();
        foreach (var j in history)
            JobHistory.Add(j);
    }

    [RelayCommand]
    private async Task CancelJobAsync()
    {
        if (SelectedJob == null) return;
        await _printJobService.CancelJobAsync(SelectedJob.Id);
        await LoadJobsAsync();
    }
}
