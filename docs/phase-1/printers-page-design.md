# Printers Page — Detailed Design & Specification

> **Scope:** Everything about the Printers page: layout, implemented improvements, button-by-button breakdown, behavioral rules, and required test coverage.

---

## 1. Page Purpose

The Printers page is where the operator manages Savema printers connected to the local network: registering new printers, monitoring connection status, inspecting on-printer storage, verifying printer/app consistency, and launching print jobs. It is the **single source of truth** for printer configuration and the operator's window into what each printer physically contains.

---

## 2. Current Layout

### 2.1 Page Structure

```
+------------------------------------------------------------------+
| PRINTERS                                          [+ New Job]     |
+------------------------------------------------------------------+
|                                                                    |
|  [ Savema-Line1 v ]  192.168.1.10  (o) IDLE       [+ Add Printer] |
|                                                                    |
|  [Configuration]  [Storage]  [Verify]                              |
|  ----------------------------------------------------------------  |
|                                                                    |
|  (tab content area)                                                |
|                                                                    |
+------------------------------------------------------------------+
```

**Top row:** Page title + [+ New Job] button (top-right).

**Selector row:** Printer dropdown (ComboBox) + IP address + status dot + status text + [+ Add Printer] button.

**Tab area:** Three tabs — Configuration, Storage, Verify.

### 2.2 Configuration Tab

```
+------------------------------------------------------------------+
|  Name:        Savema-Line1                                         |
|  IP Address:  192.168.1.10                                         |
|  Port:        9100                                                 |
|  Adapter:     savema_tto                                           |
|  ----                                                              |
|  [Connect]  [Disconnect]  [Edit]  [Delete Printer]                 |
+------------------------------------------------------------------+
```

Displays the selected printer's configuration. Click [Edit] to switch to inline editing of Name, IP Address, and Port (adapter type is read-only). [Save] persists changes; [Cancel] discards them.

### 2.3 Storage Tab

```
+------------------------------------------------------------------+
|  TEMPLATES ON PRINTER                              [Refresh]       |
|  +--------------------------------------------------------------+ |
|  | [ ]  Name                    Status                           | |
|  | [ ]  apple_05_53.rox         Used (Apple 0.5L)                | |
|  | [x]  old_test_53.rox         Not mapped to any product        | |
|  +--------------------------------------------------------------+ |
|                                                                    |
|  CSV FILES ON PRINTER                                              |
|  +--------------------------------------------------------------+ |
|  | [ ]  Name                    Status                           | |
|  | [ ]  apple_05.csv            Used (Apple 0.5L)                | |
|  | [x]  old_data.csv            Not mapped to any product        | |
|  +--------------------------------------------------------------+ |
|                                                                    |
|  [Delete Selected (2)]                                             |
+------------------------------------------------------------------+
```

Two DataGrids showing files stored on the printer. Files mapped to a product have checkboxes disabled. Orphaned files are pre-selected for cleanup.

### 2.4 Verify Tab

```
+------------------------------------------------------------------+
|  [Run Verification]                           ALL OK / WARNINGS    |
|                                                                    |
|  +--------------------------------------------------------------+ |
|  | [check] CSV File         "apple_05.csv" present on printer    | |
|  | [check] Active Template  "apple_05_53.rox" matches expected   | |
|  | [check] Counter (SPGGTP) Printer: 1700, Expected: 1700       | |
|  | [check] Printer Status   Printer state: Idle                  | |
|  +--------------------------------------------------------------+ |
+------------------------------------------------------------------+
```

Runs 4 checks against the selected printer and shows results with pass/warning/fail icons.

### 2.5 Add Printer Inline Form

When [+ Add Printer] is clicked, an inline form replaces the tab area:

```
+------------------------------------------------------------------+
|  Add New Printer                                                   |
|  Name:         [_______________]                                   |
|  IP Address:   [_______________]                                   |
|  Port:         [9100___________]                                   |
|  Adapter Type: [savema_tto  v  ]                                   |
|  [Add]  [Cancel]                                                   |
+------------------------------------------------------------------+
```

---

## 3. Implemented Changes

### 3.1 Edit Mode for Printer Configuration [DONE]

**Problem:** The Configuration tab was entirely read-only. If the operator needed to correct an IP address or rename a printer, they had to delete and re-create it (losing job history references).

**Solution:**
- Added editable text fields for Name, IP Address, Port (toggle via [Edit] button)
- [Save] persists changes, [Cancel] discards — adapter type remains read-only
- Save validates that Name and IP are non-empty
- Audit log entry recorded on save

### 3.2 Auto-Connect After Adding Printer [DONE]

**Problem:** After adding a printer via the inline form, it appeared in the dropdown but stayed Offline. The operator had to manually click [Connect].

**Solution:** After `ConfirmAddPrinterAsync` succeeds, `_connectionManager.ConnectAsync(printer)` is called automatically (fire-and-forget). The status indicator updates via the `PrinterStatusChanged` event.

