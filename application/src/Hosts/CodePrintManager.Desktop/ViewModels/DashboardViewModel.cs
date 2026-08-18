using System.Collections.ObjectModel;
using CodePrintManager.Application.Services;
using CodePrintManager.Data;
using CodePrintManager.Domain.Entities;
using CodePrintManager.Domain.Enums;
using CodePrintManager.Domain.Events;
using CodePrintManager.Domain.Interfaces;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace CodePrintManager.Desktop.ViewModels;

public partial class DashboardViewModel : ObservableObject
{
    private readonly PrinterConnectionManager _connectionManager;
    private readonly IPrintJobService _printJobService;
    private readonly JobEventBus _eventBus;
    private readonly AppDbContext _db;
    private readonly ILogger<DashboardViewModel> _logger;
    private readonly ILocalizationService _loc;

    public ObservableCollection<Components.PrinterCardViewModel> PrinterCards { get; } = new();
    public ObservableCollection<AuditEntryViewModel> RecentActivity { get; } = new();

    public event EventHandler? NavigateToNewJobRequested;
    public event EventHandler<int>? NavigateToJobRequested;

    public DashboardViewModel(
        PrinterConnectionManager connectionManager,
        IPrintJobService printJobService,
        JobEventBus eventBus,
        AppDbContext db,
        ILogger<DashboardViewModel> logger,
        ILocalizationService loc)
    {
        _connectionManager = connectionManager;
        _printJobService = printJobService;
        _eventBus = eventBus;
        _db = db;
        _logger = logger;
        _loc = loc;

        _connectionManager.PrinterStatusChanged += OnPrinterStatusChanged;
        _eventBus.ProgressChanged += OnJobProgressChanged;
        _eventBus.Completed += OnJobCompleted;
        _loc.LanguageChanged += OnLanguageChanged;
    }

    private void OnLanguageChanged()
    {
        System.Windows.Application.Current.Dispatcher.Invoke(() => _ = RefreshAsync());
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        _logger.LogInformation("Dashboard loading");
        // Load printers with their most recent job
        var printers = await _db.Printers.ToListAsync();
        PrinterCards.Clear();

        foreach (var p in printers)
        {
            // Get the most recent job for this printer
            var latestJob = await _db.PrintJobs
                .Include(j => j.Product)
                .Include(j => j.Printer)
                .Where(j => j.PrinterId == p.Id)
                .OrderByDescending(j => j.CreatedAt)
                .FirstOrDefaultAsync();

            // Only show printers that have had at least one job (per spec)
            if (latestJob == null)
                continue;

            var card = new Components.PrinterCardViewModel(p, _loc, latestJob);
            card.StartPrintRequested += OnStartPrintRequested;
            card.CancelJobRequested += OnCancelJobRequested;
            card.PauseJobRequested += OnPauseJobRequested;
            card.ResumeJobRequested += OnResumeJobRequested;
            card.CardClicked += OnCardClicked;
            PrinterCards.Add(card);
        }

        // Sort: active jobs first (Printing, Paused, Error, Ready), then completed
        var sorted = PrinterCards.OrderBy(c => c.JobStatus switch
        {
            JobStatus.Printing => 0,
            JobStatus.Paused => 1,
            JobStatus.Error => 2,
            JobStatus.Ready => 3,
            JobStatus.Preparing => 4,
            _ => 5
        }).ToList();

        PrinterCards.Clear();
        foreach (var c in sorted)
            PrinterCards.Add(c);

        // Load recent activity with product/printer name lookups
        var productNames = await _db.ProductNodes.ToDictionaryAsync(p => p.Id, p => p.Name);
        var printerNames = await _db.Printers.ToDictionaryAsync(p => p.Id, p => p.Name);

        var recentEntries = await _db.AuditLog
            .Where(a => a.EventType != "job_created")
            .OrderByDescending(a => a.CreatedAt)
            .Take(20)
            .ToListAsync();

        // Look up current job statuses for entries that reference a job
        var jobIds = recentEntries.Where(e => e.JobId.HasValue).Select(e => e.JobId!.Value).Distinct().ToList();
        var jobStatuses = await _db.PrintJobs
            .Where(j => jobIds.Contains(j.Id))
            .ToDictionaryAsync(j => j.Id, j => j.Status);

        RecentActivity.Clear();
        foreach (var entry in recentEntries)
        {
            var productName = entry.ProductId.HasValue && productNames.TryGetValue(entry.ProductId.Value, out var pn) ? pn : null;
            var printerName = entry.PrinterId.HasValue && printerNames.TryGetValue(entry.PrinterId.Value, out var prn) ? prn : null;
            JobStatus? jobStatus = entry.JobId.HasValue && jobStatuses.TryGetValue(entry.JobId.Value, out var js) ? js : null;
            RecentActivity.Add(new AuditEntryViewModel(entry, productName, printerName, jobStatus, _loc));
        }

        _logger.LogInformation("Dashboard loaded: {CardCount} printer cards, {ActivityCount} recent entries",
            PrinterCards.Count, RecentActivity.Count);
    }

