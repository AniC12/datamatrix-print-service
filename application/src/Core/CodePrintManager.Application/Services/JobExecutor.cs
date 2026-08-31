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
    private readonly Func<int, CancellationToken, Task<bool>>? _tryReconnect;
    private CancellationTokenSource? _cts;
    private Task? _pollTask;
    private int _previousCounter = -1;
    private int _crossCheckTick;
    private bool _needsInspection;
    private int _pollCycleCount;
    private int _consecutiveFailures;
    private int? _lastKnownLifetimeCounter;
    private PrinterStatus? _previousErrorStatus; // for two-consecutive-polls error confirmation
    private readonly Stopwatch _jobTimer = new();

    /// <summary>
    /// Maximum consecutive poll failures before escalating the job to Error.
    /// At ~2.5s per failure (500ms poll + 2000ms retry delay), 30 failures ≈ 75 seconds.
    /// </summary>
    private const int MaxConsecutiveFailures = 30;

    public event EventHandler<JobProgressChangedEvent>? ProgressChanged;
    public event EventHandler<JobCompletedEvent>? Completed;
    public event EventHandler<JobCountersUpdatedEvent>? CountersUpdated;

    /// <param name="counterOffset">
    /// Offset to add to the raw SPGGCP counter to get the effective job-level counter.
    /// SPGGCP may or may not reset on SPLLTF depending on firmware; we always baseline it.
    /// For fresh starts: offset = -spggcpBaseline (so effective starts at 0).
    /// For resumes: offset = CodesConfirmed - spggcpBaseline.
    /// </param>
    /// <param name="tryReconnect">
    /// Optional callback to attempt reconnection after IOException. Called with (printerId, ct).
    /// Returns true if reconnection succeeded (adapter is alive again), false otherwise.
    /// The adapter object is reconnected in-place, so the executor's _adapter reference stays valid.
    /// </param>
    public JobExecutor(
        PrintJob job,
        IPrinterAdapter adapter,
        ICodePoolService codePool,
        IAlertService alerts,
        AppDbContext db,
        ILogger logger,
        ILocalizationService loc,
        int counterOffset = 0,
        Func<int, CancellationToken, Task<bool>>? tryReconnect = null)
    {
        _job = job;
        _adapter = adapter;
        _codePool = codePool;
        _alerts = alerts;
        _db = db;
        _logger = logger;
        _loc = loc;
        _counterOffset = counterOffset;
        _tryReconnect = tryReconnect;
        _logger.LogTrace("-> JobExecutor constructed (jobId={JobId}, qty={Qty}, confirmed={Confirmed}, offset={Offset})",
            job.Id, job.Quantity, job.CodesConfirmed, counterOffset);
    }

    public void Start()
    {
        if (_pollTask != null)
            throw new InvalidOperationException(
                $"JobExecutor for Job {_job.Id} is already running. Call StopAsync() first.");

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
        {
            try { await _pollTask; }
            catch (OperationCanceledException) { /* expected during cancellation */ }
        }
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
                // DO NOT clear _needsInspection before the inspection completes —
                // if it throws (e.g., FormatException from SPPL misalignment), the
                // flag must stay true so we retry on the next poll (Invariant #9).
                if (_needsInspection)
                {
                    _logger.LogDebug("Job {JobId} poll #{Cycle}: running post-reconnect inspection", _job.Id, _pollCycleCount);
                    try
                    {
                        var shouldContinue = await RunPostReconnectInspectionAsync(ct);
                        _needsInspection = false; // clear ONLY after successful completion
                        if (!shouldContinue)
                        {
                            _logger.LogTrace("<- PollLoopAsync exiting (inspection escalated)");
                            return; // Inspection escalated — executor is done
                        }
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        // FormatException, InvalidOperationException, etc. from SPPL
                        // misalignment or disconnected adapter. _needsInspection stays
                        // true so we retry on the next poll.
                        _logger.LogWarning(ex,
                            "Job {JobId}: inspection failed with non-IO error. Will retry on next poll.",
                            _job.Id);
                        try { await Task.Delay(2000, ct); }
                        catch (OperationCanceledException) { return; }
                        continue;
                    }
                    continue;
                }

                var snapshot = await ReadCountersAsync(ct);
                var effectiveCounter = snapshot.Counter + _counterOffset;

                // Defense-in-depth: counter can NEVER exceed job quantity.
                // If it does, the SPPL stream is corrupted (e.g., SPGGTP value
                // misinterpreted as SPGGCP due to unsolicited frame shift).
                if (effectiveCounter > _job.Quantity)
                {
                    var overrun = effectiveCounter - _job.Quantity;
                    _logger.LogError(
                        "Job {JobId}: effective counter {Counter} (raw={Raw}, offset={Offset}) exceeds quantity {Qty} by {Overrun}! " +
                        "Capping to quantity. SPPL stream may be corrupted.",
                        _job.Id, effectiveCounter, snapshot.Counter, _counterOffset, _job.Quantity, overrun);
                    _alerts.Raise(AlertSeverity.Warning, _job.Printer.Name,
                        $"Printer may have printed {overrun} extra label(s) beyond requested quantity on Job #{_job.Id}.",
                        printerId: _job.PrinterId, jobId: _job.Id,
                        deduplicationKey: "counter_overrun");
                    effectiveCounter = _job.Quantity;
                }

                _logger.LogDebug("Job {JobId} poll #{Cycle}: counter={Counter} offset={Offset} effective={Effective} (prev={Previous}) [{CycleMs}ms]",
                    _job.Id, _pollCycleCount, snapshot.Counter, _counterOffset, effectiveCounter, _previousCounter, cycleSw.ElapsedMilliseconds);

                CountersUpdated?.Invoke(this,
                    new JobCountersUpdatedEvent(_job.Id, snapshot.Counter, _lastKnownLifetimeCounter, effectiveCounter));

                // Periodic printer status check (every 10th poll ≈ 5 seconds).
                // Detects hardware errors (ribbon break, mechanical failure) that don't
                // cause IOException but stop the printer from advancing the counter.
                // Uses two-consecutive-polls confirmation to avoid false positives
                // from transient error states.
                if (_pollCycleCount % 10 == 0)
                {
                    var status = await _adapter.GetStatusAsync(ct);
                    if (status is PrinterStatus.Error or PrinterStatus.Blocked)
                    {
                        if (_previousErrorStatus == status)
                        {
                            // Confirmed persistent — two checks ~5s apart both returned error.
                            // Don't send SPPSTP (never auto-stop a running printer).
                            _logger.LogError(
                                "Job {JobId}: printer status {Status} confirmed persistent (2 consecutive checks)",
                                _job.Id, status);
                            _alerts.Raise(AlertSeverity.Error, _job.Printer.Name,
                                _loc.Format("Alert_PrinterError", _job.Id, status),
                                printerId: _job.PrinterId, jobId: _job.Id);
                            await QuarantineRemainingCodesAsync();
                            await SetJobErrorAsync($"Printer {status} detected during polling");
                            return;
                        }
                        // First sighting — record and confirm on next status check
                        _previousErrorStatus = status;
                        _logger.LogWarning(
                            "Job {JobId}: printer status {Status} — will confirm on next status check",
                            _job.Id, status);
                    }
                    else
                    {
                        if (_previousErrorStatus.HasValue)
                        {
                            _logger.LogInformation(
                                "Job {JobId}: transient {OldStatus} cleared to {NewStatus}",
                                _job.Id, _previousErrorStatus, status);
                        }
                        _previousErrorStatus = null;
                    }
                }

                // Defense-in-depth: detect backward SPGGCP movement during normal polling.
                // On real hardware, power cycles cause TCP disconnect first (handled by Check 4
                // in RunPostReconnectInspectionAsync). This catches edge cases where the counter
                // goes backward without a network disruption.
                // Route through the inspection path for proper SPGGTP reconciliation and code
                // accounting rather than halting immediately.
                if (_previousCounter >= 0 && snapshot.Counter < _previousCounter)
                {
                    _logger.LogWarning(
                        "Job {JobId}: SPGGCP went backward ({Previous} → {Current}). " +
                        "Possible power cycle or firmware reset. Triggering inspection for proper SPGGTP reconciliation.",
                        _job.Id, _previousCounter, snapshot.Counter);
                    _alerts.Raise(AlertSeverity.Warning, _job.Printer.Name,
                        $"SPGGCP went backward ({_previousCounter} → {snapshot.Counter}). " +
                        $"Job #{_job.Id}: running inspection.",
                        printerId: _job.PrinterId, jobId: _job.Id);
                    _needsInspection = true;
                    continue; // Skip remaining poll logic, run inspection on next cycle
                }

                var safe = DetectAnomalies(snapshot, effectiveCounter);
                if (!safe)
                {
                    _logger.LogError(
                        "Job {JobId}: blocking anomaly detected — quarantining remaining codes and stopping executor",
                        _job.Id);
                    await QuarantineRemainingCodesAsync();
                    await SetJobErrorAsync("Counter anomaly — possible SPPL stream corruption");
                    return;
                }

                if (effectiveCounter > _job.CodesConfirmed)
                    await CommitProgressAsync(effectiveCounter);

                // Successful poll — reset consecutive failure counter
                _consecutiveFailures = 0;

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
            catch (InvalidOperationException ex) when (ex.Message.Contains("Not connected"))
            {
                // "Not connected to printer" from SendCommandAsync — treat as connection error.
                // Attempt reconnection before counting as failure.
                _logger.LogError(ex, "Job {JobId} ADAPTER DISCONNECTED on poll #{Cycle}", _job.Id, _pollCycleCount);
                if (await TryReconnectAdapterAsync(ct))
                    continue; // Reconnected — run inspection on next cycle
                _consecutiveFailures++;
                if (_consecutiveFailures >= MaxConsecutiveFailures)
                {
                    _logger.LogError("Job {JobId}: {Failures} consecutive poll failures — escalating to Error",
                        _job.Id, _consecutiveFailures);
                    await QuarantineRemainingCodesAsync();
                    await SetJobErrorAsync($"{_consecutiveFailures} consecutive failures — printer unreachable");
                    return;
                }
                try { await Task.Delay(2000, ct); }
                catch (OperationCanceledException) { return; }
            }
            catch (IOException ex)
            {
                _consecutiveFailures++;
                _logger.LogError(ex, "Job {JobId} CONNECTION LOST on poll #{Cycle} (failure #{Failure}, connected={Connected})",
                    _job.Id, _pollCycleCount, _consecutiveFailures, _adapter.IsConnected);
                _alerts.Raise(AlertSeverity.Error, _job.Printer.Name,
                    _loc.Format("Alert_ConnectionLost", _job.Id),
                    printerId: _job.PrinterId, jobId: _job.Id,
                    deduplicationKey: "connection_lost");

                // Check failure threshold FIRST — persistent failures must escalate
                // regardless of whether reconnection would succeed. This handles cases
                // where ConnectAsync succeeds but subsequent commands still fail (e.g.,
                // firmware crash, persistent protocol errors).
                if (_consecutiveFailures >= MaxConsecutiveFailures)
                {
                    _logger.LogError("Job {JobId}: {Failures} consecutive poll failures — escalating to Error",
                        _job.Id, _consecutiveFailures);
                    await QuarantineRemainingCodesAsync();
                    await SetJobErrorAsync($"{_consecutiveFailures} consecutive failures — operator intervention required");
                    return;
                }

                // Attempt reconnection — if the network comes back, the same adapter
                // object gets a fresh TCP connection and the executor continues.
                // _consecutiveFailures is NOT reset here; it resets naturally when
                // the first successful poll completes after reconnection.
                if (await TryReconnectAdapterAsync(ct))
                    continue; // Reconnected — run inspection on next cycle

                // Cancellation-safe delay: if StopAsync cancels the CTS during this
                // delay, we exit cleanly instead of throwing TaskCanceledException
                // from inside the catch block (which would propagate as an unhandled
                // exception through StopAsync → PauseJobAsync → WPF Dispatcher).
                try { await Task.Delay(2000, ct); }
                catch (OperationCanceledException) { return; }
            }
            catch (FormatException ex)
            {
                // SPPL stream corruption (e.g., unsolicited frame shift, partial response).
                // Run a full inspection on the next poll to re-sync.
                _logger.LogError(ex, "Job {JobId} SPPL FORMAT ERROR on poll #{Cycle} — possible stream corruption",
                    _job.Id, _pollCycleCount);
                _alerts.Raise(AlertSeverity.Warning, _job.Printer.Name,
                    $"SPPL format error on Job #{_job.Id}. Running inspection.",
                    printerId: _job.PrinterId, jobId: _job.Id,
                    deduplicationKey: "format_error");
                _needsInspection = true;
                _consecutiveFailures++;
                if (_consecutiveFailures >= MaxConsecutiveFailures)
                {
                    _logger.LogError("Job {JobId}: {Failures} consecutive poll failures — escalating to Error",
                        _job.Id, _consecutiveFailures);
                    await QuarantineRemainingCodesAsync();
                    await SetJobErrorAsync($"{_consecutiveFailures} consecutive failures — SPPL stream corruption");
                    return;
                }
                try { await Task.Delay(2000, ct); }
                catch (OperationCanceledException) { return; }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Job {JobId} UNEXPECTED ERROR on poll #{Cycle}", _job.Id, _pollCycleCount);
                _consecutiveFailures++;
                if (_consecutiveFailures >= MaxConsecutiveFailures)
                {
                    _logger.LogError("Job {JobId}: {Failures} consecutive poll failures — escalating to Error",
                        _job.Id, _consecutiveFailures);
                    await QuarantineRemainingCodesAsync();
                    await SetJobErrorAsync($"{_consecutiveFailures} consecutive failures — operator intervention required");
                    return;
                }
                try { await Task.Delay(2000, ct); }
                catch (OperationCanceledException) { return; }
            }
        }
        _logger.LogTrace("<- PollLoopAsync loop ended (token cancelled, {Cycles} total cycles)", _pollCycleCount);
    }

    /// <summary>
    /// Attempts to reconnect the adapter via the connection manager callback.
    /// If successful, sets _needsInspection so the next poll cycle runs
    /// RunPostReconnectInspectionAsync.  Does NOT reset _consecutiveFailures —
    /// the counter is only cleared when a poll actually succeeds, proving
    /// the connection is healthy.  This prevents infinite reconnect loops when
    /// ConnectAsync succeeds but subsequent commands still fail.
    /// Returns true if reconnected, false if unavailable or failed.
    /// </summary>
    private async Task<bool> TryReconnectAdapterAsync(CancellationToken ct)
    {
        if (_tryReconnect == null)
            return false;

        try
        {
            _logger.LogInformation("Job {JobId}: attempting reconnection via connection manager", _job.Id);
            var reconnected = await _tryReconnect(_job.PrinterId, ct);
            if (reconnected)
            {
                _logger.LogInformation("Job {JobId}: reconnected successfully — will run inspection", _job.Id);
                _needsInspection = true;
                return true;
            }
            _logger.LogDebug("Job {JobId}: reconnection attempt failed", _job.Id);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception reEx)
        {
            _logger.LogWarning(reEx, "Job {JobId}: reconnection attempt threw exception", _job.Id);
        }
        return false;
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
        // SPGGCP behavior on SPLLTF varies by firmware (some reset, some don't).
        // A backward movement (currentCounter < previousCounter) indicates a power cycle,
        // firmware-level reset, or external template reload.
        if (_previousCounter >= 0 && currentCounter < _previousCounter)
        {
            _logger.LogWarning(
                "Job {JobId}: SPGGCP reset to 0 (was {Previous}). Power cycle or template reload detected.",
                _job.Id, _previousCounter);

            // Reconcile via lifetime delta: mark any unrecorded prints
            if (lifetimeDelta.HasValue && lifetimeDelta.Value > _job.CodesConfirmed)
            {
                var unrecorded = lifetimeDelta.Value - _job.CodesConfirmed;

                // Counter cap: unrecorded prints cannot exceed remaining codes.
                // If they do, the lifetime counter is corrupted (e.g., SPPL stream
                // misalignment caused a wrong SPGGTP value). Quarantine everything.
                var remaining = _job.Quantity - _job.CodesConfirmed;
                if (unrecorded > remaining)
                {
                    _logger.LogError(
                        "Job {JobId}: SPGGTP indicates {Unrecorded} unrecorded prints but only {Remaining} codes remain. " +
                        "Counter value is corrupted (possible SPPL stream misalignment).",
                        _job.Id, unrecorded, remaining);
                    await QuarantineRemainingCodesAsync();
                    await SetJobErrorAsync("Counter corruption during inspection: unrecorded prints exceed remaining quantity");
                    return false;
                }

                _logger.LogWarning(
                    "Job {JobId}: {Unrecorded} unrecorded prints detected via SPGGTP delta",
                    _job.Id, unrecorded);

                // Mark the unrecorded prints (all but the last are definitely printed)
                var margin = _job.Printer.QuarantineMargin;
                var definitelyPrinted = Math.Max(0, unrecorded - margin);
                if (definitelyPrinted > 0)
                    await _codePool.MarkCodesPrintedAsync(_job.Id, _job.CodesConfirmed,
                        _job.CodesConfirmed + definitelyPrinted);
                // Quarantine the boundary codes per per-printer QuarantineMargin
                var quarantineCount = Math.Min(margin, unrecorded);
                if (quarantineCount > 0)
                    await _codePool.QuarantineCodesAsync(_job.Id,
                        _job.CodesConfirmed + definitelyPrinted, quarantineCount);
                _job.CodesConfirmed = _job.CodesConfirmed + unrecorded;
            }
            else if (lifetimeDelta.HasValue && lifetimeDelta.Value == _job.CodesConfirmed)
            {
                // No additional prints, but row pointer is still lost.
                // Quarantine remaining codes — the data buffer position is unknown
                // and we can't be sure which codes will be printed next.
                _logger.LogWarning(
                    "Job {JobId}: no additional prints during disconnect, but SPGGCP was reset. " +
                    "Row pointer lost — quarantining remaining codes.", _job.Id);
                await QuarantineRemainingCodesAsync();
            }
            else
            {
                // lifetimeDelta < CodesConfirmed or lifetimeDelta is null:
                // Counter regressed or unknown — quarantine everything remaining.
                _logger.LogWarning(
                    "Job {JobId}: power cycle with uncertain state (lifetimeDelta={Delta}, confirmed={Confirmed}). " +
                    "Quarantining remaining codes.",
                    _job.Id, lifetimeDelta, _job.CodesConfirmed);
                await QuarantineRemainingCodesAsync();
            }

            _alerts.Raise(AlertSeverity.Error, _job.Printer.Name,
                $"Power cycle detected. SPGGCP reset to 0. Job #{_job.Id} requires Resume Procedure.",
                printerId: _job.PrinterId, jobId: _job.Id);
            await SetJobErrorAsync("Power cycle detected — SPGGCP reset, row pointer lost");
            return false;
        }

        // --- Check 5: Scenario 7B — RUNNING + counter divergence ---
        // If SPGGTP delta exceeds raw SPGGCP, external prints occurred.
        // Both are session-only (baselined at the same Start/Resume moment).
        // NOTE: If someone reloads our SAME template while running, both counters
        // advance in lockstep — this check CANNOT detect that scenario.
        // This is a warning-only scenario (do NOT stop the printer).
        if (status == PrinterStatus.Printing && lifetimeDelta.HasValue)
        {
            if (lifetimeDelta.Value > currentCounter)
            {
                var discrepancy = lifetimeDelta.Value - currentCounter;
                _logger.LogWarning(
                    "Job {JobId}: printer RUNNING but lifetime delta ({Delta}) exceeds SPGGCP ({Counter}). " +
                    "Possible external prints or template reload while running.",
                    _job.Id, lifetimeDelta, currentCounter);
                _alerts.Raise(AlertSeverity.Warning, _job.Printer.Name,
                    $"WARNING: Lifetime counter discrepancy while printing. " +
                    $"SPGGTP delta={lifetimeDelta}, SPGGCP={currentCounter}. " +
                    $"Possible external prints (+{discrepancy}). Job #{_job.Id} requires attention.",
                    printerId: _job.PrinterId, jobId: _job.Id);
                // Warning only — do NOT stop the printer (per design doc Scenario 7B)
            }
        }

        // --- All checks passed: reconcile any missed progress ---
        // lifetimeDelta is session-only. Convert to effective counter (add offset)
        // for comparison with CodesConfirmed which spans all sessions.
        var effectiveFromLifetime = lifetimeDelta.HasValue
            ? lifetimeDelta.Value + _counterOffset
            : (int?)null;
        if (effectiveFromLifetime.HasValue && effectiveFromLifetime.Value > _job.CodesConfirmed)
        {
            // Counter cap: catch-up value cannot exceed job quantity.
            // If it does, the lifetime counter is corrupted (e.g., SPPL stream
            // misalignment). Quarantine remaining codes instead of committing bad data.
            if (effectiveFromLifetime.Value > _job.Quantity)
            {
                _logger.LogError(
                    "Job {JobId}: catch-up effective counter {Counter} exceeds quantity {Qty}. " +
                    "Lifetime counter is corrupted (possible SPPL stream misalignment).",
                    _job.Id, effectiveFromLifetime.Value, _job.Quantity);
                await QuarantineRemainingCodesAsync();
                await SetJobErrorAsync("Counter corruption: catch-up value exceeds job quantity");
                return false;
            }

            var catchUp = effectiveFromLifetime.Value - _job.CodesConfirmed;
            _logger.LogInformation(
                "Job {JobId}: catching up {CatchUp} missed prints after reconnect (lifetime delta={Delta}, effective={Effective})",
                _job.Id, catchUp, lifetimeDelta, effectiveFromLifetime.Value);
            await CommitProgressAsync(effectiveFromLifetime.Value);
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
            _lastKnownLifetimeCounter = lifetime;
            lifetimeDelta = lifetime - _job.TotalBaseline.Value;
            _logger.LogDebug("Job {JobId} cross-check: lifetime={Lifetime}, delta={Delta}, counter={Counter}",
                _job.Id, lifetime, lifetimeDelta, counter);
        }

        _logger.LogTrace("<- ReadCountersAsync = (counter={Counter}, lifetimeDelta={Delta}) in {ElapsedMs}ms",
            counter, lifetimeDelta, sw.ElapsedMilliseconds);
        return new PollSnapshot(counter, lifetimeDelta);
    }

    /// <summary>
    /// Checks for counter anomalies. Returns true if safe to continue, false if
    /// the anomaly is severe enough to block progress (Invariant #8).
    /// Warning-only anomalies still return true.
    /// </summary>
    private bool DetectAnomalies(PollSnapshot snapshot, int effectiveCounter)
    {
        _logger.LogTrace("-> DetectAnomalies(counter={Counter}, lifetimeDelta={Delta}, prevCounter={Prev}, effective={Effective})",
            snapshot.Counter, snapshot.LifetimeDelta, _previousCounter, effectiveCounter);

        var blocked = false;

        // Cross-check: compare SPGGTP delta against raw SPGGCP (session-only prints).
        // Both TotalBaseline and SPGGCP baseline are recorded at the same moment
        // (during Start or Resume), so lifetimeDelta and snapshot.Counter both
        // represent prints in THIS session only. Do NOT add _counterOffset here —
        // it includes CodesConfirmed from previous sessions whose TotalBaseline
        // has already been superseded.
        if (snapshot.LifetimeDelta.HasValue && snapshot.LifetimeDelta != snapshot.Counter)
        {
            _logger.LogWarning("Job {JobId} ANOMALY: counter mismatch SPGGCP={Counter}, SPGGTP delta={Delta} (offset={Offset})",
                _job.Id, snapshot.Counter, snapshot.LifetimeDelta, _counterOffset);
            _alerts.Raise(AlertSeverity.Warning, _job.Printer.Name,
                _loc.Format("Alert_CounterMismatch", snapshot.Counter, snapshot.LifetimeDelta),
                printerId: _job.PrinterId, jobId: _job.Id);
        }

        // Counter jump detection: skip on first poll (_previousCounter == -1 sentinel)
        var advance = snapshot.Counter - _previousCounter;
        if (_previousCounter >= 0 && advance > 10)
        {
            _logger.LogWarning("Job {JobId} ANOMALY: unexpected counter jump +{Advance} (prev={Prev}, now={Now})",
                _job.Id, advance, _previousCounter, snapshot.Counter);
            _alerts.Raise(AlertSeverity.Warning, _job.Printer.Name,
                _loc.Format("Alert_CounterJump", advance),
                printerId: _job.PrinterId, jobId: _job.Id);

            // BLOCKING: if the jump exceeds remaining codes, this is impossible
            // and indicates SPPL stream corruption (e.g., lifetime counter value
            // interpreted as current counter). Per Invariant #8: stop everything,
            // quarantine, alert.
            var remainingCodes = _job.Quantity - _job.CodesConfirmed;
            if (advance > remainingCodes)
            {
                _logger.LogError(
                    "Job {JobId} BLOCKING ANOMALY: counter jump +{Advance} exceeds remaining codes {Remaining}. " +
                    "SPPL stream is corrupted. Halting job.",
                    _job.Id, advance, remainingCodes);
                _alerts.Raise(AlertSeverity.Error, _job.Printer.Name,
                    _loc.Format("Alert_CounterJump", advance),
                    printerId: _job.PrinterId, jobId: _job.Id);
                blocked = true;
            }
        }

        _logger.LogTrace("<- DetectAnomalies = {Safe}", !blocked);
        return !blocked;
    }

    private async Task CommitProgressAsync(int effectiveCounter)
    {
        _logger.LogTrace("-> CommitProgressAsync(effectiveCounter={Counter}) for Job {JobId}", effectiveCounter, _job.Id);

        // Final safety guard: should never be called with a value beyond quantity.
        // The PollLoopAsync cap and inspection validation should prevent this,
        // but if a code path is missed, fail loudly rather than corrupting data.
        if (effectiveCounter > _job.Quantity)
            throw new InvalidOperationException(
                $"BUG: effectiveCounter ({effectiveCounter}) > Quantity ({_job.Quantity}) for Job {_job.Id}");

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

        // Stop the printer to prevent further prints beyond the job quantity
        try
        {
            await _adapter.StopPrintAsync();
            _logger.LogDebug("Job {JobId}: printer stopped on completion", _job.Id);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Job {JobId}: failed to stop printer on completion (non-fatal)", _job.Id);
        }

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
