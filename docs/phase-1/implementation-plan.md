# Phase 1 — Implementation Plan

> **Purpose:** Detailed task breakdown for completing Phase 1. Every story has been validated against the design spec (`phase1-design.md`, `multi-printer-concurrency.md`, `client-overview.md`) and the actual source code. This document should not need modification — if a story is ambiguous, the relevant design section is cited.

---

## Current State Assessment

The codebase compiles (0 errors, 0 warnings) and has a complete architectural skeleton. Every layer has **functional implementations**, not stubs. However, a systematic audit against the design spec reveals gaps ranging from "app cannot start" to "feature not matching spec."

### What works today

| Layer | Status |
|-------|--------|
| Domain (entities, enums, interfaces, events) | Complete |
| Data (DbContext, configurations, initializer) | Complete, **but no migration generated** |
| Application (all 7 services) | Fully implemented |
| Printer.Savema (adapter, SPPL protocol, factory) | Fully implemented |
| Desktop (ViewModels, Views, Converters, Styles) | Structurally complete, spec gaps below |
| PrinterTestHarness | Complete |
| Tests | Placeholder only (UnitTest1.cs in all 4 projects) |

### What this plan covers

Every task required to go from "compiles" to "production-ready Phase 1 matching the design spec."

---

## Story Point Scale

| Points | Meaning | Typical effort |
|--------|---------|---------------|
| 1 | Trivial — config change, one-liner fix | Minutes |
| 2 | Small — single-file change, straightforward | < half day |
| 3 | Moderate — a few files, some complexity | Half day to full day |
| 5 | Significant — multiple files, cross-cutting | 1-2 days |
| 8 | Complex — new feature spanning layers, edge cases | 2-3 days |
| 13 | Very complex — broad scope, integration-heavy | 3-5 days |

---

## Epic 0: Foundation

> **Must complete first.** Everything else depends on this. No parallelization within this epic.

### E0-1. Generate EF Core initial migration

**Points: 2** | **Depends on: nothing** | **Blocks: everything**

The app calls `db.Database.MigrateAsync()` on startup but no migration exists. Without this, the app crashes immediately.

- Run `dotnet ef migrations add InitialCreate -p src/Infrastructure/CodePrintManager.Data -s src/Hosts/CodePrintManager.Desktop`
- Verify the generated migration matches the schema in `phase1-design.md` §4 (tables, indexes, partial unique indexes)
- Run `dotnet ef database update` to validate
- Remove `db.Database.MigrateAsync()` + `DbInitializer.Initialize()` duplication in `App.xaml.cs` (both call `Migrate()`)

**Acceptance:** `dotnet run --project src/Hosts/CodePrintManager.Desktop` starts without error, creates `codeprintmanager.db` with correct schema.

### E0-2. Wire printer auto-connect on startup

**Points: 3** | **Depends on: E0-1** | **Blocks: E1, E2, E5**

Design (multi-printer-concurrency.md §3): *"App startup → each printer starts its own background connect task (fire-and-forget). Printers show as Connecting... in UI until resolved."*

Currently `App.xaml.cs` initializes the DB but never reads configured printers or connects to them.

- After DB initialization in `App.xaml.cs`, query all active printers
- For each printer, fire-and-forget `PrinterConnectionManager.ConnectAsync(printer)` on a background task
- Printers that fail to connect enter the exponential-backoff reconnect loop (already implemented in `PrinterConnectionManager`)
- No blocking — the main window must appear immediately

**Acceptance:** App starts, MainWindow appears immediately, configured printers begin connecting in the background. Status changes are visible when navigating to Dashboard.

### E0-3. Fix code-to-Available transition for Returned codes

**Points: 2** | **Depends on: nothing** | **Blocks: E2**

Design (phase1-design.md §2): *"returned — Was reserved but job cancelled; back in available pool."*

`CodePoolService.ReturnCodesToPoolAsync` currently sets status to `CodeStatus.Returned`. The design says returned codes re-enter the available pool. The `ReserveCodesAsync` method only selects codes with `Status == Available`, so returned codes are permanently orphaned.

- Option A (recommended): Change `ReturnCodesToPoolAsync` to set `Status = CodeStatus.Available` (and clear `JobId`). The `Returned` enum value documents history via the audit log, not via final status.
- Option B: Change `ReserveCodesAsync` to also select `Returned` codes. This preserves the status for querying but complicates pool logic.

Decision: Option A — matches the design statement *"back in available pool"* literally. The audit trail captures the return event.

**Acceptance:** After cancelling a job, the previously-reserved codes are selectable by the next job's `ReserveCodesAsync`.

### E0-4. Add SPPL forbidden-sequence validation to CSV import

**Points: 3** | **Depends on: nothing** | **Blocks: E3**

Design (phase1-design.md §3.2): *"No code contains SPPL-forbidden sequences: `^`, `~gt~`, `~sc~`, or `~`."*

Design (phase1-design.md §5.4): *"Code values are checked at import time. The adapter also asserts no forbidden content before upload (defense-in-depth)."*

`SpplResponseParser.IsValidCodeValue` already exists and checks for these sequences. But `CodePoolService.ImportCodesAsync` never calls it.

- Call `SpplResponseParser.IsValidCodeValue` (or move to a shared Domain validator) for each code during import
- Reject invalid codes and report in the errors list of `CsvImportResult`
- Add defense-in-depth assertion in `SavemaTtoAdapter.UploadCsvAsync` (log + skip invalid codes, or throw)

