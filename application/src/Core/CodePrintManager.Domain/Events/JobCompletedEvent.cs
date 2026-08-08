using CodePrintManager.Domain.Enums;

namespace CodePrintManager.Domain.Events;

public record JobCompletedEvent(int JobId, JobStatus FinalStatus);
