# Phase 1 — Implementation Plan

> **Purpose:** Detailed task breakdown for completing Phase 1. Every story has been validated against the design spec (`phase1-design.md`, `multi-printer-concurrency.md`, `client-overview.md`) and the actual source code. This document should not need modification — if a story is ambiguous, the relevant design section is cited.

---

## Current State Assessment

> **Last updated: 2026-08-09**

The codebase compiles (0 errors, 0 warnings), all 4 test suites pass, and the application starts correctly. Foundation work (Epic 0) is complete, along with the critical scoping fix (E2-3) and several feature stories across Epics 1–3. The UI has been fully redesigned to match the screen specifications.

### What works today

| Layer | Status |
|-------|--------|
| Domain (entities, enums, interfaces, events, validation) | Complete |
| Data (DbContext, configurations, initializer, migration) | Complete |
| Application (all 7 services + JobEventBus + ActiveJobRegistry) | Fully implemented |
| Printer.Savema (adapter, SPPL protocol, factory) | Fully implemented |
| Desktop (ViewModels, Views, Converters, Styles) | Redesigned to match spec; most features wired |
| PrinterTestHarness | Complete |
| Tests | Placeholder only (UnitTest1.cs in all 4 projects) |

### What this plan covers

Every task required to go from "compiles" to "production-ready Phase 1 matching the design spec."

### Progress Summary

| Epic | Stories Done | Stories Remaining | SP Done | SP Remaining |
|------|-------------|-------------------|---------|--------------|
| E0: Foundation | 4/4 | 0 | 10 | 0 |
| E1: Core Print Flow | 3/4 | 1 (E1-1) | 13 | 5 |
| E2: Safety & Recovery | 3/3 | 0 | 15 | 0 |
| E3: Product Management | 1/2 | 1 (E3-2) | 5 | 3 |
| E4: Printer Management | 0/2 | 2 | 0 | 10 |
| E5: Dashboard & Real-Time | 0/2 | 2 | 0 | 11 |
| E6: Job Management | 0/3 | 3 | 0 | 16 |
| E7: Testing | 0/7 | 7 | 0 | 33 |
| E8: Polish & Deployment | 0/5 | 5 | 0 | 12 |
| **Total** | **11/32** | **21** | **43** | **90** |

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

### E0-1. Generate EF Core initial migration — DONE

**Points: 2** | **Depends on: nothing** | **Blocks: everything** | **Status: DONE**

Migration was already generated (Aug 8). App starts and creates `codeprintmanager.db` with correct schema.

### E0-2. Wire printer auto-connect on startup — DONE

**Points: 3** | **Depends on: E0-1** | **Blocks: E1, E2, E5** | **Status: DONE**

Implemented in `App.xaml.cs`. After DB init, queries all configured printers and fire-and-forget calls `ConnectAsync` for each. Failed connections enter exponential-backoff reconnect loop. Main window appears immediately.

### E0-3. Fix code-to-Available transition for Returned codes — DONE

**Points: 2** | **Depends on: nothing** | **Blocks: E2** | **Status: DONE**

`ReturnCodesToPoolAsync` now sets `Status = CodeStatus.Available` and clears `JobId`. Returned codes immediately re-enter the available pool.

### E0-4. Add SPPL forbidden-sequence validation to CSV import — DONE

**Points: 3** | **Depends on: nothing** | **Blocks: E3** | **Status: DONE**

Created `Domain/Validation/CodeValidator.cs` with forbidden sequences (`^`, `~gt~`, `~sc~`, `~`). `CodePoolService.ImportCodesAsync` validates each code before import. Invalid codes are rejected with clear error messages per row.

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

### E1-2. Template upload from disk during Prepare — DONE

**Points: 5** | **Depends on: E0-1** | **Blocks: nothing** | **Status: DONE**

`PrepareJobAsync` now auto-uploads `.rox` file from disk if template is missing on printer. Reads file bytes and calls `adapter.UploadTemplateAsync`. On failure, raises error alert with guidance. If file not on disk, throws with clear message.

### E1-3. Wire job progress events to ViewModels — DONE

**Points: 5** | **Depends on: E0-2, E2-3** | **Blocks: E5** | **Status: DONE**

Implemented via `JobEventBus` singleton (Option B). `DashboardViewModel` subscribes to `ProgressChanged` for printer card updates. `JobsViewModel` subscribes for real-time progress text on detail pane. Both subscribe to `Completed` for auto-refresh on job finish. All dispatched to UI thread.

### E1-4. Add file dialogs for CSV import and template assignment — DONE

