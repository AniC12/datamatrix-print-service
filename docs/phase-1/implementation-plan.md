# Phase 1 — Implementation Plan

> **Purpose:** Detailed task breakdown for completing Phase 1. Every story has been validated against the design spec (`phase1-design.md`, `multi-printer-concurrency.md`, `client-overview.md`) and the actual source code. This document should not need modification — if a story is ambiguous, the relevant design section is cited.

---

## Current State Assessment

> **Last updated: 2026-08-30**

The codebase compiles (0 errors), all 5 test suites pass (279 tests), and the application starts correctly. **29 of 40 stories complete (122 SP / 164 SP = 74%).** The critical path (end-to-end flow) and minimum viable demo are fully functional. Epics 0–6, E9, and E10 are complete. E7-2 (SPPL tests) done; E7-1 and E7-7 partial. Remaining work: E7 (testing gaps) and E8 (polish/deployment).

**Recent fixes (not tied to a story):**
- **Job completion UI bug** — `JobsViewModel.OnJobCompleted` and `DashboardViewModel.OnJobCompleted` used `Dispatcher.Invoke(async () => ...)`, which silently became `async void` and swallowed DB query exceptions. Jobs stayed stuck on "Printing" status after the simulator reported completion. Fixed by using synchronous in-place updates from `JobCompletedEvent.FinalStatus` instead of re-querying the DB.
- **Savema simulator** — `demo/savema_simulator.py` enhanced: persistent TCP connections, BLOCKED/Stop-Position enforcement, auto-print counter simulation, duplicate CSV prevention, human-readable command logging.
- **New projects added** — `Printer.Mock` (in-memory mock adapter, `--mock` flag), `TestHost` (ASP.NET Core minimal API for integration tests), `Integration.Tests`. See `codebase-architecture.md` §3.7–3.9.

### What works today

| Layer | Status |
|-------|--------|
| Domain (entities, enums, interfaces, events, validation) | Complete |
| Data (DbContext, configurations, initializer, migration) | Complete |
| Application (all 7 services + JobEventBus + ActiveJobRegistry) | Fully implemented |
| Printer.Savema (adapter, SPPL protocol, factory) | Fully implemented |
| Desktop (ViewModels, Views, Converters, Styles) | Redesigned to match spec; most features wired |
| PrinterTestHarness | Complete |
| Tests | 279 tests passing: Domain (13), Savema SPPL (48), Application/ViewModel (165), Integration E2E (52), Data (1 placeholder) |

### What this plan covers

Every task required to go from "compiles" to "production-ready Phase 1 matching the design spec."

### Progress Summary

| Epic | Stories Done | Stories Remaining | SP Done | SP Remaining |
|------|-------------|-------------------|---------|--------------|
| E0: Foundation | 4/4 | 0 | 10 | 0 |
| E1: Core Print Flow | 4/4 | 0 | 18 | 0 |
| E2: Safety & Recovery | 3/3 | 0 | 15 | 0 |
| E3: Product Management | 2/2 | 0 | 8 | 0 |
| E4: Printer Management | 2/2 | 0 | 10 | 0 |
| E5: Dashboard & Real-Time | 2/2 | 0 | 11 | 0 |
| E6: Job Management | 3/3 | 0 | 16 | 0 |
| E7: Testing | 1/7 (2 partial) | 4 (+2 partial) | 3 | 30 |
| E8: Polish & Deployment | 0/5 | 5 | 0 | 12 |
| E9: Codes Management | 6/6 | 0 | 26 | 0 |
| E10: UI Preferences | 2/2 | 0 | 5 | 0 |
| **Total** | **29/40** | **11** | **122** | **42** |

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

### E1-1. Separate Prepare and Start in NewJobViewModel — DONE

**Points: 5** | **Depends on: E0-1, E0-2** | **Blocks: E5, E6** | **Status: DONE**

`PrepareJobAsync` now accepts `IProgress<string>` callback, reporting 10 distinct steps (checking_printer → printer_verified → reserving_codes → codes_reserved → uploading_data → data_uploaded → loading_template → template_loaded → complete). `NewJobViewModel` maps progress callbacks to individual checkmark properties. Back button disabled during preparation. Prepare and Start are fully separated with [Start Print] / [Go to Job] shown on success.

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

