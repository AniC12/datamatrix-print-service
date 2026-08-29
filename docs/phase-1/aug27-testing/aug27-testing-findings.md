# Aug 27 Manual Testing — Findings & Bug Report

> **Date:** 2026-08-27  
> **Tester:** Manual testing on real Savema printer (Savema-Line1, serial 26050155)  
> **Log file:** `application/src/Hosts/CodePrintManager.Desktop/bin/Debug/net8.0-windows/logs/app-20260827.log`  
> **Product tested:** Byuregh 0.5L (Product ID 5, Printer ID 3)  
> **Session time:** 01:46 — 03:16  

## Jobs Executed During Testing

| Job | Qty | Outcome | Notes |
|-----|-----|---------|-------|
| 44 | 15 | Cancelled (stale from previous session) | Recovery cancelled at startup; 0/15 confirmed |
| 45 | 5 | Completed 5/5 | Normal run. SPPSTP:OK received mid-poll but after reaching 5/5 |
| 46 | 7 | Completed 7/7 | Normal run |
| 47 | 12 | Failed during prepare | Printer unreachable, codes returned |
| 48 | 12 | Failed during prepare | Printer unreachable, codes returned |
| 49 | 12 | Completed 12/12 | Normal run (paused/resumed once) |
| 50 | 2 | Failed during prepare | Codes returned |
| 51 | 2 | Completed 2/2 | Normal run |
| 52 | 2 | Completed 2/2 | Normal run |
| 53 | 4 | Cancelled from app (1/4) | 1 printed, 1 quarantined, 2 returned |
| 54 | 8 | Cancelled after connection loss | See Bug 2 and Bug 3 |
| 55 | 10 | "Completed" 10/10 | **CRITICAL BUG** — only 3 physically printed. See Bug 1 |

---

## BUG 1 (CRITICAL): SPPL Response Stream Misalignment — Counter Corruption

### Severity: CRITICAL

### Summary

When the user presses **Cancel on the Savema printer's touchscreen** during printing, the printer sends an unsolicited `SPPSTP:OK` message into the TCP stream. This corrupts the request-response pairing in `SavemaTtoAdapter`, causing subsequent counter reads to return values from the **wrong SPPL commands**. The app then commits wildly incorrect progress (e.g., 2514/10) and falsely marks the job as completed.

### What Happened (Job 55 — qty=10)

**Timeline:**

| Time | Poll | Event |
|------|------|-------|
| 03:14:29 | | Job 55 started. SPGGTP baseline=2511, SPGGCP baseline=0 |
| 03:15:47 | #153 | counter=1, progress 1/10 |
| 03:15:49 | #157 | counter=2, progress 2/10 |
| 03:15:50 | #160 | counter=3, progress 3/10. Cross-check: lifetime=2514, delta=3 ✓ |
| 03:15:51–55 | #161–#169 | counter=3 (user pressed Cancel on printer display; printing stopped) |
| **03:15:55** | **#170** | **SPPSTP:OK received instead of SPGGCP response → FormatException** |
| 03:15:57 | #171 | Cross-check: SPGGCP=3 ✓, but SPGGTP reads as `3` (should be ~2514). Delta=-2508. **ANOMALY logged but not acted on** |
| **03:15:58** | **#172** | **SPGGCP reads as `2514` (this is actually the SPGGTP response from poll #171!). Progress committed as 2514/10 → job "completed"** |

**Key log lines:**
```
[03:15:55.913 ERR] Job 55 UNEXPECTED ERROR on poll #170
System.FormatException: The input string 'OK' was not in a correct format.

[03:15:57.915 DBG] Job 55 cross-check: lifetime=3, delta=-2508, counter=3
[03:15:57.915 WRN] Job 55 ANOMALY: counter mismatch SPGGCP=3, SPGGTP delta=-2508

[03:15:58.434 DBG] Job 55 poll #172: counter=2514 offset=0 effective=2514 (prev=3)
[03:15:58.434 WRN] Job 55 ANOMALY: unexpected counter jump +2511 (prev=3, now=2514)
[03:15:58.434 DBG] Codes printed: Job 55 [3..2514) count=2511
[03:15:58.439 INF] Job 55 progress: 2514/10 (25140%)
[03:15:58.445 INF] Job 55 COMPLETED: 10/10
```

