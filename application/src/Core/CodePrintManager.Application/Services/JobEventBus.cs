using CodePrintManager.Domain.Events;

namespace CodePrintManager.Application.Services;

/// <summary>
/// Singleton event bus for job progress and completion events.
/// ViewModels subscribe here for stable references that survive scope disposal.
/// </summary>
public class JobEventBus
{
    public event EventHandler<JobProgressChangedEvent>? ProgressChanged;
    public event EventHandler<JobCompletedEvent>? Completed;

    public void RaiseProgressChanged(object sender, JobProgressChangedEvent e)
        => ProgressChanged?.Invoke(sender, e);

    public void RaiseCompleted(object sender, JobCompletedEvent e)
        => Completed?.Invoke(sender, e);
}
