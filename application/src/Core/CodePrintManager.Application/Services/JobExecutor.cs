using CodePrintManager.Application.Models;
using CodePrintManager.Data;
using CodePrintManager.Domain.Entities;
using CodePrintManager.Domain.Enums;
using CodePrintManager.Domain.Events;
using CodePrintManager.Domain.Interfaces;
using Microsoft.Extensions.Logging;

namespace CodePrintManager.Application.Services;

public class JobExecutor
{
    private readonly PrintJob _job;
    private readonly IPrinterAdapter _adapter;
    private readonly ICodePoolService _codePool;
    private readonly IAlertService _alerts;
    private readonly AppDbContext _db;
    private readonly ILogger _logger;
    private readonly ILocalizationService _loc;

    private CancellationTokenSource? _cts;
    private Task? _pollTask;
    private int _previousCounter;
    private int _crossCheckTick;

    public event EventHandler<JobProgressChangedEvent>? ProgressChanged;
    public event EventHandler<JobCompletedEvent>? Completed;

    public JobExecutor(
        PrintJob job,
        IPrinterAdapter adapter,
        ICodePoolService codePool,
        IAlertService alerts,
        AppDbContext db,
        ILogger logger,
        ILocalizationService loc)
    {
        _job = job;
        _adapter = adapter;
        _codePool = codePool;
        _alerts = alerts;
        _db = db;
        _logger = logger;
        _loc = loc;
    }

    public void Start()
    {
        _logger.LogInformation("Executor started: Job {JobId} (qty={Qty}, confirmed={Confirmed})",
            _job.Id, _job.Quantity, _job.CodesConfirmed);
        _cts = new CancellationTokenSource();
        _pollTask = PollLoopAsync(_cts.Token);
    }

    public async Task StopAsync()
    {
        _logger.LogInformation("Executor stopped: Job {JobId}", _job.Id);
        _cts?.Cancel();
        if (_pollTask != null)
            await _pollTask;
    }

    private async Task PollLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var snapshot = await ReadCountersAsync(ct);
                _logger.LogDebug("Job {JobId} poll: counter={Counter} (prev={Previous})",
                    _job.Id, snapshot.Counter, _previousCounter);
                DetectAnomalies(snapshot);

                if (snapshot.Counter > _job.CodesConfirmed)
                    await CommitProgressAsync(snapshot);

                if (snapshot.Counter >= _job.Quantity)
                {
                    await CompleteJobAsync();
                    return;
                }

                _previousCounter = snapshot.Counter;
                await Task.Delay(500, ct);
            }
            catch (OperationCanceledException) { return; }
            catch (IOException ex)
            {
                _logger.LogError(ex, "Job {JobId} connection lost", _job.Id);
                _alerts.Raise(AlertSeverity.Error, _job.Printer.Name,
                    _loc.Format("Alert_ConnectionLost", _job.Id),
                    printerId: _job.PrinterId, jobId: _job.Id);
                await Task.Delay(2000, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Job {JobId} executor error", _job.Id);
                await Task.Delay(2000, ct);
            }
        }
    }

    private async Task<PollSnapshot> ReadCountersAsync(CancellationToken ct)
    {
        var counter = await _adapter.GetCurrentCounterAsync(ct);
        int? lifetimeDelta = null;

        if (++_crossCheckTick % 5 == 0 && _job.TotalBaseline.HasValue)
        {
            var lifetime = await _adapter.GetTotalCounterAsync(ct);
            lifetimeDelta = lifetime - _job.TotalBaseline.Value;
            _logger.LogDebug("Job {JobId} cross-check: lifetime={Lifetime}, delta={Delta}, counter={Counter}",
                _job.Id, lifetime, lifetimeDelta, counter);
        }

        return new PollSnapshot(counter, lifetimeDelta);
    }

    private void DetectAnomalies(PollSnapshot snapshot)
    {
        if (snapshot.LifetimeDelta.HasValue && snapshot.LifetimeDelta != snapshot.Counter)
        {
            _logger.LogWarning("Job {JobId} anomaly: counter mismatch SPGGCP={Counter}, SPGGTP delta={Delta}",
                _job.Id, snapshot.Counter, snapshot.LifetimeDelta);
            _alerts.Raise(AlertSeverity.Warning, _job.Printer.Name,
                _loc.Format("Alert_CounterMismatch", snapshot.Counter, snapshot.LifetimeDelta),
                printerId: _job.PrinterId, jobId: _job.Id);
        }

        var advance = snapshot.Counter - _previousCounter;
        if (_previousCounter > 0 && advance > 10)
        {
            _logger.LogWarning("Job {JobId} anomaly: unexpected counter jump +{Advance}",
                _job.Id, advance);
            _alerts.Raise(AlertSeverity.Warning, _job.Printer.Name,
                _loc.Format("Alert_CounterJump", advance),
                printerId: _job.PrinterId, jobId: _job.Id);
        }
    }

    private async Task CommitProgressAsync(PollSnapshot snapshot)
    {
        await _codePool.MarkCodesPrintedAsync(_job.Id, _job.CodesConfirmed, snapshot.Counter);
        _job.CodesConfirmed = snapshot.Counter;
        await _db.SaveChangesAsync();

        var pct = _job.Quantity > 0 ? (int)(100.0 * snapshot.Counter / _job.Quantity) : 0;
        _logger.LogInformation("Job {JobId} progress: {Confirmed}/{Total} ({Pct}%)",
            _job.Id, snapshot.Counter, _job.Quantity, pct);

        ProgressChanged?.Invoke(this,
            new JobProgressChangedEvent(_job.Id, snapshot.Counter, _job.Quantity));
    }

    private async Task CompleteJobAsync()
    {
        _job.Status = JobStatus.Completed;
        _job.CompletedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        _logger.LogInformation("Job {JobId} completed ({Total}/{Total})", _job.Id, _job.Quantity, _job.Quantity);

        _alerts.Raise(AlertSeverity.Info, _job.Printer.Name,
            _loc.Format("Alert_JobCompleted", _job.Id, _job.Quantity),
            printerId: _job.PrinterId, jobId: _job.Id);

        Completed?.Invoke(this, new JobCompletedEvent(_job.Id, JobStatus.Completed));
    }
}
