# Products Page — Detailed Design & Specification

> **Scope:** Everything about the Products page: current behavior, proposed changes, button-by-button breakdown, validation rules, and required test coverage.

---

## 1. Page Purpose

The Products page is where the operator manages the product catalog: organizing products into folders, assigning templates, importing codes, launching print jobs, and managing individual codes. It is a **split-panel** layout: tree navigation on the left, detail/actions on the right. A separate **Unassigned Codes** section below the tree provides access to codes that have no product.

---

## 2. Proposed Layout (Redesign)

### 2.1 Three-Tab Detail Pane

Split the right-side detail pane into three tabs: frequent operations, configuration, and code management.

```
+---------------------------------------------------------+
| PRODUCTS                                                |
+------------------+--------------------------------------+
|  [+F] [+P]      |  APPLE 0.5L                          |
|                  |                                       |
|  v Juice         |  [Operations]  [Settings]  [Codes]  |
|    v Apple       |  ----------------------------------  |
|      * 0.5L  <-- |                                       |
|      * 1.0L      |  === OPERATIONS TAB ===               |
|    > Orange      |                                       |
|  v Water         |  Code Pool:                           |
|    * Still 0.5L  |    Available:    8,300                 |
|  v Milk          |    Printed:      1,700                 |
|    * 1.0L        |    Burned:       3                     |
|                  |    Quarantined:  7                     |
|  ──────────────  |    Total:        10,010                |
|  ⚠ Unassigned(5) |                                       |
|                  |  [Import CSV...]  [+ New Job]         |
|                  |                                       |
|                  |  History:                             |
|                  |    Aug 10 14:30  Job #52 completed    |
|                  |                  500/500 printed      |
|                  |    Aug 09 11:00  Imported 10,000      |
|                  |                  gold_0.5_10000.csv   |
|                  |    Aug 08 16:45  Job #48 cancelled    |
|                  |                  200/500 printed      |
|                  |    Aug 06 09:20  Imported 5,000       |
|                  |                  batch_aug6.csv       |
|                  |                                       |
+------------------+--------------------------------------+
```

```
+---------------------------------------------------------+
|                  |  === SETTINGS TAB ===                  |
|                  |                                       |
|                  |  Template:  apple_05_template.rox     |
|                  |            [Change]                   |
|                  |                                       |
|                  |  CSV Name:  [apple_05.csv    ] [Save] |
|                  |                                       |
|                  |  ---                                  |
|                  |  [Delete Product]                     |
|                  |                                       |
+---------------------------------------------------------+
```

```
+---------------------------------------------------------+
|                  |  === CODES TAB ===                     |
|                  |                                       |
|                  |  Status: [All ▼]  Search: [____] [⟳] |
|                  |                                       |
|                  |  ☐  CODE_TEXT        STATUS   BATCH   |
|                  |  ☐  01046200123...  Avail.   b1.csv  |
|                  |  ☐  01046200567...  Printed  b1.csv  |
|                  |  ☑  01046200901...  Quaran.  b2.csv  |
|                  |                                       |
|                  |  [Select All] [Deselect]              |
|                  |  Page 1/83  [◀][▶]  Size: [100 ▼]   |
|                  |                                       |
|                  |  Selected (1):                        |
|                  |   [Change Status ▼] [Move ▼] [Arch.] |
|                  |                                       |
|                  |  [Undo Last Action]                   |
+---------------------------------------------------------+
```

**Tab A — "Operations" (default, frequent use):**
- Code pool stats (Available / Printed / Burned / Quarantined / Total)
- [Import CSV...] button
- [+ New Job] button
- Activity history (merged import + print job entries, last 20, chronological)

**Tab B — "Settings" (infrequent, configuration):**
- Template file path + [Change] button
- Printer CSV Name (editable text field) + [Save] button
- [Delete Product] button (danger zone, at the bottom)

**Tab C — "Codes" (admin, code management):**
- Paginated DataGrid of all codes for this product (or all unassigned codes)
- Status filter (All + each status), text search, refresh
- Select/deselect (reserved codes are always protected — checkbox disabled)
- Actions: change status, move to another product, archive
- Undo with safety validation (bounded stack of 10)
- Page size default 100, options: 100/250/500/1000

**Rationale:** The operator's primary daily workflow is: select product -> check availability -> import codes or start job. Template and CSV name changes happen only during initial setup or rarely. Code management is an occasional admin activity. Separating all three into tabs reduces visual clutter and protects against accidental configuration changes or code modifications.

### 2.2 Unassigned Codes Section

Below the tree, a separate section appears when codes exist without a product (i.e., `Code.ProductId IS NULL`). This happens when:
- A product is deleted and the operator chose "Keep Codes"
- Codes are explicitly unassigned via the Codes tab

```
+------------------+
|  (tree)          |
|  ──────────────  |
|  ⚠ Unassigned(5) |   <-- visible only when count > 0
+------------------+
```

Clicking the Unassigned section opens the Codes tab in **unassigned mode**, showing all codes with `ProductId IS NULL`. The same filter/search/select/action controls are available.

### 2.3 Left Panel — Tree with Toolbar

