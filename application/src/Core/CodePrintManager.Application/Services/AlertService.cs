using System.Collections.Concurrent;
using CodePrintManager.Domain.Enums;
using CodePrintManager.Domain.Events;
using CodePrintManager.Domain.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace CodePrintManager.Application.Services;

public class AlertService : IAlertService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<AlertService> _logger;

    /// <summary>
    /// Category-based deduplication: tracks last alert time per dedup key.
    /// Key format: "{source}:{printerId}:{deduplicationKey}".
    /// Alerts with the same key within the dedup window are suppressed.
    /// </summary>
    private readonly ConcurrentDictionary<string, DateTime> _lastAlertTimes = new();
    private static readonly TimeSpan DeduplicationWindow = TimeSpan.FromSeconds(30);

    public event EventHandler<AlertRaisedEvent>? AlertRaised;
    public event EventHandler<Guid>? AlertDismissed;

    public AlertService(IServiceScopeFactory scopeFactory, ILogger<AlertService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public void Raise(AlertSeverity severity, string source, string message,
                      int? printerId = null, int? jobId = null, string? deduplicationKey = null)
    {
        _logger.LogTrace("-> Raise(severity={Severity}, source={Source}, message={Message}, printerId={PrinterId}, jobId={JobId}, dedupKey={DedupKey})",
            severity, source, message, printerId, jobId, deduplicationKey);

        // Category-based deduplication: suppress duplicate alerts within the window.
        // Uses a category key (e.g., "connection_lost") instead of the full message
        // text, because messages contain dynamic values that change on each call.
        if (deduplicationKey != null)
        {
            var fullKey = $"{source}:{printerId}:{deduplicationKey}";
            var now = DateTime.UtcNow;
            if (_lastAlertTimes.TryGetValue(fullKey, out var lastTime)
                && now - lastTime < DeduplicationWindow)
            {
                _logger.LogTrace("Alert deduplicated: {Key} (last raised {Ago:F1}s ago)",
                    fullKey, (now - lastTime).TotalSeconds);
                return;
            }
            _lastAlertTimes[fullKey] = now;

            // Periodic cleanup: remove stale entries (older than 5 minutes)
            foreach (var entry in _lastAlertTimes)
            {
                if (now - entry.Value > TimeSpan.FromMinutes(5))
                    _lastAlertTimes.TryRemove(entry.Key, out _);
            }
        }
        // Log at the matching severity level
        var logMsg = "Alert [{Severity}] {Source}: {Message} (Printer={PrinterId}, Job={JobId})";
        switch (severity)
        {
            case AlertSeverity.Error:
                _logger.LogError(logMsg, severity, source, message, printerId, jobId);
                break;
            case AlertSeverity.Warning:
                _logger.LogWarning(logMsg, severity, source, message, printerId, jobId);
                break;
            default:
                _logger.LogInformation(logMsg, severity, source, message, printerId, jobId);
                break;
        }

        var alert = new AlertRaisedEvent(
            Id: Guid.NewGuid(),
            Timestamp: DateTime.Now,
            Severity: severity,
            Source: source,
            Message: message,
            PrinterId: printerId,
            JobId: jobId);

        AlertRaised?.Invoke(this, alert);

        // Persist to audit log (fire-and-forget, uses its own scope)
        _ = Task.Run(async () =>
        {
            using var scope = _scopeFactory.CreateScope();
            var audit = scope.ServiceProvider.GetRequiredService<IAuditService>();
            await audit.LogAsync("alert", printerId: printerId, jobId: jobId,
                details: new { severity = severity.ToString(), source, message });
        });

        // Auto-dismiss info alerts after 30s
        if (severity == AlertSeverity.Info)
            ScheduleDismiss(alert.Id, TimeSpan.FromSeconds(30));

        _logger.LogTrace("<- Raise");
    }

    public void Dismiss(Guid alertId)
    {
        _logger.LogTrace("-> Dismiss(alertId={AlertId})", alertId);
        AlertDismissed?.Invoke(this, alertId);
        _logger.LogTrace("<- Dismiss");
    }

    private async void ScheduleDismiss(Guid alertId, TimeSpan delay)
    {
        _logger.LogTrace("-> ScheduleDismiss(alertId={AlertId}, delay={Delay})", alertId, delay);
        await Task.Delay(delay);
        Dismiss(alertId);
        _logger.LogTrace("<- ScheduleDismiss");
    }
}
