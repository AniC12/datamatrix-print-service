using CodePrintManager.Domain.Enums;

namespace CodePrintManager.Domain.Events;

public record AlertRaisedEvent(
    Guid Id,
    DateTime Timestamp,
    AlertSeverity Severity,
    string Source,
    string Message,
    int? PrinterId = null,
    int? JobId = null);