Move "Add Folder" / "Add Product" buttons into the left panel as a compact icon toolbar above the tree (like VS Code's file explorer).

```
+------------------+
| [+F] [+P]       |   <-- icon toolbar
|------------------|
|  v Juice         |
|    v Apple       |
|      * 0.5L     |
|      * 1.0L     |
|    > Orange     |
|  v Water        |
|    * Still 0.5L |
|  v Milk         |
|    * 1.0L       |
+------------------+
```

- **[+F]** — Add Folder (folder icon with +)
- **[+P]** — Add Product (file/leaf icon with +)

Delete lives exclusively in the Settings tab as a "danger zone" action (see section 3.5).

**Key change:** These buttons now operate relative to the **selected node** but with corrected logic (see section 3.3 for root-level creation fix).

### 2.4 Tree Always Expanded by Default

On page load, all tree nodes should be expanded. The operator needs to see the full hierarchy immediately — there are typically 10-30 products, not thousands. If needed, individual folders can be collapsed by the user.

**Implementation:** After loading tree data, programmatically expand all `TreeViewItem` nodes. Use an attached behavior or iterate items in code-behind after render.

---

## 3. Button-by-Button Specification

### 3.1 [Import CSV...] (Operations Tab)

**Purpose:** Import codes from a CSV file into the selected product's code pool.

**Preconditions:**
- A leaf product (not folder) must be selected
- Button disabled if no product selected or selected item is a folder

**Flow:**
1. Click button -> opens `OpenFileDialog` (filter: `*.csv`)
2. User selects file, clicks Open
3. System reads file lines, filters blank lines
4. System validates each code:
   - Not empty
   - No SPPL forbidden sequences (`^`, `~gt~`, `~sc~`, `~`)
   - Not a duplicate (globally unique across ALL products, ALL statuses except `Returned`)
5. Valid codes inserted with status `Available`, `ImportOrder` assigned (continues from last max)
6. UI refreshes: code pool stats update, activity history gets new entry
7. Audit log entry created: `{batchName, imported, duplicates, errors}`

**What user sees:**
- During import: ideally a brief progress indicator (currently instant for typical file sizes)
- After success: pool counts update immediately. New row appears in activity history.
- On validation errors: error message listing rejected rows (currently via exception — should show a summary dialog)

**Edge cases & validation checks:**
- Empty file -> show message "File is empty", do not import
- All lines are duplicates -> show "0 codes imported, N duplicates"
- Mixed valid/invalid -> import valid ones, report rejected ones
- File with Windows line endings (CRLF) vs Unix (LF) -> handle both
- UTF-8 BOM at start of file -> strip it
- Extremely large file (100k+ lines) -> should not freeze UI (consider background task)

**Missing from current implementation:**
- No progress feedback during large imports
- No summary dialog after import (user has to infer success from pool count change)
- No handling of file encoding issues

### 3.2 [+ New Job] (Operations Tab)

**Purpose:** Navigate to the New Job screen with this product preselected.

**Preconditions:**
- A leaf product must be selected
- Button disabled if selected item is a folder or no selection
- **Button disabled if `AvailableCodesCount == 0`** (no codes to print)

**Flow:**
1. Click button (only clickable when `CanCreateNewJob == true`)
2. System fires `NavigateToNewJobRequested` event with `productId`
3. MainViewModel navigates to New Job page with product preselected

**What user sees:**
- Button greyed out when pool is empty — tooltip shows "Available: 0"
- When enabled: instant navigation to New Job screen
- Product dropdown already shows the selected product
- Available count visible next to product name

**Validation checks:**
- Button disabled for folders (`IsLeaf == false`)
- Button disabled when nothing selected
- **Button disabled when `AvailableCodesCount == 0`** — prevents creating jobs that will immediately fail due to insufficient codes
- Tooltip shows current available count for visibility

**Implementation:**
- `CanCreateNewJob` property set in `RefreshCodeCountsAsync()`: `CanCreateNewJob = AvailableCodesCount > 0`
- `NewJob()` command checks both `IsLeaf == true` and `CanCreateNewJob`
- XAML: `IsEnabled="{Binding CanCreateNewJob}"`

### 3.3 [+F] Add Folder (Left Panel Toolbar)

**Purpose:** Create a new folder in the product tree.

**Current behavior (BUG):**
```csharp
var parentId = SelectedProduct?.IsLeaf == false ? SelectedProduct.Id : SelectedProduct?.ParentId;
```
- If a folder is selected -> new folder becomes child of selected folder
- If a product (leaf) is selected -> new folder becomes sibling (uses parent of the leaf)
- If **nothing is selected** -> `parentId = null` -> creates at root level

**Problem:** Once root-level items exist, you cannot create more root-level items unless you deselect everything (which is hard in a TreeView — clicking outside doesn't always deselect).

**Proposed fix — new parent resolution logic:**
```
1. Nothing selected -> create at root (parentId = null)
2. Folder selected -> create as CHILD of that folder
3. Leaf selected -> create as SIBLING (parentId = leaf's parent)
4. NEW: Add explicit "Create at root" option OR
   allow deselecting by clicking empty space in tree
```

**Recommended approach:** 
- Add a small "deselect" mechanism: clicking the empty area below tree items clears selection
- When selection is null, toolbar buttons create at root
- This matches the mental model of "I'm adding to wherever I'm looking"

**What user sees:**
1. Click [+F] -> inline form appears at the TOP of the left panel (above tree)
2. Form shows: text field "Folder Name" + [Create] + [Cancel]
3. User types name, clicks Create
4. Tree refreshes with new folder in correct position
5. New folder is auto-selected and expanded

**Validation checks:**
- Empty name -> button disabled or show error
- Whitespace-only name -> trim and reject if empty
- Duplicate name at same level -> currently allowed (should we prevent? probably not — some products have same name in different categories)

### 3.4 [+P] Add Product (Left Panel Toolbar)

**Purpose:** Create a new leaf product.

**Same parent resolution as [+F]** (see 3.3 above).

**What user sees:**
1. Click [+P] -> inline form appears at the TOP of the left panel
2. Form shows:
   - Product Name (text field, required)
   - Template File (.rox) (text field + [Browse...] button)
   - Printer CSV Name (text field)
   - [Create] + [Cancel] buttons
3. User fills in name (minimum required), optionally sets template and CSV name
4. Click Create -> tree refreshes, new product appears and is selected
5. Detail pane shows the new product (Settings tab for further configuration)

**Validation checks:**
- Name required (non-empty after trim)
- Template file: if specified, verify file exists on disk
- CSV name: if empty, could auto-derive from product name (e.g., "Apple 0.5L" -> "apple_05.csv")
- CSV name should not contain spaces or special characters (printer filesystem limitation)

**Missing from current:**
- No validation that template file actually exists
- No auto-derivation of CSV name
- No validation of CSV name format

### 3.5 [Delete Product] (Settings Tab — Danger Zone)

**Purpose:** Delete the selected folder or product, with explicit handling of any codes belonging to it.

**Location:** Settings tab only, at the bottom in a visually distinct "danger zone" section. NOT in the left panel toolbar — deletion is infrequent and destructive, so it belongs behind an extra navigation step.

**Preconditions:**
- A node must be selected
- Node must pass `CanDeleteAsync` check:
  - No active jobs (status: Preparing, Ready, Printing, Paused)
  - No reserved codes (status: Reserved)
  - For folders: ALL children must also be deletable (recursive)

**Flow — zero-code products (simple):**
1. User navigates to Settings tab
2. Sees [Delete Product] button at the bottom (disabled if blocked)
3. Clicks button -> simple confirmation dialog: "Are you sure you want to delete {name}? This cannot be undone."
4. User clicks Yes -> product removed from DB
5. Tree refreshes, selection moves to parent or first sibling

**Flow — products with codes (three-button dialog):**
1. User clicks [Delete Product]
2. System checks `CanDeleteAsync` -> if false, shows error with reason
3. System counts codes via `GetCodeCountAsync`
4. Dialog: "'{name}' has {N} codes. What would you like to do?"
   - **[Keep Codes]** — codes are moved to the unassigned pool (`ProductId` set to `NULL` via `DeleteBehavior.SetNull`). Product is removed. Codes remain in the system and can be managed via the Unassigned section.
   - **[Delete Codes Too]** — codes are archived to `archived_codes` table (preserving code text, status, batch, job history). Code text becomes available for re-import elsewhere. Product is removed.
   - **[Cancel]** — no action taken.
5. Tree refreshes, selection moves to parent or first sibling. If codes were kept, the Unassigned count updates.

**What user sees:**
- Button disabled (greyed out) when selection cannot be deleted
- Explanation text next to disabled button: "Has active jobs or reserved codes"
- For zero-code products: simple Yes/No confirmation
- For products with codes: three-button dialog explaining the consequences of each choice
- On success: node disappears, detail pane clears

**Edge cases:**
- Deleting a folder with children -> refuse deletion if folder has children (require emptying first)
- Deleting a product with historical data (printed codes, completed jobs) -> allowed (only block on ACTIVE jobs/reserved codes)
- Codes are **never silently deleted**. The operator must explicitly choose what happens to them.

### 3.6 [Change] Template (Settings Tab)

**Purpose:** Change the .rox template file assigned to a product.

**Preconditions:**
- Leaf product selected

**Flow:**
1. Click [Change] -> `OpenFileDialog` (filter: `*.rox`)
2. User selects file
3. System saves the full file path to `ProductNode.TemplateFile`
4. UI refreshes to show new path

**What user sees:**
- Current template path displayed (or "(not set)" if none)
- After change: new path shown immediately

**Validation checks:**
- File must exist on disk
- File should have `.rox` extension (warn if different but allow)
- **Important safety check:** If there is an active job for this product, changing template should be blocked (the printer already has the old template loaded)

**Missing from current:**
- No check for active jobs before allowing template change
- No file existence validation (just saves whatever OpenFileDialog returns — which is always valid since the dialog requires existing files)
- Should warn if old template was different and there are already codes imported (indicates the product is in use)

### 3.7 [Save] CSV Name (Settings Tab)

**Purpose:** Save the `PrinterCsvName` field after editing.

**What it does:**
- The CSV name is what the application uses as the filename when uploading codes to the printer
- Example: product "Apple 0.5L" -> CSV name "apple_05.csv" -> printer stores codes in this file

**Flow:**
1. User edits the text field inline
2. Clicks [Save]
3. System calls `_db.SaveChangesAsync()` (saves the tracked entity change)

**What user sees:**
- Editable text field with current value
- [Save] button confirms the change
- No visible feedback other than the button click completing

**Validation checks:**
- Cannot be empty for a product that already has codes imported (needed for print flow)
- Should not contain: spaces, special chars, forbidden SPPL sequences
- Should end in `.csv` (auto-append if missing?)
- Must be valid as a filename on the printer filesystem
- **Safety check:** If there's a CSV with the old name already on the printer, warn that it will become orphaned

**Missing from current:**
- No format validation
- No feedback after save (should show brief "Saved" indicator)
- No warning about orphaned files on printer

### 3.8 Delete — Additional Context Display

Section 3.5 fully covers the delete behavior. Additional UX detail for the Settings tab:
- Shows specific reason when blocked: "Job #47 is currently printing" or "15 codes are reserved"
- Visual separation: horizontal rule or red-tinted border above the delete section
- Button text: "Delete Product" for leaves, "Delete Folder" for non-leaves

### 3.9 [Rename] (Settings Tab + Folder Detail Pane)

**Purpose:** Rename a product or folder without deleting and recreating it.

**Location:** 
- **Leaf products:** Settings tab, at the top — "Name:" label + current name + [Rename] button
- **Folders:** Folder detail pane, at the top — same layout

**Preconditions:**
- A node (leaf or folder) must be selected

**Flow:**
1. User clicks [Rename]
2. Current name appears in an editable text field with [Save] and [Cancel] buttons
3. User edits the name and clicks [Save]
4. System validates the name (non-empty, trimmed)
5. System calls `ProductService.UpdateAsync(node)` to persist
6. Tree refreshes to show the new name

**What user sees:**
- Static display: `Name: [Apple 0.5L] [Rename]`
- Editing mode: `Name: [____editable field____] [Save] [Cancel]`
- After save: editing mode closes, tree and detail header update immediately

**Validation checks:**
- Empty/whitespace-only name is rejected (no-op)
- Name is trimmed of leading/trailing whitespace
- If the name is unchanged (same as current), form closes without calling the service

**State management:**
- `IsRenaming` bool — toggles between static and editing views
- `EditName` string — bound to the text field, initialized from `SelectedProduct.Name`
- **Selection change auto-cancels** any in-progress rename (resets `IsRenaming = false`)

**Implementation:**
```csharp
[RelayCommand] void ShowRename()    // Sets EditName = SelectedProduct.Name, IsRenaming = true
[RelayCommand] void CancelRename()  // Sets IsRenaming = false, clears EditName
[RelayCommand] async Task ConfirmRenameAsync()  // Validates, updates, refreshes tree
```

### 3.10 Codes Tab (Detail Pane — Third Tab)

**Purpose:** Inspect, filter, and manage individual codes for the selected product (or all unassigned codes).

**Location:** Third tab in the detail pane, labeled "Codes". Also used in Unassigned mode (same UI, different data source).

**Preconditions:**
- A leaf product is selected (or Unassigned section is active)
- Tab is always visible for leaf products; disabled for folders

#### Controls

**Status filter** — ComboBox with options: All, Available, Reserved, Printed, Returned, Burned, Quarantined. Defaults to "All". Changing the filter reloads page 1.

**Search** — TextBox with debounced input (300ms). Searches by `CodeText` substring. Clearing the search reloads the current filter.

**Refresh** — Button that reloads the current page with current filter/search.

**Page size** — ComboBox with options: 100 (default), 250, 500, 1000. Changing page size reloads page 1.

**Pagination** — Previous / Next buttons. Page N of M display. Disabled at boundaries.

#### DataGrid Columns

| Column | Binding | Notes |
|--------|---------|-------|
| ☐ (checkbox) | `IsSelected` | Disabled for Reserved codes (protected) |
| Code Text | `CodeText` | Full code string |
| Status | `Status` | Color-coded by `CodeStatusToColorConverter` |
| Batch | `ImportBatch` | Source CSV filename |
| Job | `JobId` | Which job last touched this code (if any) |
| Changed | `StatusChangedAt` | Last status change timestamp |

#### Selection

- **Individual select** — toggle checkboxes. Reserved codes have disabled checkboxes.
- **Select All** — selects all non-reserved codes on the current page only.
- **Deselect All** — clears all selections on the current page.

#### Actions (selected codes)

**Change Status** — dropdown with target status options. Shows confirmation dialog for risky transitions:
- Printed → Available: "This code was confirmed printed. Returning it to Available could cause a DUPLICATE. Are you sure?"
- Burned → Available: "This code was burned. Returning it may risk a duplicate if it was physically printed."
- Quarantined → Available: "This code's print state is uncertain. Verify it was NOT physically printed before releasing."

Reserved codes are excluded from status changes (enforced server-side even if UI somehow allows it).

**Move to Product** — dropdown listing all leaf products. Moves selected codes to the target product. In unassigned mode, this is the primary way to reassign codes.

**Archive** — removes selected codes from the active pool. Codes are saved to `archived_codes` table. Code text becomes available for re-import. Confirmation: "Archive {N} codes? They will be removed from the active pool. Their code text can be re-imported elsewhere."

#### Bulk Actions

When a specific status filter is active (not "All"), bulk action buttons appear:
- **Change All {Status}** — changes all codes matching the current filter (not just current page) to a new status.
- **Move All {Status}** — moves all codes matching the current filter to another product.
- **Archive All {Status}** — archives all codes matching the current filter.

Bulk actions always show a count confirmation: "This will affect {N} codes. Continue?"

Bulk is disabled when filter is "All" to prevent accidental mass operations.

#### Undo

- **[Undo Last Action]** button — reverses the most recent operation.
- Bounded stack of 10 operations.
- Safety validation: undo is blocked if any of the affected codes have been touched by a subsequent print job (the undo would conflict with job state).
- Archive undo checks that the code text hasn't been re-imported elsewhere (uniqueness conflict).
- On undo failure, the operator sees a message explaining why the undo was blocked.

#### Unassigned Mode

When the operator clicks the Unassigned section below the tree:
- The detail pane switches to show the Codes tab in unassigned mode.
- Header shows "Unassigned Codes" instead of a product name.
- Operations and Settings tabs are not shown.
- Same filter/search/select/action controls are available.
- The "Move to Product" action is the primary workflow for reassigning unassigned codes.

**Implementation:**
- `CodesTabViewModel` handles all code management logic.
- `ProductsViewModel` owns a `CodesTab` instance and sets its `ProductId` (or null for unassigned mode).
- `CodesChanged` event triggers refresh of parent ViewModel stats (Available/Printed/Burned/Quarantined counts, Unassigned count).

---

## 4. Folder Detail Pane

When a **folder** (non-leaf) is selected, the right pane shows different content:

```
+--------------------------------------+
|  JUICE (folder)                      |
|  ----------------------------------- |
|                                       |
|  Name: Juice              [Rename]  |
|  ----------------------------------- |
|                                       |
|  Contains:                           |
|    3 products, 1 subfolder           |
|                                       |
|  Aggregate Pool:                     |
|    Available: 24,500                 |
|    Printed:   5,200                  |
|    Total:     30,000                 |
|                                       |
|  (Select a product for details)     |
|                                       |
+--------------------------------------+
```

The rename UI is the same as for leaf products (see §3.9): clicking [Rename] swaps the label for an editable text field with [Save] and [Cancel].

**Nice-to-have:** Show aggregate stats for all products under this folder. Not critical for Phase 1 but useful for operators managing large catalogs.

---

## 4b. Activity History (Merged Import + Print History)

The Operations tab shows a single chronological **Activity History** section that combines both import events and print job events for the selected product. This gives the operator a complete picture of what happened with this product's codes without switching between screens.

### What appears in the list

| Event Type | Source | Display Format |
|------------|--------|----------------|
| Import | `audit_log` where `event_type = 'import'` | `{date} Imported {count} codes — {batch_name}.csv` |
| Job Completed | `print_jobs` where `status = 'Completed'` AND `codes_confirmed > 0` | `{date} Job #{id} completed — {confirmed}/{quantity} printed` |
| Job Cancelled | `print_jobs` where `status = 'Cancelled'` AND `codes_confirmed > 0` | `{date} Job #{id} cancelled — {confirmed}/{quantity} printed` |
| Job Error | `print_jobs` where `status = 'Error'` AND `codes_confirmed > 0` | `{date} Job #{id} error — {confirmed}/{quantity} printed` |

> **Filter rule:** Only jobs that actually printed at least one code (`codes_confirmed > 0`) appear in history. Jobs cancelled before any printing started (e.g., cancelled during preparation) are excluded — they had no material effect on the code pool.

### Sort order

All entries merged into one list, sorted by date **descending** (newest first). Show at most 20 entries.

### Visual design

```
History:
  Aug 10 14:30  Job #52 completed — 500/500 printed         [green dot]
  Aug 09 11:00  Imported 10,000 codes — gold_0.5_10000.csv  [blue dot]
  Aug 08 16:45  Job #48 cancelled — 200/500 printed         [orange dot]
  Aug 06 09:20  Imported 5,000 codes — batch_aug6.csv       [blue dot]
  Aug 05 10:15  Job #45 completed — 1,000/1,000 printed     [green dot]
```

- Color-coded dots: green = completed/info, purple = import, blue = started/resumed, amber = warning/paused, gray = cancelled, red = error
- Each row is compact (one main line + optional detail line)
- Clicking a job entry could optionally navigate to the Jobs page with that job selected (nice-to-have)

### Data query

```csharp
private async Task LoadActivityHistoryAsync()
{
    ActivityHistory.Clear();
    if (SelectedProduct == null || !SelectedProduct.IsLeaf) return;

    // Get import events from audit log
    var imports = await _db.AuditLog
        .Where(a => a.EventType == "import" && a.ProductId == SelectedProduct.Id)
        .OrderByDescending(a => a.CreatedAt)
        .Take(20)
        .Select(a => new ActivityHistoryItem
        {
            Date = a.CreatedAt,
            Type = ActivityType.Import,
            Description = a.Details ?? "Imported codes"
        })
        .ToListAsync();

    // Get jobs that actually printed at least one code
    var jobs = await _db.PrintJobs
        .Where(j => j.ProductId == SelectedProduct.Id &&
            (j.Status == JobStatus.Completed || j.Status == JobStatus.Cancelled || j.Status == JobStatus.Error) &&
            j.CodesConfirmed > 0)
        .OrderByDescending(j => j.CompletedAt ?? j.CreatedAt)
        .Take(20)
        .Select(j => new ActivityHistoryItem
        {
            Date = j.CompletedAt ?? j.CreatedAt,
            Type = j.Status == JobStatus.Completed ? ActivityType.JobCompleted :
                   j.Status == JobStatus.Cancelled ? ActivityType.JobCancelled :
                   ActivityType.JobError,
            Description = $"Job #{j.Id} {j.Status.ToString().ToLower()} — {j.CodesConfirmed}/{j.Quantity} printed"
        })
        .ToListAsync();

    // Merge and sort by date descending, take 20
    var merged = imports.Concat(jobs)
        .OrderByDescending(h => h.Date)
        .Take(20);

    foreach (var item in merged)
        ActivityHistory.Add(item);
}
```

### ViewModel types

```csharp
public enum ActivityType { Import, JobCompleted, JobCancelled, JobError }

public class ActivityHistoryItem
{
    public DateTime Date { get; init; }
    public ActivityType Type { get; init; }
    public string Description { get; init; } = string.Empty;

    // Formatted for display
    public string DateText => Date.ToLocalTime().ToString("MMM dd HH:mm");
    public string TypeIcon => Type switch
    {
        ActivityType.Import => "blue",
        ActivityType.JobCompleted => "green",
        ActivityType.JobCancelled => "orange",
        ActivityType.JobError => "red",
        _ => "gray"
    };
}
```

### When to refresh

- On product selection change
- After a CSV import completes
- After a job completes/cancels (via `JobEventBus.Completed` subscription)

---

## 5. Functional Checks (Behavioral Requirements)

These are conditions the page must enforce at all times:

| # | Rule | Enforcement |
|---|------|-------------|
| F1 | A product cannot be created without a name | Button disabled / validation message |
| F2 | Template file must exist on disk when assigned | Validated at assignment time |
| F3 | CSV name must be a valid printer filename | Validated on save |
| F4 | Cannot delete product with active jobs | `CanDeleteAsync` + UI disabling |
| F5 | Cannot delete product with reserved codes | `CanDeleteAsync` + UI disabling |
| F6 | Import must reject SPPL-forbidden sequences | `CodeValidator.GetValidationError` per code |
| F7 | Import must reject globally duplicate codes | DB query before insert |
| F8 | Codes are always reserved in FIFO order | `OrderBy(ImportOrder)` in `ReserveCodesAsync` |
| F9 | Code pool stats must refresh after any state change | `RefreshCodeCountsAsync` called after import |
| F10 | Cannot change template while product has active job | New check needed |
| F11 | Tree must show all levels expanded by default | Behavior on load |
| F12 | Adding folder/product at root must work even when tree has items | Selection-null = root |
| F13 | Cannot create new job when available codes = 0 | `CanCreateNewJob` property + `IsEnabled` binding |
| F14 | Rename must reject empty/whitespace-only names | `ConfirmRenameAsync` validates before update |
| F15 | Rename must trim leading/trailing whitespace | `EditName.Trim()` before save |
| F16 | Rename no-ops when name is unchanged | Skip `UpdateAsync` if trimmed == current |

---

## 6. Unit Tests

### 6.1 ProductService Tests

```
ProductService_CreateFolder_RootLevel
  - parentId = null -> created with ParentId = null
  - verify Name, IsLeaf=false, timestamps set

ProductService_CreateFolder_UnderParent
  - parentId = existing folder id -> created with correct ParentId
  - verify parent exists and IsLeaf=false

ProductService_CreateProduct_WithAllFields
  - name, parentId, templateFile, csvName all set
  - verify IsLeaf=true, all fields persisted

ProductService_CreateProduct_MinimalFields
  - only name set, others empty/null
  - product created successfully

ProductService_CanDelete_NoJobsNoCodes
  - product with no active jobs, no reserved codes -> true

ProductService_CanDelete_HasActiveJob
  - product with Printing job -> false

ProductService_CanDelete_HasReservedCodes
  - product with reserved codes (no active job) -> false

ProductService_CanDelete_CompletedJobsOnly
  - product with only Completed/Cancelled jobs -> true (historical data doesn't block)

ProductService_Delete_Success
  - deletable product -> removed from DB

ProductService_Delete_Blocked
  - non-deletable product -> throws InvalidOperationException

ProductService_GetRoots_ReturnsRootNodes
  - returns only nodes with ParentId == null
  - includes Children navigation property

ProductService_GetRoots_OrderedByName
  - verify alphabetical ordering
```

### 6.2 CodePoolService Tests

```
CodePoolService_Import_ValidCodes
  - list of unique valid codes -> all imported as Available
  - ImportOrder assigned sequentially from max+1
  - ImportBatch set to provided batchName

CodePoolService_Import_DuplicateWithinFile
  - file with duplicate lines -> NOTE: current impl doesn't dedup within file, only against DB
  - decide: should we dedup within file? (probably yes)

CodePoolService_Import_DuplicateAgainstExistingPool
  - code already exists in DB -> skipped, reported as duplicate
  - does NOT count as error

CodePoolService_Import_ForbiddenSequences
  - code contains "^" -> rejected with error
  - code contains "~gt~" -> rejected with error
  - code contains "~sc~" -> rejected with error
  - code contains "~" anywhere -> rejected with error

CodePoolService_Import_EmptyCode
  - blank/whitespace-only lines filtered out by caller (ViewModel)
  - but if passed directly: should validate and reject

CodePoolService_Import_MixedValidAndInvalid
  - 10 codes: 7 valid, 2 duplicate, 1 forbidden
  - result: imported=7, duplicates=2, errors=1
  - all 7 valid codes saved to DB

CodePoolService_Import_EmptyList
  - empty list passed -> imported=0, no error thrown

CodePoolService_Import_OrderPreservation
  - import 5 codes -> ImportOrder = [max+1 .. max+5]
  - import 3 more -> ImportOrder = [max+6 .. max+8]
  - guarantees FIFO selection in future reservations

CodePoolService_Reserve_FIFO
  - import codes A, B, C, D, E in order
  - reserve 3 -> get A, B, C (lowest ImportOrder)

CodePoolService_Reserve_InsufficientCodes
  - 5 available, request 10 -> throws InvalidOperationException

CodePoolService_Reserve_LowStockAlert
  - after reserve, remaining < 500 -> alert raised

CodePoolService_Reserve_ExactlyZeroRemaining
  - reserve all available codes -> alert raised (0 remaining)

CodePoolService_ReturnCodes_BecomesAvailable
  - return reserved codes -> status = Available, JobId = null

CodePoolService_MarkPrinted_CorrectRange
  - reserve 10, mark 5 printed -> first 5 = Printed, last 5 = Reserved

CodePoolService_BurnCode_SingleCode
  - burn code at boundary -> status = Burned

CodePoolService_GetPoolStats_AllStatuses
  - product with codes in all states -> returns correct counts per status
```

### 6.3 ProductsViewModel Tests

```
ProductsViewModel_LoadProducts_PopulatesCollection
  - mock IProductService.GetRootsAsync -> VM.Products populated

ProductsViewModel_SelectProduct_RefreshesStats
  - select leaf product -> AvailableCodesCount, PrintedCodesCount, etc. updated
  - select folder -> all counts = 0

ProductsViewModel_SelectProduct_LoadsActivityHistory
  - select leaf -> ActivityHistory populated from audit log (imports + jobs, merged chronologically)
  - select folder -> ActivityHistory cleared

ProductsViewModel_ImportCsv_RefreshesCountsAndHistory
  - after import -> RefreshCodeCountsAsync + LoadActivityHistoryAsync called

ProductsViewModel_AddFolder_ParentResolution_NothingSelected
  - SelectedProduct = null -> parentId = null (root level)

ProductsViewModel_AddFolder_ParentResolution_FolderSelected
  - SelectedProduct = folder -> parentId = folder.Id

ProductsViewModel_AddFolder_ParentResolution_LeafSelected
  - SelectedProduct = leaf -> parentId = leaf.ParentId

ProductsViewModel_AddFolder_EmptyName_NoAction
  - NewNodeName = "" -> ConfirmAddFolder does nothing

ProductsViewModel_AddProduct_EmptyName_NoAction
  - NewNodeName = "" -> ConfirmAddProduct does nothing

ProductsViewModel_DeleteProduct_Confirmation
  - user clicks Yes -> delete proceeds
  - user clicks No -> no action taken

ProductsViewModel_DeleteProduct_BlockedByService
  - CanDeleteAsync returns false -> CanDeleteSelectedProduct = false
  - button disabled in UI

ProductsViewModel_ChangeTemplate_UpdatesEntity
  - after dialog -> SelectedProduct.TemplateFile updated

ProductsViewModel_SaveCsvName_PersistsChange
  - after save -> SaveChangesAsync called

ProductsViewModel_NewJob_FiresEvent
  - leaf selected WITH available codes, NewJob called -> NavigateToNewJobRequested fired with product ID

ProductsViewModel_NewJob_FolderSelected_NoEvent
  - folder selected -> event not fired

ProductsViewModel_NewJob_ZeroAvailable_NoEvent
  - leaf selected but AvailableCodesCount == 0 -> event not fired, button disabled

ProductsViewModel_CanCreateNewJob_TrueWhenAvailable
  - leaf with Available > 0 -> CanCreateNewJob = true

ProductsViewModel_CanCreateNewJob_FalseWhenDepleted
  - leaf with Available == 0 (printed/burned only) -> CanCreateNewJob = false

ProductsViewModel_CanCreateNewJob_FalseForFolderOrNull
  - folder selected or nothing selected -> CanCreateNewJob = false

ProductsViewModel_CanDelete_UpdatesOnSelection
  - select product with active job -> CanDeleteSelectedProduct = false, reason set
  - select product with no blockers -> CanDeleteSelectedProduct = true

ProductsViewModel_ShowRename_SetsEditNameAndFlag
  - node selected -> ShowRename sets EditName = current name, IsRenaming = true

ProductsViewModel_ShowRename_NothingSelected_NoOp
  - nothing selected -> IsRenaming stays false

ProductsViewModel_CancelRename_ClearsState
  - IsRenaming = false, EditName cleared

ProductsViewModel_ConfirmRename_UpdatesName
  - new name provided -> node.Name updated, UpdateAsync called, tree refreshed

ProductsViewModel_ConfirmRename_FolderWorks
  - folder selected -> rename works same as for leaf

ProductsViewModel_ConfirmRename_TrimsWhitespace
  - "  New Name  " -> saved as "New Name"

ProductsViewModel_ConfirmRename_EmptyName_NoOp
  - empty/whitespace -> no update

ProductsViewModel_ConfirmRename_SameName_NoOp
  - name unchanged -> form closes, no service call

ProductsViewModel_ConfirmRename_RefreshesTree
  - after rename -> LoadProductsAsync called

ProductsViewModel_SelectionChange_ClosesRename
  - switching selection while renaming -> IsRenaming = false
```

### 6.4 Integration Tests (with real DB)

```
Products_E2E_CreateFolderAndProduct
  - create folder at root, create product under it
  - verify tree structure, verify product has correct parent

Products_E2E_ImportAndReserve
  - create product, import 100 codes
  - reserve 50 -> verify 50 available, 50 reserved
  - reserve 50 more -> verify 0 available
  - attempt reserve 1 -> throws (insufficient)

Products_E2E_DeleteProductWithHistory
  - create product, import codes, run job to completion
  - delete product -> succeeds (completed jobs don't block)

Products_E2E_DeleteProductBlockedByActiveJob
  - create product, start job (status = Printing)
  - attempt delete -> fails
  - complete job -> delete succeeds

Products_E2E_ImportDuplicateAcrossProducts
  - create productA, import code "ABC123"
  - create productB, import code "ABC123" -> reported as duplicate, not imported

Products_E2E_ImportPreservesOrderAcrossBatches
  - import batch1 (3 codes), import batch2 (3 codes)
  - reserve 4 -> get batch1 (all 3) + batch2[0]
```

---

## 7. Detailed Change Proposals

### 7.1 Three-Tab Module

**What changes:**
- `ProductsView.xaml`: Replace single detail `StackPanel` with `TabControl` containing three tabs
- `ProductsViewModel.cs`: Owns a `CodesTabViewModel` instance, sets product context on selection change
- `CodesTabViewModel.cs`: Handles all code management logic (pagination, filter, actions, undo)
- Consider remembering last active tab per session (not critical)

**Tab content distribution:**

| Operations Tab (default) | Settings Tab | Codes Tab |
|--------------------------|--------------|-----------|
| Code Pool stats (5 lines incl. Quarantined) | **Name + [Rename]** | Status filter + Search |
| [Import CSV...] button | Template file + [Change] | Paginated DataGrid |
| [+ New Job] button (disabled if available=0) | CSV Name + [Save] | Select All / Deselect |
| Activity History (imports + jobs merged) | --- separator --- | Actions: Change Status, Move, Archive |
| | [Delete Product] (danger) | Undo Last Action |

### 7.2 Tree Always Expanded

**What changes:**
- `ProductsView.xaml.cs` (code-behind): After `LoadProductsAsync` completes, iterate all `TreeViewItem` containers and set `IsExpanded = true`
- Or: Use an attached behavior that auto-expands on `ItemsSource` change
- Or: Set `TreeViewItem` style in XAML:
  ```xml
  <TreeView.ItemContainerStyle>
      <Style TargetType="TreeViewItem">
          <Setter Property="IsExpanded" Value="True" />
      </Style>
  </TreeView.ItemContainerStyle>
  ```
- **Simplest approach:** The XAML `Style` setter (3rd option). One line, no code-behind needed.

### 7.3 Root Folder Creation Fix

**Problem:** The current logic `var parentId = SelectedProduct?.IsLeaf == false ? SelectedProduct.Id : SelectedProduct?.ParentId;` makes it impossible to create a root-level node when something is already selected.

**Fix options:**

**Option A: "Deselect" mechanism**
- Clicking empty space in the tree clears selection
- When nothing selected, [+F] and [+P] create at root
- Implementation: handle `TreeView.MouseDown` on the tree control background to clear selection

**Option B: Explicit "parent selector" in the add form**
- The inline add form shows a dropdown: "Add under: [Root / Juice / Apple / ...]"
- Defaults to selected folder, but user can change to root
- More explicit, less ambiguity

**Option C: Hold Shift/Ctrl to create at root**
- If user holds a modifier key when clicking [+F], always creates at root
- Discoverable issue: user won't know this exists

**Recommendation: Option A + clear affordance.** Add a "(root)" pseudo-item or a "Clear selection" icon button. When nothing is selected, show a hint text: "No selection — new items will be added at root level."

**Implementation (Option A):**
```csharp
// In ProductsView.xaml.cs
private void OnTreeBackgroundClick(object sender, MouseButtonEventArgs e)
{
    // Only if click was directly on TreeView background, not on an item
    if (e.OriginalSource is TreeView)
    {
        var treeView = sender as TreeView;
        // Clear selection by setting SelectedItem to null via ViewModel
        if (DataContext is ProductsViewModel vm)
            vm.SelectedProduct = null;
    }
}
```

Also add a visual indicator:
```
+------------------+
| [+F] [+P]       |
| Adding to: ROOT  |   <-- shows where new items will go
|------------------|
|  v Juice         |
```

### 7.4 Left Panel Button Placement

**Current:** Buttons are in the top-right header area of the entire page.

**Proposed:** Move to the left panel as an icon toolbar row above the tree.

**Visual design:**
```
+------------------+
| [+F] [+P]       |   16x16 icons, subtle background, 28px row height
|------------------|
|                  |   tree content below
```

**Button states:**
- [+F] always enabled (can always add a folder)
- [+P] always enabled (can always add a product)

**Icon suggestions:**
- +F: folder icon with small "+" badge
- +P: document/file icon with small "+" badge

**Tooltip text:**
- +F: "New Folder (adds under selected folder, or at root if nothing selected)"
- +P: "New Product (adds under selected folder, or at root if nothing selected)"

---

## 8. Data Flow Diagrams

### 8.1 Import CSV Flow

```
User clicks [Import CSV...]
    |
    v
OpenFileDialog (*.csv)
    |
    v
Read file lines
    |
    v
Filter blank lines
    |
    v
For each code:
    |-- CodeValidator.GetValidationError(code)
    |   |-- Contains "^"? -> error
    |   |-- Contains "~gt~"? -> error
    |   |-- Contains "~sc~"? -> error
    |   |-- Contains "~"? -> error
    |   |-- null = valid
    |
    |-- DB: EXISTS(code_text = code)? -> duplicate
    |
    |-- Valid + unique -> INSERT (Available, ImportOrder=++max)
    |
    v
SaveChangesAsync()
    |
    v
AuditService.LogAsync("import", ...)
    |
    v
RefreshCodeCountsAsync() -> UI updates
LoadActivityHistoryAsync() -> UI updates
```

### 8.2 Delete Product Flow

```
User clicks [Delete Product] (Settings tab)
    |
    v
CanDeleteAsync(id)
    |-- Any active jobs (Preparing/Ready/Printing/Paused)? -> blocked
    |-- Any reserved codes? -> blocked
    |-- Has children (folder)? -> blocked
    |
    v
GetCodeCountAsync(id)
    |
    |-- count == 0:
    |       |
    |       v
    |   Simple confirmation: "Delete {name}?"
    |       |-- No -> abort
    |       |-- Yes -> DeleteAsync(id) -> remove from DB
    |
    |-- count > 0:
    |       |
    |       v
    |   Three-button dialog:
    |   "'{name}' has {N} codes. What would you like to do?"
    |       |
    |       |-- [Keep Codes]:
    |       |       ProductId set to NULL (DeleteBehavior.SetNull)
    |       |       Codes move to unassigned pool
    |       |       DeleteAsync(id) -> remove product from DB
    |       |       RefreshUnassignedCountAsync()
    |       |
    |       |-- [Delete Codes Too]:
    |       |       ArchiveCodesBulkAsync(productId) -> codes saved to archived_codes
    |       |       Code text freed for re-import
    |       |       DeleteAsync(id) -> remove product from DB
    |       |
    |       |-- [Cancel] -> abort
    |
    v
Refresh tree
Clear selection
```

---

## 9. Error States & Messages

| Scenario | Message | Location |
|----------|---------|----------|
| Import: file is empty | "The selected file contains no codes." | Dialog after import attempt |
| Import: all duplicates | "0 new codes imported. {N} codes already exist in the system." | Summary dialog |
| Import: has errors | "Imported {X} codes. {Y} duplicates skipped. {Z} codes rejected:\n- Row 5: Contains forbidden character '^'" | Summary dialog |
| Delete: blocked by job | "Cannot delete: Job #{id} is currently {status} for this product." | Error dialog or tooltip |
| Delete: blocked by codes | "Cannot delete: {N} codes are currently reserved for active jobs." | Error dialog or tooltip |
| Template change: active job | "Cannot change template while Job #{id} is active. Complete or cancel the job first." | Error dialog |
| CSV name: invalid chars | "CSV name can only contain letters, numbers, underscores, and hyphens." | Inline validation |
| Reserve: insufficient codes | "Not enough codes. Available: {N}, Requested: {M}." | Shown in New Job screen |
| Codes: risky status change | "This code was confirmed printed. Returning it to Available could cause a DUPLICATE. Are you sure?" | Confirmation dialog (Codes tab) |
| Codes: archive confirmation | "Archive {N} codes? They will be removed from the active pool." | Confirmation dialog (Codes tab) |
| Codes: bulk operation | "This will affect {N} codes. Continue?" | Confirmation dialog (Codes tab) |
| Codes: undo blocked | "Cannot undo: {N} codes have been affected by subsequent print jobs." | Error dialog (Codes tab) |
| Codes: archive undo conflict | "Cannot undo archive: code text '{code}' has been re-imported into another product." | Error dialog (Codes tab) |
| Delete: product with codes | "'{name}' has {N} codes. What would you like to do?" | Three-button dialog (Settings tab) |

---

## 10. Accessibility & UX Notes

- **Keyboard navigation:** Tab through toolbar buttons, Enter to activate, Escape to cancel inline forms
- **Focus management:** After creating a node, focus moves to the new node in the tree. After deleting, focus moves to the previous sibling or parent.
- **Empty state:** When product tree is empty (fresh install), show: "No products yet. Click [+F] to create a folder or [+P] to create a product."
- **Loading state:** While tree loads, show a subtle spinner or skeleton
- **Large tree performance:** For 100+ products, consider virtualizing the TreeView

---

## 11. Summary of Changes from Current Implementation

| Area | Current | Proposed |
|------|---------|----------|
| Detail pane | Single scrollable panel | Three tabs: Operations + Settings + Codes |
| Add buttons | Top-right page header | Left panel icon toolbar |
| Root creation | Impossible when item is selected | Click empty area to deselect; add hint text |
| Tree expansion | User must manually expand | All expanded by default (Style setter) |
| Delete button | Right panel, always visible | Settings tab only (danger zone) — not in toolbar |
| **Delete dialog** | Simple Yes/No | Three-button dialog: Keep Codes / Delete Codes Too / Cancel (for products with codes) |
| Template change | No safety check | Block if active job exists |
| CSV name save | No validation | Format validation + feedback |
| Import result | Silent (just updates counts) | Summary dialog with counts and errors |
| Folder detail | Shows leaf-only fields (confusing) | Shows folder summary (child count, aggregate stats) + rename |
| **New Job button** | Always enabled for any leaf | **Disabled when available codes = 0** |
| **Rename** | Not available — must delete and recreate | **Inline rename in Settings tab (leaves) and folder pane** |
| **Codes tab** | Not available | **Paginated grid with filter/search, status change, move, archive, undo** |
| **Unassigned section** | Not available | **Shown below tree when unassigned codes exist; opens Codes tab in unassigned mode** |
| **Pool stats** | Available / Printed / Burned / Total | **Available / Printed / Burned / Quarantined / Total** |

---

## 12. Implementation Priority

1. **Tree always expanded** (trivial — one XAML line)
2. **Root creation fix** (small — add deselect mechanism)
3. **Three-tab layout** (medium — XAML restructure, no logic changes)
4. **Left panel toolbar** (medium — move buttons, add icons)
5. **Import summary dialog** (small — add result reporting)
6. **Template change safety check** (small — one async check)
7. **CSV name validation** (small — regex validation)
8. **Folder detail pane** (medium — new aggregate query + UI)
9. ~~**Codes tab + Unassigned section**~~ ✅ **DONE** (E9 — see `implementation-plan.md`)
10. **Unit tests** (parallel — can write alongside feature work)