### 3.3 Confirmation Dialogs for Dangerous Actions [DONE]

**Problem:** [Delete Printer] and [Delete Selected] (storage cleanup) executed immediately with no confirmation.

**Solution:**
- [Delete Printer] — MessageBox: "Are you sure you want to delete {name}? Job history referencing this printer will be preserved but the printer will no longer be available."
- [Delete Selected] — MessageBox: "Delete {N} file(s) from {printer name}? This cannot be undone." Skipped if count is 0.

### 3.4 Block Delete When Printer Has Active Jobs [DONE]

**Problem:** `DeletePrinterAsync` disconnected and deleted the printer even if it had an active job (Preparing/Ready/Printing/Paused). This could orphan a running job.

**Solution:**
- Before deletion, queries `_db.PrintJobs.AnyAsync(j => j.PrinterId == id && activeStatuses.Contains(j.Status))`
- If active job exists: shows blocking MessageBox ("Cannot delete — has active jobs") and returns
- Audit log entry recorded on successful deletion

### 3.5 Improve Status Detection [DONE]

**Problem:** `OnSelectedPrinterChanged` determined status by checking if `GetAdapter(id)` was non-null. If it was, status was hardcoded to `Idle` regardless of actual printer state. A printer that was currently `Printing`, `Error`, or `Blocked` would show as `Idle`.

**Solution:**
- When adapter exists, calls `adapter.GetStatusAsync()` to get real status
- Falls back to `Idle` only if the status call throws
- The `PrinterStatusChanged` event handler already correctly updates status for live changes — this fix is for the initial load/selection

### 3.6 Verify Template Matching Fix [DONE]

**Problem:** Template match in `VerifyPrinterAsync` used `.Contains()` which could produce false positives. For example, template `apple_05_53.rox` would match product template `apple_05` because the filename *contained* the expected string. But `orange_apple_05.rox` would also match.

**Solution:**
- Changed to `string.Equals()` with `Path.GetFileName()` for exact filename comparison (case-insensitive)
- Now consistent with how `RefreshStorageAsync` already does template mapping

### 3.7 Storage: Protect Active Template from Deletion [DONE]

**Problem (from spec, not previously enforced):** The currently active template (from `SPLGAT`) should not be deletable — the printer is using it. Previously only "mapped" files (those linked to a product config) were protected.

**Solution:**
- `RefreshStorageAsync` now queries `adapter.GetActiveTemplateAsync()` alongside template listing
- `PrinterFileItem` gained `IsActiveOnPrinter` and `IsProtected` (= mapped OR active) properties
- Active templates show "Active on printer" status text with disabled checkbox
- Delete logic uses `IsProtected` instead of `IsMapped` for all guards

---

## 4. Button-by-Button Specification

### 4.1 [+ Add Printer] (Selector Row)

**Purpose:** Register a new printer in the system.

**Preconditions:** None (always enabled).

**Flow:**
1. Click [+ Add Printer]
2. Inline form appears (hides tab area)
3. Existing printer selection is cleared
4. Default values: Port = 9100, AdapterType = "savema_tto" (or "mock" in dev mode)
5. User fills in Name and IP Address (required), optionally adjusts Port and Adapter
6. Click [Add] to save, or [Cancel] to dismiss

**What user sees:**
- Form fields with defaults pre-filled
- [Add] button disabled until Name and IP are non-empty
- On success: form disappears, printer appears in dropdown and is auto-selected
- On cancel: form disappears, previous printer (if any) re-selected

**Validation checks:**
- Name: required, non-empty after trim
- IP Address: required, non-empty after trim
- Adapter Type: must be one of the registered types

**Notes:**
- Auto-connect is now performed after add (3.2)
- Duplicate names are allowed (operator may have multiple printers with similar names)

### 4.2 [Connect] (Configuration Tab)

**Purpose:** Establish a TCP connection to the selected printer.

**Preconditions:**
- A printer must be selected
- Button should be disabled if printer is already connected

**Flow:**
1. Click [Connect]
2. `PrinterConnectionManager.ConnectAsync(printer)` is called
3. On success: status changes to Idle (or the actual printer status)
4. On failure: status stays Offline, reconnect loop starts (exponential backoff 1s-30s)

**What user sees:**
- Brief connection attempt (should show "Connecting..." state ideally)
- Status dot and text update to reflect result
- If printer is unreachable: stays Offline, system retries in background

**Resolved:** Connect button is now disabled when printer is already connected (status != Offline) via `CanExecute`.

**Remaining gap:**
- No "Connecting..." intermediate state in UI (jumps from Offline to Idle/Offline)

### 4.3 [Disconnect] (Configuration Tab)

**Purpose:** Drop the TCP connection to the selected printer.

**Preconditions:**
- A printer must be selected
- Should warn if printer has an active job

**Flow:**
1. Click [Disconnect]
2. If printer has active jobs: warning dialog "Disconnecting may interrupt printing. Continue?"
3. On Yes (or no active jobs): `PrinterConnectionManager.DisconnectAsync(printer.Id)` is called
4. Cancels any reconnect loop
5. Removes adapter from registry
6. Status changes to Offline

