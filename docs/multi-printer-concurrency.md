# Multi-Printer Concurrency: Deep Analysis

## 1. The Design Constraint and What It Eliminates

**Rule: one product → one template → one printer.**
Want two printers for a product? Create two products with separate code pools.

This is a brilliantly simplifying constraint. It means:
- **No shared code pools** — two jobs never compete for the same codes
- **No shared printers** — two jobs never fight over the same TCP connection during normal operation
- **No shared templates/CSVs** — each job's printer has its own files in its own storage
- **No coordination between jobs** — Job A knows nothing about Job B

Every job is a fully independent state machine. The only shared resources are **SQLite** (writes to different rows) and the **UI thread** (needs marshaling). The problem reduces to: *run N independent jobs in parallel without them stepping on each other's infrastructure*.

---

## 2. Invariants That Must Be Enforced

These are the hard rules that prevent chaos:

| Invariant | Why | Enforcement |
|-----------|-----|-------------|
| **Max one active job per printer** | Loading template B destroys job A's buffer, resets counter. Two command streams interleave responses. | DB constraint + UI guard |
| **Max one active job per product** | Two jobs reserving from the same pool = duplicate codes. | DB constraint + UI guard |
| **Multi-step operations on a printer are exclusive** | Prepare uploads CSV, Cancel stops printing, Cleanup deletes files. If two overlap on the same printer, they corrupt each other's state. | Per-printer operation lock (service-level SemaphoreSlim) |
| **Individual commands to a printer are serialized** | SPPL is request-response over TCP. Concurrent sends = garbled responses. | Per-adapter SemaphoreSlim (lower level) |
| **Job execution lives in service layer, not ViewModel** | User navigates away from Print screen → job must keep running. | Service-owned Task, ViewModel subscribes to events |

### Database-level enforcement (defense in depth)

```sql
-- At most one non-terminal job per printer
CREATE UNIQUE INDEX idx_one_active_job_per_printer 
  ON print_jobs(printer_id) 
  WHERE status IN ('preparing', 'ready', 'printing');

-- At most one non-terminal job per product  
CREATE UNIQUE INDEX idx_one_active_job_per_product 
  ON print_jobs(product_id) 
  WHERE status IN ('preparing', 'ready', 'printing');
```

SQLite supports partial indexes. Any attempt to insert a second active job for the same printer or product fails at the DB level — even if application logic has a bug. This is the safety net.

---

## 3. Connection Layer

### Architecture

```
PrinterConnectionManager (singleton, lives for app lifetime)
├── Printer 1: SavemaTtoAdapter (owns TcpClient, SemaphoreSlim)
├── Printer 2: SavemaTtoAdapter (owns TcpClient, SemaphoreSlim)
└── Printer 3: SavemaTtoAdapter (owns TcpClient, SemaphoreSlim)
```

Each `SavemaTtoAdapter` instance:
- **Owns one TCP connection** to its printer
- **Has a `SemaphoreSlim(1,1)`** — all commands go through it sequentially
- **Is shared** between the polling loop, manual commands (Verify, Storage Cleanup), and job control (Start/Stop)
- **Handles its own reconnection** — independent backoff timer per printer

### Connection lifecycle

| Event | Behavior |
|-------|----------|
| App startup | App is **immediately usable**. Each printer starts its own background connect task (fire-and-forget). Printers show as `Connecting...` in UI until resolved. |
| Connect succeeds | Mark Idle. Printer data (status, files, counters) becomes available. If stale job exists → enter recovery flow. |
| Connect fails | Mark Offline. No retry block — just schedule next attempt with exponential backoff (1s → 2s → 4s → ... → 30s cap). Printer shows as `Offline` in UI. App and other printers are unaffected. |
| Printer added by user | Start background connect task. Same as above. |
| Connection lost mid-use | Mark Offline. If job active → pause job, alert. Start reconnect backoff. |
| Reconnect succeeds | Mark Idle. If paused job exists → enter recovery flow (§3.4 in phase1-design). |
| App shutdown | Disconnect all. Jobs in `printing` state stay in DB for recovery on next launch. |

### Adapter internals: TCP lifecycle and reconnection

**Core rule: the adapter is a stable handle.** One instance per printer, lives for the app's lifetime. All callers (JobExecutor, Verify, Storage Cleanup) hold the same reference. Nobody ever gets a "new" adapter — the adapter swaps its own TCP internals.

