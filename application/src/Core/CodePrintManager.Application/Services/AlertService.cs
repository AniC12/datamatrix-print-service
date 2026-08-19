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

    public event EventHandler<AlertRaisedEvent>? AlertRaised;
    public event EventHandler<Guid>? AlertDismissed;

    public AlertService(IServiceScopeFactory scopeFactory, ILogger<AlertService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public void Raise(AlertSeverity severity, string source, string message,
                      int? printerId = null, int? jobId = null)
    {
        _logger.LogTrace("-> Raise(severity={Severity}, source={Source}, message={Message}, printerId={PrinterId}, jobId={JobId})",
            severity, source, message, printerId, jobId);
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
