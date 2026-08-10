# Code Print Manager — Product Overview

## 1. What This Document Is

This document describes the **Code Print Manager** application — what it does, how it works from the operator's perspective, and what each screen in the application looks like. It is intended for stakeholders who need to understand the end product without technical implementation details.

---

## 2. The Problem

Your production lines print unique product codes (Data Matrix / QR) onto items using Savema thermal printers. These codes are government-issued — each one is purchased and must be used exactly once. Today, managing these codes involves manual coordination and risk:

- **Duplicate codes are illegal.** A code accidentally printed twice is a compliance violation.
- **Wasted codes cost money.** Codes lost due to miscounting, power failures, or operator error are gone forever.
- **No central tracking.** There is no single place to see which codes have been used, which are available, and what happened during printing.
- **Multiple printers, multiple products.** Coordinating code usage across several production lines and products is error-prone.

---

## 3. The Solution

Code Print Manager is a **Windows desktop application** installed on a single computer connected to your Savema printers over the local network. It serves as the central hub for all code printing activity.

### What it does

- **Imports codes** from CSV files (downloaded from the government portal)
- **Organizes products** in a flexible folder-like hierarchy you define
- **Manages multiple printers** simultaneously on the local network
- **Prints codes to products**, tracking exactly which code was printed on which item, on which printer, and when
- **Prevents duplicate usage** — the system makes it physically impossible to assign the same code twice
- **Recovers from failures** — power outages, network drops, and crashes are handled automatically with no data loss
- **Keeps a full audit trail** — every action is recorded for troubleshooting and accountability

### What it does NOT do (Phase 1)

- No barcode scanning or verification after printing
- No rejection/recycling of defective items
- No integration with the government E-Mark API
- No aggregation (packing or palletizing)
- No user login or role-based access
- No internet or cloud dependency — everything runs locally

These features are planned for future phases and the system is designed to accommodate them without needing to be rebuilt.

### Key design principles

- **Safety first** — if there is any ambiguity about whether a code was printed, the system marks it as used rather than risking a duplicate
- **Always available** — the application works fully offline with no internet dependency; all data is stored locally and survives restarts and crashes
- **Simple deployment** — installed once by the support team, no ongoing maintenance needed

---

## 4. Core Concepts

### Products

Products are organized in a **tree structure**, similar to folders on a computer. You define the hierarchy however you like — by brand, by category, by line, etc.

```
Root
├── Juice
│   ├── Apple 0.5L
│   ├── Apple 1.0L
│   └── Orange
│       ├── 0.33L
│       └── 1.0L
├── Water
│   ├── Still 0.5L
│   └── Sparkling 1.5L
└── Milk
    └── 1.0L
```

- **Folders** are for organization only
- **Products** (the items at the bottom of the tree) are what actually gets printed — each one has its own pool of codes and its own printer template

### Code Pools

Every product has its own pool of codes. When you import a CSV file, those codes go into that product's pool. Codes move through a simple lifecycle:

- **Available** — imported and ready to be used
- **Reserved** — selected for a print job, waiting to be printed
- **Printed** — confirmed as physically printed on a product
- **Returned** — was reserved but the job was cancelled; code goes back to the available pool
- **Burned** — the system is unsure if this code was printed (for example, during a power failure); it is marked as used to be safe

A code that has been printed can never be reused. This is the core safety guarantee.

### Printers

Each Savema printer on your network is registered in the system with a name and network address. The application connects to all printers automatically on startup and continuously monitors their status (idle, printing, offline, error).

### Print Jobs

A print job ties everything together: **which product**, **which printer**, **how many codes**. The operator creates a job, the system prepares the printer, starts printing, and monitors progress in real time until all codes are printed or the job is cancelled.

---

## 5. How Printing Works

The printing process follows a clear step-by-step flow:

### Step 1 — Select Product and Printer

The operator picks a product from the tree and an available printer from the list. The system shows how many codes are available for that product and whether the printer is idle.

### Step 2 — Enter Quantity

The operator enters how many codes to print. The system checks that enough codes are available in the pool.

### Step 3 — Prepare

The operator clicks **Prepare**. The system:

1. Checks that the printer is in the correct state (idle, no errors)
2. Reserves the requested number of codes from the product's pool
3. Sends the codes and the print template to the printer
4. Confirms everything is uploaded correctly

If anything goes wrong during preparation, the operator receives a clear message explaining the issue and what to do about it.

### Step 4 — Print

The operator clicks **Print**. The printer begins printing. The system monitors the printer's internal counter in real time and updates a progress bar on screen.

