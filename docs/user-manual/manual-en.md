# Code Print Manager — Reference Manual

This manual covers every feature of Code Print Manager. For first-time setup, see the [Quick Start Guide](quick-start-en.md).

**Contents:**

1. [Application Overview](#page-1-application-overview)
2. [Setting Up](#page-2-setting-up)
3. [Printing](#page-3-printing)
4. [Managing Codes](#page-4-managing-codes)
5. [Printer Management](#page-5-printer-management)
6. [When Things Go Wrong](#page-6-when-things-go-wrong)
7. [Frequently Asked Questions](#page-7-frequently-asked-questions)

---

## Page 1: Application Overview

Code Print Manager is a Windows desktop application that manages the printing of unique government-issued product codes (Data Matrix / QR) on Savema thermal printers. It prevents duplicate code usage, minimizes waste, tracks every code from import to print, and recovers safely from failures.

### Main Window Layout

```
┌─────────────────────────────────────────────────────────┐
│                    CODE PRINT MANAGER                    │
├──────────┬──────────────────────────────────────────────┤
│          │                                              │
│   NAV    │            CONTENT AREA                      │
│          │                                              │
│ Dashboard│     (changes based on selected page)         │
│ Products │                                              │
│ Printers │                                              │
│ Jobs     │                                              │
│          │                                              │
│          │                                              │
│ [Lang ▼] │                                              │
├──────────┴──────────────────────────────────────────────┤
│ ALERTS                                                  │
└─────────────────────────────────────────────────────────┘
```

- **Left sidebar** — navigation menu. Click any item to switch the content area.
- **Content area** — the main working area, changes based on which page you selected.
- **Language selector** — at the bottom of the sidebar. Switch between English, Russian, and Armenian.
- **Alert bar** — always visible at the bottom of the window regardless of which page you are on.

### Navigation Pages

| Page | Purpose |
|------|---------|
| **Dashboard** | Live overview of all printers and their current jobs. Your main monitoring screen. |
| **Products** | Create and organize products, import codes, manage code pools. |
| **Printers** | Add and configure printers, manage files stored on printers. |
| **Jobs** | View active jobs in detail, browse job history. |

The **+ New Job** button appears on every page. It opens the job creation screen.

### Alerts

The alert bar shows real-time notifications:

- **Red alerts** — something needs your immediate attention (connection lost, job error). These stay visible until you dismiss them.
- **Yellow alerts** — something unusual happened that you should check (unexpected counter activity, low code stock). These stay visible until you dismiss them.
- **Green alerts** — informational (job completed, printer connected). These disappear automatically after 30 seconds.

Click the **X** button on any alert to dismiss it.

---

## Page 2: Setting Up

### Adding Printers

Each Savema printer on your network needs to be registered in the application.

1. Go to **Printers**
2. Click **+ Add Printer**
3. Enter:
   - **Name** — a friendly name you will recognize (e.g., "Production Line 1")
   - **IP Address** — the printer's network address
   - **Port** — 9100 (default for Savema TTO printers)
4. Click **Save**

The application connects to the printer automatically. The status indicator shows:

| Status | Meaning |
|--------|---------|
| **Idle** (green) | Connected and ready to print |
| **Printing** (blue) | Currently printing |
| **Offline** (gray) | Not connected — check network and power |
| **Error** (red) | Printer has a problem — check the printer display |
| **Blocked** (yellow) | Printer interface is not on the main screen — return to the main screen on the printer |
| **Init** (gray) | Printer is starting up — wait a moment |

The application automatically reconnects if a connection is lost. You do not need to do anything — it will keep trying in the background and notify you when the connection is restored.

### Creating Products

Products are organized in a tree, similar to folders on a computer.

- **Folders** are for grouping only (e.g., "Juice", "Water")
- **Products** are what you actually print on (e.g., "Apple 0.5L")

```
▼ Juice
  ▼ Apple
    ● 0.5L        ← product (printable)
    ● 1.0L        ← product (printable)
  ▼ Orange
    ● 0.33L       ← product (printable)
▼ Water
  ● Still 0.5L    ← product (printable)
```

**To create a folder:** Click **+ Folder**, enter a name, and save.

**To create a product:** Click **+ Product**, enter a name, and save. Then go to the **Settings** tab to configure:

- **Template File** — the `.rox` file used by the printer for this product
- **Printer CSV Name** — the filename the printer uses for the code data (e.g., `apple_05.csv`)

> **Important:** Both the template file and CSV name must be set before you can create a print job for this product.

**To rename:** Right-click a product or folder and select **Rename**.

**To organize:** Items are added relative to whatever is selected in the tree. Select a folder first, then click **+ Product** to add a product inside that folder. Click on empty space to deselect and add at the root level.

### Importing Codes

Codes are imported from CSV files (typically downloaded from the government portal).

1. Select a product in the tree
2. On the **Operations** tab, click **Import CSV...**
3. Select your CSV file

The application validates every code:
- Empty or invalid codes are rejected
- Codes that already exist anywhere in the system (any product, any status) are rejected as duplicates
- A detailed report shows how many were imported and how many were rejected

After import, the **Code Pool** section shows the updated counts. The **Operations** tab also displays a unified activity history showing both imports and job outcomes chronologically.

> **Important:** Each code must be globally unique. You cannot import the same code into two different products. This is the core safety guarantee that prevents duplicate printing.

### Backing Up Your Data

The application stores everything in a local database. Where it lives depends on how you installed the application:

| Installation type | Data folder |
| --- | --- |
| Installed with `Setup.exe` | `%LocalAppData%\CodePrintManagerData` |
| Portable (extracted ZIP) | The folder you extracted the application into |

Paste `%LocalAppData%\CodePrintManagerData` into the Windows Explorer address bar to open it. The exact path is also written at the top of every log file as `DataDir`.

To back up your data, copy these three files from the data folder:

- `codeprintmanager.db`
- `codeprintmanager.db-shm` (if it exists)
- `codeprintmanager.db-wal` (if it exists)

To restore, copy them back. Make sure the application is closed first.

The data folder also holds a `backups\` subfolder — the application automatically saves a snapshot of the database each time it starts, keeping the last five.

> **Note:** For installed builds the data folder is deliberately kept outside the installation directory so that application updates cannot delete your codes. This also means uninstalling the application leaves `%LocalAppData%\CodePrintManagerData` in place — delete it manually if you want to remove your data as well.

---

## Page 3: Printing

### Creating a Print Job

A print job connects three things: a **product** (which codes to print), a **printer** (which machine to print on), and a **quantity** (how many codes).

1. Click **+ New Job** (available on any page). If you click it from the Products page, the product is preselected. If you click it from the Printers page, the printer is preselected.
2. Select a **Product** — the dropdown shows available code counts
3. Select a **Printer** — the dropdown shows printer status (busy or offline printers are grayed out)
4. Enter the **Quantity**
5. Click **Prepare**

**What happens during Prepare:**

The application performs several checks and setup steps:
- Verifies the printer is idle and ready
- Reserves the requested codes from the product's pool
- Uploads the code data to the printer
- Loads the print template

You see each step checked off as it completes. If anything fails, you get a clear error message with a **Retry** button.

6. When preparation succeeds, click **Start Print** to begin printing immediately, or **Go to Job** to review the job first.

### Monitoring Progress

**On the Dashboard:**

Only printers that have had at least one job are shown. Each appears as a card:

```
┌─────────────────────────────────────────────────────────┐
│  Line 1  192.168.1.100                      ● PRINTING  │
│  Job #47: Apple 0.5L   342/500 (68%)                    │
│  ████████████████████░░░░░░░░░           [Pause] [Cancel]│
└─────────────────────────────────────────────────────────┘
┌─────────────────────────────────────────────────────────┐
│  Line 2  192.168.1.101                      ● IDLE      │
│  Job #48: Water 0.5L   0/1000                           │
│  Prepared, waiting to start       [Start Print] [Cancel]│
└─────────────────────────────────────────────────────────┘
```

Cards are sorted so active jobs appear first: printing, then errors, then paused, then ready. Completed jobs appear last. Within each group, the most recently updated job appears at the top.

Click any card to go to the **Jobs** page with that job selected for full detail.

Below the printer cards, the Dashboard shows a **Recent Activity** feed — a chronological list of recent events (imports, job starts, completions, cancellations, alerts). Each entry is color-coded by type.

**On the Jobs page (Active Jobs tab):**

Select a job from the list to see full details including:
- Product and printer information
- Preparation checklist
- Live counter values
- Progress bar with exact numbers

### Pausing and Resuming

While a job is printing, you can click **Pause** to temporarily stop it. The printer stops and all progress is saved.

To continue, click **Resume**. The application re-uploads the remaining codes and picks up where it left off.

**When to pause:**
- You need to adjust something on the production line
- You want to check the printer before continuing
- You need to load more material

Pausing is safe — no codes are lost or wasted.

### Cancelling a Job

You can cancel a job at any time by clicking **Cancel**. Here is what happens:

- Codes already confirmed as printed stay marked as **Printed** (they were physically printed)
- Remaining unprinted codes go back to **Available** in the pool (they can be used in future jobs)
- If the printer has a quarantine margin configured, a small number of boundary codes may be marked as **Quarantined** for safety (see [Quarantined Codes](#quarantined-codes))

Cancelling is safe. You do not lose codes unnecessarily.

### Job History

The **Job History** tab on the Jobs page shows all past jobs:

```
┌─────────────────────────────────────────────────────────┐
│  [Active Jobs]  [Job History]                           │
│                                                         │
│  Filters:  [All Printers ▼]  [All Products ▼]          │
│                                                         │
│  #   Product          Printer  Qty    Status    Date    │
│  48  Orange 0.33L     Line 2   2000   Completed  Sep 10 │
│  47  Apple 0.5L       Line 1   500    Completed  Sep 10 │
│  46  Apple 0.5L       Line 1   500    Cancelled  Sep 9  │
│  45  Water 0.5L       Line 3   1000   Completed  Sep 9  │
└─────────────────────────────────────────────────────────┘
```

Click any row to see the full summary: codes printed, duration, and outcome. Use the dropdown filters to show only jobs from a specific printer or product.

---

## Page 4: Managing Codes

### Code Statuses

Every code in the system has a status. Here is what each one means:

| Status | Meaning |
|--------|---------|
| **Available** | Ready to be used in a print job. |
| **Reserved** | Currently assigned to a print job that is in progress. You cannot change or move reserved codes. |
| **Printed** | Confirmed as physically printed on a product. Cannot be reused. |
| **Returned** | Was reserved for a job that was cancelled before this code was printed. The code is available again for future jobs. |
| **Burned** | Permanently discarded. The operator confirmed this code should never be used (e.g., after investigating a failed print). |
| **Quarantined** | The application is not sure whether this code was physically printed. Frozen until you investigate and decide. See [Quarantined Codes](#quarantined-codes). |

### The Codes Tab

On the **Products** page, select a product and click the **Codes** tab to see all its codes:

```
┌─────────────────────────────────────────────────────────┐
│  [Operations]  [Settings]  [Codes]                      │
│                                                         │
│  Status: [All ▼]    Search: [____________]              │
│                                                         │
│  ☐  Code Text              Status      Batch    Job    │
│  ☐  010460043993857221...  Available   batch1   —      │
│  ☐  010460043993857221...  Printed     batch1   #47    │
│  ☐  010460043993857221...  Quarantined batch1   #46    │
│                                                         │
│  Page 1 of 100  (10,000 total)   Show: [100 ▼]         │
│  [Prev] [Next]                                         │
│                                                         │
│  Actions on selected (0):                              │
│  Change to: [Available ▼] [Apply]                      │
│  Move to:   [Select product ▼] [Move]                  │
│  [Archive Selected]                                    │
│  [Undo]                                                │
└─────────────────────────────────────────────────────────┘
```

**Filtering:** Use the **Status** dropdown to show only codes with a specific status. Use the **Search** box to find a specific code by its text.

**Selecting codes:** Check the boxes next to individual codes, or use **Select All** / **Deselect All**. Reserved codes (assigned to active jobs) are always protected and cannot be selected.

### Changing Code Status

Select one or more codes and use the **Change to** dropdown to change their status.

Common reasons to change status:
- **Quarantined to Available** — after investigating, you confirmed the code was NOT printed
- **Quarantined to Burned** — after investigating, you confirmed the code WAS printed or is damaged
- **Printed to Burned** — the print was defective and the product was discarded

> **Warning:** Changing a **Printed** or **Burned** code back to **Available** means it could be printed again. If the code was already physically on a product, this creates a duplicate — which is illegal. The application warns you before allowing this.

### Moving Codes

Select codes and use the **Move to** dropdown to transfer them to a different product or to the unassigned pool. This is useful when:
- You imported codes to the wrong product
- You are reorganizing your product structure
- You want to redistribute codes between products

### Archiving Codes

Select codes and click **Archive Selected** to remove them from the active pool. Archived codes:
- Are removed from the product's code pool
- Have their history preserved in the archive
- Free up the code text for re-import if needed later

### Undo

Click **Undo** to reverse the last code operation (status change, move, or archive). The application checks whether it is safe to undo — for example, undo is blocked if a subsequent print job has already used the affected codes.

### Bulk Operations

At the bottom of the Codes tab, you can perform operations on **all codes matching the current filter**, not just the ones on the current page:

- **Change All to...** — change the status of all filtered codes
- **Move All to...** — move all filtered codes to another product
- **Archive All** — archive all filtered codes

These are powerful operations. The application asks for confirmation before proceeding.

### Exporting Codes

Click **Export CSV** to download all codes matching the current filter as a CSV file. This is useful for record-keeping or for transferring code lists to other systems.

### Unassigned Codes

Sometimes codes exist without belonging to any product. This happens when:
- A product is deleted and you chose to keep its codes
- Codes were explicitly moved to the unassigned pool

Unassigned codes appear in a special section below the product tree on the Products page. Click it to manage them — you can move them to a product or archive them.

### Deleting a Product

When you delete a product that still has codes:

- If the product has **active jobs or reserved codes**, deletion is blocked. Cancel the jobs first.
- Otherwise, you are asked:
  - **Yes (Keep Codes)** — codes move to the unassigned pool. You can reassign them later.
  - **No (Delete Codes Too)** — codes are archived. Their text becomes available for re-import.
  - **Cancel** — nothing happens.

To delete a product: go to the **Settings** tab and click **Delete Product** in the danger zone.

---

## Page 5: Printer Management

### Connecting and Disconnecting

The application connects to all printers automatically on startup.

**To manually disconnect a printer:**
1. Go to **Printers**
2. Select the printer
3. Click **Disconnect**

> **Important:** If the printer has an active print job, disconnecting moves the job to **Disconnected** status. This means the printer's state is unknown — the application cannot tell whether the printer continued printing after disconnection. You will need to reconnect and explicitly resume or cancel the job. See [Printer Disconnects During Printing](#printer-disconnects-during-printing).

**To reconnect:** Click **Connect**. The application re-establishes the connection. If there was a disconnected job, you can then resume or cancel it from the Jobs page.

### Configuration

The **Configuration** tab shows the printer's settings and lets you edit them:

- **Name** — the display name used throughout the application
- **IP Address** — the printer's network address
- **Port** — the TCP port (default 9100)
- **Quarantine Margin** — how many boundary codes to quarantine when cancelling a mid-print job (default 0; set to 1 for maximum safety with high-value codes)

To change any setting, click **Edit**, modify the values, and click **Save**. The application reconnects automatically if you change the IP address or port.

### Storage Management

The **Storage** tab shows files stored on the printer:

```
┌─────────────────────────────────────────────────────────┐
│  [Configuration]  [Storage]                             │
│                                                         │
│  TEMPLATES ON PRINTER                       [Refresh]   │
│  ☐  apple_05_53.rox         ✅ Used (Apple 0.5L)       │
│  ☐  orange_033_53.rox       ✅ Used (Orange 0.33L)     │
│  ☑  old_test_53.rox         ⚠ Not mapped to any product│
│                                                         │
│  CSV FILES ON PRINTER                       [Refresh]   │
│  ☐  apple_05.csv            ✅ Used (Apple 0.5L)       │
│  ☑  old_data.csv            ⚠ Not mapped to any product│
│                                                         │
│  [Delete Selected (2)]                                  │
└─────────────────────────────────────────────────────────┘
```

- Files marked **Used** are linked to a product — they are protected and cannot be deleted
- Files marked **Not mapped** are orphaned — they are safe to delete and are pre-selected for cleanup

Click **Delete Selected** to remove orphaned files and free up printer storage.

### Verification

The **Verify** tab lets you check whether the printer's state matches what the application expects:

1. Select a printer
2. Click **Run Verification**

The application checks:
- **Connection** — is the printer reachable?
- **CSV file** — is the expected data file present on the printer?
- **Active template** — is the correct template loaded?
- **Counters** — do the printer's counters match the application's records?
- **Printer status** — is the printer in a healthy state?

Results are color-coded:
- **Passed** (green) — everything matches
- **Warning** (yellow) — something is different but not necessarily a problem
- **Failed** (red) — a mismatch that needs attention

---

## Page 6: When Things Go Wrong

Code Print Manager is designed to handle failures safely. The core principle: **if there is any uncertainty about whether a code was printed, the application marks it as used rather than risking a duplicate.** Wasting a code is acceptable; duplicating one is not.

### Printer Disconnects During Printing

**What happens automatically:**
- The job changes to **Disconnected** status
- The application preserves all confirmed progress
- An alert appears telling you which printer and job are affected
- Other printers and their jobs continue normally — they are completely independent

**What you see:**

```
┌─────────────────────────────────────────────────────────┐
│  Line 1  192.168.1.100                   ● DISCONNECTED │
│  Job #47: Apple 0.5L  342/500                           │
│  Disconnected at 342/500 — reconnect to resume          │
└─────────────────────────────────────────────────────────┘

ALERTS
  ⚠ Line 1: Printer disconnected. Job #47 moved to Disconnected.
```

**What to do:**

1. Fix the connection (check cables, network, printer power)
2. Go to **Printers** and click **Connect** to reconnect
3. Go to **Jobs** and select the disconnected job
4. Choose:
   - **Resume** — the application inspects the printer, reconciles counters, re-uploads remaining codes, and continues printing
   - **Cancel** — safely ends the job, quarantining any uncertain codes

> **Important:** The application does not automatically resume a disconnected job. Because the printer's state is unknown after a disconnect (it may have continued printing, or someone may have used it for something else), you must explicitly choose what to do.

### Power Failure or Application Crash

All progress is saved to the database continuously. Nothing is lost.

**When you restart the application after an unexpected shutdown:**

If there were any active jobs when the application stopped, you see a **Recovery** screen:

- Each interrupted job is listed with:
  - How many codes the application confirmed as printed
  - How many codes the printer actually printed (read from the printer's permanent counter)
  - Any discrepancy between the two

You can then choose for each job:
- **Resume** — continue from where it left off
- **Cancel** — safely end the job

The printer's **lifetime counter** never resets, even after a power cycle. The application uses this to determine exactly what happened while it was down.

### Unexpected Counter Jumps

If someone prints using the printer's own touchscreen or another system (outside this application), the application detects the unexpected counter increase.

**What you see:**
- A yellow alert: "Unexpected counter jump (+N)"
- The affected codes are conservatively marked as used

**What to do:**
- Check whether someone printed outside the application
- If the extra prints were intentional (e.g., a test), you can manage the affected codes in the Codes tab
- If no one used the printer, investigate further — this could indicate a hardware issue

### Quarantined Codes

Codes are quarantined when the application cannot be certain whether they were physically printed. This happens in situations like:

- Power failure during printing (some codes may have been printed between the last progress update and the failure)
- Cancelling a job when the printer may have printed additional codes after the last confirmed count
- Printer disconnect where the printer state is unknown

**Quarantined codes are frozen** — they cannot be automatically reused in a new job. This prevents accidental duplicates.

**How to resolve quarantined codes:**

1. Go to **Products** > select the product > **Codes** tab
2. Filter by status: **Quarantined**
3. Investigate each code:
   - Check the physical products on the production line
   - Verify whether the code was actually printed on a product
4. For each code, change its status:
   - **Available** — if you confirmed it was NOT printed (it can be reused)
   - **Burned** — if you confirmed it WAS printed or is otherwise unusable

> **Tip:** You can select multiple quarantined codes and change them all at once if they all have the same outcome.

### Prolonged Failures

If a printer keeps failing repeatedly (connection drops, I/O errors), the application escalates:

1. It keeps retrying automatically
2. After many consecutive failures, it gives up and marks the job as **Error**
3. Any unconfirmed codes are quarantined
4. You receive a red alert

To recover from an error job, cancel it and start a new one. The quarantined codes must be resolved in the Codes tab.

---

## Page 7: Frequently Asked Questions

### Getting Started

**Do I need to install .NET or any other software?**

No. Code Print Manager is a self-contained application. Everything it needs is included in the folder. Just copy and run.

**Can I run the application on multiple computers?**

The application is designed to run on one computer that is connected to all printers. Running it on multiple computers against the same printers is not supported and could cause conflicts.

**How do I test without a real printer?**

Start the application from the command line with `--mock`:
```
CodePrintManager.Desktop.exe --mock
```
This enables mock mode — you can add printers and run jobs without real hardware. The mock printers simulate printing with a configurable delay.

**What are the system requirements?**

Windows 10 or 11 (64-bit), 2 GB RAM, 500 MB free disk space, and a network connection to your printers.

---

### Printing

**How many codes can I print in one job?**

As many as you have available in the product's code pool. There is no upper limit imposed by the application.

**Can I print to two printers at the same time?**

Yes. Each printer operates independently. You can run a separate job on each printer simultaneously. However, each printer can only have one active job at a time, and each product can only have one active job at a time.

**What happens if I close the application while printing?**

The printer may continue printing for a short time after the application closes. When you restart the application, the recovery screen shows any interrupted jobs and lets you reconcile the state. See [Power Failure or Application Crash](#power-failure-or-application-crash).

**The Prepare step failed — what do I do?**

Read the error message — it tells you specifically what went wrong. Common causes:
- Printer is not idle (someone else is using it, or it was left in a printing state)
- Printer is offline or disconnected
- Not enough codes available in the product's pool
- Template file is missing from your computer

Fix the issue and click **Retry**.

**Can I change the quantity after preparing a job?**

No. To print a different quantity, cancel the current job and create a new one with the desired quantity.

---

### Codes

**What does "Quarantined" mean?**

The application is not sure if this code was physically printed on a product. It is frozen to prevent accidental reuse. You need to investigate and decide: was it printed (change to Burned) or not (change to Available)? See [Quarantined Codes](#quarantined-codes).

**Can I reuse a code that was printed?**

Technically yes — you can change a Printed code back to Available in the Codes tab. But the application warns you strongly because this is dangerous. If the code is already physically on a product, reusing it creates an illegal duplicate. Only do this if you are absolutely certain the original product was destroyed.

**I imported codes but the Available count didn't change — why?**

Check the import report. The most likely reason is that all codes were rejected as duplicates — they already exist in the system under another product or in the same product.

**How do I know which codes are assigned to which product?**

Go to **Products**, select a product, and click the **Codes** tab. You see all codes belonging to that product. To find a specific code, use the search box.

**What happens to codes when I delete a product?**

You are asked whether to keep the codes (they move to the unassigned pool) or delete them too (they are archived). Either way, no code is silently lost.

---

### Printers

**The printer shows "Offline" — what do I do?**

1. Check that the printer is powered on
2. Check the network cable / connection
3. Verify the IP address is correct (Printers > Configuration tab)
4. Try pinging the printer from Command Prompt: `ping 192.168.1.100`
5. Check your firewall settings

The application automatically retries the connection in the background. Once the printer is reachable, it reconnects automatically.

**Can I delete a printer that has job history?**

You can delete a printer only if it has no active jobs. Past job history is preserved even after the printer is deleted.

**What is the Quarantine Margin setting?**

When you cancel a job while the printer is mid-print, there may be a small number of codes at the boundary between "confirmed printed" and "not yet printed" whose status is uncertain. The quarantine margin controls how many of these boundary codes are marked as Quarantined rather than returned to Available. The default is 0 (no quarantine margin). Setting it to 1 is the safest option for high-value codes.

See [Configuration](#configuration) for how to change this setting.

---

### Problems

**The application shows a recovery screen on startup — what happened?**

The application detected jobs that were active when it last shut down unexpectedly (crash, power failure, force-close). The recovery screen lets you inspect each job, compare your records with the printer's actual counters, and decide whether to resume or cancel each one. See [Power Failure or Application Crash](#power-failure-or-application-crash).

**I see "Unexpected counter jump" — is this serious?**

It means the printer's counter advanced more than expected between two checks. This usually means someone printed using the printer directly (via its touchscreen or another application). Investigate to find out what happened. The application has already marked the affected codes conservatively.

**The printer disconnected and the job says "Disconnected" — what do I do?**

1. Fix the printer connection
2. Reconnect (Printers > Connect)
3. Go to Jobs and resume or cancel the disconnected job

The application does not auto-resume because the printer's state is unknown. See [Printer Disconnects During Printing](#printer-disconnects-during-printing).

**The application won't start**

- Make sure you are running Windows 10 or 11 (64-bit)
- Try running as Administrator (right-click > Run as administrator)
- Check the `logs\` folder in the [data folder](#backing-up-your-data) for error details

**"Database is locked" error**

- Make sure only one instance of the application is running
- Close the application completely
- Delete `codeprintmanager.db-shm` and `codeprintmanager.db-wal` from the [data folder](#backing-up-your-data) (the main `.db` file is safe to keep)
- Restart the application

**Where are the log files?**

In the `logs\` folder inside the [data folder](#backing-up-your-data) — `%LocalAppData%\CodePrintManagerData\logs` for installed builds. Log files are organized by date. Send the latest log file when reporting issues to support.
