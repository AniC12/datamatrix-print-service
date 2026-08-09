using CodePrintManager.Domain.Entities;
using CodePrintManager.Domain.Enums;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PrinterEntity = CodePrintManager.Domain.Entities.Printer;

namespace CodePrintManager.Desktop.ViewModels.Components;

public partial class PrinterCardViewModel : ObservableObject
{
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
    private string? _jobSummaryText;

    [ObservableProperty]
    private string _progressText = string.Empty;

    [ObservableProperty]
    private bool _hasJob;

    [ObservableProperty]
    private DateTime? _completedAt;

    // Events for actions that need to be handled by parent
    public event EventHandler<int>? StartPrintRequested;
    public event EventHandler<int>? CancelJobRequested;
    public event EventHandler<int>? CardClicked;

    public PrinterCardViewModel(PrinterEntity printer, PrintJob? currentJob = null)
    {
        PrinterId = printer.Id;
        Name = printer.Name;
        IpAddress = printer.IpAddress;

        if (currentJob != null)
            SetJob(currentJob);
    }

    public void SetJob(PrintJob job)
    {
        JobId = job.Id;
        JobProductName = job.Product?.Name ?? "Unknown";
        JobStatus = job.Status;
        CurrentJobProgress = job.CodesConfirmed;
        CurrentJobTotal = job.Quantity;
        CompletedAt = job.CompletedAt;
        HasJob = true;

        UpdateProgressText();
        UpdateSummaryText(job);
    }

    private void UpdateProgressText()
    {
        if (CurrentJobTotal > 0)
        {
            var pct = CurrentJobTotal > 0 ? (int)(100.0 * CurrentJobProgress / CurrentJobTotal) : 0;
            ProgressText = $"{CurrentJobProgress}/{CurrentJobTotal} ({pct}%)";
        }
        else
        {
            ProgressText = string.Empty;
        }
    }

    private void UpdateSummaryText(PrintJob job)
    {
        JobSummaryText = job.Status switch
        {
            Domain.Enums.JobStatus.Ready => "Prepared, waiting to start",
            Domain.Enums.JobStatus.Completed => $"Completed {job.CompletedAt:MMM d HH:mm}",
            Domain.Enums.JobStatus.Cancelled => $"Cancelled {job.CompletedAt:MMM d HH:mm}",
            Domain.Enums.JobStatus.Error => "Error occurred",
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
    private void ClickCard()
    {
        if (JobId.HasValue)
            CardClicked?.Invoke(this, JobId.Value);
    }
}