### Step 5 — Complete or Cancel

- **Complete** — all codes are confirmed printed, the job finishes automatically
- **Cancel** — the operator can stop the job at any time; codes already printed stay marked as printed, unprinted codes return to the available pool, and one extra code is marked as burned for safety

---

## 6. Failure Handling

### Printer disconnects

If a printer loses its network connection during a print job, that specific job pauses and the operator is alerted. Other printers and their jobs are completely unaffected. The system automatically attempts to reconnect. Once the connection is restored, the operator can resume or abort the job.

### Power failure or application crash

All progress is continuously saved to the local database. When the application restarts after an unexpected shutdown, it detects any interrupted jobs and presents a **recovery screen** showing:

- Which jobs were interrupted
- How many codes the application confirmed as printed
- How many codes the printer actually printed (determined from the printer's permanent counter)
- Any discrepancy between the two

The operator can then choose to **resume** each job (continuing from where it left off) or **abort** it (safely returning unused codes to the pool).

### External interference

If someone prints using the printer's own interface (outside this application), the system detects the unexpected counter increase, alerts the operator, and conservatively marks the affected codes as used.

### Multiple printers

Each printer operates independently. A problem with one printer does not affect any other printer or any other job. Each printer can run its own job simultaneously.

---

## 7. Application Screens

The application has a left-side navigation menu that is always visible. Clicking a menu item changes the main content area on the right. An alert bar at the bottom of the window shows real-time notifications regardless of which screen you are on.

```
┌─────────────────────────────────────────────────────────┐
│                    MAIN WINDOW                          │
├──────────┬──────────────────────────────────────────────┤
│          │                                              │
│   NAV    │            CONTENT AREA                      │
│          │                                              │
│ • Dash   │      (changes based on navigation)           │
│ • Prods  │                                              │
│ • Prntrs │                                              │
│ • Jobs   │                                              │
│          │                                              │
├──────────┴──────────────────────────────────────────────┤
│ ALERTS (always visible at bottom)                       │
└─────────────────────────────────────────────────────────┘
```

Navigation:
- **Dashboard** — active monitoring and intervention
- **Products** — product tree management and code import
- **Printers** — printer configuration and storage management
- **Jobs** — manage active print jobs and view job history

The **New Job** screen is not a navigation item — it is accessed via [+ New Job] buttons available on the Dashboard, Products, Printers, and Jobs pages.

### 7.1 Dashboard

The first screen you see when opening the application. It is the active monitoring and intervention hub — everything the operator needs to see at a glance.

```
┌─────────────────────────────────────────────────────────────────┐
│  DASHBOARD                                        [+ New Job]   │
├─────────────────────────────────────────────────────────────────┤
│                                                                 │
│  ┌─────────────────────────────────────────────────────────────┐│
│  │ Savema-Line1  192.168.1.10                     ● PRINTING   ││
│  │ Job #47: Apple 0.5L   342/500 (68%)                         ││
│  │ ████████████████████░░░░░░░░░           [Pause] [Cancel]    ││
│  └─────────────────────────────────────────────────────────────┘│
│  ┌─────────────────────────────────────────────────────────────┐│
│  │ Savema-Line2  192.168.1.11                     ● READY      ││
│  │ Job #49: Water Still 0.5L   0/1000                          ││
│  │ Prepared, waiting to start      [Start Print] [Cancel]      ││
│  └─────────────────────────────────────────────────────────────┘│
│  ┌─────────────────────────────────────────────────────────────┐│
│  │ Savema-Line3  192.168.1.12                     ● DONE       ││
│  │ Job #46: Orange 0.33L   2000/2000 (100%)                    ││
│  │ Completed Aug 7 14:25                                       ││
│  └─────────────────────────────────────────────────────────────┘│
│                                                                 │
│  ALERTS                                                         │
│  ┌─────────────────────────────────────────────────────────────┐│
│  │ 14:32  ⚠️  Line1: Unexpected counter jump (+7)        [×]   ││
│  └─────────────────────────────────────────────────────────────┘│
│                                                                 │
│  RECENT ACTIVITY                                                │
│  14:30  Job #47 started: Apple 0.5L → Line1                     │
│  14:25  Job #46 completed: Orange 0.33L → Line3                 │
│  14:20  Imported 10,000 codes for Apple 0.5L                    │
│                                                                 │
└─────────────────────────────────────────────────────────────────┘
```

- **One card per printer** — only printers that have had at least one job are shown. Each card displays the printer name, address, its last/current job, and status.
- **Sort order** — running, error, paused, and ready jobs appear first (newest status update at the top). Completed jobs always appear last.
- **Action buttons on cards** — contextual based on job state:
  - Job is printing → [Pause], [Cancel]
  - Job is ready → [Start Print], [Cancel]
  - Job is paused → [Resume], [Cancel]
  - Job is completed → no buttons
- **Clicking a card** navigates to the Jobs page with that job selected for full detail.
- **Alerts** — inline display of current errors and warnings.
- **Recent Activity** — chronological list of recent events across the system.
- **[+ New Job]** button (top-right) — opens the New Job screen with nothing preselected.

### 7.2 Products

A split-panel screen for managing your product catalog. The detail pane has two tabs: **Operations** (daily use) and **Settings** (configuration).

```
┌─────────────────────────────────────────────────────────────────┐
│  PRODUCTS                                                        │
├───────────────────┬─────────────────────────────────────────────┤
│  [+F] [+P]       │  APPLE 0.5L                                  │
│                   │                                              │
│  ▼ Juice          │  [Operations]  [Settings]                    │
│    ▼ Apple        │  ─────────────────────────────────────────── │
│      ● 0.5L  ←    │                                              │
│      ● 1.0L       │  Code Pool:                                  │
│    ▼ Orange       │    Available: 8,300                          │
│  ▼ Water          │    Printed:   1,700                          │
│    ● Still 0.5L   │    Burned:    3                              │
│  ▼ Milk           │    Total:     10,003                         │
│    ● 1.0L         │                                              │
│                   │  [Import CSV...]  [+ New Job]                │
│                   │                                              │
│                   │  History:                                    │
│                   │    Aug 10  Job #52 completed — 500/500       │
│                   │    Aug 09  Imported 10,000 — gold_0.5.csv    │
│                   │    Aug 08  Job #48 cancelled — 200/500       │
│                   │    Aug 06  Imported 5,000 — batch_aug6.csv   │
│                   │                                              │
└───────────────────┴─────────────────────────────────────────────┘
```

- **Left side** — the product tree (always expanded by default). Icon toolbar at top: [+F] adds a folder, [+P] adds a product. Adding is relative to the selected node; click empty space to deselect and add at root.
- **Right side — Operations tab** (default): code pool statistics, [Import CSV...], [+ New Job], and a merged activity history showing both imports and print jobs chronologically.
- **Right side — Settings tab**: template file + [Change], printer CSV name + [Save], and [Delete Product] as a danger-zone action at the bottom.
- **[+ New Job]** button — opens the New Job screen with this product preselected
- **History** — unified timeline of imports (blue) and job outcomes: completed (green), cancelled (orange), error (red)

When importing codes, the system validates the file and checks for duplicates across all products. Duplicate codes are rejected with a clear message.

### 7.3 Printers

A screen for managing printer configurations, with two tabs:

```
┌─────────────────────────────────────────────────────────────────┐
│  PRINTERS                                         [+ New Job]   │
├─────────────────────────────────────────────────────────────────┤
│                                                                 │
│  [ Savema-Line1 ▼ ]  192.168.1.10  ● IDLE                       │
│                                                                 │
│  [Configuration]  [Storage]                                     │
│  ───────────────────────────────────────────────────────────    │
│                                                                 │
│  TEMPLATES ON PRINTER                             [Refresh]     │
│  ┌─────────────────────────────────────────────────────────────┐│
│  │ ☐  Name                   Status                           ││
│  │ ☐  apple_05_53.rox        ✅ Used (Apple 0.5L)             ││
│  │ ☐  orange_033_53.rox      ✅ Used (Orange 0.33L)           ││
│  │ ☑  old_test_53.rox        ⚠️ Not mapped to any product     ││
│  │ ☑  demo_53.rox            ⚠️ Not mapped to any product     ││
│  └─────────────────────────────────────────────────────────────┘│
│                                                                 │
│  CSV FILES ON PRINTER                             [Refresh]     │
│  ┌─────────────────────────────────────────────────────────────┐│
│  │ ☐  Name                   Status                           ││
│  │ ☐  apple_05.csv           ✅ Used (Apple 0.5L)             ││
│  │ ☑  old_data.csv           ⚠️ Not mapped to any product     ││
│  │ ☑  test123.csv            ⚠️ Not mapped to any product     ││
│  └─────────────────────────────────────────────────────────────┘│
│                                                                 │
│  [Delete Selected (4)]                                          │
│                                                                 │
└─────────────────────────────────────────────────────────────────┘
```

- **[+ New Job]** button (top-right) — opens the New Job screen with this printer preselected. Disabled if the printer is busy or offline.
- **Configuration tab** — set up printer names, network addresses, and connection settings
- **Storage tab** — view and manage files stored on the printer; orphaned files (not linked to any product) are pre-selected for cleanup; files actively used by a product are protected and cannot be deleted

### 7.4 Jobs

The central place to manage active print jobs and review past ones. Two tabs: **Active Jobs** and **Job History**.

#### Active Jobs tab

```
┌─────────────────────────────────────────────────────────────────┐
│  JOBS                                             [+ New Job]   │
├─────────────────────────────────────────────────────────────────┤
│  [Active Jobs]  [Job History]                                   │
│  ───────────────────────────────────────────────────────────    │
│                                                                 │
│  Select Job:                                                    │
│  ┌─────────────────────────────────────────────────────────────┐│
│  │ ● #47  Apple 0.5L → Line1       342/500   ● printing        ││
│  │   #48  Orange 0.33L → Line2     1205/2000 ● printing        ││
│  └─────────────────────────────────────────────────────────────┘│
│                                                                 │
│  ─── Job #47 ────────────────────────────────────────────────   │
│  Product:  Apple 0.5L                                           │
│  Printer:  Savema-Line1 (192.168.1.10)  ● PRINTING              │
│  Quantity: 500 codes                                            │
│                                                                 │
│  Preparation:                                                   │
│  ✓ Template present on printer                                  │
│  ✓ 500 codes reserved from pool                                 │
│  ✓ Data file uploaded and confirmed                             │
│  ✓ Template loaded (counter reset to 0)                         │
│                                                                 │
│  Print Progress:                                                │
│  Progress: 342 / 500  (68%)                                     │
│  ████████████████████████████░░░░░░░░░░░░░                      │
│                                                                 │
│  [Pause]  [Cancel]                                              │
│                                                                 │
└─────────────────────────────────────────────────────────────────┘
```

- **Job selector** at the top — lists all active jobs (preparing, ready, printing, or paused). Select one to view its full detail below.
- **Job detail area** — shows product, printer with its **live status**, quantity, preparation checklist, progress bar, and action buttons.
- **Action buttons** appear based on job state:
  - Job is ready → [Start Print], [Cancel]
  - Job is printing → [Pause], [Cancel]
  - Job is paused → [Resume], [Cancel]
  - Job completed or cancelled → no action buttons, summary displayed
- When a job completes or is cancelled, it stays displayed with its final summary until the operator selects another job or navigates away.
- If no active jobs exist, shows an empty state with a [+ New Job] button.

#### Job History tab

```
┌─────────────────────────────────────────────────────────────────┐
│  JOBS                                             [+ New Job]   │
├─────────────────────────────────────────────────────────────────┤
│  [Active Jobs]  [Job History]                                   │
│  ───────────────────────────────────────────────────────────    │
│                                                                 │
│  Filters:  [All Printers ▼]  [All Products ▼]                   │
│                                                                 │
│  ┌─────────────────────────────────────────────────────────────┐│
│  │ #   Product          Printer   Qty     Status     Date      ││
│  │ 48  Orange 0.33L     Line2     2000    ✅ done    Aug 7    ││
│  │ 47  Apple 0.5L       Line1     500     ✅ done    Aug 7    ││
│  │ 46  Apple 0.5L       Line1     500     ⛔ cancel  Aug 6    ││
│  │ 45  Water Still 0.5L Line3     1000    ✅ done    Aug 6    ││
│  │ 44  Orange 0.33L     Line2     2000    ✅ done    Aug 5    ││
│  └─────────────────────────────────────────────────────────────┘│
│                                                                 │
│  ─── Job #48 (expanded) ─────────────────────────────────────   │
│  Product:  Orange 0.33L                                         │
│  Printer:  Savema-Line2                                         │
│  Quantity: 2000 / 2000 printed                                  │
│  Duration: 14:30 – 15:12 (42 min)                               │
│  Result:   Completed successfully                               │
│                                                                 │
└─────────────────────────────────────────────────────────────────┘
```

- Lists all past jobs (completed, cancelled, error), ordered newest first
- **Filters** — by printer, by product. Date range and pagination will be added in Phase 2.
- **View-only** — no action buttons, for review purposes only
- Clicking a row expands it to show summary details (codes printed, duration, outcome)

### 7.5 New Job

A dedicated screen for creating and preparing a new print job. It is not accessible from the navigation menu — it opens via [+ New Job] buttons found on other pages.

```
┌─────────────────────────────────────────────────────────────────┐
│  NEW JOB                                            [← Back]    │
├─────────────────────────────────────────────────────────────────┤
│                                                                 │
│  Product:   [ Apple 0.5L            ▼ ]   (8,300 available)     │
│  Printer:   [ Savema-Line1          ▼ ]   (● idle)              │
│  Quantity:  [ 500                     ]                         │
│                                                                 │
│              [Prepare]                                          │
│                                                                 │
│  ─── Preparation Progress ───────────────────────────────────   │
│  ✓ Printer state verified (idle)                                │
│  ✓ 500 codes reserved from pool                                 │
│  ✓ Data file uploaded to printer                                │
│  ✓ Template loaded successfully                                 │
│                                                                 │
│  ✅ Job #49 is ready to print.                                  │
│                                                                 │
│              [Start Print]  [Go to Job]                         │
│                                                                 │
└─────────────────────────────────────────────────────────────────┘
```

- **Context-aware** — when opened from the Products page, the product is preselected. When opened from the Printers page, the printer is preselected. When opened from Dashboard or Jobs, nothing is preselected.
- **Product selector** — dropdown showing all products with their available code count
- **Printer selector** — dropdown showing all printers with their status. Printers that are busy or offline are greyed out.
- **[Prepare]** — starts the preparation process. Navigation is blocked while preparation is in progress.
- **Preparation progress** — shown inline as each step completes
- **On success** — displays a confirmation with two options:
  - **[Start Print]** — starts printing immediately and navigates to the Jobs page (Active tab) with this job selected
  - **[Go to Job]** — navigates to the Jobs page without starting (operator can review before printing)
- **On failure** — displays a clear error message with a [Retry] button

### 7.6 Alerts

The alert bar sits at the bottom of the main window and is always visible, regardless of which screen you are on:

```
┌─────────────────────────────────────────────────────────────────┐
│ ALERTS (always visible, scrollable, max 3 rows then scroll)     │
│ 14:35  🔴  Line1: Connection lost. Job #47 paused.        [×]  │
│ 14:33  ⚠️  Line2: Unexpected counter jump (+6).           [×]  │
│ 14:30  ✅  Line3: Job #49 completed (1000/1000)           [×]  │
└─────────────────────────────────────────────────────────────────┘
```

- **Errors** (red) — connection lost, upload failed, etc. Stay visible until manually dismissed.
- **Warnings** (yellow) — unexpected counter jumps, low code stock, printer blocked. Stay visible until manually dismissed.
- **Informational** (green) — job completed, printer connected. Auto-dismiss after 30 seconds.

When there are no alerts, the bar collapses and takes up no space.

---

## 8. Safety Guarantees

| Risk | How the system handles it |
|------|---------------------------|
| **Duplicate code printed** | Global uniqueness enforced — it is impossible to assign the same code to two jobs. A printed code can never return to the available pool. |
| **Power failure mid-print** | All progress is saved continuously. On restart, the system reads the printer's permanent counter to determine exactly what was printed, and presents a recovery screen. |
| **External printing on the printer** | Counter jumps are detected and flagged. Affected codes are conservatively marked as used. |
| **Application crash** | Same as power failure — all state is in the local database and is recoverable. |
| **Network disconnect** | The affected job pauses, other jobs continue. Automatic reconnection with retry. Operator is alerted. |
| **Ambiguous codes** | If the system cannot be certain whether a code was printed, it marks it as "burned" (used) rather than risking a duplicate. |
| **Two jobs targeting the same printer** | Prevented by design — only one job can be active per printer at any time. The system enforces this at every level. |

---

## 9. What Comes Next (Future Phases)

The system is architected to grow. Future phases may include:

- **Barcode scanning and verification** — camera-based scanning after printing to verify each code was printed correctly
- **Rejection and recycling** — automatic handling of defective prints
- **Government API integration** — direct communication with the E-Mark system
- **Aggregation** — grouping codes for packs, cases, and pallets
- **Production pipelines** — linking printers, scanners, and recyclers into automated workflows

None of these additions will require rebuilding the existing system. The current design is specifically structured to accommodate them as extensions.

---

## 10. Deployment

- **Platform**: Windows desktop application
- **Installation**: Single-folder install, performed once by the support team. No internet required.
- **Data storage**: Everything stored locally on the computer. No cloud, no external servers.
- **Printers**: Connected over the local network (standard TCP/IP). No special hardware or drivers needed beyond the Savema printers themselves.
