using CodePrintManager.Domain.Entities;
using CodePrintManager.Domain.Enums;
using CodePrintManager.Domain.Interfaces;
using Microsoft.Extensions.Logging;

namespace CodePrintManager.Application.Services;

/// <summary>
/// Lightweight periodic monitor for a job in Ready status.
/// Detects if someone presses Print on the printer's touchscreen
/// before the operator clicks Start in the app.
/// 
/// Polls SPPSTA and SPGGCP every few seconds. If printing is detected,
/// raises the <see cref="ExternalPrintDetected"/> event so the caller
/// can transition the job to Printing with a full JobExecutor.
/// </summary>
public class ReadyWatcher
{
    private readonly PrintJob _job;
    private readonly IPrinterAdapter _adapter;
    private readonly IAlertService _alerts;
    private readonly ILogger _logger;
    private readonly int _spggcpBaseline;

    private CancellationTokenSource? _cts;
    private Task? _watchTask;
    private int _pollCount;
    private bool _connectionLostNotified;

    /// <summary>The printer ID this watcher is monitoring.</summary>
    public int PrinterId => _job.PrinterId;

    /// <summary>
    /// Raised when the watcher detects that the printer started printing
    /// (SPPSTA = Printing or SPGGCP > 0).
    /// The int parameter is the job ID.
    /// </summary>
    public event EventHandler<int>? ExternalPrintDetected;

    /// <summary>
    /// Raised when the watcher detects a connection loss (IOException).
    /// The int parameter is the printer ID, allowing the connection manager
    /// to start a reconnect loop.
    /// </summary>
    public event EventHandler<int>? ConnectionLost;

    /// <param name="spggcpBaseline">
    /// SPGGCP value recorded right after template activation during Prepare.
    /// SPGGCP is cumulative on real hardware (does NOT reset on SPLLTF),
    /// so external printing is detected as currentCounter > baseline.
    /// </param>
    public ReadyWatcher(
        PrintJob job,
        IPrinterAdapter adapter,
        IAlertService alerts,
        ILogger logger,
        int spggcpBaseline = 0)
    {
        _job = job;
        _adapter = adapter;
        _alerts = alerts;
        _logger = logger;
        _spggcpBaseline = spggcpBaseline;
        _logger.LogTrace("-> ReadyWatcher constructed (jobId={JobId}, printerId={PrinterId}, spggcpBaseline={Baseline})",
            job.Id, job.PrinterId, spggcpBaseline);
    }

    public void Start()
    {
        if (_watchTask != null && !_watchTask.IsCompleted)
        {
            _logger.LogWarning("ReadyWatcher.Start() called while already running for Job {JobId}. Ignoring.", _job.Id);
            return;
        }
        _logger.LogTrace("-> ReadyWatcher.Start() for Job {JobId}", _job.Id);
        _logger.LogInformation(
            "ReadyWatcher started: Job {JobId} on printer {PrinterId}",
            _job.Id, _job.PrinterId);
        _cts = new CancellationTokenSource();
        _pollCount = 0;
        _watchTask = WatchLoopAsync(_cts.Token);
        _logger.LogTrace("<- ReadyWatcher.Start() (watch loop launched)");
    }

    public async Task StopAsync()
    {
        _logger.LogTrace("-> ReadyWatcher.StopAsync() for Job {JobId}", _job.Id);
        _logger.LogInformation("ReadyWatcher stopped: Job {JobId} (polled {PollCount} times)", _job.Id, _pollCount);
        _cts?.Cancel();
        if (_watchTask != null)
        {
            try { await _watchTask; }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "ReadyWatcher: watch loop for Job {JobId} ended with error (suppressed during stop)",
                    _job.Id);
            }
        }
        _logger.LogTrace("<- ReadyWatcher.StopAsync() completed");
    }

    private async Task WatchLoopAsync(CancellationToken ct)
    {
        _logger.LogTrace("-> WatchLoopAsync() starting initial 2s delay");
        // Initial delay before first check (let things settle after Prepare)
        await Task.Delay(2000, ct);

        while (!ct.IsCancellationRequested)
        {
            try
            {
                _pollCount++;
                _logger.LogTrace("   WatchLoopAsync poll #{PollCount} for Job {JobId}", _pollCount, _job.Id);

                var status = await _adapter.GetStatusAsync(ct);
                var currentCounter = await _adapter.GetCurrentCounterAsync(ct);

                _logger.LogTrace("   WatchLoopAsync poll #{PollCount}: status={Status}, counter={Counter}",
                    _pollCount, status, currentCounter);

                if (status == PrinterStatus.Printing || currentCounter > _spggcpBaseline)
                {
                    _logger.LogWarning(
                        "ReadyWatcher: EXTERNAL PRINT DETECTED for Job {JobId}! " +
                        "Status={Status}, SPGGCP={Counter} (detected on poll #{PollCount})",
                        _job.Id, status, currentCounter, _pollCount);

                    _alerts.Raise(AlertSeverity.Warning, _job.Printer.Name,
                        $"Printing started externally on '{_job.Printer.Name}'. " +
                        $"Job #{_job.Id} transitioning to Printing for tracking.",
                        printerId: _job.PrinterId, jobId: _job.Id);

                    ExternalPrintDetected?.Invoke(this, _job.Id);
                    _logger.LogTrace("<- WatchLoopAsync exiting (external print detected)");
                    return; // Stop watching — the caller will spawn a JobExecutor
                }

                await Task.Delay(3000, ct);
            }
            catch (OperationCanceledException)
            {
                _logger.LogTrace("<- WatchLoopAsync cancelled");
                return;
            }
            catch (IOException ex)
            {
                // Printer disconnected — notify connection manager, wait longer, retry
                _logger.LogDebug(
                    "ReadyWatcher: connection lost for Job {JobId} on poll #{PollCount}: {Error}",
                    _job.Id, _pollCount, ex.Message);
                if (!_connectionLostNotified)
                {
                    _connectionLostNotified = true;
                    ConnectionLost?.Invoke(this, _job.PrinterId);
                }
                await Task.Delay(5000, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "ReadyWatcher: error monitoring Job {JobId} on poll #{PollCount}", _job.Id, _pollCount);
                await Task.Delay(5000, ct);
            }
        }
        _logger.LogTrace("<- WatchLoopAsync loop ended (token cancelled)");
    }
}