    [RelayCommand]
    private void NewJob()
    {
        _logger.LogInformation("Dashboard: New Job clicked");
        NavigateToNewJobRequested?.Invoke(this, EventArgs.Empty);
    }

    private async void OnStartPrintRequested(object? sender, int jobId)
    {
        _logger.LogInformation("Dashboard: Start print requested for Job {JobId}", jobId);
        try
        {
            await _printJobService.StartJobAsync(jobId);
            await RefreshAsync();
        }
        catch (Exception ex) { _logger.LogError(ex, "Dashboard: Start print failed for Job {JobId}", jobId); }
    }

    private async void OnCancelJobRequested(object? sender, int jobId)
    {
        _logger.LogInformation("Dashboard: Cancel requested for Job {JobId}", jobId);
        try
        {
            await _printJobService.CancelJobAsync(jobId);
            await RefreshAsync();
        }
        catch (Exception ex) { _logger.LogError(ex, "Dashboard: Cancel failed for Job {JobId}", jobId); }
    }

    private async void OnPauseJobRequested(object? sender, int jobId)
    {
        _logger.LogInformation("Dashboard: Pause requested for Job {JobId}", jobId);
        try
        {
            await _printJobService.PauseJobAsync(jobId);
            await RefreshAsync();
        }
        catch (Exception ex) { _logger.LogError(ex, "Dashboard: Pause failed for Job {JobId}", jobId); }
    }

    private async void OnResumeJobRequested(object? sender, int jobId)
    {
        _logger.LogInformation("Dashboard: Resume requested for Job {JobId}", jobId);
        try
        {
            await _printJobService.ResumeJobAsync(jobId);
            await RefreshAsync();
        }
        catch (Exception ex) { _logger.LogError(ex, "Dashboard: Resume failed for Job {JobId}", jobId); }
    }

    private void OnCardClicked(object? sender, int jobId)
    {
        _logger.LogDebug("Dashboard: Card clicked for Job {JobId}", jobId);
        NavigateToJobRequested?.Invoke(this, jobId);
    }

    private void OnPrinterStatusChanged(object? sender, PrinterStatusChangedEvent e)
    {
        System.Windows.Application.Current.Dispatcher.Invoke(() =>
        {
            var card = PrinterCards.FirstOrDefault(c => c.PrinterId == e.PrinterId);
            if (card != null)
                card.Status = e.NewStatus;
        });
    }

    private void OnJobProgressChanged(object? sender, JobProgressChangedEvent e)
    {
        System.Windows.Application.Current.Dispatcher.Invoke(() =>
        {
            var card = PrinterCards.FirstOrDefault(c => c.JobId == e.JobId);
            if (card != null)
            {
                card.CurrentJobProgress = e.Confirmed;
                card.CurrentJobTotal = e.Total;
                card.JobStatus = JobStatus.Printing;
            }
        });
    }

    private void OnJobCompleted(object? sender, JobCompletedEvent e)
    {
        _logger.LogInformation("Dashboard: Job {JobId} completed", e.JobId);
        System.Windows.Application.Current.Dispatcher.Invoke(() =>
        {
            var card = PrinterCards.FirstOrDefault(c => c.JobId == e.JobId);
            if (card != null)
            {
                card.JobStatus = e.FinalStatus;
                card.Status = PrinterStatus.Idle;
            }
        });
    }
}

public class AuditEntryViewModel
{
    public AuditEntry Entry { get; }
    public string Time => Entry.CreatedAt.ToLocalTime().ToString("HH:mm");
    public string Description { get; }
    public string FullText { get; }
    public System.Windows.Media.Brush StatusBrush { get; }

    public AuditEntryViewModel(AuditEntry entry, string? productName, string? printerName, JobStatus? jobStatus, ILocalizationService loc)
    {
        Entry = entry;
        Description = FormatDescription(entry, productName, printerName, jobStatus, loc);
        FullText = $"{Time}  {Description}";
        StatusBrush = GetStatusBrush(entry.EventType, jobStatus, entry.Details);
    }