### Root Cause

The SPPL protocol is a simple request-response protocol over a single TCP stream. `ReadResponseAsync` reads the **first complete response** (delimited by `^`) from the TCP stream. It does NOT validate that the response command matches the sent command.

**Sequence of events:**

1. App sends `~SPGGCP^` (get current counter)
2. Printer has already injected unsolicited `~SPGRES{SPPSTP:OK}^` into the stream
3. `ReadResponseAsync` reads `SPPSTP:OK` instead of `SPGGCP:3`
4. `response.AsInt()` fails → FormatException. The actual SPGGCP response is still buffered
5. On the next cross-check poll (#171):
   - Send `~SPGGCP^` → reads the **buffered SPGGCP response** from step 4 (value=3) ✓
   - Send `~SPGGTP^` → reads the **response to poll #171's SPGGCP** (value=3) — WRONG, should be SPGGTP
   - The SPGGTP response (value=2514) stays in the buffer
6. On poll #172:
   - Send `~SPGGCP^` → reads the **buffered SPGGTP response** (value=2514) — WRONG
   - `effective = 2514 + 0 = 2514 ≥ 10` → job marked complete
   
The `[0ms]` timing on poll #172 confirms the response was already buffered — no network wait.

### Impact

- **7 codes falsely marked as Printed** that were never physically printed (indices 3–9 of Job 55)
- Job shows `Progress=2514/10` in UI — obviously wrong
- The anomaly detection logged warnings but **did not prevent the false completion**
- `MarkCodesPrintedAsync(jobId=55, fromIndex=3, toIndex=2514)` was called — the service only found 7 remaining reserved codes (indices 3-9) to mark

### This Also Happens on Normal Completion (StopPrint: FAIL)

The same unsolicited SPPSTP:OK issue occurs on **every normal job completion**. When the Savema printer auto-stops after reaching CSV quantity, it sends `SPPSTP:OK` into the stream. When `CompleteJobAsync` then sends its own `~SPPSTP^`, the adapter reads a stale response → `StopPrint: FAIL`. Evidence from the logs:

```
[02:25:15.574] SPPL TX -> ~SPPSTP^   → StopPrint: FAIL  (Job 45)
[02:48:48.141] SPPL TX -> ~SPPSTP^   → StopPrint: FAIL  (Job 46)
[02:58:08.354] SPPL TX -> ~SPPSTP^   → StopPrint: FAIL  (Job 49)
[02:59:44.718] SPPL TX -> ~SPPSTP^   → StopPrint: FAIL  (Job 51)
[03:01:01.091] SPPL TX -> ~SPPSTP^   → StopPrint: FAIL  (Job 52)
[03:15:58.443] SPPL TX -> ~SPPSTP^   → StopPrint: FAIL  (Job 55)
```

Some responses also contain compound data:
```
SPPL RX <- SPPSTP:OK}^~SPGRES{SPPSTP:OK  (Jobs 46, 49, 52)
```
This suggests the printer sometimes sends the unsolicited SPPSTP:OK AND responds to our SPPSTP command in the same TCP read, resulting in parsing garbage.

### Required Fixes

**In `SavemaTtoAdapter` / SPPL protocol layer:**

1. **Validate response command matches sent command.** After `SpplResponseParser.Parse(raw)`, check that `response.Command` matches the expected command (e.g., if we sent `SPGGCP`, verify response is `SPGGCP:N`). If it doesn't match, log a warning and either re-read the stream or discard and retry.

2. **Handle multiple SPPL frames in a single TCP read.** The raw TCP data can contain multiple `~SPGRES{...}^` frames concatenated. `ReadResponseAsync` currently returns the first `^`-terminated chunk. It should split on frame boundaries and process only the frame matching the sent command.

3. **Flush stale data before sending a new command.** Before `_stream.WriteAsync`, check if there is pending data on the TCP stream (using `NetworkStream.DataAvailable`). If so, read and discard (or log) the stale data before sending the new command.

**In `JobExecutor`:**

4. **Act on anomalies, don't just warn.** When `DetectAnomalies` detects a counter jump > 10 or a counter mismatch with SPGGTP delta, it currently only logs a warning. For a jump of +2511 (or any jump exceeding the remaining job quantity), the executor should **refuse to commit the progress** and instead pause the job or set it to Error.

5. **Cap effective counter at job quantity.** As a defense-in-depth: `effectiveCounter = Math.Min(effectiveCounter, _job.Quantity)`. The `CommitProgressAsync(2514)` for a qty=10 job should never happen. The code at `MarkCodesPrintedAsync(jobId=55, fromIndex=3, toIndex=2514)` tried to mark 2511 codes as printed for a 10-code job.

### Files to Modify

| File | Change |
|------|--------|
| `src/Printers/CodePrintManager.Printer.Savema/SavemaTtoAdapter.cs` | Response command validation, stream flush, multi-frame handling |
| `src/Printers/CodePrintManager.Printer.Savema/Protocol/SpplResponseParser.cs` | Multi-frame parsing support |
| `src/Core/CodePrintManager.Application/Services/JobExecutor.cs` | Anomaly action escalation, counter cap |

---

## BUG 2: Codes Remain Reserved Instead of Printed After Job Completion

### Severity: HIGH

### Summary

After some print jobs, codes that should have been marked `Printed` remain in `Reserved` status. This is a symptom of two underlying issues:

### Case A: Stale Printing Job Cancellation (Job 44 — already fixed)

Job 44 was in `Printing` state when the app was restarted. On startup recovery, `CancelJobAsync` found no active `JobExecutor` in the registry, and the original code skipped code reconciliation entirely. All 15 reserved codes remained stuck as Reserved.

```
[01:54:47.545 INF] Job 44 CANCELLING (status="Printing", confirmed=0)
[01:54:47.551 VRB] <- TryGet = false (key not found)    ← No executor exists
[01:54:47.803 INF] Job 44 CANCELLED (confirmed=0/15) in 276ms
```

**No `ReturnCodesToPool` was called.** Pool stats confirmed: Available=9950 (should have been 9965).

**Status:** A fix was applied in a previous session to handle stale Printing jobs without executors. Needs verification.

### Case B: Counter Misread During Post-Reconnect Inspection (Job 54)

Job 54 had qty=8 and reached progress 2/8 before losing connection. After multiple reconnection failures and UNEXPECTED ERRORs, the job was eventually paused and cancelled. However, the cancellation log shows:

```
[03:12:59.178 INF] Job 54 CANCELLING (status="Paused", confirmed=8)
[03:12:59.337 INF] Job 54 CANCELLED (confirmed=8/8) in 170ms
```

Only 2 `MarkCodesPrintedAsync` calls were logged (indices 0..1 and 1..2), yet `confirmed=8`. The remaining 6 were likely committed during a brief reconnection moment in `RunPostReconnectInspectionAsync`, where the catch-up code committed progress based on a possibly-corrupted lifetime counter reading (same SPPL stream misalignment issue as Bug 1).

**Impact:** 8 codes were marked as Printed even though only 2 were physically printed. 6 codes are wasted (marked Printed but never actually on products).

### Required Fixes

- Fix A is already implemented; needs verification
- Fix B is a consequence of Bug 1 — the SPPL response validation fix will prevent counter misreads from corrupting the catch-up logic in `RunPostReconnectInspectionAsync`

---

## BUG 3: Printer Connection Loss During Active Printing (Job 54)

### Severity: MEDIUM

### Summary

Job 54 lost TCP connection to the printer during active printing (poll #31). The connection could not be re-established through the poll loop's inline `TryReconnect` calls (reconnection attempts within the poll loop kept failing). This resulted in:

1. Continuous `CONNECTION LOST` / `inspection failed (connection lost again)` spam (polls #31–#55)
2. Transition to `UNEXPECTED ERROR` spam (polls #56–#76) — the errors are not even logged with stack traces
3. User had to manually pause and cancel the job

**Key log sequence:**
```
[03:08:21.836 INF] Job 54 progress: 2/8 (25%)
[03:08:25.760 ERR] Job 54 CONNECTION LOST on poll #31 (connected=false)
...
[03:08:58.011 WRN] Job 54: inspection failed (connection lost again). Will retry on next reconnect.
...
[03:09:16.375 ERR] Job 54 UNEXPECTED ERROR on poll #56    ← no stack trace logged
[03:09:18.387 ERR] Job 54 UNEXPECTED ERROR on poll #57
... (repeats 21 times until user pauses at 03:09:56)
```

### Issues Observed

1. **UNEXPECTED ERROR entries lack stack traces.** The generic `catch (Exception ex)` block only logs `ex` in the message template but the actual exception details are not visible in the log for polls #56+. This makes debugging impossible.

2. **No automatic escalation.** After ~30 consecutive failed polls with no progress, the executor should pause or error-out the job automatically, rather than spinning forever.

3. **Alert spam.** Every failed poll raises a new `Connection lost. Job #54 paused.` alert (dozens of them), flooding the alert panel.

### Required Fixes

| Fix | Description |
|-----|-------------|
| Log full exception in UNEXPECTED ERROR handler | The `catch (Exception ex)` in `PollLoopAsync` should ensure the exception is logged with stack trace |
| Auto-pause after N consecutive failures | After e.g. 10 consecutive failed polls, auto-pause the job and notify the operator instead of looping |
| Deduplicate connection-lost alerts | Only raise the alert once per connection-loss episode, not on every poll |

### Files to Modify

| File | Change |
|------|--------|
| `src/Core/CodePrintManager.Application/Services/JobExecutor.cs` | Auto-pause on repeated failures, log full exceptions |
| `src/Core/CodePrintManager.Application/Services/AlertService.cs` | Alert deduplication |

---

## BUG 4: Template Activation Causes Brief TCP Disconnect (Savema Behavior)

### Severity: LOW (handled, but user-visible)

### Summary

Every `SPLLTF` (template activation) command causes the Savema printer to briefly drop the TCP connection. This is a **known Savema firmware behavior**, not an application bug. The code already handles it with a reconnection loop (~500ms recovery).

**Evidence — every PREPARE phase:**
```
[02:24:49.488 WRN] Job 45: connection lost after template activation, waiting for reconnect...
[02:24:50.003 INF] TryReconnect: printer 'Savema-Line1' reconnected          ← ~500ms

[02:46:21.852 WRN] Job 46: connection lost after template activation, waiting for reconnect...
[02:46:22.366 INF] TryReconnect: reconnected                                  ← ~500ms

[03:05:03.420 WRN] Job 53: connection lost after template activation...
[03:05:03.931 INF] TryReconnect: reconnected                                  ← ~500ms
```

This happens 100% of the time on the real printer (never on the simulator).

### User Impact

- The printer briefly shows as "disconnected" in the UI during prepare
- If the user is watching the Printers tab, they see a momentary status change
- This is what the user described as "the printer disconnects after finishing a job" — it actually happens at the **start of the NEXT job's prepare**, not after completion

### No fix needed in the application

The reconnection handling works correctly. However:
- Consider suppressing the WRN-level log during template activation (it's expected behavior)
- Consider adding a UI note: "Brief disconnect during template activation is normal for Savema printers"

---

## Quarantine Status Analysis

### Question: Do we need the Quarantine status?

### Answer: YES — it is correctly designed and serves a necessary purpose.

### What Quarantine Means

`Quarantined` = the system cannot determine whether this code was physically printed. It is frozen (cannot be auto-reused) until the operator manually resolves it.

### Where It Is Used

| Call Site | Scenario | What Gets Quarantined |
|-----------|----------|----------------------|
| `CancelJobAsync` | Mid-print cancellation | The single boundary code at `finalCounter` (may or may not have been printed in the gap between counter read and stop command) |
| `RunPostReconnectInspectionAsync` | Serial number mismatch | All remaining reserved codes |
| `RunPostReconnectInspectionAsync` | Template mismatch | All remaining reserved codes |
| `RunPostReconnectInspectionAsync` | SPGGTP went backward | All remaining reserved codes |
| `RunPostReconnectInspectionAsync` | Power cycle with unrecorded prints | Boundary code at the uncertain position |

### Evidence from Aug 27 Testing

Job 53 was cancelled from the app at 1/4 progress. The cancellation correctly:
1. Marked code at index 0 as Printed (confirmed=1)
2. Quarantined code at index 1 (the boundary — might have been printed between counter read and stop)
3. Returned codes at indices 2-3 to Available

Pool stats confirmed: `Quarantined=1`

### Why Not Just Use "Burned"?

| | Quarantined | Burned |
|---|---|---|
| Reversible? | Yes — operator can move to Available/Printed/Burned | No — permanently consumed |
| Who decides? | System sets it automatically; operator resolves | Operator confirms permanent loss |
| Cost | Code in limbo but recoverable | Code permanently wasted |
| Safety | Cannot be auto-reused (prevents duplicates) | Cannot be used at all |

Government codes have monetary value. Burning every uncertain code wastes money unnecessarily. Quarantine preserves the duplicate-prevention guarantee while allowing recovery after manual verification.

### Implementation Quality

- UI: Codes tab supports filtering by Quarantined, bulk status change, risky-transition warning for Quarantine→Available
- Stats: Products view shows Quarantined count in amber
- Protected: Reserved codes cannot be manually changed (enforced in `CodeManagementService`)
- AGENTS.md rule: "Quarantine uncertain codes, don't auto-burn"

### Recommendation

Keep Quarantine as-is. No changes needed.

---

## Additional Observations

### Observation 1: Savema Sends Unsolicited `SPPSTP:OK` on Auto-Stop

When the Savema printer finishes printing all CSV rows, it auto-stops and sends `~SPGRES{SPPSTP:OK}^` into the TCP stream without being asked. This is the root trigger for Bug 1 (stream misalignment). The same happens when the user presses Cancel on the printer's touchscreen.

This is **Savema firmware behavior** and cannot be changed. The application must handle it at the protocol layer.

### Observation 2: Compound SPPL Responses

Some TCP reads contain multiple SPPL frames concatenated:
```
SPPSTP:OK}^~SPGRES{SPPSTP:OK
```
This means a single `ReadAsync` can return multiple complete SPPL responses. The current parser only handles one frame per read.

### Observation 3: Job 54 Confirmed=8 But Only 2 MarkCodesPrinted Logged

Between the 2/8 progress and the eventual cancellation (confirmed=8/8), the confirmed count jumped to 8. This happened during a brief reconnection moment in `RunPostReconnectInspectionAsync` where the catch-up logic committed corrupted counter values. This is a direct consequence of Bug 1's SPPL misalignment affecting the post-reconnect inspection.

### Observation 4: Pool Stats After All Testing

```
Product 'Byuregh 0.5L' pool: Available=9912, Printed=72, Burned=0, Quarantined=1, Total=10000
```

15 codes are unaccounted for (10000 - 9912 - 72 - 0 - 1 = 15). These are the Reserved codes from Job 44 (the stale cancellation bug, already fixed).

---

## Summary of Required Changes (Priority Order)

| # | Bug | Severity | Files | Description |
|---|-----|----------|-------|-------------|
| 1 | SPPL Stream Misalignment | **CRITICAL** | `SavemaTtoAdapter.cs`, `SpplResponseParser.cs`, `JobExecutor.cs` | Validate response commands match sent commands; handle unsolicited messages; flush stale TCP data; cap effective counter at job quantity |
| 2 | Anomaly Detection Is Warning-Only | **HIGH** | `JobExecutor.cs` | When a counter jump exceeds remaining quantity or SPGGTP delta is wildly negative, halt/pause the job instead of just warning |
| 3 | Connection-Loss Error Handling | **MEDIUM** | `JobExecutor.cs`, `AlertService.cs` | Auto-pause after N failures; log full exceptions; deduplicate alerts |
| 4 | StopPrint:FAIL on Completion | **LOW** | `SavemaTtoAdapter.cs` | The SPPSTP command on completion fails because the printer already auto-stopped. Either flush unsolicited messages first, or don't treat FAIL as an error when the printer is already idle |