**One lock governs everything.** Both commands and connection changes go through the same `SemaphoreSlim`. This makes it impossible for a command to run on a half-swapped stream, or for a reconnect to happen mid-command.

```csharp
class SavemaTtoAdapter : IPrinterAdapter, IDisposable
{
    private readonly SemaphoreSlim _lock = new(1, 1);
    private TcpClient? _client;
    private NetworkStream? _stream;

    public bool IsConnected => _client?.Connected == true;

    // --- Connection (called only by ConnectionManager) ---

    public async Task<bool> ConnectAsync(string host, int port, CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);          // blocks until any in-flight command finishes
        try
        {
            DisposeSocket();                 // clean up dead connection if any
            _client = new TcpClient();
            _client.ReceiveTimeout = 5000;   // 5s read timeout
            _client.SendTimeout = 5000;
            await _client.ConnectAsync(host, port, ct);
            _stream = _client.GetStream();
            return true;
        }
        catch
        {
            DisposeSocket();
            return false;
        }
        finally { _lock.Release(); }
    }

    public async Task DisconnectAsync()
    {
        await _lock.WaitAsync();
        try { DisposeSocket(); }
        finally { _lock.Release(); }
    }

    // --- Commands (called by anyone) ---

    private async Task<SpplResponse> SendCommandAsync(string cmd, CancellationToken ct)
    {
        await _lock.WaitAsync(ct);
        try
        {
            if (_stream == null)
                throw new PrinterOfflineException("Not connected");

            await _stream.WriteAsync(Encode(cmd), ct);
            return await ReadResponseAsync(ct);
        }
        // IOException: TCP died during this command.
        // Don't dispose here — let ConnectionManager do it via ConnectAsync.
        // Just rethrow so the caller (poll loop) knows it failed.
        finally { _lock.Release(); }
    }

    // --- Cleanup ---

    private void DisposeSocket()
    {
        _stream?.Dispose(); _stream = null;
        _client?.Dispose(); _client = null;
    }

    public void Dispose() => DisposeSocket();
}
```

**Why one lock for both connect and commands:**
- `ConnectAsync` acquires `_lock` → no command can run while stream is being swapped
- `SendCommandAsync` acquires `_lock` → no reconnect can happen mid-read/write
- If a command throws `IOException`, it releases `_lock` → ConnectionManager's next `ConnectAsync` can acquire it
- No double-dispose race: only `ConnectAsync` and `Dispose` call `DisposeSocket`, both under `_lock`

**The reconnection flow end-to-end:**

```
1. PollLoop calls SendCommandAsync("~SPGGCP^")
2. _lock acquired → _stream.WriteAsync succeeds → ReadResponseAsync throws IOException (TCP dead)
3. _lock released → IOException propagates to PollLoop
4. PollLoop catches IOException → RaiseAlert("Connection lost") → await Task.Delay(2000)
5. Meanwhile, ConnectionManager detects IsConnected == false
6. ConnectionManager calls adapter.ConnectAsync(host, port)
7. _lock acquired → DisposeSocket() cleans up dead client → new TcpClient created → _lock released
8. PollLoop wakes up, calls SendCommandAsync("~SPGGCP^") again
9. _lock acquired → _stream is alive → command succeeds → business as usual
```

**Key property:** JobExecutor never knows reconnection happened. It just sees "IOException, wait, retry, success." The adapter reference it holds is the same one from the start. No re-binding, no events, no handoff.

### Two-level locking model

There are two distinct concurrency problems, solved by two distinct locks:

```
Level 1: SERVICE LOCK — per printer, in PrintJobService
  SemaphoreSlim(1,1) per printer_id
  Protects: multi-step operation sequences (Prepare, Start, Cancel, Verify, Cleanup)
  Prevents: Cancel interleaving with Prepare, Cleanup deleting files mid-upload
  Held for: seconds (duration of a full operation)
  
Level 2: ADAPTER LOCK — per printer, in SavemaTtoAdapter  
  SemaphoreSlim(1,1) per adapter instance
  Protects: individual SPPL request-response pairs
  Prevents: two commands sent before first response received
  Held for: milliseconds (one command round-trip)
  
The polling loop does NOT acquire the service lock — it only uses the adapter lock.
This is safe because polling is read-only (SPGGCP/SPGGTP) and doesn't mutate printer state.
```

**Why two levels?** The adapter lock alone is insufficient. Consider:

```
Without service lock:
  Thread A (Prepare):  SPLDDF → SPLCDF(job47codes) → ... (still going)
  Thread B (Cancel):   [changes DB, returns codes to available]
  Thread A (Prepare):  SPLGSD → sees file → UPDATE status=ready
  Result: Job 47 is "ready" with codes that are back in the available pool.
         If job 48 reserves those same codes → duplicates on two printers.

With service lock:
  Thread A (Prepare):  acquires lock → SPLDDF → SPLCDF → SPLGSD → releases lock
  Thread B (Cancel):   blocks until Prepare finishes → then cancels cleanly
  Result: Cancel sees the job is already "ready" and proceeds with normal cancel flow.
```

**Implementation:**

```csharp
class PrintJobService
{
    // One lock per printer — created on first use, lives for app lifetime
    private readonly ConcurrentDictionary<int, SemaphoreSlim> _printerLocks = new();
    
    private SemaphoreSlim GetPrinterLock(int printerId)
        => _printerLocks.GetOrAdd(printerId, _ => new SemaphoreSlim(1, 1));
    
    public async Task PrepareJobAsync(int jobId, CancellationToken ct)
    {
        var job = await _db.PrintJobs.FindAsync(jobId);
        var printerLock = GetPrinterLock(job.PrinterId);
        
        await printerLock.WaitAsync(ct);
        try
        {
            // All SPPL commands run inside the lock
            await _adapter.DeleteCsvAsync(job.CsvFilename);        // SPLDDF
            ct.ThrowIfCancellationRequested();
            await _adapter.UploadCsvAsync(job.CsvFilename, codes); // SPLCDF
            ct.ThrowIfCancellationRequested();
            await _adapter.VerifyCsvExistsAsync(job.CsvFilename);  // SPLGSD
            // ... template check ...
            
            job.Status = "ready";
            await _db.SaveChangesAsync();
        }
        catch (OperationCanceledException)
        {
            // Prepare was interrupted — clean up
            // Codes are still reserved (not returned yet — Cancel handler does that)
            job.Status = "cancelled";
            await _db.SaveChangesAsync();
            throw;
        }
        finally
        {
            printerLock.Release();
        }
    }
    
    public async Task CancelJobAsync(int jobId)
    {
        var job = await _db.PrintJobs.FindAsync(jobId);
        var printerLock = GetPrinterLock(job.PrinterId);
        
        await printerLock.WaitAsync();  // waits for Prepare/Start to finish first
        try
        {
            // Now safe — no other operation is touching this printer
            if (job.Status == "printing")
            {
                _activeJobs[jobId].Cancel();   // signal poll loop
                await _activeJobs[jobId].WaitAsync();  // wait for loop to exit
                await _adapter.StopPrintAsync();
                var finalCounter = await _adapter.GetCurrentCounterAsync();
                MarkCodesAfterCancel(job, finalCounter);
            }
            else  // preparing or ready
            {
                ReturnAllReservedCodes(job);
            }
            
            job.Status = "cancelled";
            await _db.SaveChangesAsync();
        }
        finally
        {
            printerLock.Release();
        }
    }
}
```

**Polling loop exemption:** The polling loop (`JobExecutor.PollLoopAsync`) intentionally does NOT acquire the service lock. It only reads counters (SPGGCP, SPGGTP) via the adapter lock. This is safe because:
- Counter reads don't mutate printer state
- The adapter lock serializes them with any concurrent commands from Prepare/Cancel
- If Cancel is waiting on the service lock while polling reads a counter, that's fine — the counter read completes, Cancel acquires the lock, then stops the poll loop

---

## 4. Job Execution Model

### Who owns what

```
PrintJobService (singleton)
├── CreateJobAsync(productId, printerId, quantity)  → returns Job
├── PrepareJobAsync(jobId)                          → uploads CSV, verifies
├── StartJobAsync(jobId)                            → loads template, starts print, spawns monitor
├── CancelJobAsync(jobId)                           → stops printer, burns, cleans up
│
├── _activeJobs: Dictionary<int, JobExecutor>       → key: job.Id
│
JobExecutor (one per running job)
├── _job: PrintJob                                  → DB entity
├── _adapter: IPrinterAdapter                       → from ConnectionManager (shared ref)
├── _cts: CancellationTokenSource                   → for stopping the poll loop
├── _pollTask: Task                                 → the async polling loop
├── event ProgressChanged(int confirmed, int total)  → UI subscribes
├── event JobCompleted(JobResult)
├── event AlertRaised(Alert)
```