### E3-2. Add/Delete product tree nodes — DONE

**Points: 3** | **Depends on: E0-1** | **Blocks: nothing** | **Status: DONE**

Inline forms for Add Folder/Add Product with parent resolution. `BrowseNewTemplateCommand` for .rox file picker. `CanDeleteAsync` validates no active jobs or reserved codes exist before allowing deletion. Delete button disabled with tooltip when blocked. Confirmation dialog (MessageBox YesNo) shown before delete.

---

## Epic 4: Printer Management

> **Storage tab and Verify flow.** Reference: `phase1-design.md` §6.4, §3.3.

### E4-1. Printers Storage tab — list files on printer — DONE

**Points: 5** | **Depends on: E0-1, E0-2** | **Blocks: E4-2** | **Status: DONE**

Storage tab with two DataGrids (templates, CSV files). Cross-references files with product configurations using filename-only comparison (case-insensitive). Checkboxes disabled for mapped files. Orphaned files pre-selected. Delete button deletes both templates and CSVs via adapter. Audit log entry on deletion (`printer_files_deleted` event with file list).

### E4-2. Verify flow — DONE

**Points: 5** | **Depends on: E4-1** | **Blocks: nothing** | **Status: DONE**

`VerifyPrinterAsync` in `PrintersViewModel` runs 4 checks: (1) CSV file on printer via `VerifyCsvExistsAsync`, (2) active template via `GetActiveTemplateAsync` with exact filename match, (3) SPGGTP counter vs `TotalBaseline + CodesConfirmed` with ahead/behind/consistent reporting, (4) printer status via `GetStatusAsync`. Results shown inline in Verify tab with pass/warning/fail icons (`VerifyResultItem` + `VerifyStatus` enum). Overall status: "ALL OK" / "WARNINGS" / "ISSUES FOUND". Handles no-active-job and offline-printer cases gracefully. `IsVerifying` loading state disables button during check. 29 `Verify_*` localization keys in EN and RU.

---

## Epic 5: Dashboard & Real-Time

> **Live monitoring.** Reference: `phase1-design.md` §6.2, `multi-printer-concurrency.md` §7.

### E5-1. Dashboard printer cards with live progress — DONE

**Points: 8** | **Depends on: E1-3, E2-3** | **Blocks: nothing** | **Status: DONE**

`PrinterCardViewModel` has live progress via partial methods `OnCurrentJobProgressChanged`/`OnCurrentJobTotalChanged` → `UpdateDerivedProperties()`. `ProgressPercent` property for binding. `IsActive` controls progress bar visibility. Pause/Start/Cancel buttons with visibility per job status. `OnJobStatusChanged` updates summary text reactively. Cards sorted: running first, completed last.

### E5-2. Dashboard recent activity feed — DONE

**Points: 3** | **Depends on: E0-1** | **Blocks: nothing** | **Status: DONE**

`DashboardViewModel.RecentActivity` shows last 20 audit entries (format: "HH:mm {Description}"), excluding `job_created` entries (redundant with dedicated start/complete events). Auto-refreshes on `JobCompleted` events via `OnJobCompleted` handler. `AuditEntryViewModel` formats time and description with color-coded dots by event type: green (info/completed), purple (import), blue (started/resumed), amber (warning/paused), gray (cancelled), red (error). Alert messages are fully localized via `ILocalizationService`.

---

## Epic 6: Job Management

> **Active jobs and history.** Reference: `phase1-design.md` §6.5.

### E6-1. Jobs Active tab — full design implementation — DONE

**Points: 5** | **Depends on: E1-3, E2-3** | **Blocks: nothing** | **Status: DONE**

Live progress in list items via collection item replacement on progress tick. Completed jobs stay displayed in-place until user navigates away. Pause button placeholder added. Empty state with "No active jobs" message + [+ New Job] button. `HasActiveJobs` property drives visibility. Full detail pane with product, printer, status, quantity, prep checklist, progress bar, and action buttons.

### E6-2. Job History tab — filters — DONE

**Points: 3** | **Depends on: E0-1** | **Blocks: nothing** | **Status: DONE**

