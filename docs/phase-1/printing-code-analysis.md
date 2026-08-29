# Printing System — Full Code Analysis

**Date:** 2026-08-30  
**Scope:** Fresh zero-to-complete analysis of all printing code vs. documentation  
**Files analyzed:** 25+ source files, 4 design documents, all test projects

---

## 1. Executive Summary

### What We Are Building

A Windows desktop application that prints unique, government-issued Data Matrix/QR codes onto products using Savema thermal transfer (TTO) printers. Each code is a one-time-use identifier — printing the same code twice is illegal. Wasting codes costs money.

The application manages the full lifecycle: importing codes from CSV files, assigning them to products, uploading them to the printer's data buffer, monitoring the print counter in real time, and tracking which codes were confirmed printed, quarantined (uncertain), or returned to the pool.

### Why This Is Hard

The Savema printer communicates via SPPL, a simple text-based protocol over raw TCP. There is no application-level acknowledgment, no checksums, no message IDs. The printer can:
- Send unsolicited status frames (`SPPSTP:OK`) at any time
- Lose power mid-print (resetting its session counter but preserving its lifetime counter)
- Drop the TCP connection during template activation
- Be physically swapped at the same IP address

The application must survive all of this while never marking an unprinted code as printed (waste) and never allowing a printed code to be reused (duplicates).

### How Printing Works (The Happy Path)

```
 User selects product + printer + quantity
     │
     ▼
 [PREPARE] ── Reserve codes FIFO from pool
     │         Upload CSV with reserved codes to printer
     │         Upload/verify .rox template
     │         Activate template (SPLLTF)
     │         Record SPGGTP as TotalBaseline    ◄── anchor for recovery
     │         Record SPGGCP as session baseline  ◄── for cumulative counters
     │         Status → Ready
     │         Start ReadyWatcher (polls every 3s for external print)
     │
     ▼
 [START] ─── Stop ReadyWatcher
     │        Refresh TotalBaseline + SPGGCP baseline
     │        Set print quantity (SPPSLQ)
     │        Send start command (SPPSAP)
     │        Status → Printing
     │        Spawn JobExecutor (500ms polling loop)
     │
     ▼
 [MONITOR] ── Every 500ms:
     │          Read SPGGCP (session counter)
     │          Compute effectiveCounter = SPGGCP + offset
     │          Every 5th poll: cross-check SPGGTP (lifetime counter)
     │          Run anomaly detection
     │          If effectiveCounter > CodesConfirmed: commit progress
     │          If effectiveCounter >= Quantity: complete job
     │
     ▼
 [COMPLETE] ── Best-effort stop print (SPPSTP)
               Status → Completed
               All codes confirmed Printed
```

### How Corner Cases Are Handled

| Scenario | What Happens |
|----------|-------------|
| **Power cycle** | SPGGCP resets to 0. SPGGTP survives. Post-reconnect inspection computes `lifetimeDelta = SPGGTP_now - TotalBaseline`. Unrecorded prints are marked. Boundary code is quarantined. Resume requires re-uploading CSV with only remaining codes. |
| **Network disconnect** | IOException in poll loop. `_needsInspection` flag set. On reconnect, full 5-check inspection runs: serial, status, template, lifetime delta, counter consistency. |
| **Template mismatch after reconnect** | Quarantine all unconfirmed codes. Set job to Error. |
| **Counter goes backward** | Job halted immediately. Quarantine remaining codes. Error state. |
| **Counter jump > remaining codes** | Anomaly detector blocks commit. Quarantine + Error. |
| **Unsolicited SPPL frame** | Adapter validates response command name. Up to 3 retries, then forced TCP reconnect. Persistent receive buffer preserves multi-frame data. |
| **App crash** | On restart: Preparing jobs auto-cancelled. Ready/Printing/Paused jobs shown in Recovery Dialog with counter inspection. Operator chooses Resume or Abort. |
| **Cancel mid-print** | Read final counter. Mark printed codes. Quarantine the boundary code (uncertainty). Return remaining codes to pool. |
| **External print (someone presses button on printer)** | ReadyWatcher detects counter advancement or RUNNING status. Alert raised. Job transitions to Printing automatically. |
| **10 consecutive poll failures** | Escalation: quarantine remaining codes, set job to Error. |
| **Serial number mismatch** | Hardware swap detected. Job blocked. Alert raised. |