**Note:** `IsValidCodeValue` is in the Printer.Savema project. Since Domain cannot reference it, either:
- Move the validation logic to a static method in Domain (preferred — validation rules are domain rules), or
- Have the import service accept an `ICodeValidator` interface

**Acceptance:** Importing a CSV containing `ABC^DEF` or `TEST~gt~123` rejects those specific codes with clear error messages. Remaining valid codes import normally.

---

## Epic 1: Core Print Flow

> **The main use case.** A user can create a job, prepare it, start it, monitor progress, and see it complete. Matches `phase1-design.md` §3.1.

### E1-1. Separate Prepare and Start in NewJobViewModel

**Points: 5** | **Depends on: E0-1, E0-2** | **Blocks: E5, E6**

Design (phase1-design.md §6.6): New Job screen has separate [Prepare] button, shows inline preparation progress (checkmarks per step), then shows [Start Print] / [Go to Job] on success.

Current `NewJobViewModel.StartJobAsync` calls Create + Prepare + Start in one shot with a single "Preparing job..." message.

- Add observable properties: `IsPrepared`, `PrepareStatusItems` (collection of step name + done/failed)
- Split into two commands: `PrepareCommand` and `StartPrintCommand`
- `PrepareCommand`: Create job, show inline progress as each step completes:
  - "Checking printer state..." → done
  - "Reserving codes..." → done
  - "Uploading data file..." → done
  - "Loading template..." → done
- On success: show "Job #N is ready to print." with [Start Print] and [Go to Job] buttons
- `StartPrintCommand`: calls `StartJobAsync`, navigates to Jobs screen
- On prepare failure: show error with [Retry], codes returned if reserved
- Disable navigation (Back button) while preparation is in progress
- Update `NewJobView.xaml` to match design mockup

**Acceptance:** User sees each preparation step complete with a checkmark. After preparation, two buttons appear. Pressing [Start Print] starts the job and navigates to Jobs.

### E1-2. Template upload from disk during Prepare

**Points: 5** | **Depends on: E0-1** | **Blocks: nothing (but improves E1-1)**

Design (phase1-design.md §3.1 Step 4, point 5): *"If template missing → upload .rox file via SPLRTF."*

Current `PrepareJobAsync` checks if the template is on the printer (`ListTemplatesAsync`), and if missing, throws an exception. The design says it should upload the `.rox` file from disk.

- In `PrepareJobAsync`, when template is not on the printer:
  1. Read the `.rox` file from disk using `product.TemplateFile` path
  2. Call `adapter.UploadTemplateAsync(filename, bytes)` (SPLRTF — already implemented)
  3. If upload fails: set job status to `Error`, raise alert with message *"Template upload failed. Load it manually via Sayasis."*
- Add `ProductsViewModel` capability to assign a template file (file dialog → save path to `product.TemplateFile`)
- Validate that the `.rox` file exists on disk before starting preparation

**Acceptance:** If the template isn't on the printer but exists on disk, preparation auto-uploads it. If the file doesn't exist on disk, preparation fails with a clear message.

### E1-3. Wire job progress events to ViewModels

**Points: 5** | **Depends on: E0-2** | **Blocks: E5**

`PrintJobService` raises `JobProgressChanged` and `JobCompleted` events. No ViewModel subscribes to them.

- `JobsViewModel`: subscribe to `JobProgressChanged` → update progress on the active job card; subscribe to `JobCompleted` → move job from active list to history
- `MainViewModel`: subscribe to `JobCompleted` → surface as info alert (already done via AlertService, but verify)
- `DashboardViewModel`: subscribe to `JobProgressChanged` → update the matching `PrinterCardViewModel`'s progress properties
- All subscriptions must dispatch to UI thread

**Note:** `PrintJobService` is registered as **Scoped**, not Singleton. Events on a scoped service are lost between scope resolutions. This is a design bug that must be fixed:
- Option A: Make `PrintJobService` Singleton (careful with `AppDbContext` lifetime — need to create scoped DbContext per operation)
- Option B: Create a separate `IJobEventBus` Singleton that `PrintJobService` publishes to and ViewModels subscribe to
- Option C: Move the event + `_activeJobs` dictionary to `PrinterConnectionManager` (Singleton)

**Recommended:** Option B — cleanest separation. Add `JobEventBus` singleton, inject into `PrintJobService`, have ViewModels subscribe to the bus.

**Acceptance:** Starting a job from New Job screen, then navigating to Dashboard or Jobs, shows live progress updating in real time.

### E1-4. Add file dialogs for CSV import and template assignment

**Points: 3** | **Depends on: E0-1** | **Blocks: E3**

Neither the CSV import button nor template assignment has a file dialog. The XAML buttons exist but there's no `OpenFileDialog` wiring.

- `ProductsView.xaml.cs` code-behind: on Import CSV button click, open `OpenFileDialog` with filter `*.csv`, pass selected path to `ProductsViewModel.ImportCsvCommand`
- Add template assignment: open `OpenFileDialog` with filter `*.rox`, save path to `SelectedProduct.TemplateFile`, save to DB
- Add ability to set `PrinterCsvName` on the product (text field in the detail pane)

**Acceptance:** Clicking "Import CSV..." opens a file browser. Selecting a file triggers the import. Template can be assigned via file browser.

---

## Epic 2: Safety & Recovery

> **Safety-critical features.** These protect against data loss and duplicate codes. References `phase1-design.md` §3.4, `multi-printer-concurrency.md` §9.

### E2-1. Startup recovery flow

**Points: 8** | **Depends on: E0-1, E0-2, E0-3** | **Blocks: nothing**

Design (multi-printer-concurrency.md §9): On startup, detect stale jobs (status = printing/preparing/ready), compare SPGGTP counters, present recovery dialog.

