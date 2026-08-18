using CodePrintManager.Domain.Entities;
using CodePrintManager.Domain.Enums;
using CodePrintManager.Domain.Interfaces;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PrinterEntity = CodePrintManager.Domain.Entities.Printer;

namespace CodePrintManager.Desktop.ViewModels.Components;

public partial class PrinterCardViewModel : ObservableObject
{
    private readonly ILocalizationService _loc;

    public int PrinterId { get; }
    public string Name { get; }
    public string IpAddress { get; }

    [ObservableProperty]
    private PrinterStatus _status = PrinterStatus.Offline;

    // Job info
    [ObservableProperty]
    private int? _jobId;

    [ObservableProperty]
    private string? _jobProductName;

    [ObservableProperty]
    private JobStatus? _jobStatus;

    [ObservableProperty]
    private int _currentJobProgress;

    [ObservableProperty]
    private int _currentJobTotal;

    [ObservableProperty]
    private double _progressPercent;

    [ObservableProperty]
    private string? _jobSummaryText;

    [ObservableProperty]
    private string _progressText = string.Empty;

    [ObservableProperty]
    private bool _hasJob;

    [ObservableProperty]
    private bool _isActive;

    [ObservableProperty]
    private DateTime? _completedAt;

    // Events for actions that need to be handled by parent
    public event EventHandler<int>? StartPrintRequested;
    public event EventHandler<int>? CancelJobRequested;
    public event EventHandler<int>? PauseJobRequested;
    public event EventHandler<int>? ResumeJobRequested;
    public event EventHandler<int>? CardClicked;

    public PrinterCardViewModel(PrinterEntity printer, ILocalizationService loc, PrintJob? currentJob = null)
    {
        _loc = loc;
        PrinterId = printer.Id;
        Name = printer.Name;
        IpAddress = printer.IpAddress;

        if (currentJob != null)
            SetJob(currentJob);
    }

    public void SetJob(PrintJob job)
    {
        JobId = job.Id;
        JobProductName = job.Product?.Name ?? _loc["Common_Unknown"];
        JobStatus = job.Status;
        CurrentJobProgress = job.CodesConfirmed;
        CurrentJobTotal = job.Quantity;
        CompletedAt = job.CompletedAt;
        HasJob = true;
        IsActive = job.Status is Domain.Enums.JobStatus.Printing
            or Domain.Enums.JobStatus.Ready
            or Domain.Enums.JobStatus.Preparing
            or Domain.Enums.JobStatus.Paused;

        UpdateDerivedProperties();
        UpdateSummaryText(job.Status);
    }

    partial void OnCurrentJobProgressChanged(int value) => UpdateDerivedProperties();
    partial void OnCurrentJobTotalChanged(int value) => UpdateDerivedProperties();

    partial void OnJobStatusChanged(JobStatus? value)
    {
        IsActive = value is Domain.Enums.JobStatus.Printing
            or Domain.Enums.JobStatus.Ready
            or Domain.Enums.JobStatus.Preparing
            or Domain.Enums.JobStatus.Paused;
        UpdateSummaryText(value);
    }

    private void UpdateDerivedProperties()
    {
        if (CurrentJobTotal > 0)
        {
            ProgressPercent = 100.0 * CurrentJobProgress / CurrentJobTotal;
            ProgressText = _loc.Format("Status_ProgressDisplay", CurrentJobProgress, CurrentJobTotal, (int)ProgressPercent);
        }
        else
        {
            ProgressPercent = 0;
            ProgressText = string.Empty;
        }
    }

    private void UpdateSummaryText(JobStatus? status)
    {
        JobSummaryText = status switch
        {
            Domain.Enums.JobStatus.Ready => _loc["Status_PreparedWaiting"],
            Domain.Enums.JobStatus.Paused => _loc.Format("Status_PausedAt", CurrentJobProgress, CurrentJobTotal),
            Domain.Enums.JobStatus.Completed => _loc.Format("Status_CompletedDate", CompletedAt!),
            Domain.Enums.JobStatus.Cancelled => _loc.Format("Status_CancelledDate", CompletedAt!),
            Domain.Enums.JobStatus.Error => _loc["Status_ErrorOccurred"],
            _ => null
        };
    }

    [RelayCommand]
    private void StartPrint()
    {
        if (JobId.HasValue)
            StartPrintRequested?.Invoke(this, JobId.Value);
    }

    [RelayCommand]
    private void CancelJob()
    {
        if (JobId.HasValue)
            CancelJobRequested?.Invoke(this, JobId.Value);
    }

    [RelayCommand]
    private void PauseJob()
    {
        if (JobId.HasValue)
            PauseJobRequested?.Invoke(this, JobId.Value);
    }

    [RelayCommand]
    private void ResumeJob()
    {
        if (JobId.HasValue)
            ResumeJobRequested?.Invoke(this, JobId.Value);
    }

    [RelayCommand]
    private void ClickCard()
    {
        if (JobId.HasValue)
            CardClicked?.Invoke(this, JobId.Value);
    }
}
