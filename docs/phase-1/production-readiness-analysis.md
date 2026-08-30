# Production Readiness Analysis: Printing Process & Code Management

**Date:** 2026-08-30  
**Scope:** End-to-end printing pipeline, code state management, corner cases, and gaps.  
**Method:** Full review of design docs, all Application/Domain/Printer source code, EF Core configurations, and integration tests.

---

## Table of Contents

1. [Executive Summary](#1-executive-summary)
2. [How Printing Works Today](#2-how-printing-works-today)
3. [What's Solid](#3-whats-solid)
4. [Critical Issues (Must Fix Before Production)](#4-critical-issues)
5. [High-Risk Corner Cases](#5-high-risk-corner-cases)
6. [Medium-Risk Issues](#6-medium-risk-issues)
7. [Lower-Risk Gaps](#7-lower-risk-gaps)
8. [Test Coverage Assessment](#8-test-coverage-assessment)
9. [Unanswered Printer Behavior Questions](#9-unanswered-printer-behavior-questions)
10. [Recommendations Summary](#10-recommendations-summary)

---

## 1. Executive Summary

The printing pipeline is **well-designed** at the architecture level. The design documents are thorough, the safety invariants are clearly articulated, and the code follows them closely. The system correctly handles the most common scenarios: happy-path printing, cancellation, pause/resume, connection loss with reconnection, power cycle recovery, and external print detection.

However, there are **several concrete gaps between the design and the implementation** that could cause code mismanagement in production. The most concerning are:

- Cancel/Pause operations can fail silently if the printer is disconnected, leaving codes stuck in `Reserved` forever
- No Savema adapter unit tests exist (zero) — the entire TCP/SPPL protocol layer is unverified by automated tests
- A race condition in `ReserveCodesAsync` could theoretically double-reserve codes under concurrency (mitigated by the one-job-per-product constraint, but still a latent risk)
- The `CancelJobAsync` crash-recovery path (no executor, no adapter) returns codes to `Available` without quarantining the boundary — violating the quarantine-on-ambiguity safety rule
- Duplicate printer configurations (same IP:port) bypass the one-active-job-per-printer DB constraint

None of these are showstoppers if you plan a short manual-testing phase with real hardware before production. But they need to be on your radar.

---

## 2. How Printing Works Today

Here is the actual implemented flow, with code references:

### 2.1 Job Creation → Code Reservation

```
User selects product + printer + quantity
    → CreateJobAsync() validates: quantity > 0, no active job on product/printer, enough codes
    → Creates PrintJob (status = Preparing)
    → DB partial unique indexes prevent duplicate active jobs
```

`PrintJobService.cs:59-122`

### 2.2 Prepare Phase

```
PrepareJobAsync():
    1. Acquire per-printer SemaphoreSlim lock
    2. Verify printer status == Idle (SPPSTA)
    3. Check for serial number mismatch
    4. Reserve N codes (FIFO by ImportOrder) → status: Available → Reserved
    5. Delete old CSV on printer (SPLDDF), upload new CSV (SPLCDF), verify (SPLGSD)
    6. Check template exists on printer (SPLGST), upload .rox if missing (SPLRTF)
    7. Activate template (SPLLTF) — this loads the CSV data buffer
    8. Handle post-SPLLTF connection drop (Savema printers sometimes disconnect here)
    9. Record TotalBaseline (SPGGTP) and SPGGCP baseline
    10. Set job status → Ready
    11. Spawn ReadyWatcher (polls for external print starts)
```

`PrintJobService.cs:124-304`

### 2.3 Start Phase

```
StartJobAsync():
    1. Stop ReadyWatcher (before acquiring lock — avoids deadlock)
    2. Acquire per-printer lock
    3. Refresh TotalBaseline (SPGGTP) and SPGGCP baseline
    4. Set print quantity (SPPSLQ)
    5. Start printer (SPPSAP)
    6. Set job status → Printing
    7. Spawn JobExecutor with counterOffset = -spggcpBaseline
```

`PrintJobService.cs:306-409`

### 2.4 Monitoring (Poll Loop)

```
JobExecutor.PollLoopAsync() — every 500ms:
    1. If _needsInspection (post-disconnect): run full inspection, then continue
    2. Read SPGGCP; every 5th cycle also read SPGGTP
    3. Compute effectiveCounter = raw_SPGGCP + counterOffset
    4. Cap effectiveCounter to Quantity (defense-in-depth)
    5. Detect backward counter movement → halt + quarantine
    6. DetectAnomalies: SPGGTP/SPGGCP mismatch warning, jump > 10 warning, jump > remaining = fatal
    7. If effectiveCounter > CodesConfirmed: commit progress (mark codes Printed)
    8. If effectiveCounter >= Quantity: complete job
    9. On IOException: set _needsInspection, count failures; 10 failures = Error + quarantine
```

`JobExecutor.cs:90-259`

### 2.5 Cancellation

```
CancelJobAsync():
    1. Acquire per-printer lock
    2. If Printing + has executor:
        - Stop executor, stop printer (SPPSTP)
        - Read SPGGTP: effectivePrinted = SPGGTP - TotalBaseline
        - Mark [0..effectivePrinted) as Printed
        - Quarantine QuarantineMargin boundary codes
        - Return remaining to Available
    3. If Printing but NO executor (crash recovery):
        - Return (Quantity - CodesConfirmed) codes to Available  ← NO quarantine!
    4. If Paused: return remaining to Available (counter already reconciled)
    5. If Preparing/Ready: return all to Available
    6. Set job → Cancelled (in DB transaction)
```

`PrintJobService.cs:411-538`

### 2.6 Pause/Resume

```
PauseJobAsync():
    - Stop executor, stop printer
    - Reconcile: effectivePrinted = SPGGTP - TotalBaseline
    - Mark delta as Printed (in transaction)
    - Set job → Paused

ResumeJobAsync():
    - For Ready jobs: delegate to StartJobAsync
    - For Paused jobs:
        1. Query remaining Reserved codes
        2. Delete old CSV, upload new CSV with only remaining codes
        3. Verify CSV, check/upload template
        4. Reload template (SPLLTF)
        5. Record fresh TotalBaseline and SPGGCP baseline
        6. Start printer, spawn new executor
```

`PrintJobService.cs:540-958`

### 2.7 Post-Reconnect Inspection

```
JobExecutor.RunPostReconnectInspectionAsync():
    1. Read status, SPGGCP, SPGGTP, active template, serial number (atomically)
    2. Check 0: Serial mismatch → quarantine + Error
    3. Check 1: Printer in Error/Blocked → Error
    4. Check 2: Template mismatch → quarantine + Error
    5. Check 3: SPGGTP catch-up (unrecorded prints)
        - lifetimeDelta = SPGGTP - TotalBaseline
        - If lifetimeDelta > CodesConfirmed: commit the delta
    6. Check 4: Power cycle detection (SPGGCP backward)
        - If lifetimeDelta > CodesConfirmed: commit + quarantine +1
        - Update _previousCounter baseline for forward tracking
    7. Check 5: External print detection (counter jump > expected)
        - Alert, but continue polling
    8. Successful: reset _previousCounter, continue
```

`JobExecutor.cs:269-539`

---

## 3. What's Solid

These are things the codebase does well and should give you confidence:

### 3.1 SPGGTP-Based Recovery
The design is fundamentally sound. `SPGGTP` (lifetime counter) is the ground truth for "how many prints actually happened." Every critical operation (cancel, pause, resume, inspection) derives `effectivePrinted` from `SPGGTP - TotalBaseline`. This survives power cycles, connection drops, and firmware quirks.

### 3.2 Quarantine-on-Ambiguity
Instead of burning codes or silently returning them, the system quarantines boundary codes. This is the right trade-off: the operator can investigate and recover them. Implemented correctly in `CancelJobAsync` (for printing jobs with adapters), pause reconciliation, and post-reconnect inspection.

### 3.3 Two-Level Locking
The concurrency model is well-layered:
- Per-printer `SemaphoreSlim` in `PrintJobService` prevents interleaving of Prepare/Start/Cancel/Pause/Resume
- Per-adapter `SemaphoreSlim` in `SavemaTtoAdapter` serializes SPPL commands
- DB partial unique indexes prevent duplicate active jobs per printer/product

### 3.4 Baseline-Delta Counter Math
The `counterOffset` approach correctly handles both firmware variants (SPGGCP resets on SPLLTF vs. cumulative). By always capturing a baseline and computing `effectiveCounter = raw + offset`, the code doesn't care which behavior the printer exhibits.

### 3.5 Post-SPLLTF Reconnection Handling
The code explicitly handles the known Savema behavior where the printer drops TCP after template activation. Both `PrepareJobAsync` and `ResumeJobAsync` have retry loops with `TryReconnectAsync` after the SPLLTF call.

### 3.6 ReadyWatcher for External Prints
When a job is in `Ready` state, the `ReadyWatcher` polls `SPPSTA` and `SPGGCP` every 3 seconds. If the operator presses Start on the printer's touchscreen, the watcher detects it and transitions the job to `Printing` with a proper executor — preventing untracked prints.

### 3.7 Atomic Cancel/Pause DB Mutations
Both `CancelJobAsync` and `PauseJobAsync` wrap their code-state changes in explicit DB transactions. If anything fails mid-operation, the transaction rolls back and codes stay in their previous state.

### 3.8 Integration Test Coverage (Application Layer)
The integration tests via `TestHost` + `MockPrinterAdapter` cover a wide range of scenarios: full print cycle, cancel at various states, pause/resume, cumulative counter handling, power cycle detection, quarantine margin behavior, serial mismatch, and pool accounting. This is solid functional coverage for the orchestration layer.

---

## 4. Critical Issues (Must Fix Before Production)

### CRIT-1: Cancel/Pause Fail When Printer Is Disconnected

**What happens:** If the printer is offline when the operator clicks Cancel or Pause on a `Printing` job, `adapter.StopPrintAsync()` or `adapter.GetTotalCounterAsync()` throws an `IOException`. This exception is **not caught** in `CancelJobAsync` or `PauseJobAsync` — it propagates up, the `finally` block releases the lock, but the job **stays in `Printing` status** with codes stuck in `Reserved`.

**Why it matters:** The operator sees an error message and can't do anything. The job can't be cancelled. The codes can't be reused. The partial unique index blocks new jobs on that printer/product.

**Code location:**
- `PrintJobService.cs` lines 440-468 (Cancel's printer I/O section)
- `PrintJobService.cs` lines 560-587 (Pause's printer I/O section)

**Fix:** Wrap the printer I/O in a try-catch. If the adapter throws, fall back to `CodesConfirmed` as the effective print count (same as the "no executor" path already does for cancel), add the quarantine margin, and proceed with the DB transaction. The operator can reconcile later via the Codes tab.

**Severity: CRITICAL** — This will happen in production every time someone tries to cancel during a network outage, which is precisely when they're most likely to want to cancel.

---

### CRIT-2: Cancel Crash-Recovery Path Skips Quarantine

**What happens:** When `CancelJobAsync` encounters a `Printing` job with no executor (crash recovery / startup abort), it returns `Quantity - CodesConfirmed` codes directly to `Available` without quarantining the boundary.

**Why it matters:** After a crash, the printer may have printed codes beyond `CodesConfirmed`. Those codes are returned to `Available` and could be printed again — a duplicate. This directly violates Safety Invariant #5 (quarantine +1 on any ambiguous boundary).

**Code location:** `PrintJobService.cs` lines 496-504

```csharp
// Printing job but no executor (crash recovery / startup abort).
// CodesConfirmed is the last persisted progress — treat it like a Paused job.
// Return all remaining reserved codes.
if (job.CodesConfirmed < job.Quantity)
    await _codePool.ReturnCodesToPoolAsync(jobId, 0, job.Quantity - job.CodesConfirmed);
```

**Fix:** Before returning codes, quarantine at least 1 code (or `QuarantineMargin`) at the boundary:

```csharp
var margin = Math.Max(1, job.Printer?.QuarantineMargin ?? 1);
var remaining = job.Quantity - job.CodesConfirmed;
var quarantine = Math.Min(margin, remaining);
if (quarantine > 0)
    await _codePool.QuarantineCodesAsync(jobId, job.CodesConfirmed, quarantine);
if (remaining - quarantine > 0)
    await _codePool.ReturnCodesToPoolAsync(jobId, 0, remaining - quarantine);
```

**Severity: CRITICAL** — This is the exact scenario (crash during printing) where boundary uncertainty is highest.

---

### CRIT-3: Zero Savema Adapter Unit Tests

**What happens:** The `CodePrintManager.Printer.Savema.Tests` project contains a single empty `Test1` method. There are **zero tests** for:
- `SpplCommandBuilder` (command string construction, escaping, base64 encoding)
- `SpplResponseParser` (response parsing, status parsing, malformed input handling)
- `SavemaTtoAdapter` (connection, send/receive, timeout, retry, reconnect, framing)

**Why it matters:** The Savema adapter is the only component that touches real hardware. Every SPPL command encoding error, every response parsing bug, every framing issue will manifest as mysterious production failures. The integration tests use `MockPrinterAdapter`, which simulates behavior at the logical level — it doesn't test the wire protocol at all.

**What's at risk specifically:**
- `SPLCDF` command constructs codes joined by `\n` with `~gt~` separator — any code containing a newline that slipped past validation would silently corrupt the CSV upload
- `SpplResponseParser.Parse` throws `FormatException` on malformed input, but `SendCommandAsync` only catches `IOException` — a malformed printer response will crash the poll loop with an uncaught `FormatException` instead of triggering the inspection path
- The 3-retry desync logic in `SendCommandAsync` (lines 307-362) discards mismatched responses — this is the only defense against unsolicited SPPL frames and has never been tested
- Base64 encoding of template uploads (`.rox` files) — untested

**Severity: CRITICAL for production confidence** — You need at minimum: parser happy/sad path tests, command builder output verification, and a response-framing test with partial reads.

---

## 5. High-Risk Corner Cases

### HIGH-1: FormatException from SPPL Misalignment Crashes the Poll Loop

**What happens:** If the Savema printer sends a malformed response (firmware bug, TCP corruption, stream desync), `SpplResponseParser.Parse` throws `FormatException`. Inside `JobExecutor.PollLoopAsync`, this is caught by the generic `catch (Exception ex)` handler at line 243 — which counts consecutive failures but does **not** set `_needsInspection = true`. After 10 such failures (~25 seconds), the job is quarantined and set to Error.

However, in the `RunPostReconnectInspectionAsync` path (line 117), `FormatException` **is** correctly handled — the inspection retries on the next poll. So the behavior differs depending on *when* the malformed response arrives.

**Impact:** During normal polling, a temporary SPPL misalignment (e.g., unsolicited frame shifts the parser state) escalates to a permanent job failure in 25 seconds, when it might have self-corrected after one retry.

**Fix:** In the generic exception handler in `PollLoopAsync`, set `_needsInspection = true` for `FormatException` specifically, so the next poll runs the full inspection (which includes a fresh adapter read that may re-sync the stream).

---

### HIGH-2: Power Cycle Between Polls Without Connection Loss

**What happens:** If the printer power-cycles but the TCP connection is somehow maintained (unlikely but possible with certain network equipment), `SPGGCP` goes backward. The code at `JobExecutor.cs:154-164` catches this and halts the job with `SetJobErrorAsync`, quarantining remaining codes.

**However:** The halt is permanent. There is no recovery path — the job goes to `Error` and the operator must cancel it. The design document describes this scenario as requiring the post-reconnect inspection path, but that path is only entered on `IOException`.

**Impact:** The operator loses the job and must quarantine all remaining codes, even though the printer might be fine and `SPGGTP` still has the correct count.

**Mitigation idea:** Instead of immediately erroring, set `_needsInspection = true` and let the inspection path handle it (it has the power-cycle detection logic with proper `SPGGTP` reconciliation).

---

### HIGH-3: Duplicate Printer Configurations Bypass Safety

**What happens:** The `Printer` entity has no unique constraint on `(IpAddress, Port)`. An operator could create two printer entries pointing to `192.168.1.100:9100`. The DB partial unique index only prevents two active jobs on the same `PrinterId` — two different `PrinterId` values pointing to the same physical device would each be allowed one active job.

**Impact:** Two jobs could run simultaneously on the same physical printer. The CSV uploads would overwrite each other. Counter tracking would be corrupted. Codes would almost certainly be misprinted or duplicated.

**Fix:** Add a unique index on `(IpAddress, Port)` in the `PrinterConfiguration`:
```csharp
builder.HasIndex(e => new { e.IpAddress, e.Port }).IsUnique();
```

---

### HIGH-4: MarkCodesPrintedAsync Silent Under-Count

**What happens:** `CodePoolService.MarkCodesPrintedAsync` takes `count = toIndex - fromIndex` and queries for that many `Reserved` codes. If fewer `Reserved` codes exist than requested (data corruption, earlier bug), it silently marks only what's there. The caller (`JobExecutor.CommitProgressAsync`) then sets `CodesConfirmed = effectiveCounter`, believing all codes were marked.

**Impact:** `CodesConfirmed` can drift ahead of the actual number of codes marked `Printed` in the database. On cancel or pause, the reconciliation math (`Quantity - CodesConfirmed`) would return fewer codes to `Available` than it should, leaving some codes permanently stuck in `Reserved`.

**Fix:** After the query, verify `codes.Count == count`. If not, log a critical alert and do not update `CodesConfirmed` beyond the actual count.

---

### HIGH-5: effectivePrinted < CodesConfirmed Silently Ignored on Pause

**What happens:** In `PauseJobAsync` (line 593), if `SPGGTP - TotalBaseline` produces an `effectivePrinted` that is *less* than `CodesConfirmed`, the code silently skips reconciliation. `CodesConfirmed` remains at its old (higher) value.

**How could this happen:** Counter rollover (extremely unlikely but possible on old firmware), or `TotalBaseline` was corrupted/stale.

**Impact:** `CodesConfirmed` is too high. On resume, the new CSV will have fewer remaining codes than the executor expects. On cancel, fewer codes will be returned to `Available`.

**Fix:** At minimum, log a critical warning. Consider quarantining the discrepancy.

---

## 6. Medium-Risk Issues

### MED-1: 10-Failure Escalation Window Too Narrow

With a 500ms poll interval + 2s delay on failure, 10 consecutive failures takes only ~25 seconds. On a factory floor with intermittent Wi-Fi or a shared network, a 25-second outage is common.

**Impact:** Jobs get permanently errored (with all remaining codes quarantined) for transient network issues.

**Recommendation:** Make the failure threshold configurable (per-printer or global). Consider 30 failures (~75 seconds) as a more forgiving default, or implement a backoff that extends the window.

---

### MED-2: No TCP Keepalive — Stale Socket Detection Delayed

The `TcpClient` is configured with `ReceiveTimeout=5000` and `SendTimeout=5000`, but TCP keepalive is not enabled. A half-open socket (remote end crashed without FIN) won't be detected until the next `SendCommandAsync` call times out — up to 5.5 seconds.

**Impact:** During the Ready state (ReadyWatcher polls every 3 seconds), a stale socket could delay external-print detection by an extra poll cycle. During Printing (500ms polls), it's less significant.

**Recommendation:** Enable TCP keepalive on the socket:
```csharp
tcpClient.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, true);
```

---

### MED-3: No Disk Space Monitoring

SQLite writes will fail silently or throw when disk space runs out. There is no proactive monitoring.

**Impact:** Mid-print, `CommitProgressAsync` fails to save. The executor counts it as a consecutive failure. After 10 failures (25 seconds), the job is quarantined and errored. But the *actual* codes were printed — they just can't be committed to the database. If the disk issue is temporary (another program released space), the codes are now quarantined when they should be `Printed`.

**Recommendation:** Check available disk space on startup and periodically (every few minutes). Alert at < 500MB. Refuse to start new jobs at < 100MB.

---

### MED-4: No Database Backup or Integrity Check

The `failure-modes-analysis.md` rates this as **Critical risk**. There is no `PRAGMA integrity_check` on startup, no backup mechanism, and no import/export. A corrupted database means all code tracking history is lost.

**Recommendation (minimum for Phase 1):**
- Run `PRAGMA quick_check` on startup; alert on failure
- Copy the `.db` file to a timestamped backup before each `DbInitializer.InitializeAsync`
- Document manual recovery steps

---

### MED-5: Admin Can Set Codes to `Reserved` Without a Job

`CodeManagementService.ChangeStatusAsync` filters *source* codes to exclude `Reserved`, but does not validate the *target* status. An admin could change an `Available` code to `Reserved` via the Codes tab. This code would have `Status = Reserved` but `JobId = null`.

**Impact:** The next `MarkCodesPrintedAsync` or `QuarantineCodesAsync` call for any job on that product could pick up this orphan `Reserved` code (since those queries filter by `Status == Reserved` and `JobId == jobId`... actually, they DO filter by `JobId`, so this orphan would be invisible to job operations). The code would be permanently stuck in `Reserved` with no way to automatically recover it.

**Fix:** Validate that `newStatus != CodeStatus.Reserved` in `ChangeStatusAsync`.

---

### MED-6: Reconnect Loop Exceptions Unobserved

`PrinterConnectionManager.StartReconnectLoop` uses `_ = Task.Run(...)` — a fire-and-forget task. If the reconnect loop throws an unhandled exception (anything other than `OperationCanceledException`), it becomes an unobserved task exception.

**Impact:** Depending on `TaskScheduler.UnobservedTaskException` handling, this could crash the application in .NET 8. Even if swallowed, the reconnect loop silently dies and the printer never reconnects.

**Fix:** Add a top-level `try-catch` inside the `Task.Run` lambda that logs the exception and (optionally) restarts the loop.

---

### MED-7: Serial Mismatch Flag Lost on App Restart

`PrinterConnectionManager._serialMismatchFlags` is an in-memory `ConcurrentDictionary`. If the app restarts, the flag is cleared. The stored serial in `Printer.SerialNumber` is still there, so the mismatch *will* be re-detected on the next `CheckSerialNumberAsync` call — but only if `CheckSerialNumberAsync` is called before `PrepareJobAsync`. Currently, `CheckSerialNumberAsync` runs during `ConnectAsync`, so this is probably fine. But it's fragile.

---

## 7. Lower-Risk Gaps

### LOW-1: BurnCodeAsync Ignores the Index Parameter

`CodePoolService.BurnCodeAsync(int jobId, int index)` always burns the first remaining `Reserved` code, ignoring `index`. The parameter exists in the signature but is unused.

**Impact:** Low — callers currently always mean "burn the next boundary code." But the API contract is misleading.

---

### LOW-2: No CHECK Constraints on Status Columns

Both `Code.Status` and `PrintJob.Status` are stored as `TEXT` with no `CHECK` constraint. Any string value could be written if a bug bypasses the enum conversion.

**Impact:** Low in practice (EF Core always uses the enum converter), but a direct SQLite operation could corrupt the data.

---

### LOW-3: WAL Mode Not Verified

`DbInitializer` executes `PRAGMA journal_mode=WAL` but doesn't check the return value. On a read-only filesystem or certain locked conditions, this could silently fail.

---

### LOW-4: ImportCodesAsync Row-by-Row Uniqueness Check

`ImportCodesAsync` checks `AnyAsync(c => c.CodeText == code)` for each code individually. For large imports (10,000+ codes), this could be slow.

**Impact:** Performance only — not a correctness issue. The DB unique index on `CodeText` is the final safeguard.

---

### LOW-5: No Printer Name Uniqueness

Two printers can have the same display name, leading to operator confusion but no technical issue.

---

### LOW-6: Counter Over-Run Information Lost

If the printer prints more than `Quantity` codes (firmware bug, stuck trigger), the effective counter is capped at `Quantity` and the job completes normally. The extra prints are not tracked.

**Impact:** The physical products exist but have no database record. The government codes on them are marked `Printed` (correctly), but the operator won't know extra labels were produced.

**Recommendation:** Log a prominent alert when capping occurs, with the actual counter value.

---

## 8. Test Coverage Assessment

### What's Well-Tested (via Integration Tests + MockPrinterAdapter)

| Scenario | Covered? | Test File |
|----------|----------|-----------|
| Full print cycle (create → prepare → start → complete) | Yes | `PrintCycleTests.cs`, `FullE2ETests.cs` |
| Cancel Ready job (all codes returned) | Yes | `CancelJobTests.cs` |
| Cancel Printing job (boundary quarantine) | Yes | `CancelJobTests.cs` |
| QuarantineMargin = 0 vs > 0 | Yes | `CancelJobTests.cs` |
| Pause/Resume mid-print | Yes | `PauseResumeTests.cs` |
| Cumulative SPGGCP (high baseline) | Yes | `CumulativeCounterTests.cs` |
| Power cycle detection (counter backward) | Yes | `CumulativeCounterTests.cs` |
| Resume after pause (re-upload CSV) | Yes | `RecoveryScenarioTests.cs` |
| Serial mismatch detection | Yes | `RecoveryScenarioTests.cs` |
| TotalBaseline captured during Prepare | Yes | `RecoveryScenarioTests.cs` |
| Duplicate code import prevention | Yes | `FullE2ETests.cs` |
| Insufficient codes rejection | Yes | `FullE2ETests.cs` |
| Active job uniqueness per printer/product | Yes | `FullE2ETests.cs` |
| Pool accounting after operations | Yes | `CancelJobTests.cs`, `RecoveryScenarioTests.cs` |

### What's NOT Tested

| Gap | Risk | Notes |
|-----|------|-------|
| **SavemaTtoAdapter (entire class)** | Critical | No unit tests. Wire protocol untested. |
| **SpplCommandBuilder** | Critical | Command strings never verified against spec. |
| **SpplResponseParser** | Critical | Malformed/edge-case responses untested. |
| **SendCommandAsync retry/desync logic** | High | The 3-retry mismatched-response logic is untested. |
| **ReadResponseAsync partial frame accumulation** | High | Multi-frame TCP reads untested. |
| **Cancel when printer disconnected** | High | CRIT-1 scenario not covered. |
| **Cancel crash-recovery path (no executor)** | High | CRIT-2 scenario not covered. |
| **FormatException in poll loop** | Medium | HIGH-1 scenario not covered. |
| **Concurrent reservation race** | Medium | Only safe due to one-job-per-product; not directly tested. |
| **Admin status change to Reserved** | Medium | MED-5 scenario not covered. |
| **Large CSV import performance** | Low | Functional correctness tested, performance not. |
| **Template upload (base64 encoding)** | Medium | Only tested through mock (no actual encoding). |

### Test Infrastructure Quality

The `TestHost` + `MockPrinterAdapterFactory` infrastructure is well-built. Adding new scenarios is straightforward via HTTP endpoints. The mock supports error injection, counter manipulation, and power cycle simulation. This is a good foundation — the gap is specifically in the Savema wire protocol layer.

---

## 9. Unanswered Printer Behavior Questions

These are documented in `phase1-design.md` as "Still Need Testing" and remain open:

1. **Does the stored CSV file survive a printer power cycle?**  
   If yes, resume is simpler. If no, every resume must re-upload.  
   *Current code always re-uploads on resume, so this is handled conservatively.*

2. **What happens when the CSV has fewer rows than the requested print quantity?**  
   Does the printer stop, error, or loop back to row 1?  
   *If it loops, codes could be duplicated. The code sets `SPPSLQ` to the remaining count, which should match the CSV row count, but this hasn't been verified on hardware.*

3. **Exact remaining-quantity command name: SPPGLQ or SPCGLQ?**  
   The code uses `SPPGLQ`. If the real command is different, the remaining-quantity feature silently returns `null` (handled gracefully).

4. **WAITING ↔ RUNNING transition semantics.**  
   Does `SPPSTA` transition to `RUNNING` immediately on `SPPSAP`, or only when the first label reaches the print head? This affects the ReadyWatcher's detection timing.

5. **Does SPGGCP reset to 0 on SPLLTF?**  
   Firmware-dependent. The code handles both cases via baseline-delta math. But it hasn't been verified on the actual firmware version you'll deploy with.

6. **Interface lock behavior (SPGSLI).**  
   Not implemented in Phase 1. If an operator can press buttons on the Savema touchscreen during a managed job, they could start/stop printing outside the app's knowledge. `ReadyWatcher` catches the "external start" case but not "external stop."

**Recommendation:** Before production, spend a session with the physical printer validating questions 1, 2, and 5. These can be tested with the `PrinterTestHarness` tool without a full app deployment.

---

## 10. Recommendations Summary

### Must Fix (Before Production)

| # | Issue | Effort | Section |
|---|-------|--------|---------|
| CRIT-1 | Cancel/Pause fail when printer disconnected | Small | [4](#4-critical-issues) |
| CRIT-2 | Cancel crash-recovery skips quarantine | Small | [4](#4-critical-issues) |
| CRIT-3 | Write Savema adapter unit tests (parser + builder minimum) | Medium | [4](#4-critical-issues) |
| HIGH-3 | Add unique index on printer (IpAddress, Port) | Trivial | [5](#5-high-risk-corner-cases) |

### Should Fix (Before Sustained Production Use)

| # | Issue | Effort | Section |
|---|-------|--------|---------|
| HIGH-1 | FormatException handling in poll loop | Small | [5](#5-high-risk-corner-cases) |
| HIGH-2 | Power cycle without connection loss → trigger inspection | Small | [5](#5-high-risk-corner-cases) |
| HIGH-4 | Verify MarkCodesPrintedAsync count matches | Small | [5](#5-high-risk-corner-cases) |
| HIGH-5 | Log/handle effectivePrinted < CodesConfirmed | Small | [5](#5-high-risk-corner-cases) |
| MED-1 | Make failure threshold configurable / more forgiving | Small | [6](#6-medium-risk-issues) |
| MED-3 | Add disk space monitoring | Medium | [6](#6-medium-risk-issues) |
| MED-4 | Add database backup on startup | Medium | [6](#6-medium-risk-issues) |
| MED-5 | Block admin setting codes to Reserved | Trivial | [6](#6-medium-risk-issues) |
| MED-6 | Catch unobserved reconnect loop exceptions | Trivial | [6](#6-medium-risk-issues) |

### Nice to Have

| # | Issue | Effort | Section |
|---|-------|--------|---------|
| LOW-1 | Fix BurnCodeAsync index parameter | Trivial | [7](#7-lower-risk-gaps) |
| LOW-2 | Add CHECK constraints on status columns | Small | [7](#7-lower-risk-gaps) |
| LOW-3 | Verify WAL mode activation | Trivial | [7](#7-lower-risk-gaps) |
| MED-2 | Enable TCP keepalive | Trivial | [6](#6-medium-risk-issues) |
| LOW-6 | Alert on counter over-run (not just cap) | Trivial | [7](#7-lower-risk-gaps) |

### Pre-Production Hardware Validation

1. Verify CSV survival across printer power cycle
2. Verify behavior when CSV rows < requested quantity
3. Verify SPGGCP reset behavior on your specific firmware version
4. Run a full print cycle end-to-end with the `PrinterTestHarness`
5. Test cancel mid-print and verify the quarantine margin on physical output
