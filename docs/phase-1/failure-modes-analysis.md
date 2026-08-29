# Failure Modes & Technical Risk Analysis

> **Purpose:** Comprehensive catalog of every technical problem that can occur during operation of the Code Print Manager — network failures, hardware issues, data corruption, race conditions, protocol edge cases, and human error. Each entry describes the problem, whether the system already handles it, how it handles it (or what the gap is), and severity.
>
> **References:** `phase1-design.md` §3.4 / §5 / §6.7, `multi-printer-concurrency.md` §3 / §5 / §8, `client-overview.md` §6, `codebase-architecture.md` §4

---

## 1. Network & Communication Failures

These are the most common operational failures in factory environments. Savema printers communicate over TCP/IP on the LAN (port 9100), and industrial networks are subject to electrical noise, shared infrastructure, and unreliable cabling.

### 1.1 TCP Connection Drops Mid-Print

**What happens:** The TCP socket between the app and the printer dies while a print job is running. The poll loop's next `SendCommandAsync` call throws `IOException`.

**Handled?** Yes.

**How:**
- `JobExecutor.PollLoopAsync` catches `IOException`, raises an alert ("Connection lost during print job"), waits 2 seconds, and retries.
- `PrinterConnectionManager` detects `IsConnected == false` and starts an exponential-backoff reconnect loop (1s → 2s → 4s → ... → 30s cap).
- The `SavemaTtoAdapter` is a stable handle — the same instance swaps its internal TCP connection under its own `SemaphoreSlim` lock. `JobExecutor` never knows reconnection happened.
- Other printers and their jobs are completely unaffected.

**Residual risk:** Between the last successful poll and the disconnect, the printer may have physically printed additional codes. These are reconciled only when the connection is restored by cross-checking `SPGGTP` (lifetime counter). If the operator aborts before reconnection, boundary codes are quarantined (per-printer `QuarantineMargin` setting).

### 1.2 Printer Unreachable on App Startup

**What happens:** The app starts, queries all configured printers from the DB, and tries to connect to each. One or more printers are powered off or unreachable.

**Handled?** Yes.

**How:**
- Each printer starts its own background connect task (fire-and-forget).
- Failed connections enter exponential-backoff reconnect.
- The app is immediately usable — printers show as "Offline" in the UI.
- Other printers are unaffected.

**Residual risk:** None. This is a normal operational state.

### 1.3 Intermittent Network Flickers

**What happens:** The network connection drops and reconnects rapidly (e.g., loose cable, switch reboot, VLAN reconfiguration). The adapter may successfully send a command but fail to read the response, or vice versa.

**Handled?** Partially.

**How:**
- The adapter's `SemaphoreSlim` ensures one command at a time, so there's no interleaving.
- `IOException` on read/write releases the lock and the poll loop retries.

**Gap:** Rapid connect/disconnect cycles could cause the poll loop to miss several counter updates in a row. The lifetime counter (`SPGGTP`) cross-check (every 5th poll) eventually reconciles, but during the unstable period the UI progress may be stale. If the instability persists, the operator sees repeated "Connection lost" / "Connected" alerts but no clear indication that the *network itself* is the problem (as opposed to the printer).

**Severity:** Medium. Codes are safe (quarantine-on-ambiguity protects against duplicates), but the operator experience is poor during flickers and quarantined codes may increase.

### 1.4 Stale TCP Socket (Half-Open Connection)

**What happens:** The network path is severed (cable pulled, switch port dies) but the local TCP stack doesn't detect it because no keepalive probes are configured. The socket appears connected (`TcpClient.Connected == true`) but reads/writes hang until the OS-level timeout fires.

**Handled?** Partially.

**How:**
- `TcpClient.ReceiveTimeout = 5000ms` and `SendTimeout = 5000ms` are set in `ConnectAsync`.
- If a read or write exceeds 5 seconds, a `SocketException` or `IOException` is thrown, which the poll loop catches.

**Gap:** The adapter does not set `TcpClient.KeepAlive` or configure TCP keepalive intervals. This means a silently broken connection may not be detected until the *next command attempt*. If the poll loop is between polls (in `Task.Delay(500)`), detection is delayed by up to 500ms + 5s timeout = 5.5 seconds.