    private static string FormatDescription(AuditEntry entry, string? productName, string? printerName, JobStatus? jobStatus, ILocalizationService loc)
    {
        var details = ParseDetails(entry.Details);

        // For job_created events, append the current job status
        var jobStatusSuffix = "";
        if (entry.EventType == "job_created" && jobStatus.HasValue)
        {
            jobStatusSuffix = $" [{loc[GetJobStatusKey(jobStatus.Value)]}]";
        }

        return entry.EventType switch
        {
            "import" => loc.Format("Dashboard_Activity_Import",
                GetDetail(details, "imported", "?"),
                productName ?? "?",
                GetDetail(details, "batchName", "")),

            "job_created" => loc.Format("Dashboard_Activity_JobCreated",
                entry.JobId?.ToString() ?? "?",
                productName ?? "?",
                printerName ?? "?",
                GetDetail(details, "quantity", "?")) + jobStatusSuffix,

            "job_started" => loc.Format("Dashboard_Activity_JobStarted",
                entry.JobId?.ToString() ?? "?",
                printerName ?? "?"),

            "job_cancelled" => loc.Format("Dashboard_Activity_JobCancelled",
                entry.JobId?.ToString() ?? "?"),

            "job_paused" => loc.Format("Dashboard_Activity_JobPaused",
                entry.JobId?.ToString() ?? "?"),

            "job_resumed" => loc.Format("Dashboard_Activity_JobResumed",
                entry.JobId?.ToString() ?? "?"),

            "admin_status_change" => loc.Format("Dashboard_Activity_StatusChange",
                GetDetail(details, "codeCount", "?"),
                GetDetail(details, "newStatus", GetDetail(details, "toStatus", "?"))),

            "admin_move" => loc.Format("Dashboard_Activity_Move",
                GetDetail(details, "codeCount", "?"),
                GetDetail(details, "targetName", "?")),

            "admin_archive" => loc.Format("Dashboard_Activity_Archive",
                GetDetail(details, "codeCount", "?")),

            "admin_unassign" => loc.Format("Dashboard_Activity_Unassign",
                GetDetail(details, "codeCount", "?"),
                productName ?? "?"),

            "admin_undo" => loc.Format("Dashboard_Activity_Undo",
                GetDetail(details, "operationType", "?")),

            "alert" => loc.Format("Dashboard_Activity_Alert",
                GetDetail(details, "severity", ""),
                GetDetail(details, "message", "")),

            // printer_updated, printer_deleted, printer_files_deleted have human-readable Details
            _ when entry.Details != null && !entry.Details.TrimStart().StartsWith('{') =>
                entry.Details,

            _ => entry.EventType.Replace('_', ' ')
        };
    }

    private static string GetJobStatusKey(JobStatus status) => status switch
    {
        JobStatus.Preparing => "JobStatus_Preparing",
        JobStatus.Ready => "JobStatus_Ready",
        JobStatus.Printing => "JobStatus_Printing",
        JobStatus.Paused => "JobStatus_Paused",
        JobStatus.Completed => "JobStatus_Completed",
        JobStatus.Cancelled => "JobStatus_Cancelled",
        JobStatus.Error => "JobStatus_Error",
        _ => "JobStatus_Unknown"
    };

    private static System.Windows.Media.Brush GetStatusBrush(string eventType, JobStatus? jobStatus, string? details = null)
    {
        // Job creation is always light blue (neutral event)
        if (eventType == "job_created")
        {
            return Brush("#63B3ED");  // Light blue
        }

        // For alerts, color by severity level
        if (eventType == "alert")
        {
            var parsed = ParseDetails(details);
            var severity = GetDetail(parsed, "severity", "").ToLowerInvariant();
            return severity switch
            {
                "info" => Brush("#38A169"),       // Green - informational
                "warning" => Brush("#D69E2E"),   // Amber - warning
                "error" => Brush("#E53E3E"),     // Red - error
                _ => Brush("#D69E2E")            // Amber as default for unknown severity
            };
        }

        return eventType switch
        {
            "import" => Brush("#805AD5"),           // Purple
            "job_started" => Brush("#3182CE"),      // Blue
            "job_cancelled" => Brush("#A0AEC0"),    // Gray
            "job_paused" => Brush("#D69E2E"),       // Amber
            "job_resumed" => Brush("#3182CE"),      // Blue
            "admin_status_change" => Brush("#805AD5"), // Purple
            "admin_move" => Brush("#805AD5"),       // Purple
            "admin_archive" => Brush("#718096"),    // Gray
            "admin_unassign" => Brush("#718096"),   // Gray
            "admin_undo" => Brush("#DD6B20"),       // Orange
            _ => Brush("#718096")                   // Default gray
        };
    }

    private static System.Windows.Media.SolidColorBrush Brush(string hex)
    {
        return new System.Windows.Media.SolidColorBrush(
            (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(hex));
    }

    private static Dictionary<string, string> ParseDetails(string? json)
    {
        if (string.IsNullOrEmpty(json) || !json.TrimStart().StartsWith('{'))
            return new Dictionary<string, string>();

        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            var result = new Dictionary<string, string>();
            foreach (var prop in doc.RootElement.EnumerateObject())
                result[prop.Name] = prop.Value.ToString();
            return result;
        }
        catch
        {
            return new Dictionary<string, string>();
        }
    }

    private static string GetDetail(Dictionary<string, string> details, string key, string fallback)
    {
        return details.TryGetValue(key, out var val) ? val : fallback;
    }
}