`FilterPrinter`/`FilterProduct` properties with reactive `LoadHistoryAsync` on change. ComboBox dropdowns for printer and product filtering. `ClearFiltersCommand` resets both. Expanded row detail in Border for `SelectedHistoryJob` showing product, printer, quantity, and result.

### E6-3. Pause / Resume support — DONE

**Points: 8** | **Depends on: E1-3** | **Blocks: nothing** | **Status: DONE**

Full cross-layer implementation. `Paused` added to `JobStatus` enum. `PauseJobAsync`: acquires printer lock, stops executor polling via `StopAsync()`, sends `SPPSTP` via adapter, reconciles counter via SPGGTP (with clamping for negatives/regressions), sets status=Paused in DB transaction, audits `job_paused`. `ResumeJobAsync`: acquires printer lock, follows full Section 10 recovery procedure (determines remaining reserved codes, deletes old CSV, uploads NEW CSV with only remaining codes, verifies upload, checks/re-uploads template, reloads template via `ActivateTemplateAsync`, records fresh SPGGTP+SPGGCP baselines, sets print quantity, starts printing, spawns new `JobExecutor` with `counterOffset` for correct delta tracking). Handles crash recovery (Printing with no executor → transitions to Paused first). Handles connection loss after template activation with 10-attempt reconnect retry. Migration `20260809172830_AddPausedToActiveJobFilter` adds `Paused` to both partial unique index filters. `JobStatusToActionVisibilityConverter`: Pause visible when Printing, Resume visible when Paused, Cancel includes Paused. Buttons wired in both `JobsView.xaml` and `DashboardView.xaml`. TestHost exposes `POST /{id}/pause` and `POST /{id}/resume` endpoints. Localized (EN+RU).

---

## Epic 7: Testing

> **Real test coverage.** Tests can be written in parallel with feature work after E0 completes.

### E7-1. Domain entity and enum tests — PARTIAL

**Points: 2** | **Depends on: E0-1** | **Blocks: nothing** | **Parallelizable** | **Status: PARTIAL**

`CodeValidatorTests` (13 tests) in Domain.Tests covers SPPL-forbidden sequences (`^`, `~gt~`, `~sc~`, `~`, `|`, `\n`, `\r`), empty/whitespace, and valid codes. Missing: `CodeStatus` transition tests, `ProductNode` tree relationship tests, `PrintJob` status rule tests. These entity behaviors are exercised by the 52 integration tests but have no dedicated unit tests.

- [x] Test code validation (SPPL forbidden sequences)
- [ ] Test `CodeStatus` transitions (valid and invalid)
- [ ] Test `ProductNode` tree relationships
- [ ] Test `PrintJob` status rules

### E7-2. SPPL protocol tests — DONE

**Points: 3** | **Depends on: nothing** | **Blocks: nothing** | **Parallelizable** | **Status: DONE**

48 tests in `Printer.Savema.Tests`: `SpplCommandBuilderTests` (15 tests — every SPPL command verified) + `SpplResponseParserTests` (18 tests — Parse happy paths, whitespace tolerance, 6 malformed-input edge cases, ParseStatus for WAITING/ERROR/RUNNING+BLOCKED, IsValidCodeValue with all forbidden sequences) + `SpplResponseTests` (15 tests — AsInt, AsList, IsOk/IsFail, empty/single/multi payloads).

### E7-3. Data layer tests (in-memory SQLite)

**Points: 5** | **Depends on: E0-1** | **Blocks: nothing** | **Parallelizable** | **Status: NOT DONE**

Only placeholder `UnitTest1` (1 empty test) in `Data.Tests`. Partial unique index enforcement is verified by integration test `TwoJobsSamePrinter_SecondFails`. Code uniqueness is verified by `ImportDuplicateCodes_SkipsDuplicates`. No dedicated unit tests for entity configurations, cascade delete, or constraint behavior in isolation.

- [ ] Test entity configurations (required fields, defaults, relationships)
- [ ] Test partial unique indexes (two active jobs for same printer)
- [ ] Test code uniqueness constraint
- [ ] Test cascade delete behavior

### E7-4. CodePoolService tests