**Severity:** Low. The 5-second socket timeout provides a reasonable upper bound. But in edge cases where the printer is powered off cleanly (sends no TCP RST), the first symptom is a 5-second hang on the next poll.

### 1.5 Firewall or Port Conflict

**What happens:** Port 9100 is blocked by a Windows Firewall rule, or another application (e.g., a print spooler or another Savema tool) is already using the port.

**Handled?** No.

**Gap:** The connection attempt fails and the printer shows "Offline" with exponential-backoff retries. But the error message does not distinguish "host unreachable" from "connection refused" from "port blocked." The operator has no actionable diagnostic information.

**Severity:** Low (setup-time problem, not runtime). But frustrating to troubleshoot without clearer error messages.

### 1.6 DNS / IP Address Change

**What happens:** The printer's IP address changes (DHCP lease expiration, network reconfiguration) while the app is running. The adapter holds the old IP.

**Handled?** No.

**Gap:** The connection fails, reconnect loop retries the same IP indefinitely. The only fix is for the operator to manually update the printer's IP address in the Configuration tab and reconnect.

**Severity:** Low if printers use static IPs (recommended). Medium if DHCP is used without reservations.

---

## 2. Printer Hardware Failures

Savema TTO (Thermal Transfer Overprint) printers are industrial equipment subject to mechanical wear, consumable depletion, and environmental conditions.

### 2.1 Ribbon Exhausted / Ribbon Error

**What happens:** The thermal transfer ribbon runs out or breaks. The printer transitions to an ERROR state and reports it via `SPPSTA`.

**Handled?** Yes.

**How:**
- `SPPSTA` returns `ERROR<Ribbon not found>` (or similar firmware-specific message).
- The poll loop reads the status, detects ERROR, and pauses the job.
- An alert fires with the error message: "Printer error: Ribbon not found."
- The operator replaces the ribbon, the printer returns to WAITING, and the operator resumes the job.

**Residual risk:** If the ribbon runs out *between* the last SPPSTA check and the next poll, some codes may be printed with degraded quality (fading ribbon). The system can't detect print quality — it only knows the counter advanced. This is a Phase 2 scanner/verification concern.

### 2.2 Printer in BLOCKED State

**What happens:** The operator (or someone on the factory floor) navigates into the printer's settings menu or a non-main screen on the printer's local touchscreen. All SPPL commands except `SPPSTA` return FAIL.

**Handled?** Yes.

**How:**
- `SPPSTA` returns `RUNNING<BLOCKED` or `WAITING<BLOCKED`.
- During Prepare: the upfront status check catches this and shows "Printer UI is not on main screen. Return to main screen."
- During printing: poll commands fail, the poll loop detects BLOCKED via status, alerts the operator.

**Residual risk:** If the operator enters the printer menu very briefly (between polls), it might not be detected — but it also doesn't cause harm since the operator returns to the main screen before the next poll.

### 2.3 Printer Power Cycle Mid-Print

**What happens:** The printer loses power (outage, accidental unplug) while a print job is running. `SPGGCP` (current counter) resets to 0. The CSV row pointer behavior is unknown. `SPGGTP` (lifetime counter) persists.

**Handled?** Yes. This is one of the most thoroughly designed recovery scenarios.

**How:**
1. TCP connection drops → job pauses, reconnect backoff starts.
2. When the printer comes back and connection is restored (or on app restart), the recovery flow runs:
   - Read `SPGGTP` (lifetime counter, survives power cycle).
   - Compute `prints_before_failure = SPGGTP_now - job.TotalBaseline`.
   - Compare with `job.CodesConfirmed` (what the app tracked).
   - Present discrepancy to operator in the Recovery Dialog.
3. Operator chooses Resume (re-upload remaining codes, continue from last confirmed) or Abort (quarantine ambiguous code, return rest to pool).

**Residual risk:** If both the app AND the printer lose power simultaneously (factory-wide outage), the app recovers on next startup using the same `SPGGTP` logic. The risk is minimal as long as the printer's non-volatile counter is trustworthy.

### 2.4 Printer Storage Full

**What happens:** The printer's internal storage is full. `SPLCDF` (upload CSV) or `SPLRTF` (upload template) returns FAIL.

**Handled?** Partially.

**How:**
- The Prepare step detects the FAIL response and raises an error alert.
- The Storage tab on the Printers page lets operators inspect and clean up orphaned files.

