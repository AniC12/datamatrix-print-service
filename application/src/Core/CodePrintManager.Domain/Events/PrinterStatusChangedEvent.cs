using CodePrintManager.Domain.Enums;

namespace CodePrintManager.Domain.Events;

public record PrinterStatusChangedEvent(int PrinterId, PrinterStatus OldStatus, PrinterStatus NewStatus);