**Points: 5** | **Depends on: E0-1, E0-3, E0-4** | **Blocks: nothing** | **Parallelizable** | **Status: NOT DONE (covered by integration)**

No dedicated unit tests in `Application.Tests`. All behaviors are exercised by integration tests: import duplicate detection (`ImportDuplicateCodes_SkipsDuplicates`, `ImportWithinBatchDuplicates`), reserve/FIFO (every print cycle test), return (`CancelReadyJob_ReturnsAllCodes`), quarantine (`CancelPrintingJob_QuarantinesBoundaryCode`, margin 0/1/2 variants), mark printed (every completion test), insufficient codes (`CreateJob_InsufficientCodes`), pool depletion (`MultipleJobsDepleteCodes`).

- [ ] Dedicated unit tests with in-memory DB (not via TestHost)

### E7-5. PrintJobService tests

**Points: 8** | **Depends on: E2-3, E1-1** | **Blocks: nothing** | **Parallelizable** | **Status: NOT DONE (covered by integration)**

No dedicated unit tests. Extensively covered by 52 integration tests across 8 test classes: `FullE2ETests` (20 tests — happy path, 19 corner cases), `CancelJobTests` (4 — quarantine margin 0/1/2), `PauseResumeTests` (2 — pause+resume cycle, invalid state), `RecoveryScenarioTests` (10 — CSV rebuild, multi-resume, serial mismatch, baseline capture), `CumulativeCounterTests` (6 — high counter, power cycle detection, lifetime reconciliation), `DisconnectSafetyTests` (3 — offline cancel/pause, crash recovery), `MockPrinterTests` (4 — state inspection, error injection), `PrintCycleTests` (3 — full cycle, active jobs).

- [ ] Dedicated unit tests with mocked adapter (not via TestHost)

### E7-6. SavemaTtoAdapter integration tests

**Points: 5** | **Depends on: nothing** | **Blocks: nothing** | **Parallelizable** | **Status: NOT DONE (biggest gap)**

No tests exist for the real `SavemaTtoAdapter` TCP layer. All integration tests use `MockPrinterAdapter`. The SPPL protocol builder/parser are well-tested (E7-2), but the adapter's TCP connect/disconnect/reconnect, `SemaphoreSlim` serialization, timeout handling, and connection-loss behavior have no automated coverage. This is the most significant testing gap — bugs here would only surface with real hardware or the Python simulator.

- [ ] Create mock TCP server with canned SPPL responses
- [ ] Test connect, disconnect, reconnect
- [ ] Test command serialization through SemaphoreSlim
- [ ] Test response parsing end-to-end through TCP
- [ ] Test timeout behavior, connection loss

### E7-7. ViewModel tests — PARTIAL

**Points: 5** | **Depends on: E1-1, E2-3** | **Blocks: nothing** | **Parallelizable** | **Status: PARTIAL (2/4 ViewModels)**

164 tests in `Application.Tests` using NSubstitute + in-memory EF Core + `MockPrinterAdapter`:
- `PrintersViewModelTests` (~90 tests): loading, add printer form lifecycle (F1/F2/F11), selection & status (F7/F9), connect/disconnect (F10), delete (F3), storage refresh & file mapping (F4/F5), delete selected files, verify printer (F8), new job navigation, helper types.
- `ProductsViewModelTests` (~74 tests): tree loading, selection & detail pane, add folder/product parent resolution (§3.3/3.4), activity history merged timeline (§4b), code pool stats refresh, delete guards (§3.5), new job navigation (§3.2), deselect & root-level creation, rename, edge cases.
- `UnitTest1`: 1 placeholder (can be removed).

Missing:
- [ ] Test `NewJobViewModel`: Prepare flow, Start flow, error handling
- [ ] Test `JobsViewModel`: load active/history, cancel, pause, resume, filter

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

## Epic 9: Codes Management

> **Admin-level code management.** Operators can inspect, filter, move, archive, and undo changes to individual codes. Includes the Quarantined status, archived codes table, safe product deletion with code handling, and an Unassigned codes pool.

### E9-1. Domain changes — Quarantined status, nullable ProductId, ArchivedCode — DONE

**Points: 5** | **Depends on: E0-1** | **Blocks: E9-2** | **Status: DONE**