**Points: 3** | **Depends on: E0-1** | **Blocks: E3** | **Status: DONE**

CSV import dialog (`ImportCsvAsync`) fully functional with `OpenFileDialog` for `*.csv`. Template assignment via `ChangeTemplateAsync` with `*.rox` file picker. CSV name editable in product detail pane with Save button.

---

## Epic 2: Safety & Recovery

> **Safety-critical features.** These protect against data loss and duplicate codes. References `phase1-design.md` §3.4, `multi-printer-concurrency.md` §9.

### E2-1. Startup recovery flow — DONE

**Points: 8** | **Depends on: E0-1, E0-2, E0-3** | **Blocks: nothing** | **Status: DONE**

Fully implemented in `App.xaml.cs` via `RunStartupRecoveryAsync`. Auto-cancels Preparing/Ready stale jobs (returns reserved codes). For Printing jobs: reads SPGGTP counter, computes discrepancy vs `TotalBaseline`. Shows `RecoveryDialog` modal with columns: Job #, Product, Printer, App Says, Printer Says, Delta. Per-job Resume/Abort buttons. `RecoveryViewModel` and `RecoveryItemViewModel` fully implemented. Offline printers shown as "Offline" in delta column.

### E2-2. Low code stock alert — DONE

**Points: 2** | **Depends on: E0-1** | **Blocks: nothing** | **Status: DONE**

`CodePoolService` checks remaining available count after `ReserveCodesAsync`. If below threshold (500 codes), raises Warning alert via `IAlertService`: `"{ProductName}: only {N} codes remaining."`

### E2-3. PrintJobService scoping fix — DONE

**Points: 5** | **Depends on: E0-1** | **Blocks: E1-3** | **Status: DONE**

Extracted `JobEventBus` singleton (events) and `ActiveJobRegistry` singleton (executor + printer lock tracking). `PrintJobService` remains Scoped for DB operations. Events published to both bus (for ViewModels) and local (backward compatibility). Registered in `ServiceCollectionExtensions`.

---

## Epic 3: Product Management

> **Products screen matching the design.** Reference: `phase1-design.md` §6.3.

### E3-1. Product detail pane — full implementation — DONE

**Points: 5** | **Depends on: E0-1, E0-4, E1-4** | **Blocks: nothing** | **Status: DONE**

Detail pane shows: template file with [Change] button, editable CSV name with [Save] button, code pool stats (Available/Printed/Burned/Total), import history list, [Import CSV...], [+ New Job], and [Delete] buttons. All wired to ViewModel commands.

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
✅ E0-1 (Migration)
 ├──→ ✅ E0-2 (Auto-connect) ──→ E1-1 (Prepare/Start split)
 │                             ├──→ ✅ E1-3 (Event wiring) ──→ E5-1 (Dashboard cards)
 │                             ├──→ E4-1 (Storage tab) ──→ E4-2 (Verify)
 │                             └──→ ✅ E2-1 (Recovery)
 ├──→ ✅ E0-3 (Returned→Available) ──→ ✅ E2-1 (Recovery)
 ├──→ ✅ E0-4 (SPPL validation) ──→ ✅ E3-1 (Product detail)
 ├──→ ✅ E1-2 (Template upload)
 ├──→ ✅ E1-4 (File dialogs) ──→ ✅ E3-1 (Product detail)
 ├──→ ✅ E2-2 (Low stock alert)
 ├──→ ✅ E2-3 (Scoping fix) ──→ ✅ E1-3 (Event wiring)
 │                            ──→ E6-1 (Jobs Active)
 ├──→ E3-2 (Tree nodes)
 ├──→ E5-2 (Recent activity)      ← partially done (activity feed exists)
 ├──→ E6-2 (History filters)
 └──→ E7-* (Tests — after relevant features)

✅ = DONE
```

---

## Parallelization Matrix

> Updated 2026-08-09. ✅ = completed.

```
Phase 1 (Foundation):                       ✅ ALL DONE
  ✅ [E0-1] → ✅ [E0-2]

Phase 2 (service-layer fixes):              ✅ ALL DONE
  ✅ [E0-3] + ✅ [E0-4] + ✅ [E2-3]
  ✅ [E1-2] + ✅ [E1-4]

Phase 3 (features):                         ✅ MOSTLY DONE (E1-1 remaining)
  Track A: [E1-1]                           ← Prepare/Start split — NEXT UP
  Track B: [E4-1]                           ← Storage tab — READY
  Track C: ✅ [E3-1] + [E3-2]              ← E3-2 remaining (tree node guards)
  Track D: [E7-1] + [E7-3] + [E7-4]        ← Tests — READY
  Track E: [E7-2]                           ← SPPL tests — READY (zero deps)

