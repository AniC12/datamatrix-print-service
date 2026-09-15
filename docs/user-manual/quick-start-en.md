# Code Print Manager — Quick Start Guide

This guide walks you through first-time setup: installing the application, adding a printer, creating a product, importing codes, and running your first print job.

For detailed information on all features, see the [Full Reference Manual](manual-en.md).

---

## 1. Install and Launch

1. Copy the **CodePrintManager** folder to your computer (e.g., `C:\CodePrintManager`)
2. Run **CodePrintManager.Desktop.exe**
3. Done — no installation or additional software required

**Requirements:** Windows 10 or 11 (64-bit), 2 GB RAM, 500 MB free disk space.

On first launch, the application creates its database and log files automatically. You will see the Dashboard screen with no data yet.

---

## 2. Add a Printer

Your Savema printer must be connected to the same network and powered on.

1. Click **Printers** in the left sidebar
2. Click **+ Add Printer**
3. Fill in:
   - **Name** — a friendly name (e.g., "Line 1")
   - **IP Address** — the printer's network address (e.g., 192.168.1.100)
   - **Port** — 9100 (the default for Savema printers)
4. Click **Save**

The application connects to the printer automatically. A green status indicator means the connection is successful.

```
┌─────────────────────────────────────────────────────────┐
│  PRINTERS                                   [+ New Job] │
├─────────────────────────────────────────────────────────┤
│                                                         │
│  [ Line 1 ▼ ]  192.168.1.100  ● IDLE                   │
│                                                         │
│  [Configuration]  [Storage]                             │
│                                                         │
└─────────────────────────────────────────────────────────┘
```

> **Tip:** To test without a real printer, start the application from the command line with:
> `CodePrintManager.Desktop.exe --mock`

---

## 3. Create a Product

Products represent what you are printing codes on (e.g., "Apple Juice 0.5L").

1. Click **Products** in the left sidebar
2. Click **+ Product** at the top of the tree
3. Enter the product name and click **Save**
4. In the **Settings** tab on the right:
   - Set the **Template File** — browse to your `.rox` template file
   - Set the **Printer CSV Name** — the filename the printer will use (e.g., `apple_05.csv`)
   - Click **Save**

```
┌─────────────────────────────────────────────────────────┐
│  PRODUCTS                                               │
├──────────────┬──────────────────────────────────────────┤
│ [+ Folder]   │  APPLE JUICE 0.5L                       │
│ [+ Product]  │                                          │
│              │  [Operations]  [Settings]  [Codes]      │
│  ● Apple     │                                          │
│    Juice     │  Template File: apple_05_53.rox          │
│    0.5L  ←   │  Printer CSV Name: apple_05.csv          │
│              │                                          │
└──────────────┴──────────────────────────────────────────┘
```

> **Tip:** Use **+ Folder** to organize products into groups (e.g., by brand or product line). Folders are for organization only — you print from products, not folders.

---

## 4. Import Codes

Codes come from CSV files downloaded from the government portal. Each code is used exactly once across your entire system.

1. Select your product in the tree
2. On the **Operations** tab, click **Import CSV...**
3. Browse to your CSV file and open it
4. The application validates the file and reports the result

After import, you will see the code count in the **Code Pool** section:

```
  Code Pool:
    Available:    10,000
    Printed:      0
    Total:        10,000
```

The application checks every code for duplicates. If any code already exists in the system (in any product), it is rejected and you are told which ones.

---

## 5. Print Your First Job

1. Click **+ New Job** (available on the Dashboard, Products, Printers, or Jobs page)
2. Select your **Product** from the dropdown
3. Select your **Printer** from the dropdown
4. Enter the **Quantity** (number of codes to print)
5. Click **Prepare**

```
┌─────────────────────────────────────────────────────────┐
│  NEW JOB                                      [← Back]  │
├─────────────────────────────────────────────────────────┤
│                                                         │
│  Product:   [ Apple Juice 0.5L     ▼ ]  (10,000 avail) │
│  Printer:   [ Line 1              ▼ ]  (● idle)        │
│  Quantity:  [ 500                    ]                  │
│                                                         │
│              [Prepare]                                  │
│                                                         │
│  ── Preparation Progress ────────────────────────────── │
│  ✓ Printer state verified (idle)                        │
│  ✓ 500 codes reserved from pool                         │
│  ✓ Data file uploaded to printer                        │
│  ✓ Template loaded successfully                         │
│                                                         │
│  ✅ Job #1 is ready to print.                           │
│                                                         │
│              [Start Print]  [Go to Job]                 │
│                                                         │
└─────────────────────────────────────────────────────────┘
```

6. Once preparation succeeds, click **Start Print**

The application takes you to the Dashboard where you can watch the progress in real time:

```
┌─────────────────────────────────────────────────────────┐
│  Line 1  192.168.1.100                      ● PRINTING  │
│  Job #1: Apple Juice 0.5L   247/500 (49%)               │
│  █████████████████░░░░░░░░░░░░░░░░   [Pause] [Cancel]  │
└─────────────────────────────────────────────────────────┘
```

When all codes are printed, the job completes automatically.

---

## What's Next

You are now up and running. Here are some things you may want to learn about:

- **Pausing and resuming jobs** — see [Printing](manual-en.md#page-3-printing)
- **Managing codes** (moving, archiving, handling quarantined codes) — see [Managing Codes](manual-en.md#page-4-managing-codes)
- **What to do when something goes wrong** (disconnects, power failures) — see [When Things Go Wrong](manual-en.md#page-6-when-things-go-wrong)
- **Frequently asked questions** — see [FAQ](manual-en.md#page-7-frequently-asked-questions)