- Added `Quarantined` to `CodeStatus` enum.
- Changed `Code.ProductId` from `int` to `int?` (nullable). Changed `Product` navigation to nullable.
- Changed `CodeConfiguration` FK to `DeleteBehavior.SetNull`.
- Created `ArchivedCode` entity with fields: OriginalCodeId, ProductId, CodeText, Status, ImportOrder, ImportBatch, JobId, StatusChangedAt, CreatedAt, ArchivedAt, ArchivedReason.
- Created `ArchivedCodeConfiguration` mapping to `archived_codes` table with index on (ProductId, ArchivedAt).
- Added `DbSet<ArchivedCode> ArchivedCodes` to `AppDbContext`.
- Generated migration `20260817224936_AddArchivedCodesAndNullableProductId`.

### E9-2. ICodeManagementService interface + CodeManagementService implementation — DONE

**Points: 8** | **Depends on: E9-1** | **Blocks: E9-3** | **Status: DONE**

- Created `ICodeManagementService` interface in Domain with DTOs: `CodePage`, `CodeOperation`, `UndoResult`.
- Methods: `GetCodesPageAsync` (paginated, filtered, searchable), `GetUnassignedCountAsync`, `ChangeStatusAsync`, `ChangeStatusBulkAsync`, `MoveCodesAsync`, `MoveCodesBulkAsync`, `ArchiveCodesAsync`, `ArchiveCodesBulkAsync`, `UnassignCodesAsync`, `UndoOperationAsync`.
- Implemented `CodeManagementService` in Application layer with full audit logging.
- Safety: Reserved codes are excluded from all admin mutations (enforced server-side).
- Undo validates current state before reverting; skips codes affected by subsequent jobs.
- Archive undo checks for uniqueness conflicts from re-imported codes.
- Added `IProductService.GetCodeCountAsync` and implemented in `ProductService`.
- Registered `ICodeManagementService` → `CodeManagementService` as scoped in DI.

### E9-3. CodesTabViewModel — DONE

**Points: 5** | **Depends on: E9-2** | **Blocks: E9-4** | **Status: DONE**

- Created `CodesTabViewModel` with pagination, status filter (All + each status), debounced search (300ms), configurable page size (100/250/500/1000/All).
- Created `CodeItemViewModel` with `IsSelected` / `IsReserved` (disabled checkbox) support.
- Selection: Select All / Deselect All (current page, excludes reserved). Bulk requires specific status filter.
- Commands: status change, move, archive (selected + bulk variants), undo (stack of 10).
- Confirmation dialogs with risky-transition warnings (Printed→Available, Burned→Available, Quarantined→Available).
- `CodesChanged` event for parent ViewModel refresh.

### E9-4. ProductsViewModel integration — DONE

**Points: 3** | **Depends on: E9-3** | **Blocks: E9-5** | **Status: DONE**

- Added `CodesTab`, `QuarantinedCodesCount`, `UnassignedCodesCount`, `IsShowingUnassigned` properties.
- Loads Codes tab when a leaf product is selected.
- `ShowUnassignedCodesCommand` switches to unassigned mode.
- Rewrote `DeleteProductAsync`: zero-code products use simple confirmation; products with codes show three-button dialog (Keep Codes → unassign / Delete Codes Too → archive / Cancel).
- Updated `RefreshCodeCountsAsync` for Quarantined count.
- `RefreshUnassignedCountAsync` runs after load and after code mutations.
- Updated test constructor in `ProductsViewModelTests.cs`.

### E9-5. Codes tab XAML + Unassigned section — DONE

**Points: 3** | **Depends on: E9-3, E9-4** | **Blocks: nothing** | **Status: DONE**

- Added Codes tab (third tab after Operations and Settings) in `ProductsView.xaml` with: filter toolbar (status ComboBox + search TextBox + Refresh), DataGrid with checkbox/CodeText/Status/Batch/Job/Changed columns, Select All / Deselect All, pagination bar with page-size selector, selected-actions panel (change status, move, archive), bulk-actions panel, undo bar.
- Added Unassigned section below tree in left panel: separator + button with warning icon + count, visible only when count > 0.
- Added full Unassigned mode detail pane (same DataGrid layout, bound to CodesTab in unassigned mode).
- Added Quarantined row (amber) to Operations tab stats grid.
- Created `CodeStatusToColorConverter` (green/blue/gray/teal/red/amber) and registered in `Theme.xaml`.

