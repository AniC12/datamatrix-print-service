namespace CodePrintManager.Domain.Events;

public record JobProgressChangedEvent(int JobId, int Confirmed, int Total);
