# Phase 1 — Code Print Manager

## 1. Overview

A Windows desktop application that manages printing of unique product codes (Data Matrix / QR) via Savema thermal printers. The primary goal is **accurate tracking of code usage** — preventing duplicates (illegal) and minimizing waste (codes cost money).

### What it does
- Import codes from CSV files (manually downloaded from government site)
- Organize products in a flexible hierarchy
- Manage multiple Savema printers on the local network
- Print codes to products, tracking exactly which codes have been used
- Provide a dashboard showing code availability and printer status
- Keep full audit history for troubleshooting

### What it does NOT do (Phase 1)
- No scanner / verification
- No recycler / rejection
- No E-Mark API integration
- No aggregation (pack / pallet)
- No login / authentication
- No cloud / internet dependency

### Design Principles
- **Duplicate prevention is paramount** — a code printed twice is worse than a code wasted
- **Offline-first** — works without internet, local storage survives restarts and crashes
- **Future-ready** — architecture allows adding scanners, recyclers, and pipelines later without rewrite
- **Simple deployment** — installed once per client by support team

---

## 2. Core Concepts

### Product Tree
Products are organized in a **tree hierarchy** (like folders and files). Unlimited depth, defined by the client.

```
Root
├── Juice
│   ├── Apple 0.5L        ← printable product (leaf)
│   ├── Apple 1.0L        ← printable product (leaf)
│   └── Orange
│       ├── 0.33L         ← printable product (leaf)
│       └── 1.0L          ← printable product (leaf)
├── Water
│   ├── Still 0.5L        ← printable product (leaf)
│   └── Sparkling 1.5L    ← printable product (leaf)
└── Milk
    └── 1.0L              ← printable product (leaf)
```

- **Branches** (folders) = organizational grouping only
- **Leaves** (files) = printable products with code pools and templates

Each leaf product has:
- A code pool (imported CSVs)
- An assigned Savema template file (`.rox`)
- A derived CSV filename for the printer (e.g., `apple_05.csv`)

### Code Pool
Each printable product owns a pool of codes. Codes transition through states:

```
available → reserved → printed
                ├──→ returned (back to available)
                ├──→ burned (operator-confirmed permanent loss)
                └──→ quarantined (uncertain — frozen until operator verifies)
```

| State | Meaning |
|-------|---------|
| `available` | Imported, ready to use |
| `reserved` | Selected for a print job, uploaded to printer |
| `printed` | Confirmed printed (counter-based) |
| `returned` | Was reserved but job cancelled; back in available pool |
| `burned` | Operator-confirmed permanent loss; code is consumed and cannot be reused |
| `quarantined` | Ambiguous — system is unsure if this code was physically printed; frozen until operator resolves via Codes tab |

**Rules:**
- Codes are selected in import order (FIFO within each CSV)
- A code can never go from `printed` back to `available` automatically (only operator override via Codes tab)
- `burned` codes are treated as printed (never reused) — permanent
- `quarantined` codes are frozen — excluded from availability counts, cannot be auto-reused, but operators can recover them after investigation
- `Code.ProductId` is nullable — codes can exist in an unassigned pool (e.g., after product deletion)

### Printers
Savema TTO printers connected via TCP/IP on LAN. Each printer:
- Has an IP address + port (9100)
- Can store multiple templates and CSV files
- Has one active template at a time
- Exposes a print counter (reset on power cycle)
- Supports start/stop/query via SPPL protocol

### Print Jobs
A print job links: **product + printer + quantity + codes**

```
Job #47:
  Product:  Apple 0.5L
  Printer:  Savema-Line1 (192.168.1.10)
  Quantity: 500
  Codes:    codes #1201–#1700 from pool
  Status:   printing (342/500 done)
```

---

## 3. Application Flow

### 3.1 Print Flow (Main Use Case)

```
┌──────────────────────────────────────────────────────────────────┐
│  1. SELECT PRODUCT     (from tree)                                │
│  2. SELECT PRINTER     (from configured printers)                 │
│  3. ENTER QUANTITY     (how many codes to print)                  │
│  4. [PREPARE]          (validates, uploads template + CSV)        │
│  5. [PRINT]            (activates template, starts printing)      │
│  6. MONITOR            (polls counter, updates progress)          │
│  7. COMPLETE / CANCEL  (finalize or abort)                        │
└──────────────────────────────────────────────────────────────────┘
```

#### Step 4: Prepare (detailed)
1. **Check printer state** — `SPPSTA` must return WAITING before proceeding
   - RUNNING → "Printer is currently printing. Stop it first."
   - ERROR → "Printer error: {message}. Resolve before starting."
   - INIT → "Printer is still initializing. Wait."
   - BLOCKED → "Printer UI is not on main screen. Return to main screen."
   - WAITING → proceed
2. **Validate** — product has template assigned? Enough codes in pool?
3. **Reserve codes** — pick next N codes from available pool (FIFO), mark as `reserved`
4. **Generate CSV** — create CSV file with reserved codes (encoded with `~sc~` column separators)
5. **Check template on printer** — query stored templates (`SPLGST`)
   - If template present → skip (already stored)
   - If template missing → upload `.rox` file via `SPLRTF` (base64-encoded binary transfer)
   - If upload fails → fallback: prompt operator to load template via Sayasis
6. **Upload CSV** — delete old CSV (`SPLDDF`, ignore FAIL if not present), then create fresh (`SPLCDF`, require OK)
7. **Verify upload** — confirm filename appears in stored files list (`SPLGSD`)
8. **Enable Print button**

> **Why check status upfront?** Almost every command in Prepare and Print requires WAITING state ("Stop Position"). A single check at the top catches 99% of issues with an actionable message. If state changes mid-flow (e.g., operator enters settings), individual command FAILs still catch it.

> **Why not download-and-compare?** SPPL has no command to read back CSV file content. `SPLGSD` returns filenames only. However, TCP guarantees byte-level delivery, `SPLCDF:OK` confirms the printer accepted the data, and `SPLLTF:OK` in Step 5 implicitly validates the CSV was parseable. See `savema-csv-template-buffer.md` for full rationale.

#### Step 5: Print
1. **Reload template** — always call `SPLLTF` even if already active. This:
   - Loads fresh CSV data into the data buffer (ensures new codes are used)
   - Resets CSV row pointer to row 1
   - **Note:** SPGGCP behavior on `SPLLTF` varies by firmware version (some reset to 0, some are cumulative). The app always records a SPGGCP baseline after reload for delta tracking, so the formula works regardless.
   - **On FAIL**: transition job to `error`, codes stay `reserved`. Read `SPPSTA` for diagnostics. UI shows: *"Template load failed. Printer state: {state}."* with **[Retry]** and **[Cancel Job]**. Retry re-checks `SPPSTA == WAITING` then re-attempts `SPLLTF`. Cancel returns all reserved codes to `available` (no quarantine needed — nothing was printed).