### E9-6. Build verification — DONE

**Points: 2** | **Depends on: E9-5** | **Blocks: nothing** | **Status: DONE**

- `dotnet build` — 0 errors, 0 warnings.
- `dotnet test` — all 198 tests pass (165 Application + 30 Integration + 1 Domain + 1 Data + 1 Savema).

## Epic 10: UI Preferences (AppConfig)

### E10-1. AppConfig entity + app_config table — DONE

**Points: 2** | **Depends on: E0-1** | **Blocks: E10-2** | **Status: DONE**

- `AppConfig.cs` entity (Key/Value strings).
- `AppConfigConfiguration.cs` — maps to `app_config` table, Key as PK.
- `DbSet<AppConfig>` on `AppDbContext`.
- EF Core migration `AddAppConfigTable`.

### E10-2. Zoom control (ScaleTransform) — DONE (HIDDEN)

**Points: 3** | **Depends on: E10-1** | **Blocks: nothing** | **Status: DONE — UI hidden**

- `ZoomLevel` property, `ZoomIn`/`ZoomOut`/`ZoomReset` commands in `MainViewModel`.
- Persists to `app_config` table via `AppDbContext`.
- `ScaleTransform` on content area + sidebar +/− buttons + `Ctrl+Plus`/`Ctrl+Minus`/`Ctrl+0` shortcuts — **all commented out / removed from MainWindow.xaml**.
- Hidden because the ScaleTransform approach scales everything (padding, margins, hit areas), causing layout issues at non-100% zoom. Needs UX rework (e.g., DynamicResource font-size scaling) before re-enabling.
- The `app_config` table and persistence code remain functional for future use.

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
| E1: Core Print Flow | 4 | **4/4** | 18 | 18 | Yes |
| E2: Safety & Recovery | 3 | **3/3** | 15 | 15 | Yes |
| E3: Product Management | 2 | **2/2** | 8 | 8 | Yes |
| E4: Printer Management | 2 | **1/2** | 10 | 5 | No |
| E5: Dashboard & Real-Time | 2 | **2/2** | 11 | 11 | Yes |
| E6: Job Management | 3 | **2/3** | 16 | 8 | No |
| E7: Testing | 7 | 0/7 | 33 | 0 | No |
| E8: Polish & Deployment | 5 | 0/5 | 12 | 0 | No |
| E9: Codes Management | 6 | **6/6** | 26 | 26 | No |
| **Total** | **38** | **24/38** | **159 SP** | **101 SP** | |

### Critical path (shortest path to "works end-to-end")

```
✅ E0-1 (2) → ✅ E0-2 (3) → ✅ E2-3 (5) → ✅ E1-1 (5) → ✅ E1-3 (5) → ✅ E5-1 (8)
                                              
ALL DONE — end-to-end flow is complete (28 SP)
```

### Minimum viable demo (operator can print)

```
✅ E0-1 + ✅ E0-2 + ✅ E0-3 + ✅ E1-1 + ✅ E1-4 = 18 SP — ALL COMPLETE
```

The minimum viable demo is **fully functional**. An operator can: start the app, configure a printer, import codes, create a job with separate prepare/start steps, monitor live progress, and see it complete.

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
| `CodeStatus` enum | Done | Available, Reserved, Printed, Returned, Burned, Quarantined (E9-1) |
| `JobStatus` enum | Partial | Missing `Paused` state (needed for E6-3) |
| `PrinterStatus` enum | Done | Offline, Init, Idle, Printing, Error, Blocked |
| `AlertSeverity` enum | Done | |
| `IPrinterAdapter` interface | Done | 16 methods, matches §5.1 |
| `IPrinterAdapterFactory` interface | Done | |
| `IProductService` interface | Done | |
| `ICodePoolService` interface | Done | |
| `ICodeManagementService` interface | Done | Paginated query, status change, move, archive, undo (E9-2) |
| `IPrintJobService` interface | Partial | Missing `PauseJobAsync` (E6-3) |
| `IAlertService` interface | Done | |
| `IAuditService` interface | Done | |
| `ICurrentUser` interface | Done | Placeholder for future auth |
| Domain events (4 records) | Done | Progress, Completed, StatusChanged, Alert |