### Lifecycle

```
CreateJobAsync()
  → INSERT print_job (status=preparing)
  → Reserve codes (status=reserved, job_id set)
  → Return job

PrepareJobAsync()
  → Acquire printer service lock              ← prevents overlap with Cancel/Verify/Cleanup
  → Guard: printer has no other active job (DB check)
  → Guard: printer status == WAITING (SPPSTA check)
  → SPLDDF old CSV (ignore FAIL)
  → ct.ThrowIfCancellationRequested()          ← check between each step
  → SPLCDF new CSV (require OK)
  → ct.ThrowIfCancellationRequested()
  → SPLGSD confirm file exists
  → SPLGST check template exists, upload if missing
  → UPDATE job status=ready
  → Release printer service lock

StartJobAsync()
  → Guard: job.status == ready
  → SPLLTF (resets counter, loads buffer)
  → SPGGTP → store as total_baseline
  → SPPSLQ{quantity}
  → SPPSAP
  → UPDATE job status=printing, started_at=now
  → Create JobExecutor, store in _activeJobs
  → JobExecutor.Start() → spawns _pollTask

CancelJobAsync()
  → Acquire printer service lock              ← waits for Prepare to finish if in-flight
  → If printing: signal _cts.Cancel(), await _pollTask
  → If printing: SPPSTP (stop printer), read final SPGGCP
  → Mark remaining codes: printed up to counter, burn +1, return rest
  → If preparing/ready: return all reserved codes to available
  → UPDATE job status=cancelled
  → Remove from _activeJobs
  → Release printer service lock
```

### Polling loop (inside JobExecutor)

Each poll iteration is a pipeline of discrete steps. The loop itself only orchestrates — each responsibility is a separate method.

```csharp
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
            RaiseAlert("Connection lost during print job");
            await Task.Delay(2000, ct);
        }
    }
}
```

**Step methods** — each does exactly one thing:

```csharp
// 1. Read: gather raw data from printer
private async Task<PollSnapshot> ReadCountersAsync(CancellationToken ct)
{
    var counter = await _adapter.GetCurrentCounterAsync();   // SPGGCP
    int? lifetimeDelta = null;
    
    if (++_crossCheckTick % 5 == 0)
    {
        var lifetime = await _adapter.GetTotalCounterAsync();  // SPGGTP
        lifetimeDelta = lifetime - _job.TotalBaseline;
    }
    
    return new PollSnapshot(counter, lifetimeDelta);
}

// 2. Detect: compare snapshot against expectations (pure logic, no side effects)
private void DetectAnomalies(PollSnapshot snapshot)
{
    if (snapshot.LifetimeDelta.HasValue && snapshot.LifetimeDelta != snapshot.Counter)
        RaiseAlert($"Counter mismatch: SPGGCP={snapshot.Counter}, SPGGTP delta={snapshot.LifetimeDelta}");
    
    var advance = snapshot.Counter - _previousCounter;
    if (advance > _maxExpectedAdvance)
        RaiseAlert($"Unexpected counter jump (+{advance})");
}

// 3. Commit: persist new prints to DB + notify UI
private async Task CommitProgressAsync(PollSnapshot snapshot)
{
    await MarkCodesPrintedAsync(_job.CodesConfirmed, snapshot.Counter);
    _job.CodesConfirmed = snapshot.Counter;
    await _db.SaveChangesAsync();
    _progress.Report(new JobProgressUpdate(snapshot.Counter, _job.Quantity));
}

// 4. Complete: finalize job
private async Task CompleteJobAsync()
{
    _job.Status = "completed";
    _job.CompletedAt = DateTime.UtcNow;
    await _db.SaveChangesAsync();
    _progress.Report(new JobCompletedUpdate(_job.Id));
}

private record PollSnapshot(int Counter, int? LifetimeDelta);
```

Key properties:
- **Each job has its own loop** — Printer 1 being slow doesn't delay Printer 2's updates
- **CancellationToken** — clean shutdown on Cancel or app exit
- **IOException handling** — connection loss pauses this job, others unaffected
- **DB writes are short** — update a few rows, save, release. No long transactions.

---

## 5. Cancellation Sequence (Detailed)

This is where races live. The sequence must be airtight:

```
User clicks Cancel on Job #47 (Printer 1)
│
├─ 1. PrintJobService.CancelJobAsync(47)
│     ├─ Acquire per-printer lock (SemaphoreSlim in service layer, NOT the adapter lock)
│     ├─ _cts.Cancel()                          → signals poll loop to stop
│     ├─ await _pollTask                        → waits for current poll iteration to finish
│     │                                          (poll loop catches OperationCanceledException, returns)
│     ├─ Poll loop is now DEAD. No more adapter calls from it.
│     │
│     ├─ var finalCounter = await adapter.GetCurrentCounterAsync()   → one last read
│     ├─ await adapter.StopPrintAsync()                              → SPPSTP
│     │
│     ├─ Mark codes [0..finalCounter-1] as printed (if not already)
│     ├─ Mark code [finalCounter] as burned (+1 safety)
│     ├─ Mark codes [finalCounter+1..quantity-1] as returned → available
│     │
│     ├─ job.status = "cancelled"
│     ├─ job.completed_at = now
│     ├─ Save to DB
│     ├─ Remove from _activeJobs
│     └─ Release per-printer lock
│
└─ Meanwhile: Job #48 on Printer 2 continues polling, completely unaware.
```

**Why await `_pollTask` before reading final counter?**
If we don't, the poll loop might be mid-read (adapter lock held). Our cancel code tries to read → deadlock on adapter SemaphoreSlim. By awaiting `_pollTask` first, we guarantee the loop is done and the adapter lock is free.

---

## 6. SQLite Under Concurrency

### Configuration

```csharp
// In DbContext configuration
options.UseSqlite(connectionString, o => {
    // WAL mode: allows concurrent reads while one writer writes
    // Critical for multi-job polling
});

// On first connection:
// PRAGMA journal_mode=WAL;
// PRAGMA busy_timeout=5000;  -- wait up to 5s if another write is in progress
```

### Write patterns

| Writer | What it writes | Frequency |
|--------|---------------|-----------|
| Job 1 poll loop | `UPDATE codes SET status='printed' WHERE id IN (...)` + `UPDATE print_jobs SET codes_confirmed=N` | Every 500ms (only if counter changed) |
| Job 2 poll loop | Same, but different code IDs, different job ID | Every 500ms |
| User imports CSV | `INSERT INTO codes (...)` — different product_id | Rare, manual |
| Audit logger | `INSERT INTO audit_log (...)` | On events |

**Conflict analysis:**
- Job 1 and Job 2 never touch the same rows (different products, different code IDs, different job IDs)
- WAL mode allows Job 2's read to proceed while Job 1 is writing
- Worst case: two jobs' polls both trigger writes at the same instant → one waits up to `busy_timeout` → succeeds
- At 500ms poll intervals, the probability of exact collision is low, and the writes are fast (~1ms)

**Conclusion: SQLite WAL is sufficient. No need for a heavier database.**

---

## 7. UI Architecture

### Core principle: Job execution is decoupled from UI navigation

```
SERVICE LAYER (always running)          UI LAYER (view-dependent)
┌──────────────────────────────────┐   ┌──────────────────────────┐
│ PrintJobService                  │   │ PrintViewModel           │
│  ├ _activeJobs:                  │   │  - Shows ONE job         │
│  │  Job#47 executor ─────────────┼──→│  - Progress bar          │
│  │  Job#48 executor ─────────┐   │   │  - Cancel button         │
│  │  Job#49 executor ──────┐  │   │   └──────────────────────────┘
│  └────────────────────────┼──┼───│
└───────────────────────────┼──┼───┘   ┌──────────────────────────┐
                            │  └──────→│ DashboardViewModel       │
                            │          │  - Shows ALL printers    │
                            └─────────→│  - Mini progress each    │
                                       └──────────────────────────┘
```

- User creates Job #47 on Print screen → `PrintJobService` starts the executor
- User navigates to Dashboard → Print screen ViewModel is disposed, **job keeps running**
- Dashboard ViewModel subscribes to all active executors → shows progress for each printer card
- User navigates back to Print screen → ViewModel reconnects to the active job (or shows "create new")

### Dashboard with multiple active jobs

