namespace CodePrintManager.Domain.Events;

/// <summary>
/// Fired every poll cycle (~500ms) with the latest raw counter values from the printer.
/// </summary>
/// <param name="JobId">The job being monitored.</param>
/// <param name="CurrentCounter">Raw SPGGCP value (session counter).</param>
/// <param name="LifetimeCounter">Raw SPGGTP value (total lifetime counter). Null if not read this cycle.</param>
/// <param name="EffectiveCounter">Computed job-level counter (SPGGCP + offset).</param>
public record JobCountersUpdatedEvent(int JobId, int CurrentCounter, int? LifetimeCounter, int EffectiveCounter);