2. **Record lifetime counter** — read `SPGGTP` as `total_baseline` (cross-check, survives power cycle)
3. **Set print quantity** — `SPPSLQ{N}` (limited print count)
4. **Start printing** — `SPPSAP`
5. **Transition to monitoring state**

#### Step 6: Monitor
- Poll `SPGGCP` at regular interval (e.g., every 500ms)
- `codes_printed = SPGGCP - spggcp_baseline` (baseline-delta approach works regardless of firmware reset behavior)
- **Cross-check**: periodically verify `SPGGTP - total_baseline == SPGGCP - spggcp_baseline`
- **Tertiary check**: `quantity - SPPGLQ == SPGGCP` (if firmware supports)
- Mark codes as `printed` in order (first N codes from the reserved list)
- Update UI progress bar
- **External print detection**: if counter jumps more than expected between polls, alert (someone may have printed outside our app)
- Continue until `codes_printed >= quantity` or operator cancels

#### Step 7: Complete / Cancel
- **Complete**: all reserved codes confirmed printed. Job done.
- **Cancel**: 
  - Codes already marked `printed` stay printed
  - Boundary codes after last confirmed print are marked `quarantined` (per-printer `QuarantineMargin` setting, default 0 — operator can recover via Codes tab)
  - Remaining reserved codes return to `available` (status → `returned`)
  - Stop printer

### 3.2 CSV Import Flow

```
1. Select product (leaf node in tree)
2. Click "Import CSV"
3. Browse for file
4. App validates:
   - File readable, non-empty
   - No duplicate codes within file
   - No duplicate codes against existing pool (all products, all states except returned)
   - No code contains SPPL-forbidden sequences: `^`, `~gt~`, `~sc~`, or `~` (see §5.4)
5. Codes added to product's pool as `available`
6. Audit log entry: "Imported 10000 codes from gold_0.5_10000.csv at 2026-08-06 14:30"
```

**Duplicate detection scope**: A code must be unique across the **entire application** (all products, all pools). A code that exists anywhere (available, reserved, printed, burned) in any product cannot be imported again.

### 3.3 Verify Flow

When the operator wants to confirm printer state matches expectations (e.g., after power cycle):

```
1. Select printer
2. Click "Verify"
3. App checks stored files on printer (SPLGSD):
   - Expected CSV present? → ✅ File exists
   - Expected CSV missing? → ⚠️ "CSV not found on printer"
4. App checks active template (SPLGAT):
   - Matches expected? → ✅ Template active
   - Different or none? → ⚠️ "Template mismatch" or "No active template"
5. App reads counters and compares with its records:
   - SPGGTP (lifetime) vs app's total_baseline + codes_confirmed
   - SPGGCP (current) vs app's codes_confirmed (if no power cycle)
   - Match? → ✅ Counters consistent
   - Mismatch? → ⚠️ Shows discrepancy details
6. Summary: green/yellow/red status with actionable details
```

> **Note:** The app is the source of truth for code state. Verification checks whether the printer's observable state (file existence, active template, counters) is consistent with what the app expects — it does not read back CSV content.

### 3.4 Power Failure Recovery

The research doc confirms: **`SPGGCP` (current counter) resets on power cycle**, CSV row pointer behavior is unknown, but **`SPGGTP` (lifetime counter) persists**.

Recovery strategy:
1. On app startup (or reconnect), read `SPGGTP` (lifetime counter)
2. Compare with app's stored `total_baseline`: `prints_before_failure = SPGGTP_now - total_baseline`
3. Compare with `job.codes_confirmed`:
   - If `prints_before_failure == codes_confirmed` → no prints were lost, resume cleanly
   - If `prints_before_failure > codes_confirmed` → some prints happened between last poll and failure; mark those codes as printed
   - If `prints_before_failure < codes_confirmed` → anomaly (should not happen), alert operator
4. Operator can:
   - **Re-upload and resume**: app re-uploads CSV (only remaining unprinted codes), reloads template, continues
   - **Verify first**: check stored files (`SPLGSD`) and counters (`SPGGTP`), compare with app records
   - **Abort job**: quarantine the ambiguous code (+1 after last confirmed), return rest to pool

> **Key insight**: Because we always record a SPGGCP baseline at job start (delta tracking works regardless of firmware reset behavior) and record `SPGGTP` as a persistent checkpoint, we can determine exactly how many codes were physically printed even after a power cycle.

---

## 4. Data Model (SQLite)

### Tables