```
┌──────────────────────────────────────────────────────────────────┐
│  DASHBOARD                                                        │
├──────────────────────────────────────────────────────────────────┤
│                                                                    │
│  PRINTERS                                                         │
│  ┌─────────────────────────────┐  ┌─────────────────────────────┐│
│  │ Savema-Line1                │  │ Savema-Line2                ││
│  │ 192.168.1.10  ● PRINTING   │  │ 192.168.1.11  ● PRINTING   ││
│  │ Job #47: Apple 0.5L        │  │ Job #48: Orange 0.33L       ││
│  │ Progress: 342/500 (68%)    │  │ Progress: 1205/2000 (60%)   ││
│  │ ████████████░░░░░          │  │ ██████████░░░░░░░           ││
│  └─────────────────────────────┘  └─────────────────────────────┘│
│  ┌─────────────────────────────┐                                  │
│  │ Savema-Line3                │                                  │
│  │ 192.168.1.12  ● IDLE       │                                  │
│  │                             │                                  │
│  └─────────────────────────────┘                                  │
│                                                                    │
│  ALERTS                                                           │
│  ┌──────────────────────────────────────────────────────────────┐│
│  │ 14:32  ⚠️  Savema-Line1: Unexpected counter jump (+7)       ││
│  │ 14:30  ✅  Savema-Line2: Job #48 started (2000 codes)       ││
│  │ 14:28  ✅  Savema-Line1: Job #47 started (500 codes)        ││
│  └──────────────────────────────────────────────────────────────┘│
└──────────────────────────────────────────────────────────────────┘
```

Each printer card is a ViewModel bound to one printer + its active job (if any). Updates come from the service layer via events, marshaled to UI thread.

### Print screen behavior with multiple jobs

```
┌──────────────────────────────────────────────────────────────────┐
│  PRINT JOB                                                        │
├──────────────────────────────────────────────────────────────────┤
│                                                                    │
│  ┌─── Active Jobs ────────────────────────────────────────────┐  │
│  │ #47  Apple 0.5L → Line1    342/500  (68%)  [View] [Cancel]│  │
│  │ #48  Orange 0.33L → Line2  1205/2000 (60%) [View] [Cancel]│  │
│  └────────────────────────────────────────────────────────────┘  │
│                                                                    │
│  ─── New Job ─────────────────────────────────────────────────   │
│  Product:   [ Water Still 0.5L    ▼ ]   (5,000 codes available)  │
│  Printer:   [ Savema-Line3        ▼ ]   (● idle)                 │
│  Quantity:  [ 1000                  ]                             │
│                                                                    │
│              [Prepare]                                             │
└──────────────────────────────────────────────────────────────────┘
```

Key behaviors:
- **Active jobs panel** at top — always visible, shows all running jobs with mini-progress
- **New job form** below — only shows printers/products **without** active jobs in dropdowns
- **[View]** — expands to the full progress view (progress bar, detailed counters, Stop/Cancel)
- **Printers with active jobs are disabled** in the dropdown (greyed out, "(printing Job #47)")
- **Products with active jobs are disabled** in the dropdown

### Thread marshaling

All background → UI updates use `IProgress<T>`. It auto-marshals to the UI thread via `SynchronizationContext` — no manual `Dispatcher.Invoke` calls anywhere.

```csharp
// Created on UI thread, captures SynchronizationContext
var progress = new Progress<JobProgressUpdate>(update => {
    printerCard.Progress = update.Confirmed;  // already on UI thread
    printerCard.Total = update.Total;
    printerCard.Percent = (double)update.Confirmed / update.Total * 100;
});

// Passed to JobExecutor at construction, called from polling loop
_progress.Report(new JobProgressUpdate(counter, quantity));
```

---

## 8. Error Isolation

| Failure | Affected | Unaffected | Behavior |
|---------|----------|------------|----------|
| Printer 1 disconnects | Job #47 | Jobs #48, #49 | Job #47 pauses. Alert fires with printer name. Reconnect backoff starts for Printer 1 only. |
| Printer 2 returns BLOCKED | Job #48 polls return FAIL | Jobs #47, #49 | Job #48 logs warning, retries next poll. Alert: "Printer 2 BLOCKED — operator not in main window." |
| Printer 3 has ERROR (ribbon) | Job #49 status check returns ERROR | Jobs #47, #48 | Job #49 pauses. Alert with error message. |
| SQLite write fails (disk full) | All jobs | None | All poll loops catch exception, raise critical alert, pause. This is a system-level failure. |
| App crash | All jobs | N/A | All jobs persist in DB as `printing`. On restart, recovery flow runs for each. |