**What user sees:**
- Warning dialog if active jobs exist
- Status dot changes to Offline immediately
- Disconnect button is disabled when already offline

**Resolved:** Disconnect button disabled when offline, active-job warning dialog added.

### 4.4 [Delete Printer] (Configuration Tab — Danger Zone)

**Purpose:** Remove the selected printer from the system.

**Preconditions:**
- A printer must be selected
- Printer must have no active jobs (Preparing, Ready, Printing, Paused)

**Flow:**
1. Click [Delete Printer]
2. Confirmation dialog: "Are you sure you want to delete {name}?"
3. On Yes: disconnect printer, remove from DB, refresh list
4. On No: cancel

**What user sees:**
- Button disabled (greyed) if printer has active jobs, with tooltip explaining why
- Confirmation dialog before destructive action
- On delete: printer disappears from dropdown, another printer auto-selected (or empty state)
- Historical jobs referencing this printer are preserved in DB (the FK allows it)

**Resolved (3.3, 3.4):** Confirmation dialog, active-job guard, and audit log entry are now implemented.

### 4.5 [Refresh] (Storage Tab)

**Purpose:** Re-read the file lists from the printer's internal storage.

**Preconditions:**
- A printer must be selected and connected

**Flow:**
1. Click [Refresh]
2. Calls `adapter.ListTemplatesAsync()` and `adapter.ListCsvFilesAsync()`
3. Cross-references each file against `product_nodes.template_file` and `product_nodes.printer_csv_name`
4. Populates two DataGrids with mapped/unmapped status

**What user sees:**
- Template grid and CSV grid populate with current files
- Mapped files show "Used (Product Name)" with disabled checkboxes
- Unmapped files show "Not mapped to any product" with pre-selected checkboxes
- Count updates in Delete Selected button

**Edge cases:**
- Empty storage: grids show empty with no files
- Product was deleted but file still on printer: shows as unmapped (correct behavior)

**Resolved:** Refresh button is now disabled when printer is offline via `CanExecute`.

### 4.6 [Delete Selected (N)] (Storage Tab)

**Purpose:** Delete selected orphaned files from the printer's internal storage.

**Preconditions:**
- Printer must be connected
- At least one file selected (N > 0)
- Only unmapped files can be selected (mapped files have checkboxes disabled)

**Flow:**
1. Click [Delete Selected (N)]
2. Confirmation dialog: "Delete {N} file(s) from {printer name}?"
3. For each selected template: `adapter.DeleteTemplateAsync(name)`
4. For each selected CSV: `adapter.DeleteCsvAsync(name)`
5. Audit log entry with list of deleted files
6. Refresh storage view

**What user sees:**
- Files disappear from the grids after deletion
- Audit entry: "Deleted {N} files from Savema-Line1: old_test.rox, old_data.csv"

**Safety guarantees:**
- Cannot select files mapped to a product (checkbox disabled)
- Active template is also protected (see 3.7)
- Deletion failures for individual files should not abort the batch — log and continue