### Key Safety Invariants (from documentation)

1. `Printed` codes never return to `Available`
2. `Quarantined` codes never auto-return to `Available`
3. `SPGGTP` is the source of truth across power cycles
4. After power cycle, CSV must be re-uploaded with ONLY remaining codes
5. Quarantine +1 on any ambiguous boundary
6. Never auto-resume after disconnect
7. Template mismatch → conservative abort + quarantine
8. Counter backward → stop everything, quarantine, alert
9. Inspection must complete atomically
10. Quarantined codes excluded from Available pool
11. Ready jobs must NOT be auto-cancelled on startup
12. TotalBaseline recorded during Prepare, not Start

---

## 2. Discrepancies: Code vs. Documentation

### CRITICAL — D1: PauseJobAsync and CancelJobAsync Use Raw SPGGCP Instead of Effective Counter

**Severity:** Critical (confirmed code bug — dormant on current hardware, exploitable on stale-job resume)  
**Files:** `PrintJobService.cs` lines 526-534, 429-447  
**Doc reference:** `phase1-design.md` §5.3, `connection-recovery-deep-dive.md` §10  
**Status:** Code is wrong. Documentation is correct.

**The bug:**

`PauseJobAsync` reads raw `SPGGCP` and directly compares it with `CodesConfirmed`:

```csharp
var finalCounter = await adapter.GetCurrentCounterAsync();  // raw SPGGCP
if (finalCounter > job.CodesConfirmed)  // CodesConfirmed is offset-adjusted!
{
    await _codePool.MarkCodesPrintedAsync(jobId, job.CodesConfirmed, finalCounter);
    job.CodesConfirmed = finalCounter;  // Sets CodesConfirmed to raw value
}
```