```sql
-- Product hierarchy (tree)
product_nodes (
  id            INTEGER PRIMARY KEY,
  parent_id     INTEGER REFERENCES product_nodes(id),  -- NULL for root
  name          TEXT NOT NULL,
  is_leaf       BOOLEAN NOT NULL DEFAULT FALSE,
  -- Leaf-only fields:
  template_file TEXT,           -- path to .rox file on disk
  printer_csv_name TEXT,        -- filename used on printer (e.g., "apple_05.csv")
  created_at    DATETIME NOT NULL,
  updated_at    DATETIME NOT NULL
)

-- Imported codes
codes (
  id            INTEGER PRIMARY KEY,
  product_id    INTEGER REFERENCES product_nodes(id) ON DELETE SET NULL,  -- NULL = unassigned pool
  code_text     TEXT NOT NULL,           -- raw code string
  status        TEXT NOT NULL DEFAULT 'available',  -- available|reserved|printed|returned|burned|quarantined
  import_order  INTEGER NOT NULL,        -- order within import (for FIFO selection)
  import_batch  TEXT,                    -- source filename for reference
  job_id        INTEGER REFERENCES print_jobs(id),  -- which job reserved/printed this
  status_changed_at DATETIME,
  created_at    DATETIME NOT NULL,
  UNIQUE(code_text)                      -- global uniqueness enforced
)

-- Archived codes (preserves history when codes are deleted via admin)
archived_codes (
  id                 INTEGER PRIMARY KEY,
  original_code_id   INTEGER NOT NULL,        -- original codes.id before archival
  product_id         INTEGER,                 -- product at time of archival (may be NULL)
  code_text          TEXT NOT NULL,            -- preserved for re-import eligibility
  status             TEXT NOT NULL,            -- status at time of archival
  import_order       INTEGER NOT NULL,
  import_batch       TEXT,
  job_id             INTEGER,
  status_changed_at  DATETIME,
  created_at         DATETIME NOT NULL,       -- original creation timestamp
  archived_at        DATETIME NOT NULL,       -- when the code was archived
  archived_reason    TEXT                      -- e.g., "product_deletion", "manual_archive"
)

-- Printers
printers (
  id            INTEGER PRIMARY KEY,
  name          TEXT NOT NULL,
  ip_address    TEXT NOT NULL,
  port          INTEGER NOT NULL DEFAULT 9100,
  model         TEXT,                    -- e.g., "Savema SVM 53*70 I"
  adapter_type  TEXT NOT NULL DEFAULT 'savema_tto',
  is_active     BOOLEAN NOT NULL DEFAULT TRUE,
  created_at    DATETIME NOT NULL,
  updated_at    DATETIME NOT NULL
)

-- Print jobs
print_jobs (
  id                INTEGER PRIMARY KEY,
  product_id        INTEGER NOT NULL REFERENCES product_nodes(id),
  printer_id        INTEGER NOT NULL REFERENCES printers(id),
  quantity          INTEGER NOT NULL,
  status            TEXT NOT NULL DEFAULT 'preparing', -- preparing|ready|printing|completed|cancelled|error
  total_baseline    INTEGER,             -- SPGGTP (lifetime counter) recorded at job start
  codes_confirmed   INTEGER DEFAULT 0,   -- how many codes confirmed printed
  started_at        DATETIME,
  completed_at      DATETIME,
  created_at        DATETIME NOT NULL
)

-- Audit log
audit_log (
  id            INTEGER PRIMARY KEY,
  event_type    TEXT NOT NULL,   -- import|reserve|print|burn|return|cancel|alert|verify|...
  product_id    INTEGER,
  printer_id    INTEGER,
  job_id        INTEGER,
  details       TEXT,            -- JSON blob with event-specific info
  created_at    DATETIME NOT NULL
)

app_config (
  key           TEXT PRIMARY KEY,  -- e.g. 'ZoomLevel'
  value         TEXT NOT NULL      -- string-encoded value
)
```

> **Note:** The `app_config` table stores user preferences as key-value pairs. Currently used for zoom level (feature implemented but UI hidden pending UX rework). The table is extensible for future preferences.

### Indexes
```sql
CREATE INDEX idx_codes_product_status ON codes(product_id, status);
CREATE INDEX idx_codes_status ON codes(status);
CREATE INDEX idx_codes_code_text ON codes(code_text);  -- for dedup lookups
CREATE INDEX idx_jobs_status ON print_jobs(status);
CREATE INDEX idx_audit_created ON audit_log(created_at);
CREATE INDEX idx_audit_product ON audit_log(product_id);
CREATE INDEX idx_archived_product_date ON archived_codes(product_id, archived_at);

-- Concurrency guards (see multi-printer-concurrency.md §2)
CREATE UNIQUE INDEX idx_one_active_job_per_printer
  ON print_jobs(printer_id)
  WHERE status IN ('preparing', 'ready', 'printing', 'paused');

CREATE UNIQUE INDEX idx_one_active_job_per_product
  ON print_jobs(product_id)
  WHERE status IN ('preparing', 'ready', 'printing', 'paused');
```

---

## 5. Printer Communication

### 5.1 Adapter Interface

```csharp
public interface IPrinterAdapter : IDisposable
{
    // Connection
    Task<bool> ConnectAsync(string host, int port);
    Task DisconnectAsync();
    bool IsConnected { get; }

    // Status
    Task<PrinterStatus> GetStatusAsync();        // SPPSTA → Init, Idle, Printing, Error, Blocked
    Task<int> GetCurrentCounterAsync();          // SPGGCP (resets on template load)
    Task<int> GetTotalCounterAsync();            // SPGGTP (lifetime, survives power cycle)
    Task<int?> GetRemainingQuantityAsync();       // SPPGLQ (prints left in limited job)

    // Template management
    Task<List<string>> ListTemplatesAsync();              // SPLGST
    Task<bool> UploadTemplateAsync(string name, byte[] rox); // SPLRTF (base64-encoded .rox file)
    Task<bool> ActivateTemplateAsync(string name);         // SPLLTF
    Task<string?> GetActiveTemplateAsync();                // SPLGAT

    // CSV / Database management
    Task<List<string>> ListCsvFilesAsync();
    Task<bool> UploadCsvAsync(string filename, IReadOnlyList<string> codes);
    Task<bool> VerifyCsvExistsAsync(string filename); // checks SPLGSD response for filename
    Task<bool> DeleteCsvAsync(string filename);

    // Template management (storage)
    Task<bool> DeleteTemplateAsync(string name);

    // Print control
    Task<bool> SetPrintQuantityAsync(int quantity);
    Task<bool> StartPrintAsync();
    Task<bool> StopPrintAsync();

    // Events (for future async notifications if printer supports them)
    event EventHandler<PrinterErrorEventArgs>? OnError;
}

public enum PrinterStatus { Offline, Init, Idle, Printing, Error, Blocked }
```

### 5.2 Savema Implementation

The `SavemaTtoAdapter` implements `IPrinterAdapter` using SPPL over TCP:

| Operation | SPPL Command | Response |
|-----------|-------------|----------|
| Get status | `~SPPSTA^` | `~SPGRES{SPPSTA:WAITING<}^` / `RUNNING<` / `ERROR<msg` / `INIT<` |
| Get current counter | `~SPGGCP^` | `~SPGRES{SPGGCP:1250}^` (resets on template load) |
| Get total counter | `~SPGGTP^` | `~SPGRES{SPGGTP:458200}^` (lifetime, never resets) |
| Get remaining qty | `~SPPGLQ^` | `~SPGRES{SPCGLQ:500}^` (naming inconsistent in docs) |
| List templates | `~SPLGST^` | `~SPGRES{SPLGST:name1.rox<name2.rox}^` |
| Get active template | `~SPLGAT^` | `~SPGRES{SPLGAT:temp1_53.rox}^` |
| Upload template (XML) | `~SPLTDS{<Template>...</Template>}^` | `~SPGRES{SPLTDS:OK}^` (Stop Position only, programmatic) |
| Upload template (file) | `~SPLRTF{name.rox>base64data}^` | `~SPGRES{SPLRTF:OK}^` (Stop Position only, binary .rox) |
| Activate template | `~SPLLTF{name.rox}^` | `~SPGRES{SPLLTF:OK}^` (Stop Position only) |
| Upload CSV | `~SPLCDF{filename~gt~data}^` | `~SPGRES{SPLCDF:OK}^` (columns: `~sc~`) |
| List CSV files | `~SPLGSD^` | `~SPGRES{SPLGSD:file1.csv<file2.csv}^` |
| Delete CSV | `~SPLDDF{filename}^` | `~SPGRES{SPLDDF:OK}^` |
| Clear data buffer | `~SPLCDB^` | `~SPGRES{SPLCDB:OK}^` |
| Set quantity | `~SPPSLQ{N}^` | `~SPGRES{SPPSLQ:OK}^` |
| Start print | `~SPPSAP^` | `~SPGRES{SPPSAP:OK}^` (Stop Position only) |
| Stop print | `~SPPSTP^` | `~SPGRES{SPPSTP:OK}^` (Print Position only) |
| Modify 2D barcode | `~SPMC2D{name~gt~value}^` | `~SPGRES{SPMC2D:OK}^` |
| Modify text | `~SPMCTV{name~gt~value}^` | `~SPGRES{SPMCTV:OK}^` |
| Lock interface | `~SPGSLI{1}^` | `~SPGRES{SPGSLI:OK}^` (lock print/stop/edit buttons) |

> **SPPL Encoding Rules:**
> - Commands start with `~` and end with `^`
> - Multiple commands separated by `|` (e.g., `~SPPSLQ{1000}|SPPSAP^`)
> - Parameters use `~gt~` separator (filename~gt~content)
> - CSV columns use `~sc~` separator (col1~sc~col2)
> - Multi-value responses separated by `<` (name1<name2<name3)
> - XML special chars must be escaped: `"` → `&quot;`, `'` → `&apos;`, `<` → `&lt;`, `>` → `&gt;`, `&` → `&amp;`
> - "Stop Position" = printer must be in WAITING state; "Print Position" = printer must be in RUNNING state
> - BLOCKED state: if operator is not in main window, all commands except SPPSTA return FAIL

### 5.3 Counter Tracking Logic

> Each active print job runs its own independent polling loop (one `JobExecutor` per job). Multiple jobs poll their respective printers concurrently without interference. See `multi-printer-concurrency.md` §4 for the full execution model.

```
Every 500ms while job is active:
  raw_counter = await printer.GetCurrentCounterAsync()       // SPGGCP (may be cumulative)
  codes_printed = raw_counter + counter_offset               // offset = -baseline (fresh job)
                                                             //        = CodesConfirmed - baseline (resume)
  // Cap to quantity as defense-in-depth
  if codes_printed > job.quantity: codes_printed = job.quantity

  // Cross-check with lifetime counter (periodic, e.g., every 5th poll)
  if should_cross_check:
    lifetime = await printer.GetTotalCounterAsync()           // SPGGTP
    expected = lifetime - job.total_baseline
    if expected != codes_printed:
      LOG_WARNING: "Counter mismatch: SPGGCP-based={codes_printed}, SPGGTP delta={expected}"

  if codes_printed > job.codes_confirmed:
    // New prints detected
    mark codes[job.codes_confirmed .. codes_printed-1] as "printed"
    job.codes_confirmed = codes_printed
    save to DB

  if codes_printed == job.quantity:
    // Job complete
    job.status = "completed"
```

### 5.4 SPPL Data Encoding

The adapter is responsible for encoding code values into the SPLCDF command format. Callers pass raw code strings; the adapter handles formatting.

**Forbidden sequences** — these cannot appear in code values because SPPL has no escape mechanism:

| Sequence | Reason |
|----------|--------|
| `^` | Terminates any SPPL command |
| `~gt~` | Interpreted as parameter separator in SPLCDF |
| `~sc~` | Interpreted as column separator in SPLCDF |
| `~` | Could form part of separator sequences; disallow preventatively |

**Validation**: Code values are checked at import time (see §3.2). The adapter also asserts no forbidden content before upload (defense-in-depth). If a code fails either check, it is rejected with a clear error.

**XML entity encoding** (`&lt;`, `&gt;`, `&amp;`, `&quot;`, `&apos;`) applies only to modification commands (`SPMC2D`, `SPMCTV`, `SPMCSV`) where values are embedded in an XML-like command context — NOT to SPLCDF data content.

**Adapter formatting** for `UploadCsvAsync(filename, codes)`:
```
// Single-column CSV (our use case: one code per row)
var payload = $"~SPLCDF{{{filename}~gt~{string.Join("\n", codes)}}}^";
```

### 5.5 Response Parsing

All SPPL responses follow `~SPGRES{COMMAND:PAYLOAD}^`. The adapter must handle several payload shapes:

**Success / Failure:**
```
~ SPGRES{SPPSAP:OK}^       → success
~ SPGRES{SPPSAP:FAIL}^     → failure (printer not in correct state, or BLOCKED)
```

**Scalar values:**
```
~ SPGRES{SPGGCP:1250}^     → parse "1250" as integer
~ SPGRES{SPLGAT:temp1_53.rox}^  → parse as string
```

**Multi-value lists** (separated by `<`):
```
~ SPGRES{SPLGST:temp1.rox<abc.rox<temp2.rox}^  → split on '<' → 3 template names
~ SPGRES{SPLGSD:codes.csv<backup.csv}^          → split on '<' → 2 data files
```

**Status with sub-fields** (`SPPSTA` uses `<` as state/info separator, not list separator):
```
~ SPGRES{SPPSTA:WAITING<}^                    → state=WAITING, no block
~ SPGRES{SPPSTA:RUNNING<}^                    → state=RUNNING, no block
~ SPGRES{SPPSTA:RUNNING<BLOCKED}^             → state=RUNNING, UI blocked
~ SPGRES{SPPSTA:ERROR<Ribbon not found}^      → state=ERROR, message follows
```

**Field-not-found** (modification commands):
```
~ SPGRES{SPMC2D:<ProductQRCode> not found}^   → field missing from template
```

**Parsing steps:**
1. Strip leading `~ ` (note the space) and trailing `^`
2. Verify `SPGRES{` wrapper; extract inner content
3. Split on first `:` → command name + payload
4. Match command name to expected; route payload by command type
5. `OK` / `FAIL` → boolean; numeric string → int; `<`-delimited → list or status sub-fields

> **Edge case:** The docs show inconsistent whitespace in `SPGRES` (sometimes `~ SPGRES`, sometimes `~SPGRES`). The parser should trim whitespace between `~` and `SPGRES`.

