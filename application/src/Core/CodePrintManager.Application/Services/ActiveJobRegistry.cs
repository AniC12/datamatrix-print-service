using System.Collections.Concurrent;

namespace CodePrintManager.Application.Services;

/// <summary>
/// Singleton registry tracking running JobExecutors and per-printer locks.
/// Outlives any individual scoped PrintJobService instance.
/// </summary>
public class ActiveJobRegistry
{
    private readonly ConcurrentDictionary<int, JobExecutor> _executors = new();
    private readonly ConcurrentDictionary<int, SemaphoreSlim> _printerLocks = new();

    public SemaphoreSlim GetPrinterLock(int printerId)
        => _printerLocks.GetOrAdd(printerId, _ => new SemaphoreSlim(1, 1));

    public void Register(int jobId, JobExecutor executor)
        => _executors[jobId] = executor;

    public bool TryGet(int jobId, out JobExecutor? executor)
        => _executors.TryGetValue(jobId, out executor);

    public bool TryRemove(int jobId)
        => _executors.TryRemove(jobId, out _);

    public JobExecutor? Get(int jobId)
        => _executors.TryGetValue(jobId, out var e) ? e : null;
}
