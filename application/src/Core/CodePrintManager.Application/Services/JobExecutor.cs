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
        ILogger logger)
    {
        _job = job;
        _adapter = adapter;
        _codePool = codePool;
        _alerts = alerts;
        _db = db;
        _logger = logger;
    }

    public void Start()
    {
        _cts = new CancellationTokenSource();
        _pollTask = PollLoopAsync(_cts.Token);
    }

    public async Task StopAsync()
    {
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
            catch (IOException)
            {
                _alerts.Raise(AlertSeverity.Error, _job.Printer.Name,
                    $"Connection lost. Job #{_job.Id} paused.",
                    printerId: _job.PrinterId, jobId: _job.Id);
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
        }

        return new PollSnapshot(counter, lifetimeDelta);
    }

    private void DetectAnomalies(PollSnapshot snapshot)
    {
        if (snapshot.LifetimeDelta.HasValue && snapshot.LifetimeDelta != snapshot.Counter)
        {
            _alerts.Raise(AlertSeverity.Warning, _job.Printer.Name,
                $"Counter mismatch: SPGGCP={snapshot.Counter}, SPGGTP delta={snapshot.LifetimeDelta}",
                printerId: _job.PrinterId, jobId: _job.Id);
        }

        var advance = snapshot.Counter - _previousCounter;
        if (_previousCounter > 0 && advance > 10)
        {
            _alerts.Raise(AlertSeverity.Warning, _job.Printer.Name,
                $"Unexpected counter jump (+{advance})",
                printerId: _job.PrinterId, jobId: _job.Id);
        }
    }

    private async Task CommitProgressAsync(PollSnapshot snapshot)
    {
        await _codePool.MarkCodesPrintedAsync(_job.Id, _job.CodesConfirmed, snapshot.Counter);
        _job.CodesConfirmed = snapshot.Counter;
        await _db.SaveChangesAsync();

        ProgressChanged?.Invoke(this,
            new JobProgressChangedEvent(_job.Id, snapshot.Counter, _job.Quantity));
    }

    private async Task CompleteJobAsync()
    {
        _job.Status = JobStatus.Completed;
        _job.CompletedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        _alerts.Raise(AlertSeverity.Info, _job.Printer.Name,
            $"Job #{_job.Id} completed ({_job.Quantity}/{_job.Quantity})",
            printerId: _job.PrinterId, jobId: _job.Id);

        Completed?.Invoke(this, new JobCompletedEvent(_job.Id, JobStatus.Completed));
    }
}