### 5.6 External Print Detection

If the counter advances more than expected between polls:
```
expected_max_advance = (poll_interval_ms / min_print_time_ms) + 1  // e.g., 500ms/180ms + 1 ≈ 4
actual_advance = new_counter - previous_counter

if actual_advance > expected_max_advance * 2:
  // Suspicious jump — possible external print or counter glitch
  ALERT: "Unexpected counter jump (+{actual_advance}). Check if printer was used externally."
  // Still mark codes as printed (conservative — assume they were printed)
```

---

## 6. User Interface

### 6.1 Screen Map

```
┌─────────────────────────────────────┐
│            MAIN WINDOW              │
├─────────┬───────────────────────────┤
│         │                           │
│  NAV    │      CONTENT AREA         │
│         │                           │
│ • Dash  │  (changes based on nav)   │
│ • Prods │                           │
│ • Prntrs│                           │
│ • Jobs  │                           │
│         │                           │
├─────────┴───────────────────────────┤
│ ALERTS (always visible at bottom)   │
└─────────────────────────────────────┘
```

Navigation:
- **Dashboard** — active monitoring and intervention (printer status, active jobs, alerts, recent activity)
- **Products** — tree management + CSV import
- **Printers** — printer configuration + storage management
- **Jobs** — manage active print jobs + view job history

**New Job** is not a nav item — accessed via [+ New Job] buttons on Dashboard, Products, Printers, and Jobs pages. Each context preselects the relevant field (product or printer).

### 6.2 Dashboard

Merged printer/job cards — one card per printer that has ever had a job, showing the last/current job.

```
┌──────────────────────────────────────────────────────────────────┐
│  DASHBOARD                                         [+ New Job]   │
├──────────────────────────────────────────────────────────────────┤
│                                                                    │
│  ┌──────────────────────────────────────────────────────────────┐│
│  │ Savema-Line1  192.168.1.10                      ● PRINTING   ││
│  │ Job #47: Apple 0.5L   342/500 (68%)                          ││
│  │ ████████████████████░░░░░░░░░            [Pause] [Cancel]    ││
│  └──────────────────────────────────────────────────────────────┘│
│  ┌──────────────────────────────────────────────────────────────┐│
│  │ Savema-Line2  192.168.1.11                      ● READY      ││
│  │ Job #49: Water Still 0.5L   0/1000                           ││
│  │ Prepared, waiting to start       [Start Print] [Cancel]      ││
│  └──────────────────────────────────────────────────────────────┘│
│  ┌──────────────────────────────────────────────────────────────┐│
│  │ Savema-Line3  192.168.1.12                      ● DONE       ││
│  │ Job #46: Orange 0.33L   2000/2000 (100%)                     ││
│  │ Completed Aug 7 14:25                                        ││
│  └──────────────────────────────────────────────────────────────┘│
│                                                                    │
│  ALERTS                                                           │
│  ┌──────────────────────────────────────────────────────────────┐│
│  │ 14:32  ⚠️  Line1: Unexpected counter jump (+7)        [×]   ││
│  └──────────────────────────────────────────────────────────────┘│
│                                                                    │
│  RECENT ACTIVITY                                                  │
│  14:30  Job #47 started: Apple 0.5L → Line1                      │
│  14:25  Job #46 completed: Orange 0.33L → Line3                  │
│  14:20  Imported 10,000 codes for Apple 0.5L                     │
│                                                                    │
└──────────────────────────────────────────────────────────────────┘
```

Key behaviors:
- **One card per printer** (only printers with job history). Shows last/current job and status.
- **Sort order:** running/error/paused/ready first (newest status update), completed last.
- **Contextual action buttons on cards:**
  - Job is `printing` → [Pause], [Cancel]
  - Job is `ready` → [Start Print], [Cancel]
  - Job is `paused` → [Resume], [Cancel]
  - Job is `completed` → no buttons
- **Clicking a card** navigates to Jobs page with that job selected.
- **Alerts** — inline mirror of the bottom alert bar (errors and warnings).
- **Recent Activity** — last ~20 events (excludes `job_created`). Color-coded dots: green (info/completed), purple (import), blue (started/resumed), amber (warning/paused), gray (cancelled), red (error). Alert messages are localized.
- **[+ New Job]** (top-right) — opens New Job screen with nothing preselected.

### 6.3 Products Screen

Three-tab detail pane: **Operations** (daily workflow), **Settings** (configuration), and **Codes** (admin code management). Tree toolbar for adding nodes. A separate **Unassigned Codes** section appears below the tree when codes exist without a product.

```
┌──────────────────────────────────────────────────────────────────┐
│  PRODUCTS                                                          │
├────────────────────┬─────────────────────────────────────────────┤
│  [+F] [+P]        │  APPLE 0.5L                                  │
│                    │                                               │
│  ▼ Juice           │  [Operations]  [Settings]  [Codes]          │
│    ▼ Apple         │  ──────────────────────────────────────────  │
│      ● 0.5L  ←    │                                               │
│      ● 1.0L       │  Code Pool:                                   │
│    ▼ Orange        │    Available:    8,300                        │
│  ▼ Water           │    Printed:      1,700                        │
│    ● Still 0.5L    │    Burned:       3                            │
│  ▼ Milk            │    Quarantined:  7                            │
│    ● 1.0L          │    Total:        10,010                       │
│                    │                                               │
│  ──────────────    │  [Import CSV...]  [+ New Job]                 │
│  ⚠ Unassigned (5) │                                               │
│                    │  History:                                     │
│                    │    Aug 10  Job #52 completed — 500/500        │
│                    │    Aug 09  Imported 10,000 — gold_0.5.csv     │
│                    │    Aug 08  Job #48 cancelled — 200/500        │
│                    │    Aug 06  Imported 5,000 — batch_aug6.csv    │
│                    │                                               │
└────────────────────┴─────────────────────────────────────────────┘
```

```
┌────────────────────┬─────────────────────────────────────────────┐
│                    │  [Operations]  [Settings]  [Codes]          │
│  (tree unchanged)  │  ──────────────────────────────────────────  │
│                    │                                               │
│                    │  Template:  apple_05_template.rox  [Change]  │
│                    │  CSV Name:  [apple_05.csv       ]  [Save]   │
│                    │                                               │
│                    │  ─── Danger Zone ────────────────────────    │
│                    │  [Delete Product]                            │
│                    │                                               │
└────────────────────┴─────────────────────────────────────────────┘
```

