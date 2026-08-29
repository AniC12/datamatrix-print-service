using CodePrintManager.Domain.Enums;
using CodePrintManager.Domain.Events;

namespace CodePrintManager.Domain.Interfaces;

public interface IAlertService
{
    event EventHandler<AlertRaisedEvent>? AlertRaised;
    event EventHandler<Guid>? AlertDismissed;

    void Raise(AlertSeverity severity, string source, string message,
               int? printerId = null, int? jobId = null, string? deduplicationKey = null);
    void Dismiss(Guid alertId);
}
