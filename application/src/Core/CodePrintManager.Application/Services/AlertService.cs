using CodePrintManager.Domain.Enums;
using CodePrintManager.Domain.Events;
using CodePrintManager.Domain.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace CodePrintManager.Application.Services;

public class AlertService : IAlertService
{
    private readonly IServiceScopeFactory _scopeFactory;

    public event EventHandler<AlertRaisedEvent>? AlertRaised;
    public event EventHandler<Guid>? AlertDismissed;

    public AlertService(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
    }

    public void Raise(AlertSeverity severity, string source, string message,
                      int? printerId = null, int? jobId = null)
    {
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
    }

    public void Dismiss(Guid alertId)
    {
        AlertDismissed?.Invoke(this, alertId);
    }

    private async void ScheduleDismiss(Guid alertId, TimeSpan delay)
    {
        await Task.Delay(delay);
        Dismiss(alertId);
    }
}