```
┌────────────────────┬─────────────────────────────────────────────┐
│                    │  [Operations]  [Settings]  [Codes]          │
│  (tree unchanged)  │  ──────────────────────────────────────────  │
│                    │                                               │
│                    │  Status: [All ▼]  Search: [________]  [⟳]  │
│                    │                                               │
│                    │  ☐  CODE_TEXT          STATUS    BATCH  JOB  │
│                    │  ☐  010462001234...   Available  b1.csv  —   │
│                    │  ☐  010462005678...   Printed    b1.csv  #52 │
│                    │  ☑  010462009012...   Quarantin  b2.csv  #48 │
│                    │                                               │
│                    │  [Select All] [Deselect] Page 1/83 [◀][▶]  │
│                    │  Page size: [100 ▼]                          │
│                    │                                               │
│                    │  Selected (1):                                │
│                    │    [Change Status ▼] [Move ▼] [Archive]     │
│                    │                                               │
│                    │  [Undo Last Action]                          │
└────────────────────┴─────────────────────────────────────────────┘
```

Key behaviors:
- **Tree** always expanded by default. Toolbar: [+F] = add folder, [+P] = add product (relative to selection; click empty space to deselect and add at root).
- **Unassigned section** — visible below the tree when codes exist without a product (after product deletion with "Keep Codes"). Clicking it opens the Codes tab in unassigned mode.
- **Operations tab** (default): code pool stats (including Quarantined in amber), [Import CSV...], [+ New Job], unified activity history (imports + job outcomes merged chronologically, newest first).
- **Settings tab**: template file path + [Change], printer CSV name + [Save], [Delete Product] in a danger zone at the bottom.
- **Codes tab**: paginated DataGrid with status filter, search, select/deselect, status change, move to product, archive, undo. Reserved codes are protected (checkbox disabled). Confirmation dialogs for risky transitions. Page size default 100, max 1000.
- **[+ New Job]** — opens New Job screen with this product preselected.
- **History** — merged timeline: imports (blue), completed (green), cancelled (orange), error (red). Max 20 entries.
- **Delete** — blocked if product has active jobs or reserved codes. Products with codes show a three-button dialog: Keep Codes (→ unassigned pool) / Delete Codes Too (→ archive) / Cancel. Zero-code products use simple Yes/No.

> See `products-page-design.md` for full button-by-button specification, validation rules, and unit test coverage.

### 6.4 Printers Screen

Two tabs: **Configuration** (existing IP/port/model setup) and **Storage**.

```
┌──────────────────────────────────────────────────────────────────┐
│  PRINTERS                                          [+ New Job]   │
├──────────────────────────────────────────────────────────────────┤
│                                                                    │
│  [ Savema-Line1 ▼ ]  192.168.1.10  ● IDLE                        │
│                                                                    │
│  [Configuration]  [Storage]                                       │
│  ─────────────────────────────────────────────────────────────    │
│                                                                    │
│  TEMPLATES ON PRINTER                              [Refresh]      │
│  ┌──────────────────────────────────────────────────────────────┐│
│  │ ☐  Name                    Status             Size          ││
│  │ ☐  apple_05_53.rox         ✅ Used (Apple 0.5L)             ││
│  │ ☐  orange_033_53.rox       ✅ Used (Orange 0.33L)           ││
│  │ ☑  old_test_53.rox         ⚠️ Not mapped to any product     ││
│  │ ☑  demo_53.rox             ⚠️ Not mapped to any product     ││
│  └──────────────────────────────────────────────────────────────┘│
│                                                                    │
│  CSV FILES ON PRINTER                              [Refresh]      │
│  ┌──────────────────────────────────────────────────────────────┐│
│  │ ☐  Name                    Status                           ││
│  │ ☐  apple_05.csv            ✅ Used (Apple 0.5L)              ││
│  │ ☑  old_data.csv            ⚠️ Not mapped to any product      ││
│  │ ☑  test123.csv             ⚠️ Not mapped to any product      ││
│  └──────────────────────────────────────────────────────────────┘│
│                                                                    │
│  [Delete Selected (4)]                                            │
│                                                                    │
└──────────────────────────────────────────────────────────────────┘
```

Key behaviors:
- **[+ New Job]** (top-right) — opens New Job screen with this printer preselected. Disabled if printer is busy or offline.
- **How "Used" is determined:**
  - Templates: filename matches any product's `template_file` (the `.rox` filename)
  - CSV files: filename matches any product's `printer_csv_name`

**Storage cleanup flow:**
1. Click **Refresh** → app queries `SPLGST` (templates) and `SPLGSD` (CSV files)
2. App cross-references each file against `product_nodes.template_file` and `product_nodes.printer_csv_name`
3. Files not mapped to any product are pre-selected and marked ⚠️
4. Operator reviews, adjusts selection
5. Click **Delete Selected** → app calls `SPLDTF{name}` for each template, `SPLDDF{name}` for each CSV
6. Audit log entry: "Deleted 4 files from Savema-Line1: old_test_53.rox, demo_53.rox, old_data.csv, test123.csv"

> **Safety:** Files mapped to a product cannot be selected for deletion (checkbox disabled). The active template (from `SPLGAT`) is also protected — deletion requires stopping the printer first.

### 6.5 Jobs Screen

Two tabs: **Active Jobs** (manage running jobs) and **Job History** (review past jobs).

#### Active Jobs tab

```
┌──────────────────────────────────────────────────────────────────┐
│  JOBS                                              [+ New Job]   │
├──────────────────────────────────────────────────────────────────┤
│  [Active Jobs]  [Job History]                                     │
│  ─────────────────────────────────────────────────────────────    │
│                                                                    │
│  Select Job:                                                      │
│  ┌──────────────────────────────────────────────────────────────┐│
│  │ ● #47  Apple 0.5L → Line1       342/500   ● printing        ││
│  │   #48  Orange 0.33L → Line2     1205/2000 ● printing        ││
│  └──────────────────────────────────────────────────────────────┘│
│                                                                    │
│  ─── Job #47 ─────────────────────────────────────────────────   │
│  Product:  Apple 0.5L                                             │
│  Printer:  Savema-Line1 (192.168.1.10)  ● PRINTING               │
│  Quantity: 500 codes                                              │
│                                                                    │
│  Preparation:                                                     │
│  ✓ Template present on printer                                    │
│  ✓ 500 codes reserved from pool                                   │
│  ✓ CSV uploaded (SPLCDF OK + SPLGSD confirmed)                    │
│  ✓ Template loaded (counter reset to 0)                           │
│                                                                    │
│  Print Progress:                                                  │
│  Progress: 342 / 500  (68%)                                       │
│  ████████████████████████████░░░░░░░░░░░░░                        │
│                                                                    │
│  [Pause]  [Cancel]                                               │
│                                                                    │
└──────────────────────────────────────────────────────────────────┘
```