`JobExecutor` correctly uses an offset: `effectiveCounter = SPGGCP + counterOffset`. But `PauseJobAsync` and `CancelJobAsync` bypass the executor and read the raw counter directly. On cumulative-counter firmware (where `SPGGCP` doesn't reset on `SPLLTF`):

- Job starts with `SPGGCP = 5000`, `counterOffset = -5000`
- After printing 3 codes: `SPGGCP = 5003`, `CodesConfirmed = 3`
- Pause reads `finalCounter = 5003`
- `5003 > 3` → marks `5003 - 3 = 5000` codes as printed
- Only 7 remain as `Reserved`, so all 7 are flipped to `Printed`
- `CodesConfirmed` set to 5003 (should be at most 10 for a 10-code job)

**Impact:**
- All remaining reserved codes incorrectly marked as `Printed` (over-marking)
- `CodesConfirmed` set to a value exceeding `Quantity`
- Resume after pause fails ("no remaining codes")
- Cancel calculates negative `remaining` count (harmless due to guards, but wrong)

**Evidence from Aug 27 logs:**

The codebase comments explicitly state `"SPGGCP is cumulative on real hardware (does NOT
reset on SPLLTF)"`. The Aug 27 logs confirm this:
- **Job 44** (stale Ready resume): `SPGGCP baseline = 15`, proving SPGGCP retained its
  cumulative value across app restarts without `SPLLTF`.
- **Jobs 45-55** (fresh Prepare with `SPLLTF`): `SPGGCP baseline = 0`, meaning `SPLLTF`
  happened to reset the counter on this firmware (serial `26050155`).

The bug is **dormant** when every job starts with a fresh `SPLLTF` (which resets SPGGCP
to 0 on our current hardware). It **manifests** when:
1. A stale Ready job is resumed without re-calling `SPLLTF` (Job 44 scenario — SPGGCP=15).
2. Future firmware does not reset SPGGCP on `SPLLTF`.

On Aug 27, Job 44's pause/cancel happened to work only because the counter stayed low.
With a larger cumulative offset, the bug would have corrupted code accounting.

**Why tests don't catch it:** Integration tests use `MockPrinterAdapter` starting at counter 0.

**Fix:** `PauseJobAsync` and `CancelJobAsync` must use `SPGGTP - TotalBaseline` as the
authoritative print count (not raw SPGGCP). This is robust across power cycles, firmware
variations, and stale-job resumes.

---

### NOT A STANDALONE BUG — D2: CancelJobAsync Negative Remaining Calculation

**Severity:** Secondary symptom of D1 (not independently exploitable)  
**File:** `PrintJobService.cs` line 445  

```csharp
var remaining = job.Quantity - finalCounter - 1;
```

If `finalCounter >= Quantity`, `remaining` becomes negative. The `if (remaining > 0)` guard
prevents a crash, but quarantine and return are both skipped.

**This is not independently exploitable.** Under normal operation, the printer respects
`SPPSLQ{N}` and will not over-print. The only way `finalCounter` can exceed `Quantity` is:
1. **D1's raw-counter bug** — the measurement is wrong, not the printer.
2. **SPPL stream corruption** — a `SPGGTP` value misinterpreted as `SPGGCP` (addressed by
   the command-validation fix from Aug 27).

Once D1 is fixed (using `SPGGTP - TotalBaseline`), this calculation becomes correct
automatically. No separate fix needed.

---

### HIGH — D3: StartJobAsync Does Not Acquire Printer Lock

**Severity:** High (race condition)  
**File:** `PrintJobService.cs` line 306  
**Doc reference:** `multi-printer-concurrency.md` §2 ("Multi-step operations on a printer are exclusive")

`PrepareJobAsync`, `CancelJobAsync`, `PauseJobAsync`, and `ResumeJobAsync` all acquire the per-printer `SemaphoreSlim` from `ActiveJobRegistry`. But `StartJobAsync` does not. If a user clicks Start while a Prepare, Cancel, or Resume is in flight on the same printer, they can race:

- `StartJobAsync` calls `adapter.SetPrintQuantityAsync` and `adapter.StartPrintAsync`
- Concurrently, `CancelJobAsync` calls `adapter.StopPrintAsync` and reads `finalCounter`
- The per-adapter lock serializes individual commands, but the multi-step sequence is not atomic

The documentation explicitly states: "Multi-step operations on a printer are exclusive." `StartJobAsync` violates this.

---

### MEDIUM — D4: Design Doc Says "Burn +1" on Cancel; Code Says "Quarantine +1"; Margin Should Be Configurable

**Severity:** Medium (documentation drift + missing feature)  
**Files:** `PrintJobService.cs` line 439, `phase1-design.md` §3.1 Step 7  

The design doc says:
> "The next code after last confirmed print is marked `burned` (+1 safety)."

The code actually calls `QuarantineCodeAsync` (not `BurnCodeAsync`). This matches the later
`connection-recovery-deep-dive.md` which says "Quarantine +1 on any ambiguous boundary."
The code is correct per the safety invariants; `phase1-design.md` is outdated on this point.

**Improvement needed:** The quarantine margin should be a per-printer setting rather than
hard-coded to 1. Different printers or production lines may have different risk tolerances.

Design considerations:
- Add `QuarantineMargin` column to `printers` table (`int NOT NULL DEFAULT 1`).
- Semantics: total codes to quarantine at the boundary (0 = no quarantine, 1 = current
  behavior, 2+ = extra safety margin for high-speed lines or unreliable firmware).
- A setting of 0 violates safety Invariant #5 ("Quarantine +1 on any ambiguous boundary").
  The UI should warn operators when setting margin to 0, but allow it for lines where
  physical verification is available.
- Applies to: cancel boundary, pause boundary, power-cycle boundary. Does NOT apply to
  bulk quarantine on anomaly (template mismatch, counter backward) — those always
  quarantine all remaining codes regardless of the margin setting.

---

### RESOLVED — D5: Partial Unique Index Filter — Code Is Correct, Documentation Is Stale

**Severity:** Documentation-only (code is already correct)  
**Doc reference:** `multi-printer-concurrency.md` §2, `codebase-architecture.md` §2, `phase1-design.md` §4  

The documentation in multiple places defines:
```sql
WHERE status IN ('Preparing', 'Ready', 'Printing')
```

But the **actual database** already includes `Paused`:
- `PrintJobConfiguration.cs` lines 33-40: both indexes filter on
  `"[Status] IN ('Preparing', 'Ready', 'Printing', 'Paused')"`.
- Migration `20260809172830_AddPausedToActiveJobFilter` explicitly dropped the old indexes
  and recreated them with `Paused` included.
- The model snapshot confirms the current state includes `Paused`.
- `PrintJobService.ActiveStatuses` array includes `Paused`.
- `GetStaleJobsAsync` includes `Paused`.

**No code fix needed.** The three documentation files (`phase1-design.md`,
`multi-printer-concurrency.md`, `codebase-architecture.md`) should be updated to show the
correct filter that includes `Paused`.

**However, `Error` status is NOT in the index or in `ActiveStatuses`.** A job in `Error`
state has `CompletedAt` set and is treated as terminal. This is correct: `SetJobErrorAsync`
already quarantines remaining codes before setting `Error`, so there are no reserved codes
left to protect. The `Error` status does not need to be in the index.

**One remaining question:** `GetStaleJobsAsync` does NOT include `Error` jobs. If the app
crashes between `QuarantineRemainingCodesAsync` and `SetJobErrorAsync` in the executor, the
job stays in `Printing` status (no executor) and gets picked up by startup recovery. This
is correct. If the crash happens after `SetJobErrorAsync`, the job is terminal and no
recovery is needed. The current behavior is sound.

---

### LOW — D6: GetActiveTemplateAsync Doesn't Use SpplCommandBuilder

**Severity:** Low (code inconsistency, needs fix)  
**File:** `SavemaTtoAdapter.cs` line 167  

```csharp
var response = await SendCommandAsync(
    $"{SpplConstants.CommandStart}SPLGAT{SpplConstants.CommandEnd}", ct);
```

`SpplCommandBuilder.GetActiveTemplate()` exists and returns the same string. Every other
adapter method uses the builder. This should be changed to use the builder for consistency.

---

### LOW — D7: Design Doc Counter Tracking Says "baseline is always 0"

**Severity:** Low (documentation outdated, needs update)  
**Doc reference:** `phase1-design.md` §5.3  

The design doc says `codes_printed = current_counter` (baseline always 0). The actual code
correctly uses `effectiveCounter = SPGGCP + counterOffset` to handle cumulative counters.
`phase1-design.md` §5.3 should be updated to reflect the baseline-delta tracking that is
actually implemented.

---

## 3. Safety Gaps

### S1: ConnectAsync Swallows OperationCanceledException

**File:** `SavemaTtoAdapter.cs` line 49  
**Risk:** If the caller's cancellation token fires during `ConnectAsync`, the method catches it and returns `false` (connection failed) instead of throwing `OperationCanceledException`. The caller cannot distinguish "printer unreachable" from "user cancelled."

This is especially problematic during the `PrepareJobAsync` reconnect retry loop — a user-initiated cancellation would be silently treated as a connection failure and retried.

---

### S2: No Concurrency Guard on Code Reservation

**File:** `CodePoolService.cs` line 101-153  
**Risk:** `ReserveCodesAsync` queries `Available` codes and marks them `Reserved` without a database lock or `SELECT ... FOR UPDATE`. Under multi-printer concurrency (two jobs reserving from the same product pool simultaneously), both could read the same `Available` codes, then both `SaveChangesAsync` — one would succeed and the other might not get a unique constraint violation because they're modifying different columns (`Status`, `JobId`) on the same rows.

The partial unique index on `print_jobs` prevents two active jobs for the same product, which mitigates this in practice. But if the index check and reservation are not atomic, there's a window.

---

### S3: AlertService.ScheduleDismiss Is async void

**File:** `AlertService.cs` line 110  
**Risk:** `async void` methods cannot have their exceptions observed. If `Task.Delay` or `Dismiss` throws, the exception propagates to the `SynchronizationContext` and crashes the WPF application. Should be `_ = Task.Run(...)` or use a try-catch wrapper.

---

### S4: ReadyWatcher.Start Has No Re-entrance Guard

**File:** `ReadyWatcher.cs` line 60  
**Risk:** Calling `Start()` twice overwrites `_cts` and `_watchTask`. The first loop continues running but can no longer be stopped by `StopAsync()`, which only cancels the latest `_cts`.

---

### S5: Reconnect Loop Does Not Re-Check Serial Number

**File:** `PrinterConnectionManager.cs` `TryReconnectAsync` line 267  
**Risk:** When `TryReconnectAsync` is called (e.g., from `ResumeJobAsync`'s post-`SPLLTF` reconnect retry), it reconnects the adapter but does not call `CheckSerialNumberAsync`. A swapped printer at the same IP could be used for the job without detection until the next full `ConnectAsync`.

---

### S6: PrinterConnectionManager.ConnectAsync Leaks Previous Adapter

**File:** `PrinterConnectionManager.cs` line 82  
**Risk:** `_adapters[printer.Id] = adapter` overwrites any existing adapter without disposing it. If `ConnectAsync` is called twice for the same printer (e.g., rapid UI clicks), the previous `TcpClient` leaks.

---

### S7: StopAsync on ReadyWatcher Only Catches OperationCanceledException

**File:** `ReadyWatcher.cs` line 72  
**Risk:** If the watch loop throws an unexpected exception (e.g., `FormatException` from a corrupted SPPL response), `StopAsync` propagates it to the caller (typically `StartJobAsync` or `DisconnectAsync`).

---

### S8: No Transaction Scope Around Cancel Code Mutations

**File:** `PrintJobService.cs` lines 421-479  
**Doc reference:** `connection-recovery-deep-dive.md` §11.10  

The cancel sequence performs multiple DB writes: `MarkCodesPrintedAsync`, `QuarantineCodeAsync`, `ReturnCodesToPoolAsync`, then sets job `Cancelled`. If the app crashes between these calls, codes could be partially marked — some printed, some still reserved. The documentation specifically calls this out:

> "Cancel DB mutations must be one transaction; store CancelRequested flag."

This is not implemented.

---

### S9: 10-Second Read Timeout May Be Too Short for Large CSV Uploads

**File:** `SavemaTtoAdapter.cs` `ReadResponseAsync`  
**Risk:** The 10-second per-`ReadAsync` timeout is applied to ALL commands, including `UploadCsvAsync` and `UploadTemplateAsync`. Large CSV uploads (thousands of codes) may legitimately take longer than 10 seconds for the printer to process and respond. This would trigger a false `IOException("Read timeout")` and force a reconnect.

---

## 4. Improvement Opportunities

### I1: Add Counter Offset to PrintJobService Pause/Cancel (fixes D1)

Store the executor's `counterOffset` in `ActiveJobRegistry` alongside the executor, or compute it from `TotalBaseline`:

```csharp
// In PauseJobAsync / CancelJobAsync:
var rawCounter = await adapter.GetCurrentCounterAsync();
var lifetimeNow = await adapter.GetTotalCounterAsync();
var effectiveCounter = lifetimeNow - job.TotalBaseline;  // uses SPGGTP, not SPGGCP
```

Using `SPGGTP - TotalBaseline` is more robust than trying to retrieve the SPGGCP offset because it works across power cycles.

### I2: Add Paused to the Partial Unique Index (fixes D5)

```sql
WHERE status IN ('Preparing', 'Ready', 'Printing', 'Paused')
```

This ensures the database enforces the "one active job per printer/product" invariant for paused jobs too.

### I3: Wrap Cancel Code Mutations in a Transaction (fixes S8)

```csharp
using var transaction = await _db.Database.BeginTransactionAsync();
try
{
    // ... mark printed, quarantine, return ...
    job.Status = JobStatus.Cancelled;
    await _db.SaveChangesAsync();
    await transaction.CommitAsync();
}
catch
{
    await transaction.RollbackAsync();
    throw;
}
```

### I4: Add Per-Command Timeout Overrides (fixes S9)

Allow `SendCommandAsync` to accept an optional timeout parameter, with longer defaults for upload commands:

```csharp
private async Task<SpplResponse> SendCommandAsync(string cmd, CancellationToken ct,
    TimeSpan? readTimeout = null)
{
    // ...
    readCts.CancelAfter(readTimeout ?? TimeSpan.FromSeconds(10));
}
```

`UploadCsvAsync` and `UploadTemplateAsync` would pass `TimeSpan.FromSeconds(60)`.

### I5: Add Printer Lock to StartJobAsync (fixes D3)

```csharp
public async Task StartJobAsync(int jobId, CancellationToken ct = default)
{
    // ...
    var printerLock = _jobRegistry.GetPrinterLock(job.PrinterId);
    await printerLock.WaitAsync(ct);
    try
    {
        // ... existing start logic ...
    }
    finally
    {
        printerLock.Release();
    }
}
```

### I6: Dispose Previous Adapter in ConnectAsync (fixes S6)

```csharp
if (_adapters.TryGetValue(printer.Id, out var existing))
{
    existing.Dispose();
    _adapters.TryRemove(printer.Id, out _);
}
```

### I7: Add \n and | to ForbiddenSequences

`SpplConstants.ForbiddenSequences` does not include `\n` (CSV row separator in `SPLCDF`) or `|` (SPPL command separator). A code containing either would corrupt the upload payload.

### I8: Raise PrinterStatusChanged on DisconnectAsync

`PrinterConnectionManager.DisconnectAsync` does not fire `PrinterStatusChanged`. UI consumers are not notified when a printer is manually disconnected, leaving the dashboard showing stale status.

### I9: Guard async void in AlertService.ScheduleDismiss (fixes S3)

```csharp
private void ScheduleDismiss(Guid alertId, TimeSpan delay)
{
    _ = Task.Run(async () =>
    {
        try
        {
            await Task.Delay(delay);
            Dismiss(alertId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to auto-dismiss alert {AlertId}", alertId);
        }
    });
}
```

### I10: Import Deduplication Within Batch

`CodePoolService.ImportCodesAsync` checks each code against the database but not against other codes in the same import batch. Two identical codes in one CSV would both pass the `AnyAsync` check (neither is saved yet), then `SaveChangesAsync` would fail with a DB unique constraint violation.

### I11: Alert Deduplication Key Should Include JobId

The current dedup key format is `"{source}:{printerId}:{deduplicationKey}"`. Two different jobs on the same printer would share the same dedup key, causing the second job's connection-loss alert to be suppressed.

---

## 5. Test Coverage Gaps

### T1: Savema Adapter — Zero Unit Tests

`CodePrintManager.Printer.Savema.Tests` contains only an empty `UnitTest1.cs` placeholder. None of the following are tested:

- `SpplCommandBuilder` command framing
- `SpplResponseParser` parsing, validation, edge cases
- `SpplConstants.ForbiddenSequences` validation
- `SavemaTtoAdapter` TCP send/receive, persistent buffer, read timeout
- Response command validation and retry logic
- Forced reconnect after mismatch exhaustion
- Stream flush before send

### T2: JobExecutor — No Direct Unit Tests

The executor is only tested indirectly through integration tests (via `MockPrinterAdapter`). Missing:

- Anomaly detection blocking (counter jump > remaining)
- Counter cap behavior
- `_needsInspection` lifecycle (set on error, cleared after success)
- Consecutive failure escalation (10 failures → Error)
- `InvalidOperationException("Not connected")` handling
- Post-reconnect inspection (serial, template, counter checks)
- `CommitProgressAsync` guard (`effectiveCounter > Quantity`)

### T3: No Cumulative Counter Tests for Pause/Cancel

Integration tests only run with `MockPrinterAdapter` starting at counter 0. Bug D1 is invisible. Need tests with `mockPrinter.SetCounters(5000, 50000)` followed by pause and cancel.

### T4: No Multi-Printer Concurrency Tests

Despite the documented two-level locking model, no test runs two jobs on two printers simultaneously. Missing:

- Concurrent Prepare on different printers
- One printer disconnects while another prints
- SQLite WAL under concurrent writes

### T5: No Positive ReadyWatcher External-Print Test

`CumulativeCounterTests` only tests that the watcher does NOT false-positive on high counters. There is no test confirming that the watcher DOES detect a real external print start (counter advancing past baseline while the job is Ready).

### T6: No App-Restart Recovery Tests

No test simulates a crash and restart with stale `Ready`/`Printing`/`Paused` jobs in the database. The startup recovery logic in `App.xaml.cs.RunStartupRecoveryAsync` and the Recovery Dialog flow are untested.

### T7: No Forbidden-Sequence Import Tests

No test verifies that importing a CSV with codes containing `~`, `^`, `~gt~`, or `~sc~` is rejected or handled safely.

### T8: No Adapter Failure Injection Tests

`MockPrinterAdapter` cannot throw `IOException` mid-command or simulate network timeouts. The integration test suite cannot exercise:

- Connection loss during polling
- Reconnect + inspection flow
- Template mismatch after reconnect
- SPPL response corruption / misalignment

---

## 6. Architecture Observations

### What's Done Well

1. **Layered defense against SPPL corruption.** The adapter has three layers: stream flush, command-name validation with retry, and forced reconnect. The executor adds counter caps, anomaly blocking, and post-reconnect inspection on top.

2. **Quarantine over burn.** The shift from burning uncertain codes to quarantining them preserves operator recovery options. This is consistently applied across cancel, pause, power-cycle, and anomaly paths.

3. **TotalBaseline during Prepare.** Recording `SPGGTP` during Prepare (not Start) closes the gap where Ready jobs had no baseline for recovery inspection.

4. **Resume procedure re-uploads CSV.** `ResumeJobAsync` implements the full 10-step resume procedure from the recovery deep-dive, including deleting the old CSV and uploading only remaining codes.

5. **Persistent receive buffer.** The `_receiveBuffer` in `SavemaTtoAdapter` correctly handles multi-frame SPPL responses, preventing the stream desynchronization that caused the August 27 Job 55 incident.

6. **Separation of concerns.** Domain has zero dependencies. Printer adapters only reference Domain. Application services own lifecycle. ViewModels are thin event consumers.

### What Needs Attention

1. **The PauseJobAsync/CancelJobAsync raw-counter bug (D1)** is the most dangerous finding. It silently corrupts `CodesConfirmed` on cumulative-counter firmware and breaks resume.

2. **Test coverage for the SPPL layer is zero.** All the protocol robustness added after August 27 (persistent buffer, command validation, read timeout, forced reconnect) has no unit tests.

3. **The code pool service has no concurrency protection.** While mitigated by the partial unique index and UI flow, the `ReserveCodesAsync` → `SaveChangesAsync` window is theoretically exploitable.

---

## 7. Priority Ranking

| Priority | Item | Risk | Effort |
|----------|------|------|--------|
| P0 | D1: Fix PauseJobAsync/CancelJobAsync to use SPGGTP-TotalBaseline | Data corruption (dormant, confirmed real) | Small |
| P0 | T3: Add cumulative-counter pause/cancel tests | Validates D1 fix | Small |
| P1 | D3: Add printer lock to StartJobAsync | Race condition | Small |
| P2 | D5: Update docs to show Paused in index filter (code already correct) | Stale docs | Trivial |
| P1 | S8: Transaction around cancel mutations | Partial state on crash | Small |
| P1 | T1: Savema adapter unit tests | Protocol bugs invisible | Medium |
| P1 | T2: JobExecutor unit tests | Safety logic untested | Medium |
| P2 | D4: Per-printer configurable quarantine margin | Flexibility | Medium |
| P2 | D6: GetActiveTemplateAsync use SpplCommandBuilder | Code consistency | Trivial |
| P2 | D7: Update phase1-design.md §5.3 counter tracking | Stale docs | Trivial |
| P2 | S9: Per-command read timeout | False timeout on uploads | Small |
| P2 | I7: Add \n and \| to ForbiddenSequences | CSV corruption | Trivial |
| P2 | S6: Dispose previous adapter in ConnectAsync | Resource leak | Trivial |
| P2 | I8: Raise PrinterStatusChanged on disconnect | UI staleness | Trivial |
| P3 | S3: Fix async void ScheduleDismiss | Potential crash | Trivial |
| P3 | I10: Import dedup within batch | Import crash | Small |
| P3 | I11: Dedup key include jobId | Suppressed alerts | Trivial |
| P3 | S4: ReadyWatcher re-entrance guard | Orphaned loop | Trivial |
| P3 | S5: TryReconnectAsync serial check | Swap detection gap | Small |
| P3 | T4-T8: Remaining test gaps | Coverage | Medium-Large |
