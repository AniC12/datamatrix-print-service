namespace CodePrintManager.Domain.Enums;

public enum JobStatus
{
    Preparing,
    Ready,
    Printing,
    Paused,
    Disconnected,
    Completed,
    Cancelled,
    Error
}