Key behaviors:
- **Job selector** — lists all active jobs (preparing/ready/printing/paused). One selected at a time.
- **Job detail** — shows product, printer with **live printer status** (● PRINTING / ● PAUSED / ● OFFLINE / ● ERROR), quantity, preparation checklist, progress bar.
- **Contextual action buttons:**
  - Job is `ready` → [Start Print], [Cancel]
  - Job is `printing` → [Pause], [Cancel]
  - Job is `paused` → [Resume], [Cancel]
  - Job `completed` or `cancelled` → no buttons, final summary shown
- When a job completes or is cancelled, it stays displayed until operator selects another job or navigates away.
- If no active jobs → empty state with [+ New Job] button.
- **[+ New Job]** (top-right, always visible) — opens New Job screen, nothing preselected.

#### Job History tab

```
┌──────────────────────────────────────────────────────────────────┐
│  JOBS                                              [+ New Job]   │
├──────────────────────────────────────────────────────────────────┤
│  [Active Jobs]  [Job History]                                     │
│  ─────────────────────────────────────────────────────────────    │
│                                                                    │
│  Filters:  [All Printers ▼]  [All Products ▼]                    │
│                                                                    │
│  ┌──────────────────────────────────────────────────────────────┐│
│  │ #   Product          Printer   Qty     Status     Date      ││
│  │ 48  Orange 0.33L     Line2     2000    ✅ done    Aug 7     ││
│  │ 47  Apple 0.5L       Line1     500     ✅ done    Aug 7     ││
│  │ 46  Apple 0.5L       Line1     500     ⛔ cancel  Aug 6     ││
│  │ 45  Water Still 0.5L Line3     1000    ✅ done    Aug 6     ││
│  └──────────────────────────────────────────────────────────────┘│
│                                                                    │
│  ─── Job #48 (expanded) ──────────────────────────────────────   │
│  Product:  Orange 0.33L                                           │
│  Printer:  Savema-Line2                                           │
│  Quantity: 2000 / 2000 printed                                    │
│  Duration: 14:30 – 15:12 (42 min)                                 │
│  Result:   Completed successfully                                 │
│                                                                    │
└──────────────────────────────────────────────────────────────────┘
```

Key behaviors:
- All past jobs (completed, cancelled, error), ordered newest first
- **Filters:** by printer, by product. Date range and pagination → Phase 2.
- **View-only** — no action buttons
- Click row to expand: shows codes printed, duration, outcome
- Replaces the old standalone History nav item. The `audit_log` table still records all system events internally; this view focuses on job-level history for operators.

### 6.6 New Job Screen

A dedicated screen for creating and preparing a new print job. **Not a nav item** — accessed via [+ New Job] buttons from other pages.

```
┌──────────────────────────────────────────────────────────────────┐
│  NEW JOB                                             [← Back]    │
├──────────────────────────────────────────────────────────────────┤
│                                                                    │
│  Product:   [ Apple 0.5L            ▼ ]   (8,300 available)       │
│  Printer:   [ Savema-Line1          ▼ ]   (● idle)                │
│  Quantity:  [ 500                     ]                           │
│                                                                    │
│              [Prepare]                                             │
│                                                                    │
│  ─── Preparation Progress ────────────────────────────────────   │
│  ✓ Printer state verified — WAITING (idle)                        │
│  ✓ 500 codes reserved from pool                                   │
│  ✓ CSV uploaded (SPLCDF OK + SPLGSD confirmed)                    │
│  ✓ Template loaded (SPLLTF OK, counter reset to 0)                │
│                                                                    │
│  ✅ Job #49 is ready to print.                                    │
│                                                                    │
│              [Start Print]  [Go to Job]                           │
│                                                                    │
└──────────────────────────────────────────────────────────────────┘
```

Key behaviors:
- **Context preselection:**
  - From Products → product field preselected
  - From Printers → printer field preselected
  - From Dashboard or Jobs → nothing preselected
- **Product selector** — dropdown from product tree. Shows available code count.
- **Printer selector** — shows status per printer. Busy/offline printers greyed out.
- **[Prepare]** — triggers the full preparation flow (§3.1 Step 4). **Navigation is blocked** while preparation is in progress (prevents orphaned half-prepared jobs).
- **Inline progress** — each preparation step checks off as it completes.
- **On success** → shows confirmation with two buttons:
  - **[Start Print]** — starts printing immediately and navigates to Jobs > Active tab with this job selected
  - **[Go to Job]** — navigates to Jobs > Active tab without starting (operator can review before printing)
- **On failure** → error message with [Retry]. Codes returned to pool if reservation happened.
- **[← Back]** — returns to previous page. Disabled during preparation.

### 6.7 Alerts

| Condition | Alert |
|-----------|-------|
| Counter jump > expected | "⚠️ Line1: Unexpected prints detected (+7). Possible external print." |
| Printer disconnected during job | "🔴 Line1: Connection lost. Job #47 paused." |
| Low code stock (< configurable threshold) | "⚠️ Apple 0.5L: only 120 codes remaining" |
| CSV upload failed | "🔴 Line1: CSV upload failed. SPLCDF returned FAIL." |
| Counter reset detected (power cycle) | "⚠️ Line1: Printer counter reset. Please verify printer state." |
| Job completed | "✅ Line2: Job #48 completed (2000/2000)" |
| Printer BLOCKED | "⚠️ Line1: Printer BLOCKED — operator not in main window." |

Alerts live in the **main window shell** (always visible regardless of current page). Error and Warning alerts stay until dismissed; Info alerts auto-dismiss after 30s. See `multi-printer-concurrency.md` §10 for implementation.

---

## 7. Architecture

### 7.1 Layer Diagram

```
┌─────────────────────────────────────────────┐
│              UI Layer (WPF/MVVM)             │
├─────────────────────────────────────────────┤
│           Application Services              │
│  (PrintJobService, ProductService,          │
│   ImportService, AuditService,              │
│   PrinterConnectionManager, AlertService)   │
├─────────────────────────────────────────────┤
│            Domain Model                      │
│  (Code, Product, PrintJob, Printer)         │
├──────────────────────┬──────────────────────┤
│   Data Access        │  Printer Adapters    │
│   (EF Core/SQLite)   │  (IPrinterAdapter)   │
└──────────────────────┴──────────────────────┘
```