Phase 4 (after Phase 3 deps):              READY NOW (deps met)
  Track A: ✅ [E1-3]
  Track B: [E4-2]                           ← Verify flow (needs E4-1)
  Track C: ✅ [E2-1] + ✅ [E2-2]
  Track D: [E6-2]                           ← History filters — READY
  Track E: [E7-5] + [E7-6]                 ← Service + adapter tests — READY

Phase 5 (dashboard + jobs):                 READY NOW (E1-3 done)
  Track A: [E5-1] + [E5-2]                 ← Dashboard live (E5-2 partially done)
  Track B: [E6-1]                           ← Jobs Active tab — READY
  Track C: [E6-3]                           ← Pause/Resume
  Track D: [E7-7]                           ← ViewModel tests

Phase 6 (polish):
  [E8-1] + [E8-2] + [E8-3] + [E8-4]       ← all parallel (E8-1/E8-2 partially done)
  [E8-5]                                    ← final validation
```

---

## Summary

| Epic | Stories | Done | Total SP | SP Done | Critical Path? |
|------|---------|------|----------|---------|---------------|
| E0: Foundation | 4 | **4/4** | 10 | 10 | Yes |
| E1: Core Print Flow | 4 | **3/4** | 18 | 13 | Yes |
| E2: Safety & Recovery | 3 | **3/3** | 15 | 15 | Yes |
| E3: Product Management | 2 | **1/2** | 8 | 5 | No |
| E4: Printer Management | 2 | 0/2 | 10 | 0 | No |
| E5: Dashboard & Real-Time | 2 | 0/2 | 11 | 0 | No |
| E6: Job Management | 3 | 0/3 | 16 | 0 | No |
| E7: Testing | 7 | 0/7 | 33 | 0 | No |
| E8: Polish & Deployment | 5 | 0/5 | 12 | 0 | No |
| **Total** | **32** | **11/32** | **133 SP** | **43 SP** | |

### Critical path (shortest path to "works end-to-end")

```
✅ E0-1 (2) → ✅ E0-2 (3) → ✅ E2-3 (5) → E1-1 (5) → ✅ E1-3 (5) → E5-1 (8)
                                              ↑
                                       REMAINING: 13 SP
