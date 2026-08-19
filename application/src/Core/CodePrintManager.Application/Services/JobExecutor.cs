using System.Diagnostics;
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

    private readonly int _counterOffset;
    private CancellationTokenSource? _cts;
    private Task? _pollTask;
    private int _previousCounter;
    private int _crossCheckTick;
    private bool _needsInspection;
    private int _pollCycleCount;
    private readonly Stopwatch _jobTimer = new();

    public event EventHandler<JobProgressChangedEvent>? ProgressChanged;
    public event EventHandler<JobCompletedEvent>? Completed;

    /// <param name="counterOffset">
    /// Offset to add to the raw SPGGCP counter to get the effective job-level counter.
    /// Set to job.CodesConfirmed when resuming after a full Resume Procedure (template
    /// reload resets SPGGCP to 0, but the job already has confirmed codes).
    /// Default 0 for fresh starts.
    /// </param>
    public JobExecutor(
        PrintJob job,
        IPrinterAdapter adapter,
        ICodePoolService codePool,
        IAlertService alerts,
        AppDbContext db,
        ILogger logger,
        ILocalizationService loc,
        int counterOffset = 0)
    {
        _job = job;
        _adapter = adapter;
        _codePool = codePool;
        _alerts = alerts;
        _db = db;
        _logger = logger;
        _loc = loc;
        _counterOffset = counterOffset;
        _logger.LogTrace("-> JobExecutor constructed (jobId={JobId}, qty={Qty}, confirmed={Confirmed}, offset={Offset})",
            job.Id, job.Quantity, job.CodesConfirmed, counterOffset);
    }

    public void Start()
    {
        _logger.LogTrace("-> JobExecutor.Start() for Job {JobId}", _job.Id);
        _logger.LogInformation("Executor STARTED: Job {JobId} (qty={Qty}, confirmed={Confirmed}, offset={Offset})",
            _job.Id, _job.Quantity, _job.CodesConfirmed, _counterOffset);
        _cts = new CancellationTokenSource();
        _pollCycleCount = 0;
        _jobTimer.Start();
        _pollTask = PollLoopAsync(_cts.Token);
        _logger.LogTrace("<- JobExecutor.Start() (poll loop launched)");
    }

    public async Task StopAsync()
    {
        _logger.LogTrace("-> JobExecutor.StopAsync() for Job {JobId}", _job.Id);
        _jobTimer.Stop();
        _logger.LogInformation("Executor STOPPED: Job {JobId} (ran for {ElapsedSec:F1}s, {PollCycles} poll cycles)",
            _job.Id, _jobTimer.Elapsed.TotalSeconds, _pollCycleCount);
        _cts?.Cancel();
        if (_pollTask != null)
            await _pollTask;
        _logger.LogTrace("<- JobExecutor.StopAsync() completed");
    }

    private async Task PollLoopAsync(CancellationToken ct)
    {
        _logger.LogTrace("-> PollLoopAsync() starting for Job {JobId}", _job.Id);
        while (!ct.IsCancellationRequested)
        {
            try
            {
                _pollCycleCount++;
                var cycleSw = Stopwatch.StartNew();

                // After an IOException, run a full inspection before resuming normal polling.
                if (_needsInspection)
                {
                    _logger.LogDebug("Job {JobId} poll #{Cycle}: running post-reconnect inspection", _job.Id, _pollCycleCount);
                    _needsInspection = false;
                    var shouldContinue = await RunPostReconnectInspectionAsync(ct);
                    if (!shouldContinue)
                    {
                        _logger.LogTrace("<- PollLoopAsync exiting (inspection escalated)");
                        return; // Inspection escalated — executor is done
                    }
                }

                var snapshot = await ReadCountersAsync(ct);
                var effectiveCounter = snapshot.Counter + _counterOffset;
                _logger.LogDebug("Job {JobId} poll #{Cycle}: counter={Counter} offset={Offset} effective={Effective} (prev={Previous}) [{CycleMs}ms]",
                    _job.Id, _pollCycleCount, snapshot.Counter, _counterOffset, effectiveCounter, _previousCounter, cycleSw.ElapsedMilliseconds);
                DetectAnomalies(snapshot);

                if (effectiveCounter > _job.CodesConfirmed)
                    await CommitProgressAsync(effectiveCounter);

                if (effectiveCounter >= _job.Quantity)
                {
                    _logger.LogDebug("Job {JobId} poll #{Cycle}: quantity reached, completing", _job.Id, _pollCycleCount);
                    await CompleteJobAsync();
                    _logger.LogTrace("<- PollLoopAsync exiting (job completed)");
                    return;
                }

                _previousCounter = snapshot.Counter;
                await Task.Delay(500, ct);
            }
            catch (OperationCanceledException)
            {
                _logger.LogTrace("<- PollLoopAsync cancelled (after {Cycles} cycles)", _pollCycleCount);
                return;
            }
            catch (IOException ex)
            {
                _logger.LogError(ex, "Job {JobId} CONNECTION LOST on poll #{Cycle} (connected={Connected})",
                    _job.Id, _pollCycleCount, _adapter.IsConnected);
                _alerts.Raise(AlertSeverity.Error, _job.Printer.Name,
                    _loc.Format("Alert_ConnectionLost", _job.Id),
                    printerId: _job.PrinterId, jobId: _job.Id);
                _needsInspection = true;
                await Task.Delay(2000, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Job {JobId} UNEXPECTED ERROR on poll #{Cycle}", _job.Id, _pollCycleCount);
                await Task.Delay(2000, ct);
            }
        }
        _logger.LogTrace("<- PollLoopAsync loop ended (token cancelled, {Cycles} total cycles)", _pollCycleCount);
    }

    /// <summary>
    /// Mini-inspection after reconnection. Reads printer status, counters, and template
    /// to detect power cycles, template mismatches, or printer errors that occurred
    /// while we were disconnected.
    /// 
    /// Returns true if polling should continue normally, false if the executor should stop
    /// (job was escalated to Error status).
    /// </summary>
    private async Task<bool> RunPostReconnectInspectionAsync(CancellationToken ct)
    {
        _logger.LogTrace("-> RunPostReconnectInspectionAsync() for Job {JobId}", _job.Id);
        var sw = Stopwatch.StartNew();
        _logger.LogInformation("Job {JobId}: running post-reconnect inspection", _job.Id);

        // --- Read all inspection data atomically ---
        // If any read fails, we abort the inspection and wait for the next reconnect.
        PrinterStatus status;
        int currentCounter;
        int lifetimeCounter;
        string? activeTemplate;
        string? serialNumber;

        try
        {
            status = await _adapter.GetStatusAsync(ct);
            currentCounter = await _adapter.GetCurrentCounterAsync(ct);
            lifetimeCounter = await _adapter.GetTotalCounterAsync(ct);
            activeTemplate = await _adapter.GetActiveTemplateAsync(ct);
            serialNumber = await _adapter.GetSerialNumberAsync(ct);
        }
        catch (IOException ex)
        {
            _logger.LogWarning(ex,
                "Job {JobId}: inspection failed (connection lost again). Will retry on next reconnect.",
                _job.Id);
            _needsInspection = true;
            return true; // Keep polling — the next IOException will trigger another retry
        }

        _logger.LogInformation(
            "Job {JobId} inspection: status={Status}, SPGGCP={Counter}, SPGGTP={Lifetime}, template={Template}, serial={Serial}",
            _job.Id, status, currentCounter, lifetimeCounter, activeTemplate, serialNumber);

        // --- Check 0: Serial number mismatch (hardware swap) ---
        var storedSerial = _job.Printer?.SerialNumber;
        if (!string.IsNullOrEmpty(storedSerial) && !string.IsNullOrEmpty(serialNumber)
            && !string.Equals(storedSerial, serialNumber, StringComparison.Ordinal))
        {
            _logger.LogError(
                "Job {JobId}: SERIAL MISMATCH after reconnect! Expected={Expected}, Got={Actual}",
                _job.Id, storedSerial, serialNumber);
            _alerts.Raise(AlertSeverity.Error, _job.Printer.Name,
                $"Hardware swap detected! Serial changed from {storedSerial} to {serialNumber}. " +
                $"Job #{_job.Id} stopped — codes quarantined.",
                printerId: _job.PrinterId, jobId: _job.Id);

            await QuarantineRemainingCodesAsync();
            await SetJobErrorAsync($"Serial mismatch: expected '{storedSerial}', found '{serialNumber}'");
            return false;
        }

        // --- Check 1: Printer in error state ---
        if (status is PrinterStatus.Error or PrinterStatus.Blocked)
        {
            _logger.LogError("Job {JobId}: printer is in {Status} state after reconnect", _job.Id, status);
            _alerts.Raise(AlertSeverity.Error, _job.Printer.Name,
                $"Printer is in {status} state. Job #{_job.Id} requires attention.",
                printerId: _job.PrinterId, jobId: _job.Id);
            await SetJobErrorAsync($"Printer {status} after reconnect");
            return false;
        }

        // --- Check 2: Template mismatch ---
        // The product's template name may be stored as a full path; compare filenames.
        var expectedTemplate = _job.Product?.TemplateFile;
        if (expectedTemplate != null && activeTemplate != null)
        {
            var expectedName = Path.GetFileName(expectedTemplate);
            if (!string.Equals(expectedName, activeTemplate, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogError(
                    "Job {JobId}: template mismatch after reconnect. Expected='{Expected}', Active='{Active}'",
                    _job.Id, expectedName, activeTemplate);
                _alerts.Raise(AlertSeverity.Error, _job.Printer.Name,
                    $"Template mismatch! Expected '{expectedName}', found '{activeTemplate}'. " +
                    $"Job #{_job.Id} stopped — codes quarantined.",
                    printerId: _job.PrinterId, jobId: _job.Id);

                // Quarantine all remaining reserved codes — we can't trust what printed
                await QuarantineRemainingCodesAsync();
                await SetJobErrorAsync($"Template mismatch: expected '{expectedName}', found '{activeTemplate}'");
                return false;
            }
        }

        // --- Check 3: Compute lifetime delta ---
        int? lifetimeDelta = null;
        if (_job.TotalBaseline.HasValue)
        {
            lifetimeDelta = lifetimeCounter - _job.TotalBaseline.Value;

            // Counter went backward — hardware swap or counter reset
            if (lifetimeDelta < 0)
            {
                _logger.LogError(
                    "Job {JobId}: SPGGTP went backward! baseline={Baseline}, now={Now}. Possible hardware swap.",
                    _job.Id, _job.TotalBaseline, lifetimeCounter);
                _alerts.Raise(AlertSeverity.Error, _job.Printer.Name,
                    $"Lifetime counter went backward (was {_job.TotalBaseline}, now {lifetimeCounter}). " +
                    $"Possible hardware swap. Job #{_job.Id} stopped — codes quarantined.",
                    printerId: _job.PrinterId, jobId: _job.Id);

                await QuarantineRemainingCodesAsync();
                await SetJobErrorAsync("SPGGTP counter went backward — possible hardware swap");
                return false;
            }
        }

        var effectiveDelta = lifetimeDelta ?? (currentCounter + _counterOffset);

        // --- Check 4: Power cycle / template reload detection ---
        // SPGGCP == 0 means the current-session counter was reset (power cycle or template reload).
        // If lifetime delta > confirmed, prints happened but the row pointer is lost.
        if (currentCounter == 0 && _previousCounter > 0)
        {
            _logger.LogWarning(
                "Job {JobId}: SPGGCP reset to 0 (was {Previous}). Power cycle or template reload detected.",
                _job.Id, _previousCounter);

            // Reconcile via lifetime delta: mark any unrecorded prints
            if (lifetimeDelta.HasValue && lifetimeDelta.Value > _job.CodesConfirmed)
            {
                var unrecorded = lifetimeDelta.Value - _job.CodesConfirmed;
                _logger.LogWarning(
                    "Job {JobId}: {Unrecorded} unrecorded prints detected via SPGGTP delta",
                    _job.Id, unrecorded);

                // Mark the unrecorded prints, then quarantine the boundary
                if (unrecorded > 1)
                    await _codePool.MarkCodesPrintedAsync(_job.Id, _job.CodesConfirmed,
                        _job.CodesConfirmed + unrecorded - 1);
                // Quarantine the boundary code (might or might not have printed)
                await _codePool.QuarantineCodeAsync(_job.Id, _job.CodesConfirmed + unrecorded - 1);
                _job.CodesConfirmed = _job.CodesConfirmed + unrecorded;
            }
            else if (lifetimeDelta.HasValue && lifetimeDelta.Value == _job.CodesConfirmed)
            {
                // No additional prints, but row pointer is still lost
                _logger.LogWarning(
                    "Job {JobId}: no additional prints during disconnect, but SPGGCP was reset. " +
                    "Row pointer lost — full Resume Procedure required.", _job.Id);
            }

            _alerts.Raise(AlertSeverity.Error, _job.Printer.Name,
                $"Power cycle detected. SPGGCP reset to 0. Job #{_job.Id} requires Resume Procedure.",
                printerId: _job.PrinterId, jobId: _job.Id);
            await SetJobErrorAsync("Power cycle detected — SPGGCP reset, row pointer lost");
            return false;
        }

        // --- Check 5: Scenario 7B — RUNNING + template match + SPGGCP reset ---
        // Printer is running but counter seems inconsistent with our tracking.
        // This is a warning-only scenario (do NOT stop the printer).
        if (status == PrinterStatus.Printing && lifetimeDelta.HasValue)
        {
            var effectiveCounter = currentCounter + _counterOffset;
            if (lifetimeDelta.Value > effectiveCounter)
            {
                var discrepancy = lifetimeDelta.Value - effectiveCounter;
                _logger.LogWarning(
                    "Job {JobId}: printer RUNNING but lifetime delta ({Delta}) exceeds counter ({Counter}). " +
                    "Possible external prints or template reload while running.",
                    _job.Id, lifetimeDelta, effectiveCounter);
                _alerts.Raise(AlertSeverity.Warning, _job.Printer.Name,
                    $"WARNING: Lifetime counter discrepancy while printing. " +
                    $"SPGGTP delta={lifetimeDelta}, SPGGCP={currentCounter}. " +
                    $"Possible external prints (+{discrepancy}). Job #{_job.Id} requires attention.",
                    printerId: _job.PrinterId, jobId: _job.Id);
                // Warning only — do NOT stop the printer (per design doc Scenario 7B)
            }
        }

        // --- All checks passed: reconcile any missed progress ---
        if (lifetimeDelta.HasValue && lifetimeDelta.Value > _job.CodesConfirmed)
        {
            var catchUp = lifetimeDelta.Value - _job.CodesConfirmed;
            _logger.LogInformation(
                "Job {JobId}: catching up {CatchUp} missed prints after reconnect (lifetime delta={Delta})",
                _job.Id, catchUp, lifetimeDelta);
            await CommitProgressAsync(lifetimeDelta.Value);
        }

        _logger.LogInformation("Job {JobId}: post-reconnect inspection PASSED in {ElapsedMs}ms. Resuming normal polling.",
            _job.Id, sw.ElapsedMilliseconds);
        _previousCounter = currentCounter;
        _logger.LogTrace("<- RunPostReconnectInspectionAsync = true ({ElapsedMs}ms)", sw.ElapsedMilliseconds);
        return true;
    }

    /// <summary>
    /// Quarantine all remaining reserved codes for this job.
    /// Used when the state is too uncertain to continue (template mismatch, counter rollback).
    /// </summary>
    private async Task QuarantineRemainingCodesAsync()
    {
        _logger.LogTrace("-> QuarantineRemainingCodesAsync() for Job {JobId}", _job.Id);
        var remainingCount = _job.Quantity - _job.CodesConfirmed;
        if (remainingCount > 0)
        {
            _logger.LogWarning("Job {JobId}: quarantining {Count} remaining reserved codes",
                _job.Id, remainingCount);
            await _codePool.QuarantineCodesAsync(_job.Id, _job.CodesConfirmed, remainingCount);
        }
        else
        {
            _logger.LogTrace("   QuarantineRemainingCodesAsync: no remaining codes to quarantine");
        }
        _logger.LogTrace("<- QuarantineRemainingCodesAsync completed");
    }

    /// <summary>
    /// Set the job to Error status and fire the Completed event to clean up.
    /// </summary>
    private async Task SetJobErrorAsync(string reason)
    {
        _logger.LogTrace("-> SetJobErrorAsync(reason={Reason}) for Job {JobId}", reason, _job.Id);
        _job.Status = JobStatus.Error;
        _job.CompletedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        _jobTimer.Stop();
        _logger.LogError("Job {JobId} set to ERROR: {Reason} (ran for {ElapsedSec:F1}s, {PollCycles} cycles)",
            _job.Id, reason, _jobTimer.Elapsed.TotalSeconds, _pollCycleCount);
        Completed?.Invoke(this, new JobCompletedEvent(_job.Id, JobStatus.Error));
        _logger.LogTrace("<- SetJobErrorAsync completed");
    }

    private async Task<PollSnapshot> ReadCountersAsync(CancellationToken ct)
    {
        _logger.LogTrace("-> ReadCountersAsync() for Job {JobId}", _job.Id);
        var sw = Stopwatch.StartNew();
        var counter = await _adapter.GetCurrentCounterAsync(ct);
        int? lifetimeDelta = null;

        if (++_crossCheckTick % 5 == 0 && _job.TotalBaseline.HasValue)
        {
            var lifetime = await _adapter.GetTotalCounterAsync(ct);
            lifetimeDelta = lifetime - _job.TotalBaseline.Value;
            _logger.LogDebug("Job {JobId} cross-check: lifetime={Lifetime}, delta={Delta}, counter={Counter}",
                _job.Id, lifetime, lifetimeDelta, counter);
        }

        _logger.LogTrace("<- ReadCountersAsync = (counter={Counter}, lifetimeDelta={Delta}) in {ElapsedMs}ms",
            counter, lifetimeDelta, sw.ElapsedMilliseconds);
        return new PollSnapshot(counter, lifetimeDelta);
    }

    private void DetectAnomalies(PollSnapshot snapshot)
    {
        _logger.LogTrace("-> DetectAnomalies(counter={Counter}, lifetimeDelta={Delta}, prevCounter={Prev})",
            snapshot.Counter, snapshot.LifetimeDelta, _previousCounter);

        if (snapshot.LifetimeDelta.HasValue && snapshot.LifetimeDelta != snapshot.Counter)
        {
            _logger.LogWarning("Job {JobId} ANOMALY: counter mismatch SPGGCP={Counter}, SPGGTP delta={Delta}",
                _job.Id, snapshot.Counter, snapshot.LifetimeDelta);
            _alerts.Raise(AlertSeverity.Warning, _job.Printer.Name,
                _loc.Format("Alert_CounterMismatch", snapshot.Counter, snapshot.LifetimeDelta),
                printerId: _job.PrinterId, jobId: _job.Id);
        }

        var advance = snapshot.Counter - _previousCounter;
        if (_previousCounter > 0 && advance > 10)
        {
            _logger.LogWarning("Job {JobId} ANOMALY: unexpected counter jump +{Advance} (prev={Prev}, now={Now})",
                _job.Id, advance, _previousCounter, snapshot.Counter);
            _alerts.Raise(AlertSeverity.Warning, _job.Printer.Name,
                _loc.Format("Alert_CounterJump", advance),
                printerId: _job.PrinterId, jobId: _job.Id);
        }

        _logger.LogTrace("<- DetectAnomalies completed");
    }

    private async Task CommitProgressAsync(int effectiveCounter)
    {
        _logger.LogTrace("-> CommitProgressAsync(effectiveCounter={Counter}) for Job {JobId}", effectiveCounter, _job.Id);
        var sw = Stopwatch.StartNew();
        await _codePool.MarkCodesPrintedAsync(_job.Id, _job.CodesConfirmed, effectiveCounter);
        _job.CodesConfirmed = effectiveCounter;
        await _db.SaveChangesAsync();

        var pct = _job.Quantity > 0 ? (int)(100.0 * effectiveCounter / _job.Quantity) : 0;
        _logger.LogInformation("Job {JobId} progress: {Confirmed}/{Total} ({Pct}%) [commit took {ElapsedMs}ms]",
            _job.Id, effectiveCounter, _job.Quantity, pct, sw.ElapsedMilliseconds);

        ProgressChanged?.Invoke(this,
            new JobProgressChangedEvent(_job.Id, effectiveCounter, _job.Quantity));
        _logger.LogTrace("<- CommitProgressAsync completed ({ElapsedMs}ms)", sw.ElapsedMilliseconds);
    }

    private async Task CompleteJobAsync()
    {
        _logger.LogTrace("-> CompleteJobAsync() for Job {JobId}", _job.Id);
        _job.Status = JobStatus.Completed;
        _job.CompletedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        _jobTimer.Stop();
        _logger.LogInformation("Job {JobId} COMPLETED: {Total}/{Total} (total runtime: {ElapsedSec:F1}s, {PollCycles} cycles)",
            _job.Id, _job.Quantity, _job.Quantity, _jobTimer.Elapsed.TotalSeconds, _pollCycleCount);

        _alerts.Raise(AlertSeverity.Info, _job.Printer.Name,
            _loc.Format("Alert_JobCompleted", _job.Id, _job.Quantity),
            printerId: _job.PrinterId, jobId: _job.Id);

        Completed?.Invoke(this, new JobCompletedEvent(_job.Id, JobStatus.Completed));
        _logger.LogTrace("<- CompleteJobAsync completed");
    }
}
