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

    private CancellationTokenSource? _cts;
    private Task? _watchTask;

    /// <summary>
    /// Raised when the watcher detects that the printer started printing
    /// (SPPSTA = Printing or SPGGCP > 0).
    /// The int parameter is the job ID.
    /// </summary>
    public event EventHandler<int>? ExternalPrintDetected;

    public ReadyWatcher(
        PrintJob job,
        IPrinterAdapter adapter,
        IAlertService alerts,
        ILogger logger)
    {
        _job = job;
        _adapter = adapter;
        _alerts = alerts;
        _logger = logger;
    }

    public void Start()
    {
        _logger.LogInformation(
            "ReadyWatcher started: Job {JobId} on printer {PrinterId}",
            _job.Id, _job.PrinterId);
        _cts = new CancellationTokenSource();
        _watchTask = WatchLoopAsync(_cts.Token);
    }

    public async Task StopAsync()
    {
        _logger.LogInformation("ReadyWatcher stopped: Job {JobId}", _job.Id);
        _cts?.Cancel();
        if (_watchTask != null)
        {
            try { await _watchTask; }
            catch (OperationCanceledException) { }
        }
    }

    private async Task WatchLoopAsync(CancellationToken ct)
    {
        // Initial delay before first check (let things settle after Prepare)
        await Task.Delay(2000, ct);

        while (!ct.IsCancellationRequested)
        {
            try
            {
                var status = await _adapter.GetStatusAsync(ct);
                var currentCounter = await _adapter.GetCurrentCounterAsync(ct);

                if (status == PrinterStatus.Printing || currentCounter > 0)
                {
                    _logger.LogWarning(
                        "ReadyWatcher: external print detected for Job {JobId}! " +
                        "Status={Status}, SPGGCP={Counter}",
                        _job.Id, status, currentCounter);

                    _alerts.Raise(AlertSeverity.Warning, _job.Printer.Name,
                        $"Printing started externally on '{_job.Printer.Name}'. " +
                        $"Job #{_job.Id} transitioning to Printing for tracking.",
                        printerId: _job.PrinterId, jobId: _job.Id);

                    ExternalPrintDetected?.Invoke(this, _job.Id);
                    return; // Stop watching — the caller will spawn a JobExecutor
                }

                await Task.Delay(3000, ct);
            }
            catch (OperationCanceledException) { return; }
            catch (IOException)
            {
                // Printer disconnected — wait longer, retry
                _logger.LogDebug(
                    "ReadyWatcher: connection lost for Job {JobId}, will retry",
                    _job.Id);
                await Task.Delay(5000, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "ReadyWatcher: error monitoring Job {JobId}", _job.Id);
                await Task.Delay(5000, ct);
            }
        }
    }
}