### Data Layer

| Component | Status | Notes |
|-----------|--------|-------|
| `AppDbContext` | Done | 6 DbSets (added ArchivedCodes in E9-1) |
| `DbInitializer` (WAL mode) | Done | PRAGMA WAL + busy_timeout=5000 |
| `ProductNodeConfiguration` | Done | Self-referencing tree, indexes |
| `CodeConfiguration` | Done | Unique constraint, composite indexes, nullable ProductId FK with SetNull (E9-1) |
| `ArchivedCodeConfiguration` | Done | Maps to `archived_codes`, index on (ProductId, ArchivedAt) (E9-1) |
| `PrinterConfiguration` | Done | |
| `PrintJobConfiguration` | Done | Partial unique indexes for concurrency guards |
| `AuditEntryConfiguration` | Done | |
| **EF Core migration** | **Done** | Migration generated and applied (E0-1) |

### Application Services

| Component | Status | Notes |
|-----------|--------|-------|
| `ProductService` | Done | Full CRUD, tree operations, `GetCodeCountAsync` (E9-2) |
| `CodeManagementService` | Done | Admin code operations: query, status change, move, archive, undo (E9-2) |
| `CodePoolService` | Done | SPPL forbidden-sequence validation via `CodeValidator` (E0-4), low stock alert (E2-2) |
| `CodePoolService.ReturnCodesToPoolAsync` | Done | Sets `Available` status and clears `JobId` (E0-3) |
| `PrintJobService.CreateJobAsync` | Done | |
| `PrintJobService.PrepareJobAsync` | Done | Auto-uploads .rox template from disk if missing on printer (E1-2) |
| `PrintJobService.StartJobAsync` | Done | Records baseline, sets qty, starts, spawns executor |
| `PrintJobService.CancelJobAsync` | Done | Quarantine per QuarantineMargin (configurable, default 0) |
| `PrintJobService` (lifetime) | Done | Fixed: `JobEventBus` + `ActiveJobRegistry` singletons extracted (E2-3) |
| `JobExecutor` | Done | Poll loop, anomaly detection, commit, complete |
| `PrinterConnectionManager` | Done | Factory lookup, connect, reconnect with backoff |
| `AlertService` | Done | Events, auto-dismiss, audit bridge |
| `AuditService` | Done | JSON serialization, DB persist |
| `ServiceCollectionExtensions` | Done | All registrations including `ActiveJobRegistry` + `JobEventBus` singletons + `CodeManagementService` (E9-2) |

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
| Converters (Bool, Null, Status, CodeStatus) | Done | Added `CodeStatusToColorConverter` (E9-5) |

### Desktop — Dashboard (§6.2)

| Feature | Status | Notes |
|---------|--------|-------|
| Summary cards (active jobs, available, printed today) | Done | |
| Printer cards (one per printer) | Done | Full redesign with IsActive, ProgressPercent, reactive summary (E5-1) |
| Live progress on cards | Done | Partial methods on property change trigger UpdateDerivedProperties (E5-1) |
| Action buttons (Pause/Resume/Cancel/Start) | Partial | Start/Cancel/Pause wired, Resume needs E6-3 |
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
| Product detail pane — pool stats by status | Done | Available/Printed/Burned/Quarantined/Total (E3-1, E9-5) |
| Product detail pane — import history | Done | Date + details from audit log (E3-1) |
| Import CSV with file dialog | Done | OpenFileDialog wired (E1-4) |
| [+ New Job] on product | Done | NavigateToNewJobRequested event (E8-1) |
| [+ Add Folder] / [+ Add Product] | Done | Inline forms with Create/Cancel buttons, ViewModel commands |
| Delete product node | Done | Three-button dialog: Keep Codes / Delete Codes Too / Cancel. Codes unassigned or archived. (E3-2, E9-4) |
| Codes tab (third tab) | Done | Paginated DataGrid, filter/search, status change, move, archive, undo (E9-5) |
| Unassigned codes section | Done | Shown below tree when count > 0, opens Codes tab in unassigned mode (E9-5) |

