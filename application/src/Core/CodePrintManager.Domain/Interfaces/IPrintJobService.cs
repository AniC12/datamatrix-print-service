using CodePrintManager.Domain.Entities;

namespace CodePrintManager.Domain.Interfaces;

public interface IPrintJobService
{
    Task<PrintJob> CreateJobAsync(int productId, int printerId, int quantity);
    Task PrepareJobAsync(int jobId, CancellationToken ct = default, IProgress<string>? progress = null);
    Task StartJobAsync(int jobId, CancellationToken ct = default);
    Task CancelJobAsync(int jobId);
    Task PauseJobAsync(int jobId);
    Task<List<PrintJob>> GetActiveJobsAsync();
    Task<List<PrintJob>> GetJobHistoryAsync(int? printerId = null, int? productId = null);
    Task<List<PrintJob>> GetStaleJobsAsync();
    Task ResumeJobAsync(int jobId, CancellationToken ct = default);
    /// <summary>
    /// Respawns ReadyWatchers for any Ready jobs on the given printer.
    /// Called after a manual reconnect so watchers use the new adapter instance.
    /// </summary>
    Task RespawnWatchersForPrinterAsync(int printerId, CancellationToken ct = default);

    /// <summary>
    /// Restores stale jobs automatically at startup (replaces the old RecoveryDialog flow).
    /// - Preparing → auto-cancelled (incomplete preparation)
    /// - Ready → restored with ReadyWatcher + first-poll SPGGTP validation
    /// - Printing → restored with executor from persisted state + post-reconnect inspection
    /// - Paused → left as Paused (operator resumes or cancels manually)
    /// </summary>
    Task RestoreStaleJobsAsync(CancellationToken ct = default);
}