`RecoveryViewModel` and `RecoveryDialog.xaml` exist as shells. The actual recovery logic is not wired.

- In `App.xaml.cs` after DB init and before showing MainWindow:
  1. Query stale jobs via `PrintJobService.GetStaleJobsAsync()`
  2. For `preparing` / `ready` jobs: auto-cancel (return reserved codes, set status = cancelled)
  3. For `printing` jobs: attempt to connect to each job's printer, read SPGGTP, compare with `TotalBaseline`
  4. Build recovery items showing: job info, app-confirmed count, printer-confirmed count, discrepancy
  5. If any printing jobs need resolution: show `RecoveryDialog` as modal
- `RecoveryDialog` UI (matches design mockup in multi-printer-concurrency.md §9):
  - Table of stale jobs with columns: Job #, Product, Printer, App Says, Printer Says, Delta
  - Per-job explanation of discrepancy
  - Per-job buttons: [Resume] and [Abort]
- Resume flow: mark the discrepancy codes as printed, burn +1 if needed, re-upload remaining codes CSV, reload template, restart
- Abort flow: burn ambiguous code, return remaining to pool, set status = cancelled
- If printer is unreachable: show as "Printer offline — connect manually to resolve"

**Acceptance:** After force-killing the app during an active print, relaunching shows the recovery dialog with accurate counter comparison. Choosing Resume or Abort leaves the code pool in a consistent state.

### E2-2. Low code stock alert

**Points: 2** | **Depends on: E0-1** | **Blocks: nothing**

Design (phase1-design.md §6.7): *"Low code stock (< configurable threshold) → warning alert."*

- After each `ReserveCodesAsync`, check remaining available count for that product
- If below threshold (configurable in `appsettings.json`, default 500): raise Warning alert
- Also check after CSV import completion

**Acceptance:** After reserving codes that bring the available count below 500, a warning alert appears: "Apple 0.5L: only 120 codes remaining."

### E2-3. PrintJobService scoping fix

**Points: 5** | **Depends on: E0-1** | **Blocks: E1-3**

`PrintJobService` is registered as **Scoped** but holds `_activeJobs` (a `ConcurrentDictionary<int, JobExecutor>`) and raises events. When the scope ends, the service instance is disposed, along with its event subscriptions. Active `JobExecutor` instances outlive their parent service.

This is a fundamental lifetime mismatch that will cause:
- Lost event subscriptions after scope disposal
- `JobExecutor` holding references to disposed `AppDbContext`
- `_activeJobs` dictionary being empty on next scope resolution

Fix:
- Extract `JobEventBus` as a Singleton: `event EventHandler<JobProgressChangedEvent>? ProgressChanged`, `event EventHandler<JobCompletedEvent>? Completed`
- Extract `ActiveJobRegistry` as a Singleton: `ConcurrentDictionary<int, JobExecutor>` + methods to add/remove/get
- `PrintJobService` remains Scoped for DB operations, but delegates event raising to `JobEventBus` and executor tracking to `ActiveJobRegistry`
- Each `JobExecutor` gets its own scoped `AppDbContext` (created via `IServiceScopeFactory`)
- ViewModels subscribe to `JobEventBus` (Singleton — stable reference)

**Acceptance:** Start a job, navigate away, navigate back — progress is still updating. Events are never lost. No disposed-context exceptions in logs.

---

## Epic 3: Product Management

> **Products screen matching the design.** Reference: `phase1-design.md` §6.3.

### E3-1. Product detail pane — full implementation

**Points: 5** | **Depends on: E0-1, E0-4, E1-4** | **Blocks: nothing**

Design (phase1-design.md §6.3) specifies a detail pane showing: template file, CSV name, code pool stats by status (available/printed/burned/total), import history, [Import CSV] and [+ New Job] buttons.

Current `ProductsView.xaml` detail pane only shows name, available/total counts, and import CSV button.

- Add to `ProductsViewModel`:
  - `TemplateFile` (bound to `SelectedProduct.TemplateFile`)
  - `PrinterCsvName` (bound to `SelectedProduct.PrinterCsvName`)
  - `PrintedCount`, `BurnedCount` (from `CodePoolService.GetPoolStatsAsync`)
  - `ImportHistory` (query audit_log for `event_type = 'import'` and matching product_id)
  - `ChangeTemplateCommand` (opens file dialog)
  - `NewJobCommand` (navigates to New Job with product preselected)
- Update `ProductsView.xaml` to match design mockup:
  - Template row with [Change] button
  - CSV Name row (editable text field)
  - Code Pool stats: Available, Printed, Burned, Total
  - Import History list (date, filename, count)

**Acceptance:** Selecting a leaf product shows its template, CSV name, full pool breakdown, and import history. Clicking [Change] opens a file dialog to assign a `.rox` file.

### E3-2. Add/Delete product tree nodes

**Points: 3** | **Depends on: E0-1** | **Blocks: nothing**

Design (phase1-design.md §6.3) has [+ Add Folder] and [+ Add Product] buttons at the top.

`ProductsViewModel` has `AddProductAsync(string name)` which creates a folder. Need to also support creating leaf products.

- Add `AddFolderCommand` and `AddProductCommand` with input dialogs (simple popup or inline form)
- `AddProductCommand` creates a leaf node (`IsLeaf = true`) and opens template assignment
- Add `DeleteProductCommand` with confirmation dialog
- Validate: cannot delete a product that has active jobs or reserved codes

**Acceptance:** User can create folders and products in the tree, assign templates to products, and delete unused nodes.

---