### Desktop — Printers (§6.4)

| Feature | Status | Notes |
|---------|--------|-------|
| Printer list | Done | |
| Add printer form | Done | Name, IP, port, adapter type |
| Connect / Disconnect buttons | Done | |
| Delete printer | Done | |
| Configuration tab | Done | (Current implementation is effectively the Config tab) |
| Storage tab — list templates on printer | Done | Cross-referenced by filename (case-insensitive) (E4-1) |
| Storage tab — list CSV files on printer | Done | Cross-referenced by PrinterCsvName (E4-1) |
| Storage tab — cross-reference with products | Done | Mapped status shown, checkbox disabled for mapped files (E4-1) |
| Storage tab — delete unmapped files | Done | Audit logged, templates + CSVs deleted (E4-1) |
| Verify flow | Not Started | (E4-2) |
| [+ New Job] on printer | Not Started | (E8-1) |
| Test Connection button | Not Started | |

### Desktop — Jobs (§6.5)

| Feature | Status | Notes |
|---------|--------|-------|
| Active Jobs tab — job list | Done | DataGrid with active jobs |
| Active Jobs tab — selected job detail | Done | Full detail: product, printer, status, quantity, prep checklist (E6-1) |
| Active Jobs tab — live progress bar | Done | Collection item replacement on tick, progress bar + text (E6-1) |
| Active Jobs tab — action buttons | Partial | Start/Cancel/Pause wired, Resume needs E6-3 |
| Active Jobs tab — empty state | Done | "No active jobs" + [+ New Job] when empty (E6-1) |
| Active Jobs tab — completed job retention | Done | Job stays in list until user navigates away (E6-1) |
| Job History tab — list | Done | Shows completed/cancelled |
| Job History tab — filters (printer, product) | Done | ComboBox filters + Clear button, reactive load (E6-2) |
| Job History tab — expanded row detail | Done | Border with product, printer, qty, result on selection (E6-2) |
| [+ New Job] button | Done | Navigates to New Job screen |

### Desktop — New Job (§6.6)

| Feature | Status | Notes |
|---------|--------|-------|
| Product selector (dropdown, leaf only) | Done | |
| Printer selector (dropdown) | Done | |
| Quantity field | Done | |
| Available count display | Done | Updates on product selection |
| Separate [Prepare] step | Done | IProgress<string> callback with 10 steps (E1-1) |
| Inline preparation progress (checkmarks) | Done | Maps progress to PrepVerified/CodesReserved/DataUploaded/TemplateLoaded (E1-1) |
| [Start Print] / [Go to Job] after prepare | Done | Shown on PrepComplete (E1-1) |
| On failure: error + [Retry] | Done | Shows error, Retry button re-runs prepare (E1-1) |
| Block navigation during prepare | Done | Back button disabled via IsProcessing inverse binding (E1-1) |
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
| `CodePrintManager.Integration.Tests` | Placeholder | End-to-end tests via TestHost + MockPrinterAdapter |

### Development Tooling

| Tool | Status | Notes |
|------|--------|-------|
| `Printer.Mock` adapter | Done | Full `IPrinterAdapter` in-memory implementation, `--mock` CLI flag |
| `TestHost` (ASP.NET Core) | Done | Minimal API host with mock printer for integration tests |
| `demo/savema_simulator.py` | Done | External SPPL simulator over TCP for full-stack manual testing |

---

### Totals by Status

| Status | Count |
|--------|-------|
| Done | 99 |
| Partial | 5 |
| Bug | 0 |
| Not Started | 7 |
| Placeholder | 5 |
| **Total line items** | **115** |

### What's Next (recommended priority order)

1. **E4-2** (5 SP) — Verify flow (depends on E4-1 done)
2. **E6-3** (8 SP) — Pause/Resume support (adds `Paused` state, SPPSTP/SPPSAP commands)
3. **E7-**** (33 SP) — Testing (can run in parallel with features)
4. **E8-**** (12 SP) — Polish & Deployment (final phase)