---

## 9. Power Failure Recovery with Multiple Jobs

On app startup:

```csharp
var staleJobs = db.PrintJobs
    .Where(j => j.Status == "printing" || j.Status == "preparing" || j.Status == "ready")
    .Include(j => j.Printer)
    .Include(j => j.Product)
    .ToList();

foreach (var job in staleJobs)
{
    if (job.Status == "preparing" || job.Status == "ready")
    {
        // Never started printing — safe to return all reserved codes
        ReturnReservedCodes(job);
        job.Status = "cancelled";
        continue;
    }
    
    // Status was "printing" — need to determine how many actually printed
    var adapter = connectionManager.GetAdapter(job.PrinterId);
    if (!await adapter.ConnectAsync(job.Printer.IpAddress, job.Printer.Port))
    {
        // Printer unreachable — mark for manual recovery
        pendingRecovery.Add(job);
        continue;
    }
    
    var currentLifetime = await adapter.GetTotalCounterAsync();  // SPGGTP
    var printedBeforeFailure = currentLifetime - job.TotalBaseline;
    
    // Present to operator per-job
    recoveryItems.Add(new RecoveryItem {
        Job = job,
        ConfirmedByApp = job.CodesConfirmed,
        ConfirmedByPrinter = printedBeforeFailure,
        Discrepancy = printedBeforeFailure - job.CodesConfirmed
    });
}

// Show recovery dialog with ALL stale jobs at once
ShowRecoveryDialog(recoveryItems);
```

Recovery dialog shows **all** jobs in a table:

```
┌──────────────────────────────────────────────────────────────────┐
│  RECOVERY — Jobs interrupted by shutdown                          │
├──────────────────────────────────────────────────────────────────┤
│                                                                    │
│  Job   Product         Printer    App says  Printer says  Delta  │
│  #47   Apple 0.5L      Line1      342       345           +3     │
│  #48   Orange 0.33L    Line2      1205      1205          0      │
│                                                                    │
│  Job #47: 3 prints happened after last app checkpoint.            │
│           These codes will be marked as printed.                   │
│           Code at position 346 will be burned (+1 safety).        │
│                                                                    │
│  Job #48: Counters match. Clean resume.                           │
│                                                                    │
│  For each job:                                                     │
│  [Resume (re-upload remaining codes)]  [Abort (burn + return)]    │
│                                                                    │
└──────────────────────────────────────────────────────────────────┘
```

---

## 10. Alert System

### Design principle

Alerts are **ephemeral UI notifications** — "look at this now." The `audit_log` table is the permanent record. So alerts live entirely in memory: no extra DB table, no persistence, no migration.

### Implementation

```csharp
public enum AlertSeverity { Info, Warning, Error }

public class AlertItem : ObservableObject  // CommunityToolkit.Mvvm base
{
    public Guid Id { get; } = Guid.NewGuid();
    public DateTime Timestamp { get; init; }
    public AlertSeverity Severity { get; init; }
    public int? PrinterId { get; init; }
    public int? JobId { get; init; }
    public string Source { get; init; }     // printer name or "System"
    public string Message { get; init; }
}

public class AlertService
{
    private const int MaxAlerts = 50;
    
    // UI binds directly to this — ObservableCollection raises CollectionChanged on UI thread
    public ObservableCollection<AlertItem> Alerts { get; } = new();
    
    private readonly IAuditService _audit;
    private readonly IDispatcherService _dispatcher;
    
    public void Raise(AlertSeverity severity, string source, string message,
                      int? printerId = null, int? jobId = null)
    {
        var alert = new AlertItem
        {
            Timestamp = DateTime.Now,
            Severity = severity,
            Source = source,
            Message = message,
            PrinterId = printerId,
            JobId = jobId
        };
        
        _dispatcher.Invoke(() =>
        {
            Alerts.Insert(0, alert);  // newest first
            
            // Cap size — drop oldest
            while (Alerts.Count > MaxAlerts)
                Alerts.RemoveAt(Alerts.Count - 1);
        });
        
        // Also write to permanent audit log (fire-and-forget)
        _audit.LogAsync("alert", printerId, jobId,
            new { severity, source, message });
        
        // Auto-dismiss Info alerts after 30s
        if (severity == AlertSeverity.Info)
            ScheduleDismiss(alert.Id, TimeSpan.FromSeconds(30));
    }
    
    public void Dismiss(Guid alertId)
    {
        _dispatcher.Invoke(() =>
        {
            var item = Alerts.FirstOrDefault(a => a.Id == alertId);
            if (item != null) Alerts.Remove(item);
        });
    }
    
    private async void ScheduleDismiss(Guid alertId, TimeSpan delay)
    {
        await Task.Delay(delay);
        Dismiss(alertId);
    }
}
```