## Epic 4: Printer Management

> **Storage tab and Verify flow.** Reference: `phase1-design.md` §6.4, §3.3.

### E4-1. Printers Storage tab — list files on printer

**Points: 5** | **Depends on: E0-1, E0-2** | **Blocks: E4-2**

Design (phase1-design.md §6.4): Storage tab lists templates and CSV files on the selected printer, cross-references them with product configurations, and allows deleting unmapped files.

- Add to `PrintersViewModel`:
  - `PrinterTemplates` collection: `{ Name, IsMapped, MappedProductName }`
  - `PrinterCsvFiles` collection: same shape
  - `RefreshStorageCommand`: calls `adapter.ListTemplatesAsync()` and `adapter.ListCsvFilesAsync()`, cross-references with `product_nodes.template_file` and `product_nodes.printer_csv_name`
  - `DeleteSelectedFilesCommand`: deletes selected unmapped files via `adapter.DeleteTemplateAsync` / `adapter.DeleteCsvAsync`
  - Files mapped to a product cannot be selected for deletion (checkbox disabled)
- Update `PrintersView.xaml`:
  - Add TabControl with [Configuration] and [Storage] tabs
  - Storage tab: two DataGrids (templates, CSV files) with checkboxes and status column
  - [Refresh] and [Delete Selected] buttons
- Audit log entry on deletion

**Acceptance:** Selecting a printer and clicking the Storage tab queries the printer for its files, shows which are mapped to products, and allows deleting unmapped files.

### E4-2. Verify flow

**Points: 5** | **Depends on: E4-1** | **Blocks: nothing**

Design (phase1-design.md §3.3): Operator clicks "Verify" on a printer to check stored files, active template, and counters against app records.

- Add `VerifyCommand` to `PrintersViewModel`:
  1. Check stored files (`SPLGSD`): expected CSV present?
  2. Check active template (`SPLGAT`): matches expected?
  3. Read counters: `SPGGTP` vs app's `total_baseline + codes_confirmed` for active job (if any)
  4. Display summary: green/yellow/red per check with details
- Create a `VerifyResultViewModel` with status items
- Show results inline in the Printers screen or as a dialog

**Acceptance:** Clicking Verify on a printer with an active job shows whether files are present, template is correct, and counters match. Mismatches are highlighted with actionable details.

---

## Epic 5: Dashboard & Real-Time

> **Live monitoring.** Reference: `phase1-design.md` §6.2, `multi-printer-concurrency.md` §7.

### E5-1. Dashboard printer cards with live progress

**Points: 8** | **Depends on: E1-3, E2-3** | **Blocks: nothing**

Design (phase1-design.md §6.2): One card per printer (only printers with job history). Each card shows printer name, IP, status, last/current job, progress bar, and action buttons.

Current `PrinterCardViewModel` has basic properties but no job data or actions.

- Redesign `PrinterCardViewModel`:
  - `CurrentJob` (nullable — last or active job for this printer)
  - `CurrentJobProgress`, `CurrentJobTotal`, `ProgressPercent`
  - `JobStatusText` (e.g., "342/500 (68%)" or "Completed Aug 7 14:25")
  - Action commands: `PauseCommand`, `ResumeCommand`, `CancelCommand`, `StartPrintCommand` — visibility based on job status
- Subscribe `PrinterCardViewModel` to `JobEventBus.ProgressChanged` — update progress for matching printer
- Sort cards: running/error first, completed last
- Update `DashboardView.xaml`:
  - Card template matching design mockup (name, IP, status indicator, job info, progress bar, action buttons)
  - Conditional button visibility based on job state
  - Card click navigates to Jobs with that job selected
- Add [+ New Job] button (top-right) — navigates to New Job screen

**Acceptance:** Dashboard shows one card per printer with job history. Active jobs show live progress bars updating every 500ms. Action buttons match job state. Clicking a card navigates to the Jobs screen.

### E5-2. Dashboard recent activity feed

**Points: 3** | **Depends on: E0-1** | **Blocks: nothing**

Design (phase1-design.md §6.2): *"Recent Activity — last ~10 events."*

- Query audit_log for last 10 entries, display as a simple list below printer cards
- Format: "14:30 Job #47 started: Apple 0.5L -> Line1"
- Auto-refresh on `JobCompleted` events

**Acceptance:** Bottom of dashboard shows last 10 audit events in chronological order.

---

## Epic 6: Job Management

> **Active jobs and history.** Reference: `phase1-design.md` §6.5.

### E6-1. Jobs Active tab — full design implementation

**Points: 5** | **Depends on: E1-3, E2-3** | **Blocks: nothing**

Design (phase1-design.md §6.5): Job selector at top, full detail below with product, printer + live status, quantity, preparation checklist, progress bar, action buttons.

Current `JobsView.xaml` has a basic DataGrid. Needs redesign:

- Job selector list (top): all active jobs with mini status
- Selected job detail (bottom): product, printer with live status indicator, quantity, progress bar
- Preparation checklist (if status = Ready): checkmarks for each prepare step
- Contextual action buttons:
  - Ready → [Start Print], [Cancel]
  - Printing → [Cancel] (Pause is a stretch — see E6-3)
  - Completed → summary, no buttons
- Subscribe to `JobEventBus.ProgressChanged` for live progress
- When job completes, keep it displayed until user navigates away

**Acceptance:** Selecting an active job shows full detail with live progress. Action buttons match job state.

### E6-2. Job History tab — filters

**Points: 3** | **Depends on: E0-1** | **Blocks: nothing**

Design (phase1-design.md §6.5): Two filter dropdowns: [All Printers] and [All Products]. Jobs shown newest first.