```

### Minimum viable demo (operator can print)

```
✅ E0-1 + ✅ E0-2 + ✅ E0-3 + E1-1 + ✅ E1-4 = 18 SP (13 SP done, 5 SP remaining)
```

Only **E1-1** (Separate Prepare/Start in NewJobViewModel) remains for the minimum viable demo. After that, an operator can: start the app, configure a printer, import codes, create a job with separate prepare/start steps, and see it complete.

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
| **EF Core migration** | **Done** | Migration generated and applied (E0-1) |

### Application Services

| Component | Status | Notes |
|-----------|--------|-------|
| `ProductService` | Done | Full CRUD, tree operations |
| `CodePoolService` | Done | SPPL forbidden-sequence validation via `CodeValidator` (E0-4), low stock alert (E2-2) |
| `CodePoolService.ReturnCodesToPoolAsync` | Done | Sets `Available` status and clears `JobId` (E0-3) |
| `PrintJobService.CreateJobAsync` | Done | |
| `PrintJobService.PrepareJobAsync` | Done | Auto-uploads .rox template from disk if missing on printer (E1-2) |
| `PrintJobService.StartJobAsync` | Done | Records baseline, sets qty, starts, spawns executor |
| `PrintJobService.CancelJobAsync` | Done | Burn +1 logic correct |
| `PrintJobService` (lifetime) | Done | Fixed: `JobEventBus` + `ActiveJobRegistry` singletons extracted (E2-3) |
| `JobExecutor` | Done | Poll loop, anomaly detection, commit, complete |
| `PrinterConnectionManager` | Done | Factory lookup, connect, reconnect with backoff |
| `AlertService` | Done | Events, auto-dismiss, audit bridge |
| `AuditService` | Done | JSON serialization, DB persist |
| `ServiceCollectionExtensions` | Done | All registrations including `ActiveJobRegistry` + `JobEventBus` singletons |

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
| `App.xaml.cs` (DI, Host, Serilog) | Done | Printer auto-connect (E0-2), startup recovery (E2-1) wired |
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
| Printer cards (one per printer) | Partial | Shows job info and status, needs full redesign per E5-1 |
| Live progress on cards | Partial | Event subscription wired (E1-3), card property updates done; full UI polish in E5-1 |
| Action buttons (Pause/Resume/Cancel/Start) | Partial | Start/Cancel wired, Pause/Resume needs E6-3 |
| Card click → navigate to Jobs | Done | Wired in DashboardViewModel (E8-2) |
| Recent activity feed | Done | Last 20 audit entries shown (E5-2) |
| [+ New Job] button | Done | Navigates to New Job screen (E8-1) |
| Sort: running first, completed last | Done | Cards sorted by job status priority |

### Desktop — Products (§6.3)

| Feature | Status | Notes |
|---------|--------|-------|
| Tree view (expand/collapse) | Done | HierarchicalDataTemplate |
| Product detail pane — name | Done | |
| Product detail pane — available/total counts | Done | |
| Product detail pane — template file + [Change] | Done | Template path shown with [Change] button (E3-1) |
| Product detail pane — CSV name | Done | Editable field with [Save] button (E3-1) |
| Product detail pane — pool stats by status | Done | Available/Printed/Burned/Total (E3-1) |
| Product detail pane — import history | Done | Date + details from audit log (E3-1) |
| Import CSV with file dialog | Done | OpenFileDialog wired (E1-4) |
| [+ New Job] on product | Done | NavigateToNewJobRequested event (E8-1) |
| [+ Add Folder] / [+ Add Product] | Done | Inline forms with Create/Cancel buttons, ViewModel commands |
| Delete product node | Partial | [Delete] button added, no confirmation dialog or active-job guard (E3-2) |

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
| Active Jobs tab — live progress bar | Partial | Event subscription wired (E1-3), progress text updates; full UI bar in E6-1 |
| Active Jobs tab — action buttons | Partial | Cancel and Start exist, Pause/Resume needs E6-3 |
| Job History tab — list | Done | Shows completed/cancelled |
| Job History tab — filters (printer, product) | Not Started | (E6-2) |
| Job History tab — expanded row detail | Not Started | (E6-2) |
| [+ New Job] button | Done | Navigates to New Job screen |

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
| `RecoveryViewModel` | Done | Full implementation with `RecoveryItemViewModel`, Resume/Abort commands |
| `RecoveryDialog.xaml` | Done | Table with Job #, Product, Printer, App Says, Printer Says, Delta columns |
| Startup detection of stale jobs | Done | `RunStartupRecoveryAsync` in App.xaml.cs (E2-1) |
| SPGGTP counter comparison | Done | Reads lifetime counter, computes delta vs TotalBaseline (E2-1) |
| Discrepancy display (app vs printer) | Done | Shows delta or "Offline" if printer unreachable (E2-1) |
| Per-job [Resume] / [Abort] with code cleanup | Done | Resume/Abort buttons per selected job (E2-1) |
| Show dialog modal on startup | Done | Modal shown before MainWindow if printing jobs exist (E2-1) |

### Cross-Cutting

| Feature | Status | Notes |
|---------|--------|-------|
| Pause/Resume support | Not Started | No `Paused` state, no SPPSTP/SPPSAP toggle (E6-3) |
| Low code stock alert | Done | Warning at < 500 codes after reserve (E2-2) |
| Context preselection (all [+ New Job] buttons) | Partial | Products → New Job preselection done; Printers → New Job done; full E8-1 remaining |
| Navigation with context (card click, Go to Job) | Partial | Dashboard card click → Jobs done; full E8-2 remaining |
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
| Done | 80 |
| Partial | 12 |
| Bug | 0 |
| Not Started | 19 |
| Placeholder | 4 |
| **Total line items** | **115** |

### What's Next (recommended priority order)

1. **E1-1** (5 SP) — Separate Prepare/Start in NewJobViewModel (critical path for end-to-end flow)
2. **E5-1** (8 SP) — Dashboard printer cards with live progress (depends on E1-3 done)
3. **E6-1** (5 SP) — Jobs Active tab full design (depends on E1-3 done)
4. **E4-1** (5 SP) — Printers Storage tab (depends on E0-2 done)
5. **E3-2** (3 SP) — Add/Delete product tree nodes (confirmation dialog, active-job guard)
6. **E6-2** (3 SP) — Job History tab filters
7. **E4-2** (5 SP) — Verify flow (depends on E4-1)
8. **E6-3** (8 SP) — Pause/Resume support
9. **E7-**** (33 SP) — Testing (can run in parallel with features)
10. **E8-**** (12 SP) — Polish & Deployment (final phase)