That's the entire service. ~50 lines of real code.

### Who raises what

| Caller | When | Severity | Example message |
|--------|------|----------|-----------------|
| `JobExecutor` | Counter mismatch | Warning | "Counter mismatch: SPGGCP=342, SPGGTP delta=345" |
| `JobExecutor` | Counter jump | Warning | "Unexpected counter jump (+7)" |
| `JobExecutor` | Job completed | Info | "Job #47 completed (500/500)" |
| `JobExecutor` | Connection lost during job | Error | "Connection lost. Job #47 paused." |
| `PrinterConnectionManager` | Printer connected | Info | "Connected" |
| `PrinterConnectionManager` | Printer went offline | Warning | "Offline — reconnecting..." |
| `PrintJobService` | Prepare failed | Error | "CSV upload failed: SPLCDF returned FAIL" |
| `PrintJobService` | BLOCKED state detected | Warning | "Printer BLOCKED — operator not in main window" |

### UI placement

Alerts live in the **main window shell**, not inside any specific page. They're always visible regardless of which screen the user is on:

```
┌─────────────────────────────────────────────────────────────┐
│  NAV    │      CONTENT AREA (Dashboard/Products/Print/...)  │
│         │                                                    │
│  • Dash │                                                    │
│  • Prods│                                                    │
│  • Print│                                                    │
│  • Hist │                                                    │
│         │                                                    │
├─────────┴────────────────────────────────────────────────────┤
│ ALERTS (always visible, scrollable, max 3 rows then scroll) │
│ 14:35  🔴  Line1: Connection lost. Job #47 paused.    [×]   │
│ 14:33  ⚠️  Line2: Counter jump (+6).                  [×]   │
│ 14:30  ✅  Line3: Job #49 completed (1000/1000)       [×]   │
└──────────────────────────────────────────────────────────────┘
```

- **Max 3 visible rows** — beyond that, scroll. Keeps the panel from eating the screen.
- **Error alerts stay** until manually dismissed (operator must acknowledge).
- **Warning alerts stay** until manually dismissed.
- **Info alerts auto-dismiss** after 30 seconds.
- **[×] button** on each alert dismisses it.
- **Empty state**: alert bar collapses to zero height (no wasted space when nothing is happening).

---

## 11. What Changes in phase1-design.md

| Section | Change needed |
|---------|--------------|
| **§4 Data Model** | Add two partial unique indexes (one active job per printer, one per product) |
| **§5.1 Interface** | Already correct — adapter is per-printer instance, stateless to multi-job |
| **§5.3 Counter Tracking** | Clarify this runs per-job, not a single global loop |
| **§6.2 Dashboard** | Already supports multiple printer cards — add job info per card |
| **§6.4 Print Screen** | Add active jobs panel + disable busy printers/products in dropdowns |
| **§6.6 Alerts** | Add printer/job identification to each alert |
| **§7.1 Architecture** | Add `PrinterConnectionManager` to layer diagram |
| **§7.2 Services** | Split `CounterMonitor` → per-job `JobExecutor`. Add `PrinterConnectionManager`. |
| **§8 Tech Stack** | Note SQLite WAL mode requirement |
| **§10 Risk Mitigation** | Add row: "Concurrent job interference → DB constraints + per-printer locking" |
| **§3.4 Recovery** | Expand to handle multiple stale jobs on startup |

---

## 12. Summary: Why This Works

The one-product-one-printer constraint makes multi-printer concurrency almost trivially safe:

- **No shared mutable state between jobs** — each touches different DB rows, different printer connections, different code pools
- **The only contention point is SQLite writes** — handled by WAL mode + short transactions
- **The only coordination point is "don't let two jobs target the same printer"** — handled by a DB constraint that's impossible to violate
- **Each job is a self-contained state machine** — lifecycle is start → poll → complete/cancel, fully independent
- **UI just subscribes to N event streams** — one per active job, marshaled to UI thread

The single-printer design already does the hard work. Multi-printer is just *running N copies of it in parallel with isolation guards*.