**Gap:** There is no proactive "storage getting full" warning. The operator discovers the problem only when a Prepare step fails. The error message from SPPL may not clearly indicate "storage full" vs. other failure reasons.

**Severity:** Low-Medium. The Storage tab provides the remedy, but the operator has to know to use it. Consider adding a post-connect storage check or at least logging available storage if the SPPL protocol supports it.

### 2.5 Thermal Head Overheating

**What happens:** The printer's thermal head overheats during extended print runs. Behavior depends on firmware — it may enter ERROR state, reduce speed, or pause.

**Handled?** Depends on firmware behavior.

**How:** If the firmware reports overheating via `SPPSTA:ERROR<...>`, the system handles it like any other printer error (alert + pause). If the printer silently reduces quality or pauses without reporting, the system sees the counter stop advancing and eventually the operator notices the stall.

**Gap:** No specific detection or messaging for thermal issues.

**Severity:** Low. Modern Savema printers typically manage thermal protection internally.

### 2.6 Label / Product Jam on Production Line

**What happens:** Products stop flowing past the printer on the production line — conveyor jam, product stack-up, or manual stoppage. The printer may keep printing (counter advances) even though there are no products to print on. Codes are "printed" into empty air or onto the ribbon backing.

**Handled?** No. This is fundamentally undetectable by the software.

**How it manifests:** The counter advances normally. The system marks codes as "printed." But the physical products don't have codes on them. This wastes codes and creates compliance gaps.

**Gap:** Without a scanner/verification system (Phase 2), there is no way to confirm that a code was actually printed onto a product vs. lost.

**Severity:** Medium-High in production. This is one of the strongest arguments for the Phase 2 scanner integration. In Phase 1, operators must rely on visual inspection and production-line monitoring.

### 2.7 Printer Firmware Bug or Unexpected Behavior

**What happens:** The printer's firmware has a bug — counter returns wrong values, commands succeed but have no effect, responses are malformed.

**Handled?** Partially.

**How:**
- Counter cross-checks (SPGGCP vs SPGGTP delta) detect inconsistencies.
- The SPPL response parser handles known patterns but may throw on completely unexpected formats.

**Gap:** If the firmware silently accepts a CSV upload but doesn't actually store it (`SPLCDF:OK` but file not written), the verify step (`SPLGSD`) would catch it. But if `SPLGSD` also lies (unlikely but possible with buggy firmware), the system would think everything is ready when it isn't.

**Severity:** Low. Firmware bugs are rare and usually caught during initial deployment testing with the PrinterTestHarness.

---

## 3. Counter & Tracking Anomalies

Counter tracking is the core safety mechanism. The system uses three counter sources for redundancy.

### 3.1 External Printing (Someone Uses the Printer's UI)

**What happens:** A person on the factory floor starts a print job directly from the printer's touchscreen, bypassing the app. The counters advance without the app's knowledge.

**Handled?** Yes.