**Resolved (3.3, 3.7):** Confirmation dialog and active template protection are now implemented. Per-file error handling was already correct (failures don't abort the batch).

### 4.7 [Run Verification] (Verify Tab)

**Purpose:** Check that the printer's observable state is consistent with the app's records.

**Preconditions:**
- A printer must be selected

**Flow:**
1. Click [Run Verification]
2. System runs 4 checks (can be extended):

| # | Check | What it does | Pass | Warning | Fail |
|---|-------|-------------|------|---------|------|
| 1 | CSV File | Queries `SPLGSD`, checks if active job's CSV exists on printer | File present | File missing (but job exists) | — |
| 2 | Active Template | Queries `SPLGAT`, compares with active job's template | Match | Mismatch or none active | — |
| 3 | Counter (SPGGTP) | Reads lifetime counter, compares with `total_baseline + codes_confirmed` | Delta = 0 | Printer ahead (+N, possible prints during downtime) | Printer behind (anomaly) |
| 4 | Printer Status | Queries `SPPSTA` | Idle, Printing, Init | Blocked | Error |

3. Results shown as a list with icons and detail text
4. Overall status: ALL OK / WARNINGS / ISSUES FOUND

**What user sees:**
- "Checking printer state..." during verification
- Results appear one by one (or all at once after completion)
- Overall status badge in top-right
- Each result has a colored icon (green check, yellow warning, red X) with explanation text

**Edge cases:**
- Printer not connected: immediately shows Fail: "Printer is not connected"
- No active job: checks are still useful (shows template/counter info without job context)
- Job has no TotalBaseline (not yet started): counter check shows warning "Job has not started printing yet"

**Resolved (3.6):** Template matching now uses exact filename comparison instead of `.Contains()`.

### 4.8 [+ New Job] (Page Header)

**Purpose:** Navigate to the New Job screen with this printer preselected.

**Preconditions:**
- A printer must be selected
- Button should be disabled if no printer selected

**Flow:**
1. Click [+ New Job]
2. Fires `NavigateToNewJobRequested` event with `printerId`
3. MainViewModel navigates to New Job page with printer preselected

**What user sees:**
- Instant navigation to New Job screen
- Printer dropdown already shows the selected printer

**Resolved:** Button is now disabled when no printer is selected or printer is offline via `CanExecute`.

---

## 5. Functional Checks (Behavioral Requirements)

These are conditions the page must enforce at all times:

| # | Rule | Enforcement |
|---|------|-------------|
| F1 | A printer cannot be added without a name | Button disabled / validation message |
| F2 | A printer cannot be added without an IP address | Button disabled / validation message |
| F3 | Cannot delete a printer with active jobs | `CanDelete` check + MessageBox |
| F4 | Mapped files cannot be selected for deletion | Checkbox disabled |
| F5 | Active template cannot be deleted from storage | Checkbox disabled + "Active on printer" status |
| F6 | Delete requires confirmation dialog | MessageBox before destructive action |
| F7 | Status must reflect actual printer state | Query `GetStatusAsync()` on selection |
| F8 | Verify must handle offline printers gracefully | Shows Fail result, doesn't crash |
| F9 | Storage auto-refreshes when printer selection changes | `OnSelectedPrinterChanged` calls `RefreshStorageAsync` |
| F10 | Connect/Disconnect buttons should reflect current state | Disable when already in target state |
| F11 | Newly added printer auto-connects | `ConnectAsync` after DB save |

---

## 6. Data Sources & Cross-References

### How "Used" / "Mapped" is determined for Storage files:

**Templates:**
```csharp
// File on printer: "apple_05_53.rox"
// Product's TemplateFile might be: "C:\Templates\apple_05_53.rox"
// Match by: Path.GetFileName(product.TemplateFile) == file on printer (case-insensitive)
var mapped = products.FirstOrDefault(p =>
    !string.IsNullOrEmpty(p.TemplateFile) &&
    string.Equals(Path.GetFileName(p.TemplateFile), templateName, StringComparison.OrdinalIgnoreCase));
```

**CSV files:**
```csharp
// File on printer: "apple_05.csv"
// Product's PrinterCsvName: "apple_05.csv"
// Match by: exact match (case-insensitive)
var mapped = products.FirstOrDefault(p =>
    string.Equals(p.PrinterCsvName, csvName, StringComparison.OrdinalIgnoreCase));
```

**Important:** This cross-references against ALL products in the system, not just products assigned to this specific printer. This is correct because templates/CSVs could be shared or leftover from reassignment.

### Verify counter math:

```
expectedTotal = job.TotalBaseline + job.CodesConfirmed
actualTotal   = adapter.GetTotalCounterAsync()
delta         = actualTotal - expectedTotal

delta == 0  → consistent (pass)
delta > 0   → printer printed more than app knows (warning: prints during downtime?)
delta < 0   → printer shows fewer than app recorded (fail: anomaly)
```

---

## 7. ViewModel Structure

### Properties

```csharp
// Printer list & selection
ObservableCollection<Printer> Printers
Printer? SelectedPrinter
PrinterStatus SelectedPrinterStatus

// Add Printer form
bool IsAddingPrinter
string NewPrinterName
string NewPrinterIp
int NewPrinterPort = 9100
string NewPrinterAdapterType = "savema_tto"
List<string> AvailableAdapterTypes

// Storage tab
ObservableCollection<PrinterFileItem> TemplateFiles
ObservableCollection<PrinterFileItem> CsvFiles
int SelectedDeleteCount

// Verify tab
ObservableCollection<VerifyResultItem> VerifyResults
bool IsVerifying
bool HasVerifyResults
string VerifyOverallStatus
```

### Commands

```
LoadPrintersCommand          → Load all printers from DB, select first
ShowAddPrinterCommand        → Show inline Add form
CancelAddPrinterCommand      → Hide form, reselect previous
ConfirmAddPrinterCommand     → Validate, save to DB, auto-connect, select new
ConnectPrinterCommand        → Connect selected printer via ConnectionManager
DisconnectPrinterCommand     → Disconnect selected printer
DeletePrinterCommand         → Guard check, confirm, disconnect, delete from DB
RefreshStorageCommand        → Query printer for files, cross-reference with products
DeleteSelectedFilesCommand   → Delete unmapped selected files from printer
VerifyPrinterCommand         → Run 4-check verification suite
NewJobCommand                → Navigate to New Job with printer preselected
```

### Helper types

```csharp
public partial class PrinterFileItem : ObservableObject
{
    string FileName
    string? MappedProduct
    bool IsMapped => MappedProduct != null
    string StatusText => IsMapped ? $"Used ({MappedProduct})" : "Not mapped to any product"
    bool IsSelected  // bindable, disabled when IsMapped
}

public enum VerifyStatus { Pass, Warning, Fail }

public class VerifyResultItem
{
    string CheckName
    VerifyStatus Status
    string Details
    string StatusIcon  // emoji based on status
}
```

---

## 8. Unit Tests

### 8.1 Test Infrastructure

Tests use the same pattern as `ProductsViewModelTests`:
- In-memory SQLite via `DbContextOptionsBuilder.UseInMemoryDatabase`
- `NSubstitute` mocks for service interfaces
- `MockPrinterAdapter` and `MockPrinterAdapterFactory` for printer simulation
- `FluentAssertions` for readable assertions
- Direct ViewModel instantiation (no WPF dispatcher needed for non-UI tests)

### 8.2 PrintersViewModel Tests

```
===============================================================
1. LOADING & INITIAL STATE
===============================================================

LoadPrinters_PopulatesList
  - DB has 3 printers → Printers collection has 3 items
  - First printer auto-selected

LoadPrinters_EmptyDb_EmptyList
  - DB has no printers → Printers is empty
  - SelectedPrinter is null

InitialState_NoSelection
  - SelectedPrinter is null
  - SelectedPrinterStatus is Offline
  - IsAddingPrinter is false
  - Storage collections are empty
  - VerifyResults is empty

LoadPrinters_PreservesSelection
  - SelectedPrinter was printer #2 before reload
  - After LoadPrinters, if printer #2 still exists → re-select it
  - (Currently doesn't do this — auto-selects first)

===============================================================
2. ADD PRINTER — FORM LIFECYCLE
===============================================================

ShowAddPrinter_OpensForm
  - IsAddingPrinter becomes true
  - SelectedPrinter becomes null
  - NewPrinterName/Ip are empty
  - NewPrinterPort is 9100

CancelAddPrinter_ClosesForm
  - IsAddingPrinter becomes false
  - If printers exist, first one re-selected

ConfirmAddPrinter_ValidInput_CreatesPrinter
  - Set Name="Line1", Ip="192.168.1.10", Port=9100
  - After confirm: printer in DB, IsAddingPrinter=false
  - New printer auto-selected in dropdown
  - Printers collection updated

ConfirmAddPrinter_EmptyName_DoesNothing
  - Set Name="", Ip="192.168.1.10"
  - After confirm: no new printer in DB
  - IsAddingPrinter stays true

ConfirmAddPrinter_EmptyIp_DoesNothing
  - Set Name="Line1", Ip=""
  - After confirm: no new printer in DB

ConfirmAddPrinter_WhitespaceOnlyName_DoesNothing
  - Set Name="   ", Ip="192.168.1.10"
  - After confirm: no new printer in DB

ConfirmAddPrinter_TrimsWhitespace
  - Set Name="  Line1  ", Ip=" 192.168.1.10 "
  - After confirm: printer.Name = "Line1", printer.IpAddress = "192.168.1.10"

ConfirmAddPrinter_DefaultPort
  - Don't change port → saved as 9100

ConfirmAddPrinter_CustomPort
  - Set Port=9200 → saved as 9200

ConfirmAddPrinter_AdapterType_SavesCorrectly
  - Set AdapterType="mock" → saved as "mock"

===============================================================
3. PRINTER SELECTION & STATUS
===============================================================

SelectPrinter_UpdatesStatus_Connected
  - Printer is connected (adapter exists) → SelectedPrinterStatus reflects actual status
  - Mock adapter with Idle → SelectedPrinterStatus = Idle

SelectPrinter_UpdatesStatus_Offline
  - Printer not connected (adapter is null) → SelectedPrinterStatus = Offline

SelectPrinter_TriggersStorageRefresh
  - After selecting a connected printer → TemplateFiles and CsvFiles populated

SelectPrinter_Offline_EmptyStorage
  - Selecting an offline printer → TemplateFiles and CsvFiles are empty

StatusChanged_Event_UpdatesStatus
  - ConnectionManager raises PrinterStatusChanged for selected printer
  - SelectedPrinterStatus updates to new status

StatusChanged_Event_DifferentPrinter_NoUpdate
  - ConnectionManager raises PrinterStatusChanged for a different printer
  - SelectedPrinterStatus does NOT change

===============================================================
4. CONNECT / DISCONNECT
===============================================================

ConnectPrinter_CallsConnectionManager
  - Click Connect → connectionManager.ConnectAsync called with correct printer

ConnectPrinter_NoPrinterSelected_DoesNothing
  - SelectedPrinter is null → ConnectAsync not called

DisconnectPrinter_CallsConnectionManager
  - Click Disconnect → connectionManager.DisconnectAsync called
  - SelectedPrinterStatus set to Offline

DisconnectPrinter_NoPrinterSelected_DoesNothing
  - SelectedPrinter is null → no call

===============================================================
5. DELETE PRINTER
===============================================================

DeletePrinter_Success
  - Printer has no active jobs → disconnects, removes from DB
  - SelectedPrinter becomes null, list refreshes

DeletePrinter_NoPrinterSelected_DoesNothing
  - SelectedPrinter is null → no deletion

DeletePrinter_HasActiveJob_Blocked
  - Printer has a Printing job → deletion refused (MessageBox shown)

DeletePrinter_HasPausedJob_Blocked
  - Printer has a Paused job → deletion refused

DeletePrinter_HasPreparingJob_Blocked
  - Printer has a Preparing job → deletion refused

DeletePrinter_CompletedJobsOnly_Allowed
  - Printer has only Completed/Cancelled jobs → deletion succeeds
  - Historical jobs remain in DB (FK preserved)

DeletePrinter_DisconnectsFirst
  - Printer is connected → DisconnectAsync called before removal

===============================================================
6. STORAGE — REFRESH & FILE MAPPING
===============================================================

RefreshStorage_Connected_PopulatesBothGrids
  - Mock adapter has 2 templates and 3 CSVs
  - DB has products mapping to some files
  - TemplateFiles has 2 items, CsvFiles has 3 items
  - Mapped files show product name, unmapped show null

RefreshStorage_MappedFiles_NotPreSelected
  - File mapped to a product → IsSelected = false
  - File mapped to a product → IsMapped = true

RefreshStorage_UnmappedFiles_PreSelected
  - File not mapped to any product → IsSelected = true
  - File not mapped → IsMapped = false

RefreshStorage_NoPrinterSelected_ClearsLists
  - SelectedPrinter is null → both lists empty

RefreshStorage_PrinterOffline_ClearsLists
  - Adapter is null → both lists empty, no exception

RefreshStorage_TemplateMapping_ByFilenameOnly
  - Product.TemplateFile = "C:\Templates\apple.rox"
  - Printer has "apple.rox" → mapped (matches by filename only)

RefreshStorage_CsvMapping_ExactMatch
  - Product.PrinterCsvName = "apple_05.csv"
  - Printer has "apple_05.csv" → mapped
  - Printer has "APPLE_05.CSV" → mapped (case-insensitive)

RefreshStorage_SelectedDeleteCount_Tracks
  - 2 unmapped files pre-selected → SelectedDeleteCount = 2
  - User unchecks one → SelectedDeleteCount = 1
  - User checks a mapped file → not possible (checkbox disabled)

RefreshStorage_EmptyStorage_ShowsEmptyGrids
  - Printer has no files → both grids empty
  - SelectedDeleteCount = 0

===============================================================
7. STORAGE — DELETE SELECTED FILES
===============================================================

DeleteSelectedFiles_DeletesOnlyUnmapped
  - 2 templates selected (unmapped), 1 CSV selected (unmapped)
  - adapter.DeleteTemplateAsync called 2x
  - adapter.DeleteCsvAsync called 1x
  - Mapped files not touched even if somehow selected

DeleteSelectedFiles_AuditLogEntry
  - After deletion → audit.LogAsync called with "printer_files_deleted"
  - Details include file count and names

DeleteSelectedFiles_RefreshesAfterDelete
  - After deletion → RefreshStorageAsync called
  - Deleted files no longer appear in grid

DeleteSelectedFiles_NoPrinterSelected_DoesNothing
  - SelectedPrinter is null → no deletion attempted

DeleteSelectedFiles_NothingSelected_DoesNothing
  - All files are mapped → SelectedDeleteCount = 0
  - Delete command does nothing (no audit entry either)

DeleteSelectedFiles_PartialFailure_ContinuesOthers
  - Adapter returns false for one delete → other deletes still attempted
  - Only successful deletions counted in audit entry

===============================================================
8. VERIFY PRINTER
===============================================================

VerifyPrinter_NotConnected_ShowsFailResult
  - Adapter is null → single Fail result: "Printer is not connected"
  - HasVerifyResults = true
  - VerifyOverallStatus = "FAILED"

VerifyPrinter_NoActiveJob_AllPass
  - No active job for this printer
  - CSV: Pass ("No active job — no CSV expected")
  - Template: Pass ("No active job. Printer has: ...")
  - Counter: Pass ("No active job. Lifetime counter: N")
  - Status: Pass ("Printer state: Idle")
  - VerifyOverallStatus = "ALL OK"

VerifyPrinter_ActiveJob_CsvPresent_Pass
  - Active Printing job with product.PrinterCsvName = "apple.csv"
  - Printer has "apple.csv" → CSV check passes

VerifyPrinter_ActiveJob_CsvMissing_Warning
  - Active job with CSV name, but printer doesn't have the file
  - CSV check → Warning: "NOT found on printer"

VerifyPrinter_ActiveJob_NoCsvNameConfigured_Warning
  - Active job but product.PrinterCsvName is null
  - CSV check → Warning: "No CSV name configured"

VerifyPrinter_ActiveJob_TemplateMatch_Pass
  - Active job with template "apple_05.rox"
  - Printer active template contains "apple_05"
  - Template check → Pass

VerifyPrinter_ActiveJob_TemplateMismatch_Warning
  - Active job expects template X, printer has template Y active
  - Template check → Warning with both names shown

VerifyPrinter_ActiveJob_CounterConsistent_Pass
  - TotalBaseline=1000, CodesConfirmed=500
  - Printer SPGGTP=1500 → delta=0 → Pass

VerifyPrinter_ActiveJob_CounterAhead_Warning
  - TotalBaseline=1000, CodesConfirmed=500
  - Printer SPGGTP=1510 → delta=+10 → Warning: "printer is +10 ahead"

VerifyPrinter_ActiveJob_CounterBehind_Fail
  - TotalBaseline=1000, CodesConfirmed=500
  - Printer SPGGTP=1490 → delta=-10 → Fail: "printer is -10 behind (anomaly)"

VerifyPrinter_ActiveJob_NoBaseline_Warning
  - Active job but TotalBaseline is null (not started)
  - Counter check → Warning: "Job has not started printing yet"

VerifyPrinter_PrinterError_StatusFail
  - Adapter returns PrinterStatus.Error → Status check is Fail

VerifyPrinter_PrinterBlocked_StatusWarning
  - Adapter returns PrinterStatus.Blocked → Status check is Warning

VerifyPrinter_PrinterIdle_StatusPass
  - Adapter returns PrinterStatus.Idle → Status check is Pass

VerifyPrinter_OverallStatus_AllPass
  - All 4 checks pass → "ALL OK"

VerifyPrinter_OverallStatus_HasWarning
  - At least one Warning, no Fail → "WARNINGS"

VerifyPrinter_OverallStatus_HasFail
  - At least one Fail → "ISSUES FOUND"

VerifyPrinter_Exception_ShowsError
  - Adapter throws during verification → catch, show error result
  - VerifyOverallStatus = "ERROR"

VerifyPrinter_NoPrinterSelected_DoesNothing
  - SelectedPrinter is null → no verification performed

VerifyPrinter_IsVerifying_TracksState
  - During verification: IsVerifying = true
  - After verification: IsVerifying = false
  - Button disabled while IsVerifying = true

===============================================================
9. NEW JOB NAVIGATION
===============================================================

NewJob_FiresNavigationEvent
  - SelectedPrinter is set → click New Job
  - NavigateToNewJobRequested fires with printer.Id

NewJob_NoPrinterSelected_DoesNothing
  - SelectedPrinter is null → event not fired

===============================================================
10. PRINTERJOB-RELATED EDGE CASES
===============================================================

MultipleJobStatuses_OnlyActiveBlock
  - Printer has: 1 Completed, 1 Cancelled, 1 Printing job
  - Delete blocked (has Printing job)
  - After Printing job completes: delete allowed

PausedJobStatus_ConsideredActive
  - Printer has a Paused job → treated as active
  - Blocks delete, blocks disconnect without warning
```

### 8.3 PrinterConnectionManager Tests

```
===============================================================
CONNECTION LIFECYCLE
===============================================================

ConnectAsync_Success_AdapterRegistered
  - After connect → GetAdapter(id) returns non-null
  - PrinterStatusChanged fired with Idle

ConnectAsync_Failure_ReconnectLoopStarted
  - Adapter.ConnectAsync returns false
  - PrinterStatusChanged fired with Offline
  - Reconnect loop started (verify via mock delay)

DisconnectAsync_RemovesAdapter
  - After disconnect → GetAdapter(id) returns null
  - Cancels any reconnect loop

DisconnectAsync_NonExistentPrinter_NoError
  - Disconnect printer ID that was never connected → no exception

ConnectAsync_ReplacesExistingAdapter
  - Connect printer that already has an adapter → old adapter replaced
  - Old adapter NOT disposed (ConnectionManager creates new one)

ReconnectLoop_ExponentialBackoff
  - First attempt at 1s, second at 2s, third at 4s...
  - Caps at 30s

ReconnectLoop_SuccessfulReconnect_StopsLoop
  - After successful reconnect → loop exits
  - PrinterStatusChanged fired with Idle

Dispose_CleansUpAll
  - Multiple adapters connected → Dispose cancels all reconnect loops
  - All adapters disposed
```

### 8.4 PrinterFileItem Tests

```
PrinterFileItem_Mapped_Properties
  - fileName="apple.rox", mappedProduct="Apple 0.5L"
  - IsMapped = true
  - StatusText = "Used (Apple 0.5L)"
  - IsSelected = false (default)

PrinterFileItem_Unmapped_Properties
  - fileName="old.rox", mappedProduct=null
  - IsMapped = false
  - StatusText = "Not mapped to any product"

PrinterFileItem_IsSelected_NotifiesChange
  - Change IsSelected from false to true
  - PropertyChanged fires for "IsSelected"
```

### 8.5 VerifyResultItem Tests

```
VerifyResultItem_Pass_Icon
  - Status=Pass → StatusIcon = green checkmark

VerifyResultItem_Warning_Icon
  - Status=Warning → StatusIcon = warning triangle

VerifyResultItem_Fail_Icon
  - Status=Fail → StatusIcon = red X

VerifyResultItem_Properties
  - CheckName, Status, Details all accessible
```

### 8.6 Storage File Mapping Logic (Integration-Level)

These test the cross-referencing logic in isolation (extract into a testable method):

```
MapFiles_TemplateOnPrinter_MatchesProductByFilename
  - Product.TemplateFile = "/full/path/to/template.rox"
  - Printer file = "template.rox" → match

MapFiles_TemplateOnPrinter_CaseInsensitive
  - Product.TemplateFile = "Template.ROX"
  - Printer file = "template.rox" → match

MapFiles_TemplateOnPrinter_NoMatch
  - Product.TemplateFile = "other.rox"
  - Printer file = "template.rox" → no match

MapFiles_CsvOnPrinter_ExactMatch
  - Product.PrinterCsvName = "data.csv"
  - Printer file = "data.csv" → match

MapFiles_CsvOnPrinter_CaseInsensitive
  - Product.PrinterCsvName = "DATA.CSV"
  - Printer file = "data.csv" → match

MapFiles_MultipleProducts_BestMatch
  - 2 products have templates → each maps to its own file
  - Leftover files are unmapped
```

---

## 9. Test Implementation Notes

### 9.1 Mocking Strategy

The `PrintersViewModel` constructor takes:
```csharp
PrintersViewModel(AppDbContext db, PrinterConnectionManager connectionManager,
    IAuditService audit, IPrinterAdapterFactory adapterFactory, ILogger logger)
```

For unit tests:
- `AppDbContext` — use `UseInMemoryDatabase` (same as ProductsViewModelTests)
- `PrinterConnectionManager` — construct with a `MockPrinterAdapterFactory` and null logger (it's a concrete class, not an interface, so we can't substitute it; instead use the real one with mock adapters)
- `IAuditService` — use `NSubstitute.Substitute.For<IAuditService>()`
- `IPrinterAdapterFactory` — use `MockPrinterAdapterFactory` directly
- `ILogger` — use `NSubstitute` or `NullLogger`

### 9.2 Handling the Dispatcher Issue

The `PrintersViewModel` subscribes to `PrinterStatusChanged` and dispatches to `System.Windows.Application.Current.Dispatcher`. In unit tests, there is no WPF application running.

**Solutions (pick one):**
1. **Guard clause:** Check `Application.Current != null` before dispatching, fall back to synchronous update
2. **Abstract dispatcher:** Inject an `IDispatcher` interface (better long-term, but requires refactoring)
3. **Test-only flag:** Set a static flag to skip dispatching in tests (quick but hacky)

The ProductsViewModelTests handle this by not testing event-driven updates that require the dispatcher. The same approach works here — test the command methods directly and verify DB state / mock calls.

### 9.3 Setting Up Mock Printers for Tests

```csharp
// Setup helper method
private async Task<(Printer printer, MockPrinterAdapter adapter)> SetupConnectedPrinter(string name = "Line1")
{
    var printer = new Printer
    {
        Name = name,
        IpAddress = "192.168.1.10",
        Port = 9100,
        AdapterType = "mock"
    };
    _db.Printers.Add(printer);
    await _db.SaveChangesAsync();

    var adapter = new MockPrinterAdapter();
    await adapter.ConnectAsync("192.168.1.10", 9100);
    // Register adapter in connection manager
    await _connectionManager.ConnectAsync(printer);

    return (printer, _mockFactory.GetMock(printer.Id)!);
}
```

### 9.4 Setting Up Storage Files for Tests

```csharp
// Setup products and mock adapter files
private async Task SetupStorageScenario(MockPrinterAdapter adapter)
{
    // Add templates to mock printer
    await adapter.UploadTemplateAsync("apple_05.rox", Array.Empty<byte>());
    await adapter.UploadTemplateAsync("old_test.rox", Array.Empty<byte>());

    // Add CSV files to mock printer
    await adapter.UploadCsvAsync("apple_05.csv", new[] { "code1" });
    await adapter.UploadCsvAsync("orphan.csv", new[] { "code2" });

    // Add product that maps to some files
    var product = new ProductNode
    {
        Name = "Apple 0.5L",
        IsLeaf = true,
        TemplateFile = @"C:\Templates\apple_05.rox",
        PrinterCsvName = "apple_05.csv"
    };
    _db.ProductNodes.Add(product);
    await _db.SaveChangesAsync();
}
```

---

## 10. Summary of Changes

All changes have been implemented.

| # | Change | Status |
|---|--------|--------|
| 3.1 | Edit mode for printer configuration | Done |
| 3.2 | Auto-connect after adding printer | Done |
| 3.3 | Confirmation dialogs for delete actions | Done |
| 3.4 | Block delete when active jobs exist | Done |
| 3.5 | Query actual printer status on selection | Done |
| 3.6 | Fix verify template matching | Done |
| 3.7 | Protect active template from storage deletion | Done |
