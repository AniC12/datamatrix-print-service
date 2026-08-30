using CodePrintManager.Domain.Events;
using Microsoft.Extensions.Logging;

namespace CodePrintManager.Application.Services;

/// <summary>
/// Singleton event bus for job progress and completion events.
/// ViewModels subscribe here for stable references that survive scope disposal.
/// </summary>
public class JobEventBus
{
    private readonly ILogger<JobEventBus> _logger;

    public JobEventBus(ILogger<JobEventBus> logger)
    {
        _logger = logger;
    }

    public event EventHandler<JobProgressChangedEvent>? ProgressChanged;
    public event EventHandler<JobCompletedEvent>? Completed;
    public event EventHandler<JobCountersUpdatedEvent>? CountersUpdated;

    public void RaiseProgressChanged(object sender, JobProgressChangedEvent e)
    {
        _logger.LogTrace("-> RaiseProgressChanged(JobId={JobId}, Confirmed={Confirmed}, Total={Total})", e.JobId, e.Confirmed, e.Total);
        ProgressChanged?.Invoke(sender, e);
        _logger.LogTrace("<- RaiseProgressChanged");
    }

    public void RaiseCompleted(object sender, JobCompletedEvent e)
    {
        _logger.LogTrace("-> RaiseCompleted(JobId={JobId}, FinalStatus={FinalStatus})", e.JobId, e.FinalStatus);
        Completed?.Invoke(sender, e);
        _logger.LogTrace("<- RaiseCompleted");
    }

    public void RaiseCountersUpdated(object sender, JobCountersUpdatedEvent e)
    {
        CountersUpdated?.Invoke(sender, e);
    }
}