**How:**
- The poll loop compares `actual_advance = new_counter - previous_counter` with `expected_max_advance`.
- If the advance is suspiciously large (> `expected_max_advance * 2`), an alert fires: "Unexpected counter jump (+N). Check if printer was used externally."
- Codes at those positions are conservatively marked as printed (even though the app didn't send them — safety first).
- The `SPGGTP` cross-check also detects the discrepancy.

**Residual risk:** The codes marked as "printed" may not correspond to what was actually printed externally. The external user may have printed completely different data. But from the app's perspective, those code positions are consumed and cannot be reused — which is the safe behavior.

**Mitigation idea:** The SPPL protocol supports `SPGSLI{1}` (lock interface) to prevent operators from using the printer's buttons during a job. This is not currently implemented.

### 3.2 Counter Mismatch (SPGGCP vs SPGGTP)

**What happens:** The current counter (`SPGGCP`, reset on template load) and the lifetime counter delta (`SPGGTP - total_baseline`) disagree.

**Handled?** Yes.

**How:**
- Cross-checked every 5th poll tick (every ~2.5 seconds).
- Discrepancy raises a warning alert: "Counter mismatch: SPGGCP={N}, SPGGTP delta={M}."
- The system uses `SPGGCP` as the primary source (it's read every tick) but logs the mismatch for investigation.

**Severity:** Low. This is usually a transient condition (e.g., the two reads happen slightly out of sync) and resolves on the next check.

### 3.3 Counter Decreases (Goes Backward)

**What happens:** The current counter returns a value *lower* than the previous reading. This should be impossible during normal operation.

**Handled?** Partially.

**How:** The poll loop checks `if (snapshot.Counter > _job.CodesConfirmed)` — if the counter hasn't advanced, nothing happens. If the counter decreased, the condition is simply false and the system does nothing (no new codes marked as printed, no alert).

**Gap:** There is no explicit detection of a counter *decrease*. The system silently ignores it. This could be a firmware bug, a man-in-the-middle attack on the TCP stream (extremely unlikely), or a protocol parsing error. At minimum, it should log a warning.

**Severity:** Very low probability, but if it occurs, it could indicate a serious problem that goes undiagnosed.

### 3.4 Printer Prints More Than Requested

**What happens:** The `SPPSLQ{N}` command sets a limited print count, but the printer prints beyond N (firmware bug, or the limited-quantity feature doesn't work reliably).

**Handled?** Yes.

**How:**
- The poll loop detects `codes_printed > job.quantity` and raises an alert: "Printer printed more than requested. Possible external print."
- However, only `quantity` codes were reserved. The extra prints have no corresponding code records in the database.

**Gap:** The system alerts but has no corrective action. The extra prints are "phantom" — the printer physically printed something (possibly from the CSV data buffer looping around), but the app has no codes to track them against. These phantom prints may contain duplicate code values if the printer's data buffer wraps.

**Severity:** Medium. This would be a firmware bug and should be investigated immediately. The alert ensures it doesn't go unnoticed.

---

## 4. Data & Storage Failures

### 4.1 Disk Full (Cannot Write to SQLite)

**What happens:** The disk containing `codeprintmanager.db` runs out of space. All database writes fail.

**Handled?** Partially.

**How:**
- Poll loops catch exceptions on `SaveChangesAsync` and raise critical alerts.
- All active jobs effectively stall — they keep polling but can't persist progress.

**Gap:** This is a system-level failure with no graceful degradation. If codes are physically printed but the DB write fails, the `codes_confirmed` count in the database falls behind the printer's actual counter. On recovery, the `SPGGTP` cross-check reconciles, but any codes printed during the disk-full period are in an ambiguous state.

**Severity:** High. No backup mechanism, no disk-space monitoring, no advance warning. The app should monitor available disk space and alert before it becomes critical.

### 4.2 SQLite Database Corruption

**What happens:** The database file becomes corrupted — incomplete WAL checkpoint, disk error, antivirus interference, or the app being forcibly terminated during a write.

**Handled?** No.

**Gap:** There is no database backup mechanism, no integrity checking on startup, and no export/import facility. If the database is corrupted, ALL code tracking history is lost. For government-compliance codes, this could be a serious regulatory problem.

**Severity:** High. Mitigation should include:
- Periodic automatic backups of the `.db` file
- `PRAGMA integrity_check` on startup
- A manual export function for audit data

### 4.3 Duplicate Codes in CSV Import

**What happens:** An operator tries to import a CSV file containing codes that already exist in the system (in any product, in any status).

**Handled?** Yes.

**How:**
- Global `UNIQUE(code_text)` constraint on the `codes` table.
- `CodePoolService.ImportCodesAsync` validates each code against the entire database before insertion.
- Duplicate codes are rejected with a clear error message listing the duplicates.
- Valid codes in the same file are still imported (partial success).

**Residual risk:** None for data integrity. The operator simply re-downloads a corrected CSV.

### 4.4 SPPL Forbidden Characters in Code Values

**What happens:** A government-issued code contains characters that are part of the SPPL protocol syntax (`^`, `~gt~`, `~sc~`, `~`). Sending such a code in an `SPLCDF` command would corrupt the command structure.

**Handled?** Yes.

**How:**
- Validated at import time by `CodeValidator` in Domain. Codes containing forbidden sequences are rejected.
- The adapter also validates as defense-in-depth before uploading.

**Residual risk:** None, assuming the government codes don't legitimately require these character sequences (which would be extremely unusual for Data Matrix / GS1 codes).

### 4.5 Large CSV Import Performance

**What happens:** The operator imports a very large CSV file (100,000+ codes). The import runs on the UI thread and freezes the application.

**Handled?** No (documented gap).

**Gap:** No progress feedback during large imports. The UI freezes until the import completes. No background task or cancellation support.

**Severity:** Low-Medium. Typical imports are in the 1,000–10,000 range (fast). But a 100k+ import could freeze the UI for several seconds, causing the operator to think the app has crashed.

---

## 5. Application Crash & Unexpected Shutdown

### 5.1 App Crash Mid-Print

**What happens:** The application process terminates unexpectedly while one or more print jobs are running. Jobs remain in "Printing" status in the database.

**Handled?** Yes.

**How:**
1. On next startup, `RunStartupRecoveryAsync` queries all stale jobs (status: Preparing, Ready, or Printing).
2. Preparing/Ready jobs: auto-cancelled, reserved codes returned to pool.
3. Printing jobs: reads `SPGGTP` from each printer, computes discrepancy vs `TotalBaseline + CodesConfirmed`.
4. Presents Recovery Dialog with per-job Resume/Abort options.
5. Resume: re-uploads remaining unprinted codes, continues job.
6. Abort: quarantines ambiguous code (+1 after last confirmed), returns rest to pool.

**Residual risk:** If the printer is also offline during recovery, the job is marked "pending manual recovery" and the operator must wait for the printer to come back online.

### 5.2 App Crash During Prepare Step

**What happens:** The app crashes while uploading a CSV or template to the printer. The printer may have received partial data.

**Handled?** Yes.

**How:**
- On restart, the stale job (status: Preparing) is auto-cancelled.
- All reserved codes are returned to the Available pool.
- On the next Prepare attempt, the old CSV is deleted first (`SPLDDF`, ignore FAIL if not present) before uploading a fresh one.

**Residual risk:** None. The Prepare flow is idempotent by design.

### 5.3 Windows Update / Forced Restart

**What happens:** Windows forces a restart for updates while the app is running with active print jobs.

**Handled?** Same as crash recovery (§5.1).

**Gap:** The app has no mechanism to detect an impending shutdown and cleanly pause jobs. It relies entirely on post-crash recovery. Windows could be configured to delay updates, but the app itself doesn't request this.

**Severity:** Low. The recovery flow handles it correctly.

### 5.4 Out of Memory

**What happens:** The application runs out of memory — very large audit log in memory, massive CSV import, or memory leak over time.

**Handled?** No explicit handling.

**Gap:** The alert system caps at 50 alerts in memory. But other collections (job history, product tree, code pools) grow with data. No memory monitoring or pressure relief.

**Severity:** Very low. A WPF desktop app on a modern machine with typical usage (10–30 products, a few printers) will use negligible memory.

---

## 6. Concurrency & Race Conditions

### 6.1 Two Jobs on Same Printer

**What happens:** Two jobs are somehow created targeting the same printer. Loading template B destroys job A's data buffer, resets the counter, and interleaves SPPL responses.

**Handled?** Yes. Triple-layered defense.

**How:**
1. **UI guard:** New Job screen disables (greys out) printers with active jobs.
2. **Service guard:** `PrintJobService.CreateJobAsync` checks for existing active jobs.
3. **DB constraint:** Partial unique index `idx_one_active_job_per_printer` — even if 1 and 2 fail, the database INSERT throws.

**Residual risk:** None. This invariant cannot be violated.

### 6.2 Two Jobs from Same Product

**What happens:** Two jobs reserve codes from the same product's pool concurrently, potentially reserving the same codes.

**Handled?** Yes. Same triple-layered defense as §6.1.

**How:** Partial unique index `idx_one_active_job_per_product` prevents this at the database level.

**Residual risk:** None.

### 6.3 Cancel Interleaving with Prepare

**What happens:** The operator clicks Cancel while Prepare is still in progress (uploading CSV, checking templates). Without synchronization, Cancel might return codes to the pool while Prepare is still referencing them.

**Handled?** Yes.

**How:** Per-printer `SemaphoreSlim` in `PrintJobService`. Cancel acquires the lock, waits for Prepare to finish, then proceeds. The documented sequence:

```
Thread A (Prepare): acquires lock → SPLDDF → SPLCDF → SPLGSD → releases lock
Thread B (Cancel):  blocks until Prepare finishes → then cancels cleanly
```

**Residual risk:** None.

### 6.4 Reconnect Racing with In-Flight Commands

**What happens:** The TCP connection dies mid-command. `PrinterConnectionManager` tries to reconnect at the same time the poll loop tries to send a command.

**Handled?** Yes.

**How:** Single `SemaphoreSlim` in `SavemaTtoAdapter` governs both `ConnectAsync` and `SendCommandAsync`. They cannot run concurrently:
- `ConnectAsync` acquires lock → disposes dead socket → creates new one → releases lock.
- `SendCommandAsync` acquires lock → writes/reads on stream → releases lock.
- If a command throws `IOException`, it releases the lock, and `ConnectAsync` can then acquire it.

**Residual risk:** None. This is a clean design.

### 6.5 Poll Loop vs Cancel Race

**What happens:** Cancel signals the poll loop to stop (`_cts.Cancel()`), but the poll loop is mid-read on the adapter (holding the adapter lock). Cancel then tries to read the final counter, deadlocking on the adapter lock.

**Handled?** Yes.

**How:** Cancel calls `_cts.Cancel()` then `await _pollTask`. This ensures the poll loop exits and releases the adapter lock before Cancel attempts its own adapter calls.

**Residual risk:** None.

### 6.6 SQLite Write Contention

**What happens:** Two `JobExecutor` instances both try to write to the database at the same instant — updating different code rows and job records.

**Handled?** Yes.

**How:**
- SQLite WAL mode allows concurrent reads while one writer writes.
- `PRAGMA busy_timeout=5000` — if a write is in progress, the second writer waits up to 5 seconds.
- Job executors write to different rows (different code IDs, different job IDs), so there's no logical conflict.
- At 500ms poll intervals, the probability of exact collision is low, and writes are fast (~1ms).

**Residual risk:** None under normal operation. Under extreme load (many printers all polling simultaneously), worst case is a brief delay, not data loss.

---

## 7. SPPL Protocol Issues

### 7.1 Template Load Failure (SPLLTF Returns FAIL)

**What happens:** The `SPLLTF{name.rox}` command fails. This happens if the printer isn't in WAITING state, the template doesn't exist in storage, or the template is corrupt.

**Handled?** Yes.

**How:**
- Job transitions to "error" status.
- Codes stay in "reserved" status (nothing was printed yet).
- UI shows: "Template load failed. Printer state: {state}" with **[Retry]** and **[Cancel Job]** buttons.
- Retry re-checks `SPPSTA == WAITING`, then re-attempts `SPLLTF`.
- Cancel returns all reserved codes to Available (no quarantine needed — nothing was printed).

**Residual risk:** None.

### 7.2 CSV Upload Failure (SPLCDF Returns FAIL)

**What happens:** The CSV data upload command fails. Possible causes: printer not in WAITING state, storage full, filename conflict.

**Handled?** Yes.

**How:**
- The Prepare step deletes the old CSV first (`SPLDDF`, ignore FAIL) then uploads fresh (`SPLCDF`, require OK).
- If `SPLCDF` returns FAIL, Prepare aborts with an error alert.
- Codes remain reserved until the operator retries or cancels.

**Residual risk:** The error message may not indicate the root cause (storage full vs. state issue vs. other).

### 7.3 Malformed SPPL Response

**What happens:** The printer sends a response that doesn't match expected patterns — truncated, extra whitespace, unexpected command name, missing `^` terminator.

**Handled?** Partially.

**How:**
- `SpplResponseParser` handles known patterns and the documented edge case of inconsistent whitespace (`~ SPGRES` vs `~SPGRES`).
- The parser trims whitespace between `~` and `SPGRES`.

**Gap:** Truly malformed responses (garbage bytes, truncated mid-response, binary data in a text response) would cause parse exceptions. These bubble up as unhandled exceptions in the adapter, which the poll loop catches as `IOException` equivalents. But the error message to the operator is generic ("Connection lost") rather than "Received malformed data from printer."

**Severity:** Low. Malformed responses are extremely rare with properly functioning printer firmware.

### 7.4 Command Timeout (Printer Doesn't Respond)

**What happens:** The printer accepts the TCP connection but doesn't respond to a command — firmware hang, internal error, or the printer is in a state where it can't process commands.

**Handled?** Partially.

**How:**
- `TcpClient.ReceiveTimeout = 5000ms` causes a `SocketException` after 5 seconds of no response.
- The poll loop treats this like an `IOException` — alert, wait, retry.

**Gap:** There is no per-command timeout at the SPPL level (e.g., "if SPLCDF hasn't responded in 10 seconds, abort"). The 5-second socket timeout is the only safeguard. For large template uploads (`SPLRTF` with a big `.rox` file encoded in base64), 5 seconds might not be enough — the printer may legitimately need more time to process.

**Severity:** Low-Medium. If template uploads regularly timeout, the `ReceiveTimeout` may need to be increased for specific commands.

### 7.5 BLOCKED State During Print

**What happens:** The operator enters the printer's local menu while a job is running. All commands except `SPPSTA` return FAIL.

**Handled?** Yes.

**How:**
- Poll commands return FAIL. The poll loop detects this, alerts: "Printer BLOCKED — operator not in main window."
- The poll loop retries on the next tick.
- Once the operator returns to the main screen, commands succeed again.

**Gap:** The printer continues printing while BLOCKED — the counter still advances. The app just can't read it. When the block clears, the next poll reads the current counter and catches up. No data is lost, but progress updates are delayed.

**Severity:** Low.

---

## 8. Operational & Human Error

### 8.1 Wrong Template Assigned to Product

**What happens:** The operator assigns the wrong `.rox` template file to a product. The printer prints the wrong layout, barcode format, or content for that product.

**Handled?** No.

**Gap:** The system has no way to validate template *content* against a product. It trusts the operator's template assignment. If the wrong template is used, physically incorrect barcodes are printed, codes are consumed, and the error is only caught by visual inspection on the production line.

**Severity:** High in terms of business impact (wasted codes, wasted product), but this is fundamentally a human error that can only be mitigated by operator training and Phase 2 scanner verification.

### 8.2 Changing Template While Job Exists

**What happens:** The operator changes a product's template in the Settings tab while a job using that product is active (Preparing, Ready, or Printing).

**Handled?** No (documented gap).

**Gap:** The system does not check for active jobs before allowing template changes. The in-progress job is unaffected (it already uploaded the old template to the printer), but the next job for this product will use the new template. This could be intentional or accidental.

**Severity:** Low. The in-progress job is safe. But it's confusing if the operator didn't intend the change.

### 8.3 Running Out of Codes

**What happens:** A product's code pool reaches zero available codes. The operator can't create new jobs for that product.

**Handled?** Yes.

**How:**
- Low stock alert at 500 remaining codes: "Product: only N codes remaining."
- [+ New Job] button is disabled when `AvailableCodesCount == 0`.
- Quantity field validation: cannot request more codes than available.

**Residual risk:** None for data integrity. The operator must import more codes.

### 8.4 Accidental Job Cancellation

**What happens:** The operator clicks Cancel on the wrong job, or cancels intentionally but regrets it.

**Handled?** Partially.

**How:**
- Codes already printed stay marked as Printed (irreversible, correct).
- Boundary codes after the last confirmed print are quarantined (per-printer `QuarantineMargin` setting, default 0).
- Remaining reserved codes are returned to Available.

**Gap:** There is no "undo cancel." The quarantined code is frozen until the operator resolves it (move to Available or Printed via the Codes tab). There is a confirmation step (button click), but no confirmation dialog specifically for Cancel.

**Severity:** Low. The cost is one quarantined code (recoverable after verification). The returned codes are immediately available for the next job.

### 8.5 Incorrect Recovery Decision

**What happens:** After a crash/power failure, the operator sees the Recovery Dialog and makes the wrong choice — aborting a job that should have been resumed, or resuming a job when the printer state is actually inconsistent.

**Handled?** Partially.

**How:**
- The Recovery Dialog shows detailed information: App says X codes, Printer says Y codes, Delta = Z.
- Per-job Resume/Abort options.

**Gap:** The operator must understand what the numbers mean. If they don't, they might abort a job (quarantining codes unnecessarily) or resume a job that has stale data on the printer. Resume re-uploads remaining codes, which should be safe, but if the printer's internal state is truly inconsistent (firmware bug), resuming could cause issues.

**Severity:** Medium. Training and clear UI messaging are the mitigations. The system errs on the side of safety (quarantine-on-ambiguity), so even wrong decisions tend to freeze codes rather than duplicate them.

---

## 9. System-Level / Environmental

### 9.1 Antivirus Interference

**What happens:** Antivirus software quarantines the SQLite database file, blocks TCP connections to port 9100, or flags the application as suspicious.

**Handled?** No.

**Gap:** No specific mitigation. The app should be whitelisted in the antivirus configuration during deployment.

**Severity:** Low (deployment concern).

### 9.2 Windows Sleep / Hibernate

**What happens:** The computer running the app enters sleep or hibernate mode. All TCP connections drop, timers stop, background tasks freeze.

**Handled?** Same as crash recovery on wake.

**Gap:** The app doesn't prevent sleep or request a sleep exemption. On wake, all connections are dead. The reconnect loops eventually restore them, and any interrupted jobs go through recovery. But the operator may not realize the machine slept.

**Severity:** Medium. The deployment machine should be configured to never sleep, but there's no enforcement from the app side.

### 9.3 Clock Skew / Time Change

**What happens:** The system clock changes (manual adjustment, NTP sync, daylight saving). Timestamps in the database and audit log become inconsistent.

**Handled?** No.

**Gap:** All timestamps use `DateTime.UtcNow` or `DateTime.Now` directly. A sudden clock jump could make audit log entries appear out of order. The auto-dismiss timer for Info alerts (30 seconds) uses `Task.Delay`, which is relative and unaffected.

**Severity:** Very low. Cosmetic issue only — no functional impact.

---

## 10. Summary: Risk Priority Matrix

| Priority | Problem | Impact | Likelihood | Current State |
|----------|---------|--------|------------|---------------|
| **Critical** | Database corruption / no backups (§4.2) | All tracking history lost | Low | Not handled |
| **Critical** | Disk full stops all jobs (§4.1) | All jobs stall, codes in limbo | Low | Partially handled |
| **High** | Production line jam — codes printed into air (§2.6) | Code waste, compliance gaps | Medium | Not detectable (Phase 2) |
| **High** | Wrong template assigned (§8.1) | Wasted codes + product | Low-Medium | Not handled |
| **Medium** | Network instability in factory (§1.3) | Increased quarantined codes | Medium | Partially handled |
| **Medium** | Printer storage full (§2.4) | Job preparation fails | Low | Partially handled |
| **Medium** | Incorrect recovery decision (§8.5) | Code waste or stale data | Low | Partially handled |
| **Medium** | Windows sleep/hibernate (§9.2) | Jobs interrupted | Low | Not handled |
| **Low** | Stale TCP socket detection (§1.4) | 5s delay in detection | Medium | Partially handled |
| **Low** | Counter decrease anomaly (§3.3) | Silent ignoring of potential issue | Very low | Not handled |
| **Low** | Large CSV import freezes UI (§4.5) | Poor UX | Low | Not handled |
| **Low** | Command timeout for large uploads (§7.4) | Template upload fails | Low | Partially handled |

---

## 11. Recommended Mitigations (Not Yet Implemented)

These are improvements that could be added to strengthen the system against the identified risks:

1. **Database backup** — Periodic automatic copy of `codeprintmanager.db` to a backup location. `PRAGMA integrity_check` on startup.

2. **Disk space monitoring** — Alert when available disk space drops below a threshold (e.g., 500 MB).

3. **Printer interface lock** — Send `SPGSLI{1}` at job start to lock the printer's physical buttons, preventing BLOCKED state and external printing. Unlock at job end.

4. **TCP keepalive** — Enable TCP keepalive on the `TcpClient` to detect silently broken connections faster.

5. **Counter decrease detection** — Log a warning if `SPGGCP` returns a value lower than the previous reading.

6. **Connection error diagnostics** — Distinguish "host unreachable" from "connection refused" from "timeout" in the UI.

7. **Storage space check** — After connecting to a printer, query stored files and warn if the count is unusually high.

8. **Sleep prevention** — Call `SetThreadExecutionState` (Win32 API) to prevent system sleep while jobs are active.

9. **Active-job guard on template change** — Block template/CSV-name changes in the Settings tab when the product has an active job.

10. **Cancel confirmation dialog** — Add a MessageBox confirmation before cancelling a running print job.
