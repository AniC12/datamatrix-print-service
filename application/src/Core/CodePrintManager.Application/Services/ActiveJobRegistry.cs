using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace CodePrintManager.Application.Services;

/// <summary>
/// Singleton registry tracking running JobExecutors, ReadyWatchers, and per-printer locks.
/// Outlives any individual scoped PrintJobService instance.
/// </summary>
public class ActiveJobRegistry
{
    private readonly ConcurrentDictionary<int, JobExecutor> _executors = new();
    private readonly ConcurrentDictionary<int, ReadyWatcher> _watchers = new();
    private readonly ConcurrentDictionary<int, SemaphoreSlim> _printerLocks = new();
    private readonly ILogger<ActiveJobRegistry> _logger;

    public ActiveJobRegistry(ILogger<ActiveJobRegistry> logger)
    {
        _logger = logger;
    }

    public SemaphoreSlim GetPrinterLock(int printerId)
    {
        _logger.LogTrace("-> GetPrinterLock(printerId={PrinterId})", printerId);
        var result = _printerLocks.GetOrAdd(printerId, _ => new SemaphoreSlim(1, 1));
        _logger.LogTrace("<- GetPrinterLock = {Result}", result);
        return result;
    }

    // --- Executors ---

    public void Register(int jobId, JobExecutor executor)
    {
        _logger.LogTrace("-> Register(jobId={JobId}, executor={Executor})", jobId, executor);
        _executors[jobId] = executor;
        _logger.LogTrace("<- Register");
    }

    public bool TryGet(int jobId, out JobExecutor? executor)
    {
        _logger.LogTrace("-> TryGet(jobId={JobId})", jobId);
        var found = _executors.TryGetValue(jobId, out executor);
        _logger.LogTrace("<- TryGet = {Found} (key {Status})", found, found ? "found" : "not found");
        return found;
    }

    public bool TryRemove(int jobId)
    {
        _logger.LogTrace("-> TryRemove(jobId={JobId})", jobId);
        var removed = _executors.TryRemove(jobId, out _);
        _logger.LogTrace("<- TryRemove = {Removed} (key {Status})", removed, removed ? "found" : "not found");
        return removed;
    }

    public JobExecutor? Get(int jobId)
    {
        _logger.LogTrace("-> Get(jobId={JobId})", jobId);
        var result = _executors.TryGetValue(jobId, out var e) ? e : null;
        _logger.LogTrace("<- Get = {Result}", result?.ToString() ?? "null");
        return result;
    }

    // --- ReadyWatchers ---

    public void RegisterWatcher(int jobId, ReadyWatcher watcher)
    {
        _logger.LogTrace("-> RegisterWatcher(jobId={JobId}, watcher={Watcher})", jobId, watcher);
        _watchers[jobId] = watcher;
        _logger.LogTrace("<- RegisterWatcher");
    }

    public bool TryGetWatcher(int jobId, out ReadyWatcher? watcher)
    {
        _logger.LogTrace("-> TryGetWatcher(jobId={JobId})", jobId);
        var found = _watchers.TryGetValue(jobId, out watcher);
        _logger.LogTrace("<- TryGetWatcher = {Found} (key {Status})", found, found ? "found" : "not found");
        return found;
    }

    public bool TryRemoveWatcher(int jobId)
    {
        _logger.LogTrace("-> TryRemoveWatcher(jobId={JobId})", jobId);
        var removed = _watchers.TryRemove(jobId, out _);
        _logger.LogTrace("<- TryRemoveWatcher = {Removed} (key {Status})", removed, removed ? "found" : "not found");
        return removed;
    }

    /// <summary>
    /// Stops all running executors and watchers. Called during graceful shutdown
    /// to ensure poll loops are terminated before adapters are disposed.
    /// </summary>
    public async Task StopAllAsync()
    {
        _logger.LogInformation("StopAllAsync: stopping {ExecutorCount} executor(s), {WatcherCount} watcher(s)",
            _executors.Count, _watchers.Count);

        foreach (var (jobId, watcher) in _watchers)
        {
            try { await watcher.StopAsync(); }
            catch (Exception ex) { _logger.LogWarning(ex, "Error stopping watcher for Job {JobId}", jobId); }
        }
        _watchers.Clear();

        foreach (var (jobId, executor) in _executors)
        {
            try { await executor.StopAsync(); }
            catch (Exception ex) { _logger.LogWarning(ex, "Error stopping executor for Job {JobId}", jobId); }
        }
        _executors.Clear();

        _logger.LogInformation("StopAllAsync: all executors and watchers stopped");
    }

    /// <summary>
    /// Stops and removes all executors for jobs on the given printer.
    /// Called when a printer is manually disconnected so executors don't poll a disposed adapter.
    /// Jobs stay in their current DB status — user must resume after reconnecting.
    /// </summary>
    public async Task<List<int>> StopExecutorsForPrinterAsync(int printerId)
    {
        _logger.LogTrace("-> StopExecutorsForPrinterAsync(printerId={PrinterId})", printerId);
        var stoppedJobIds = new List<int>();

        foreach (var (jobId, executor) in _executors)
        {
            if (executor.PrinterId == printerId)
            {
                try { await executor.StopAsync(); }
                catch (Exception ex) { _logger.LogWarning(ex, "Error stopping executor for Job {JobId}", jobId); }
                _executors.TryRemove(jobId, out _);
                stoppedJobIds.Add(jobId);
                _logger.LogDebug("Executor stopped for Job {JobId} (printer disconnect)", jobId);
            }
        }

        _logger.LogTrace("<- StopExecutorsForPrinterAsync: stopped {Count} executor(s)", stoppedJobIds.Count);
        return stoppedJobIds;
    }

    /// <summary>
    /// Stops and removes all ReadyWatchers for jobs on the given printer.
    /// Called when a printer is manually disconnected so watchers don't poll a stale adapter.
    /// Returns the job IDs whose watchers were stopped (for potential respawn on reconnect).
    /// </summary>
    public async Task<List<int>> StopWatchersForPrinterAsync(int printerId)
    {
        _logger.LogTrace("-> StopWatchersForPrinterAsync(printerId={PrinterId})", printerId);
        var stoppedJobIds = new List<int>();

        foreach (var (jobId, watcher) in _watchers)
        {
            if (watcher.PrinterId == printerId)
            {
                await watcher.StopAsync();
                _watchers.TryRemove(jobId, out _);
                stoppedJobIds.Add(jobId);
                _logger.LogDebug("ReadyWatcher stopped for Job {JobId} (printer disconnect)", jobId);
            }
        }

        _logger.LogTrace("<- StopWatchersForPrinterAsync: stopped {Count} watcher(s)", stoppedJobIds.Count);
        return stoppedJobIds;
    }
}