- Add filter properties to `JobsViewModel`: `SelectedPrinterFilter`, `SelectedProductFilter`
- Load printer and product lists for dropdown options
- Apply filters in `GetJobHistoryAsync` call (already supports `printerId` and `productId` parameters)
- Click row to expand: show codes printed, duration, outcome

**Acceptance:** History tab shows all past jobs. Selecting a printer or product from the dropdown filters the list.

### E6-3. Pause / Resume support

**Points: 8** | **Depends on: E1-3** | **Blocks: nothing**

Design (phase1-design.md §6.2, §6.5) shows [Pause] and [Resume] buttons. The current `JobStatus` enum has no `Paused` state.

This is a significant feature requiring changes across layers:

- Add `Paused` to `JobStatus` enum
- `PrintJobService.PauseJobAsync(int jobId)`:
  1. Acquire printer lock
  2. Signal JobExecutor to stop polling (but don't destroy it)
  3. Call `adapter.StopPrintAsync()` (SPPSTP)
  4. Set job status = Paused
  5. Release printer lock
- `PrintJobService.ResumeJobAsync(int jobId)` (already exists, needs enhancement):
  1. Acquire printer lock
  2. Check printer is still WAITING
  3. Call `adapter.StartPrintAsync()` (SPPSAP)
  4. Set job status = Printing
  5. Restart JobExecutor polling loop
  6. Release printer lock
- Update partial unique indexes to include `Paused` in the active states filter
- Update ViewModels: action button visibility for Paused state
- Update EF migration (alter filtered index)

**Acceptance:** Pausing a printing job stops the printer and freezes progress. Resuming restarts the printer and polling continues from the last confirmed count.

---

## Epic 7: Testing

> **Real test coverage.** Tests can be written in parallel with feature work after E0 completes.

### E7-1. Domain entity and enum tests

**Points: 2** | **Depends on: E0-1** | **Blocks: nothing** | **Parallelizable**

- Test `CodeStatus` transitions (valid and invalid)
- Test `ProductNode` tree relationships
- Test `PrintJob` status rules

### E7-2. SPPL protocol tests

**Points: 3** | **Depends on: nothing** | **Blocks: nothing** | **Parallelizable**

- `SpplCommandBuilder`: verify every command produces correct SPPL string
- `SpplResponseParser.Parse`: test OK, FAIL, scalar, list, status variants
- `SpplResponseParser.ParseStatus`: test WAITING, RUNNING, ERROR with message, BLOCKED
- `SpplResponseParser.IsValidCodeValue`: test all forbidden sequences
- Edge cases: empty payload, malformed response, missing terminator

### E7-3. Data layer tests (in-memory SQLite)

**Points: 5** | **Depends on: E0-1** | **Blocks: nothing** | **Parallelizable**

- Test entity configurations (required fields, defaults, relationships)
- Test partial unique indexes (attempt to insert two active jobs for same printer — expect failure)
- Test code uniqueness constraint
- Test cascade delete behavior

### E7-4. CodePoolService tests

**Points: 5** | **Depends on: E0-1, E0-3, E0-4** | **Blocks: nothing** | **Parallelizable**

- Import: duplicate detection, SPPL validation, batch tracking, import order
- Reserve: FIFO order, quantity validation, status transition
- Return: codes become Available again (E0-3)
- Burn: single code at correct index
- MarkPrinted: range-based marking
- Edge cases: reserve more than available, import empty list, import with all duplicates

### E7-5. PrintJobService tests

**Points: 8** | **Depends on: E2-3, E1-1** | **Blocks: nothing** | **Parallelizable**

- Create: job created with correct initial status
- Prepare: mock adapter, verify SPPL command sequence (delete CSV → upload → verify → check template → activate)
- Start: verify baseline recorded, quantity set, print started, executor spawned
- Cancel while Printing: verify burn +1 logic, code return, printer stop
- Cancel while Preparing: verify all codes returned
- Cancel while Ready: verify all codes returned
- Concurrent: attempt two jobs on same printer — expect failure (partial unique index)

### E7-6. SavemaTtoAdapter integration tests

**Points: 5** | **Depends on: nothing** | **Blocks: nothing** | **Parallelizable**

- Use a mock TCP server that responds with canned SPPL responses
- Test connect, disconnect, reconnect
- Test command serialization through the SemaphoreSlim lock
- Test response parsing end-to-end
- Test timeout behavior, connection loss

### E7-7. ViewModel tests

**Points: 5** | **Depends on: E1-1, E2-3** | **Blocks: nothing** | **Parallelizable**

- Test `NewJobViewModel`: Prepare flow, Start flow, error handling
- Test `ProductsViewModel`: load tree, import CSV, refresh counts
- Test `PrintersViewModel`: add, connect, disconnect, delete
- Test `JobsViewModel`: load active/history, cancel, filter
- Use NSubstitute mocks for all services

---

## Epic 8: Polish & Deployment

> **Final touches.** Depends on all feature epics being complete.

### E8-1. Context preselection for New Job

**Points: 3** | **Depends on: E1-1** | **Blocks: nothing**

Design (phase1-design.md §6.6): *"From Products → product field preselected. From Printers → printer field preselected."*

- Add navigation parameters to `MainViewModel.NavigateTo`: optional `preselectedProductId` and `preselectedPrinterId`
- `NewJobViewModel.LoadAsync` accepts and applies preselection
- [+ New Job] buttons on Products, Printers, Dashboard, and Jobs pass appropriate context

**Acceptance:** Clicking [+ New Job] from the Products page with "Apple 0.5L" selected opens New Job with that product already selected.

### E8-2. Navigation from dashboard cards to Jobs

**Points: 2** | **Depends on: E5-1** | **Blocks: nothing**

Design (phase1-design.md §6.2): *"Clicking a card navigates to Jobs page with that job selected."*

- Add click handler on dashboard printer card
- Navigate to Jobs tab with `preselectedJobId`
- `JobsViewModel` selects and scrolls to that job

**Acceptance:** Clicking a printer card on the Dashboard navigates to Jobs and highlights that job.

### E8-3. Grey out busy printers/products in New Job

**Points: 2** | **Depends on: E1-1** | **Blocks: nothing**

Design (phase1-design.md §6.5): *"Printers with active jobs are greyed out. Products with active jobs are greyed out."*

- In `NewJobViewModel.LoadAsync`, check each printer and product for active jobs
- Disable (but still show) items with active jobs in the ComboBox
- Add tooltip: "This printer has an active job"

**Acceptance:** Creating a new job shows busy printers greyed out with explanation.

### E8-4. Alert bar collapse and scroll

**Points: 2** | **Depends on: E0-1** | **Blocks: nothing**

Design (phase1-design.md §6.7): *"Max 3 visible rows — beyond that, scroll. Empty state: alert bar collapses to zero height."*

Current `MainWindow.xaml` has an alert bar but may not implement collapse or max-height scroll.

- Alert bar: `MaxHeight` to show 3 rows, `ScrollViewer` for overflow
- When `Alerts.Count == 0`, collapse the alert container entirely
- Test with many alerts to verify scrolling

**Acceptance:** Alert bar takes zero space when empty. With >3 alerts, a scrollbar appears.

### E8-5. Self-contained publish and deployment validation

**Points: 3** | **Depends on: all features** | **Blocks: nothing**

- Configure publish profile: `dotnet publish -c Release -r win-x64 --self-contained -o publish/`
- Verify the published folder runs without .NET runtime installed
- Verify `codeprintmanager.db` is created in the app directory
- Verify `logs/` directory is created with Serilog output
- Verify `appsettings.json` is included
- Test on a clean Windows machine (or VM)

**Acceptance:** The `publish/` folder can be copied to any Windows 10/11 machine and run without installing .NET.

---

## Dependency Graph

```
E0-1 (Migration)
 ├──→ E0-2 (Auto-connect) ──→ E1-1 (Prepare/Start split)
 │                          ├──→ E1-3 (Event wiring) ──→ E5-1 (Dashboard cards)
 │                          ├──→ E4-1 (Storage tab) ──→ E4-2 (Verify)
 │                          └──→ E2-1 (Recovery)
 ├──→ E0-3 (Returned→Available) ──→ E2-1 (Recovery)
 ├──→ E0-4 (SPPL validation) ──→ E3-1 (Product detail)
 ├──→ E1-2 (Template upload)
 ├──→ E1-4 (File dialogs) ──→ E3-1 (Product detail)
 ├──→ E2-2 (Low stock alert)
 ├──→ E2-3 (Scoping fix) ──→ E1-3 (Event wiring)
 │                        ──→ E6-1 (Jobs Active)
 ├──→ E3-2 (Tree nodes)
 ├──→ E5-2 (Recent activity)
 ├──→ E6-2 (History filters)
 └──→ E7-* (Tests — after relevant features)
```

---

## Parallelization Matrix

Tasks on the same row can run concurrently (different files, no conflicts):

```
Phase 1 (Foundation):
  [E0-1] → [E0-2] sequentially, then:

Phase 2 (can all run in parallel after E0):
  Track A: [E0-3] + [E0-4] + [E2-3]     ← service-layer fixes
  Track B: [E1-2] + [E1-4]               ← template upload + file dialogs
  Track C: [E7-2]                         ← SPPL protocol tests (zero deps)

Phase 3 (after their deps in Phase 2):
  Track A: [E1-1]                         ← Prepare/Start split (needs E0-2)
  Track B: [E4-1]                         ← Storage tab (needs E0-2)
  Track C: [E3-1] + [E3-2]               ← Product management (needs E0-4, E1-4)
  Track D: [E7-1] + [E7-3] + [E7-4]      ← Tests (need E0 fixes)

Phase 4 (after Phase 3 deps):
  Track A: [E1-3]                         ← Event wiring (needs E2-3)
  Track B: [E4-2]                         ← Verify flow (needs E4-1)
  Track C: [E2-1]                         ← Recovery (needs E0-2, E0-3)
  Track D: [E6-2] + [E2-2]               ← History filters + low stock alert
  Track E: [E7-5] + [E7-6]               ← Service + adapter tests

Phase 5 (after event wiring):
  Track A: [E5-1] + [E5-2]               ← Dashboard live
  Track B: [E6-1]                         ← Jobs Active tab
  Track C: [E6-3]                         ← Pause/Resume
  Track D: [E7-7]                         ← ViewModel tests

Phase 6 (polish):
  [E8-1] + [E8-2] + [E8-3] + [E8-4]     ← all parallel
  [E8-5]                                  ← final validation
```

---

## Summary

| Epic | Stories | Total SP | Critical Path? |
|------|---------|----------|---------------|
| E0: Foundation | 4 | 10 | Yes |
| E1: Core Print Flow | 4 | 18 | Yes |
| E2: Safety & Recovery | 3 | 15 | Yes |
| E3: Product Management | 2 | 8 | No |
| E4: Printer Management | 2 | 10 | No |
| E5: Dashboard & Real-Time | 2 | 11 | No |
| E6: Job Management | 3 | 16 | No |
| E7: Testing | 7 | 33 | No |
| E8: Polish & Deployment | 5 | 12 | No |
| **Total** | **32 stories** | **133 SP** | |

### Critical path (shortest path to "works end-to-end")

```
E0-1 (2) → E0-2 (3) → E2-3 (5) → E1-1 (5) → E1-3 (5) → E5-1 (8)
Total: 28 SP
```

### Minimum viable demo (operator can print)

```
E0-1 + E0-2 + E0-3 + E1-1 + E1-4 = 18 SP
```

After these 5 stories, an operator can: start the app, configure a printer, import codes, create a job with separate prepare/start steps, and see it complete. No live dashboard, no recovery, no storage management — but the core flow works.

---

## Status Matrix

Current implementation status of every feature area in the design spec.

### Legend

| Status | Meaning |
|--------|---------|
| Done | Fully implemented and matches design spec |
| Partial | Code exists but has gaps vs design spec (details noted) |
| Bug | Implemented but has a correctness issue |
| Not Started | No implementation exists |

---

### Domain Layer

| Component | Status | Notes |
|-----------|--------|-------|
| `ProductNode` entity | Done | All fields match §4 schema |
| `Code` entity | Done | All fields, nav properties |
| `Printer` entity | Done | All fields match §4 schema |
| `PrintJob` entity | Done | All fields match §4 schema |
| `AuditEntry` entity | Done | |
| `CodeStatus` enum | Done | Available, Reserved, Printed, Returned, Burned |
| `JobStatus` enum | Partial | Missing `Paused` state (needed for E6-3) |
| `PrinterStatus` enum | Done | Offline, Init, Idle, Printing, Error, Blocked |
| `AlertSeverity` enum | Done | |
| `IPrinterAdapter` interface | Done | 16 methods, matches §5.1 |
| `IPrinterAdapterFactory` interface | Done | |
| `IProductService` interface | Done | |
| `ICodePoolService` interface | Done | |
| `IPrintJobService` interface | Partial | Missing `PauseJobAsync` (E6-3) |
| `IAlertService` interface | Done | |
| `IAuditService` interface | Done | |
| `ICurrentUser` interface | Done | Placeholder for future auth |
| Domain events (4 records) | Done | Progress, Completed, StatusChanged, Alert |

### Data Layer

| Component | Status | Notes |
|-----------|--------|-------|
| `AppDbContext` | Done | All 5 DbSets |
| `DbInitializer` (WAL mode) | Done | PRAGMA WAL + busy_timeout=5000 |
| `ProductNodeConfiguration` | Done | Self-referencing tree, indexes |
| `CodeConfiguration` | Done | Unique constraint, composite indexes |
| `PrinterConfiguration` | Done | |
| `PrintJobConfiguration` | Done | Partial unique indexes for concurrency guards |
| `AuditEntryConfiguration` | Done | |
| **EF Core migration** | **Not Started** | No migration generated — app crashes on startup (E0-1) |

### Application Services

| Component | Status | Notes |
|-----------|--------|-------|
| `ProductService` | Done | Full CRUD, tree operations |
| `CodePoolService` | Partial | Missing SPPL forbidden-sequence validation at import (E0-4) |
| `CodePoolService.ReturnCodesToPoolAsync` | Bug | Sets `Returned` status but codes never re-enter pool (E0-3) |
| `PrintJobService.CreateJobAsync` | Done | |
| `PrintJobService.PrepareJobAsync` | Partial | Template not uploaded from disk if missing (E1-2) |
| `PrintJobService.StartJobAsync` | Done | Records baseline, sets qty, starts, spawns executor |
| `PrintJobService.CancelJobAsync` | Done | Burn +1 logic correct |
| `PrintJobService` (lifetime) | Bug | Registered Scoped but holds Singleton state (`_activeJobs`, events) (E2-3) |
| `JobExecutor` | Done | Poll loop, anomaly detection, commit, complete |
| `PrinterConnectionManager` | Done | Factory lookup, connect, reconnect with backoff |
| `AlertService` | Done | Events, auto-dismiss, audit bridge |
| `AuditService` | Done | JSON serialization, DB persist |
| `ServiceCollectionExtensions` | Done | All registrations |

### Printer.Savema

| Component | Status | Notes |
|-----------|--------|-------|
| `SavemaTtoAdapter` | Done | Full IPrinterAdapter (16 methods), TCP + SemaphoreSlim |
| `SavemaAdapterFactory` | Done | `CanHandle("savema*")` → creates adapter |
| `SpplCommandBuilder` | Done | All 15 SPPL commands |
| `SpplResponseParser` | Done | Parse, ParseStatus, IsValidCodeValue |
| `SpplConstants` | Done | Delimiters, timeouts, forbidden sequences |
| `SpplResponse` | Done | IsOk, IsFail, Payload, AsInt, AsList |

### Desktop — App Shell

| Component | Status | Notes |
|-----------|--------|-------|
| `App.xaml.cs` (DI, Host, Serilog) | Partial | Missing printer auto-connect (E0-2), recovery check (E2-1) |
| `appsettings.json` | Done | Poll interval, reconnect settings, thresholds |
| `MainWindow.xaml` (nav + content + alerts) | Done | Sidebar, content area, alert bar |
| `MainViewModel` (navigation, alerts) | Done | Event subscription, alert collection, dispatch |
| Alert bar (collapse, scroll) | Partial | Needs max-height scroll and collapse when empty (E8-4) |
| Theme.xaml + Controls.xaml | Done | Colors, card styles, button styles |
| Converters (Bool, Null, Status) | Done | |

### Desktop — Dashboard (§6.2)

| Feature | Status | Notes |
|---------|--------|-------|
| Summary cards (active jobs, available, printed today) | Done | |
| Printer cards (one per printer) | Partial | No job info, no progress bar data, no actions |
| Live progress on cards | Not Started | No event subscription (E5-1) |
| Action buttons (Pause/Resume/Cancel/Start) | Not Started | (E5-1) |
| Card click → navigate to Jobs | Not Started | (E8-2) |
| Recent activity feed | Not Started | (E5-2) |
| [+ New Job] button | Not Started | (E8-1) |
| Sort: running first, completed last | Not Started | (E5-1) |

### Desktop — Products (§6.3)

| Feature | Status | Notes |
|---------|--------|-------|
| Tree view (expand/collapse) | Done | HierarchicalDataTemplate |
| Product detail pane — name | Done | |
| Product detail pane — available/total counts | Done | |
| Product detail pane — template file + [Change] | Not Started | (E3-1) |
| Product detail pane — CSV name | Not Started | (E3-1) |
| Product detail pane — pool stats by status | Not Started | (E3-1) |
| Product detail pane — import history | Not Started | (E3-1) |
| Import CSV with file dialog | Partial | ViewModel done, file dialog not wired (E1-4) |
| [+ New Job] on product | Not Started | (E8-1) |
| [+ Add Folder] / [+ Add Product] | Partial | ViewModel has AddProduct, no UI buttons/dialogs (E3-2) |
| Delete product node | Partial | No confirmation, no active-job guard (E3-2) |

### Desktop — Printers (§6.4)

| Feature | Status | Notes |
|---------|--------|-------|
| Printer list | Done | |
| Add printer form | Done | Name, IP, port, adapter type |
| Connect / Disconnect buttons | Done | |
| Delete printer | Done | |
| Configuration tab | Done | (Current implementation is effectively the Config tab) |
| Storage tab — list templates on printer | Not Started | (E4-1) |
| Storage tab — list CSV files on printer | Not Started | (E4-1) |
| Storage tab — cross-reference with products | Not Started | (E4-1) |
| Storage tab — delete unmapped files | Not Started | (E4-1) |
| Verify flow | Not Started | (E4-2) |
| [+ New Job] on printer | Not Started | (E8-1) |
| Test Connection button | Not Started | |

### Desktop — Jobs (§6.5)

| Feature | Status | Notes |
|---------|--------|-------|
| Active Jobs tab — job list | Done | DataGrid with active jobs |
| Active Jobs tab — selected job detail | Partial | Basic info shown, no prep checklist or live status |
| Active Jobs tab — live progress bar | Not Started | No event subscription (E6-1) |
| Active Jobs tab — action buttons | Partial | Cancel exists, no Start/Pause/Resume (E6-1) |
| Job History tab — list | Done | Shows completed/cancelled |
| Job History tab — filters (printer, product) | Not Started | (E6-2) |
| Job History tab — expanded row detail | Not Started | (E6-2) |
| [+ New Job] button | Not Started | (E8-1) |

### Desktop — New Job (§6.6)

| Feature | Status | Notes |
|---------|--------|-------|
| Product selector (dropdown, leaf only) | Done | |
| Printer selector (dropdown) | Done | |
| Quantity field | Done | |
| Available count display | Done | Updates on product selection |
| Separate [Prepare] step | Not Started | Currently one-shot Start (E1-1) |
| Inline preparation progress (checkmarks) | Not Started | (E1-1) |
| [Start Print] / [Go to Job] after prepare | Not Started | (E1-1) |
| On failure: error + [Retry] | Partial | Shows error, no Retry button |
| Block navigation during prepare | Not Started | (E1-1) |
| Context preselection | Not Started | (E8-1) |
| Grey out busy printers/products | Not Started | (E8-3) |

### Desktop — Recovery (§3.4)

| Feature | Status | Notes |
|---------|--------|-------|
| `RecoveryViewModel` | Done | Shell with stale job loading and resume/cancel |
| `RecoveryDialog.xaml` | Done | Basic layout exists |
| Startup detection of stale jobs | Not Started | Not wired in App.xaml.cs (E2-1) |
| SPGGTP counter comparison | Not Started | (E2-1) |
| Discrepancy display (app vs printer) | Not Started | (E2-1) |
| Per-job [Resume] / [Abort] with code cleanup | Not Started | (E2-1) |
| Show dialog modal on startup | Not Started | (E2-1) |

### Cross-Cutting

| Feature | Status | Notes |
|---------|--------|-------|
| Pause/Resume support | Not Started | No `Paused` state, no SPPSTP/SPPSAP toggle (E6-3) |
| Low code stock alert | Not Started | (E2-2) |
| Context preselection (all [+ New Job] buttons) | Not Started | (E8-1) |
| Navigation with context (card click, Go to Job) | Not Started | (E8-2) |
| Self-contained publish | Not Started | (E8-5) |

### Testing

| Project | Status | Notes |
|---------|--------|-------|
| `CodePrintManager.Domain.Tests` | Placeholder | 1 empty test |
| `CodePrintManager.Data.Tests` | Placeholder | 1 empty test |
| `CodePrintManager.Printer.Savema.Tests` | Placeholder | 1 empty test |
| `CodePrintManager.Application.Tests` | Placeholder | 1 empty test |

---

### Totals by Status

| Status | Count |
|--------|-------|
| Done | 56 |
| Partial | 14 |
| Bug | 3 |
| Not Started | 38 |
| Placeholder | 4 |
| **Total line items** | **115** |