### 7.2 Key Services

| Service | Responsibility |
|---------|---------------|
| `ProductService` | CRUD for product tree, template assignment |
| `CodePoolService` | CSV import, code reservation, status transitions, dedup |
| `PrinterConnectionManager` | Owns adapter instances (one per printer), manages TCP connections, handles reconnection with exponential backoff |
| `PrintJobService` | Job lifecycle: prepare → print → monitor → complete/cancel. Holds per-printer service lock for operation exclusivity |
| `JobExecutor` | Per-job polling loop: reads counters, detects anomalies, commits progress, fires events to UI |
| `AlertService` | In-memory alert queue (`ObservableCollection`), auto-dismiss, bridges to `AuditService` for persistence |
| `AuditService` | Log all events with timestamps |

### 7.3 Future Extension Points

When Phase 2 arrives (scanners, recyclers, pipelines):

```
Current:  Product → PrintJob → Printer
Future:   Product → Pipeline → [Printer, Scanner, Recycler, ...]
```

- `IPrinterAdapter` stays as-is
- Add `IScannerAdapter`, `IRecyclerAdapter`
- `PrintJob` evolves into `ProductionRun` (managing the full pipeline)
- Code states gain: `verified`, `rejected`, `aggregated`
- Dashboard gains per-pipeline view

The data model supports this by adding tables (not modifying existing ones):
- `pipelines` (groups devices)
- `pipeline_devices` (device membership)
- Code status enum just gets new values

---

## 8. Tech Stack

| Component | Choice | Rationale |
|-----------|--------|-----------|
| Language | C# / .NET 8 | Native Windows, HikRobot SDK (.NET) ready for Phase 2, strong async |
| UI | WPF + MVVM (CommunityToolkit.Mvvm) | Native Windows desktop, rich data binding, mature |
| Database | SQLite via EF Core | Zero-config, local, survives crashes, no server. WAL mode for concurrent multi-job writes |
| Logging | Serilog → file | Structured, rotated, no dependencies |
| DI | Microsoft.Extensions.DependencyInjection | Standard .NET DI |
| Deployment | Self-contained single-folder publish | No runtime install required on client machines |

### Project Structure (proposed)

```
src/
  PrintManager/
    PrintManager.csproj
    App.xaml / App.xaml.cs
    Models/           -- EF entities (Code, ProductNode, Printer, PrintJob, AuditEntry)
    Data/             -- DbContext, migrations
    Services/         -- Business logic (ProductService, CodePoolService, etc.)
    Adapters/         -- IPrinterAdapter + SavemaTtoAdapter
    ViewModels/       -- MVVM view models
    Views/            -- WPF XAML views
    Converters/       -- Value converters for UI
    Assets/           -- Icons, styles
```

---

## 9. Open Questions (Require Real Printer Testing)

### Mitigated by Design (answer no longer blocks implementation)

These were originally unknowns, but the "always reload template" strategy (§3.1 Step 5) makes the software correct regardless of the answer:

| # | Question | Why it no longer blocks |
|---|----------|----------------------|
| 1 | Does active template survive printer reboot? | We always call `SPLLTF` at job start. Even if it survives, we reload anyway. |
| 2 | Does CSV row pointer survive reboot? | We always upload a fresh CSV and reload the template, resetting the pointer to row 0. |
| 5 | Can CSV be selected independently of template? | We never try to — we always reload the template, which reloads the CSV into the buffer. |

> Still worth testing for general knowledge, but no design decision depends on the answers.

### Still Need Testing

| # | Question | Impact on Software |
|---|----------|-------------------|
| 3 | Does the stored CSV file survive reboot? | If no: recovery flow (§3.4) must re-upload CSV before reloading template. Current design already re-uploads, so low risk — but good to confirm. |
| 4 | Exact command for remaining quantity (`SPPGLQ` vs `SPCGLQ`)? | Tertiary counter check (§3.1 Step 6) needs the correct name. Try both during integration; disable check if neither works. |
| 6 | Does WAITING↔RUNNING transition correspond exactly to completed print? | Could enable event-driven tracking instead of polling. Not critical — polling works fine. |
| 7 | What happens if CSV has fewer rows than print quantity? | Does the printer error, stop, or wrap? We set `SPPSLQ` equal to CSV row count, so this shouldn't happen in normal operation — but we need to know the failure mode for edge cases (e.g., CSV upload was truncated). |

### Recommended First Experiment

```
1. Upload CSV with 5 rows: [A, B, C, D, E]       → SPLCDF
2. Load template                                    → SPLLTF
3. Set quantity: 3                                  → SPPSLQ{3}
4. Start print                                      → SPPSAP
5. Observe: counter = 3, printed A, B, C            → SPGGCP
6. Power cycle printer
7. Read SPGGCP → expected: 0 (confirmed by docs)
8. Read SPGGTP → should reflect +3 from step 5
9. Re-upload CSV: [D, E]                            → SPLCDF
10. Reload template                                  → SPLLTF
11. Set quantity: 1, print                           → SPPSLQ{1}, SPPSAP
12. Observe: is it "D" (fresh CSV loaded correctly)?
```

This test validates: stored CSV persistence (#3), counter behavior, power cycle recovery flow, and the "re-upload remaining codes" strategy from §3.4.

---

## 10. Risk Mitigation

| Risk | Mitigation |
|------|-----------|
| **Duplicate print** (worst case) | Global uniqueness constraint in DB. Code can never return to `available` after `printed`. Quarantine boundary codes on ambiguity (per-printer `QuarantineMargin`, default 0; operator can recover after verification). |
| **Counter reset (power cycle)** | Detect counter < expected. Alert operator. Require explicit re-verify before resuming. |
| **External print (Sayasis used directly)** | Detect counter jump. Alert. Conservatively mark codes as printed. |
| **App crash mid-job** | All state transitions written to SQLite immediately. On restart, job is in `printing` state with last known `codes_confirmed`. Resume or abort. |
| **CSV upload corruption** | TCP guarantees delivery. Require SPLCDF:OK response + SPLGSD existence check. SPLLTF:OK implicitly validates CSV was parseable. |
| **Network disconnect** | Detect TCP disconnect. Pause job. Retry connection with backoff. Alert operator. |
| **Concurrent job interference** | One active job per printer/product enforced by DB partial unique indexes. Per-printer service lock prevents operation interleaving. See `multi-printer-concurrency.md`. |
