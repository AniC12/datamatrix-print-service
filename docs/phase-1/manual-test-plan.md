# Code Print Manager - Phase 1 Manual Test Plan

> **Version:** 1.0  
> **Scope:** Systematic testing of all Phase 1 features against the Savema simulator first, then against real hardware.

---

## Table of Contents

1. [Test Environment Setup](#1-test-environment-setup)
2. [Test Data](#2-test-data)
3. [Cleanup Procedure](#3-cleanup-procedure)
4. [Test Sections](#4-test-sections)
   - [A. Application Startup & Database](#a-application-startup--database)
   - [B. Printer Management](#b-printer-management)
   - [C. Product & Folder Management](#c-product--folder-management)
   - [D. CSV Import & Code Pool](#d-csv-import--code-pool)
   - [E. Printer Storage (Templates & CSV)](#e-printer-storage-templates--csv)
   - [F. Normal Print Lifecycle](#f-normal-print-lifecycle)
   - [G. Job Cancellation](#g-job-cancellation)
   - [H. Job Pause & Resume](#h-job-pause--resume)
   - [I. Concurrency Guards](#i-concurrency-guards)
   - [J. Connection Loss & Reconnect](#j-connection-loss--reconnect)
   - [K. Startup Recovery](#k-startup-recovery)
   - [L. Anomaly Detection & Quarantine](#l-anomaly-detection--quarantine)
   - [M. External Print Detection (ReadyWatcher)](#m-external-print-detection-readywatcher)
   - [N. Codes Tab Administration](#n-codes-tab-administration)
   - [O. Printer Verify Tab](#o-printer-verify-tab)
   - [P. Dashboard](#p-dashboard)
   - [Q. Logging & Audit Trail](#q-logging--audit-trail)
   - [R. Localization & UI](#r-localization--ui)
   - [S. Persistence & Restart](#s-persistence--restart)
5. [Real Printer Adaptation Notes](#5-real-printer-adaptation-notes)

---

## 1. Test Environment Setup

### Simulator Setup

```bash
# Terminal 1 - Start the simulator (default: WAITING status, template gs1label_32.rox preloaded)
cd demo/
python savema_simulator.py --port 9100
```

### Application Setup (Savema mode)

```bash
# Terminal 2 - Run the app without --mock flag to use the real Savema adapter
cd application/
dotnet run --project src/Hosts/CodePrintManager.Desktop
```

Alternatively, set `"UseMockPrinter": false` in `application/src/Hosts/CodePrintManager.Desktop/appsettings.json`.

### Verify Connection

- In the app, go to **Printers** tab
- Add a printer: Name = `Sim1`, IP = `127.0.0.1`, Port = `9100`
- Confirm status shows **Idle** (green)
- In the simulator terminal, confirm `Client connected` log appears

### Simulator CLI Modes (use as needed)

| Mode | Command |
|------|---------|
| Normal (WAITING) | `python savema_simulator.py --port 9100` |
| ERROR state | `python savema_simulator.py --port 9100 --status error` |
| INIT state | `python savema_simulator.py --port 9100 --status init` |
| BLOCKED state | `python savema_simulator.py --port 9100 --blocked` |
| Custom templates | `python savema_simulator.py --port 9100 --templates test32_32.rox apple_05_53.rox` |
| No templates | `python savema_simulator.py --port 9100 --templates` |

---

## 2. Test Data

### CSV Files (in `demo/`)

| File | Codes | Notes |
|------|-------|-------|
| `test_5_codes.csv` | 5 | Quick smoke tests |
| `test_50_codes.csv` | 50 | Medium tests with progress observation |
| `test_gs_no_header.csv` | 3 | GS1 codes, no header row |
| `test_gs_with_header.csv` | 3+header | GS1 codes, has "QR" header row |
| `test_no_gs_no_header.csv` | 3 | Plain codes, no header |
| `test_no_gs_with_header.csv` | 3+header | Plain codes, has header |
| `sample_data.csv` | 3+header | GS1 codes with "code" header |
| `gold_0.5_10000 (1).csv` | 10,000 | Large import stress test |

### Template Files (in `demo/`)

| File | Notes |
|------|-------|
| `test32_32.rox` | Full Savema template with GS1-Datamatrix barcode, references `QR` column |
| `apple_05_53.rox` | Dummy/minimal template for mock testing |

### Test Data to Create (before testing)

Create a CSV file `demo/test_duplicates.csv` with intentional duplicates:
```
010485000607001121260820ABC01\x1D93XYZW01
010485000607001121260820ABC02\x1D93XYZW02
010485000607001121260820ABC01\x1D93XYZW01
DUPLICATE_LINE_FOR_TESTING
DUPLICATE_LINE_FOR_TESTING
```

---

## 3. Cleanup Procedure

Run between test sections or when contaminated state is suspected.

### Quick Reset (between individual tests)

1. Cancel all active jobs in the Jobs tab
2. In Codes tab, filter by **Reserved** -- if any remain, cancel the owning job
3. Verify no active jobs exist

### Full Reset (between test sections)

1. Close the application
2. Delete the database: remove `codeprintmanager.db` from the output directory (`application/src/Hosts/CodePrintManager.Desktop/bin/Debug/net8.0-windows/`)
3. Restart the simulator: `Ctrl+C` then relaunch `python savema_simulator.py --port 9100`
4. Restart the application
5. Re-add the simulator printer (`Sim1`, `127.0.0.1:9100`)

### Verify Clean State

- Dashboard: no printer cards or jobs
- Jobs tab: Active list empty
- Products tab: no products (or only the ones you create for the current test)
- Printers tab: only `Sim1` with Idle status
- Simulator terminal: counters at 0, no data files

---

## 4. Test Sections

---

### A. Application Startup & Database

#### A-01: Fresh Start (No Database)

| Field | Value |
|-------|-------|
| **Objective** | Verify application creates database and starts cleanly on first launch |
| **Preconditions** | No `codeprintmanager.db` exists in output directory |
| **Steps** | 1. Delete `codeprintmanager.db` if it exists<br>2. Launch the application<br>3. Observe the main window |
| **Expected** | - Application starts without errors<br>- `codeprintmanager.db` is created<br>- `logs/` directory is created with a log file<br>- Dashboard shows empty state<br>- All tabs are accessible<br>- No recovery dialog appears |
| **Safety invariant** | Database initialization with WAL mode |
| **Pass/Fail** | |

#### A-02: Restart with Existing Database

| Field | Value |
|-------|-------|
| **Objective** | Verify data persists across restarts when no stale jobs exist |
| **Preconditions** | Application has been used (has printers, products in DB), no active jobs |
| **Steps** | 1. Note current printers and products<br>2. Close the application<br>3. Restart the application |
| **Expected** | - All printers and products still present<br>- Printer auto-connect fires for configured printers<br>- No recovery dialog (no stale jobs)<br>- Log file shows startup banner with version, mode, paths |
| **Pass/Fail** | |

#### A-03: Log File Verification

| Field | Value |
|-------|-------|
| **Objective** | Verify Serilog logging works correctly |
| **Preconditions** | Application running |
| **Steps** | 1. Perform some actions (add printer, import codes)<br>2. Open the log file from `logs/` directory<br>3. Check log content |
| **Expected** | - Log file has daily rolling name pattern `app-YYYYMMDD.log`<br>- Entries include timestamp, level, thread, source context<br>- Startup banner with version, runtime, OS, mode (MOCK/REAL) |
| **Pass/Fail** | |

---

### B. Printer Management

#### B-01: Add Printer

| Field | Value |
|-------|-------|
| **Objective** | Add a new printer pointing to the simulator |
| **Preconditions** | Simulator running on port 9100, application open |
| **Steps** | 1. Go to Printers tab<br>2. Click "Add Printer"<br>3. Enter: Name=`Sim1`, IP=`127.0.0.1`, Port=`9100`<br>4. Save |
| **Expected** | - Printer appears in list<br>- Auto-connect fires, status shows Idle (green indicator)<br>- Simulator logs `Client connected`<br>- Serial number is read and stored (SPGGSN)<br>- Audit log entry: printer created |
| **Pass/Fail** | |

#### B-02: Add Second Printer (Different Port)

| Field | Value |
|-------|-------|
| **Objective** | Add a second printer to test multi-printer support |
| **Simulator** | Start a second simulator: `python savema_simulator.py --port 9101` |
| **Steps** | 1. Add printer: Name=`Sim2`, IP=`127.0.0.1`, Port=`9101`<br>2. Verify both printers show Idle |
| **Expected** | - Both printers listed, both Idle<br>- Each has its own serial number stored |
| **Pass/Fail** | |

#### B-03: Connect/Disconnect Printer

| Field | Value |
|-------|-------|
| **Objective** | Manually disconnect and reconnect a printer |
| **Preconditions** | `Sim1` connected and Idle, no active jobs |
| **Steps** | 1. Select `Sim1`<br>2. Click Disconnect<br>3. Verify status changes to Offline<br>4. Click Connect<br>5. Verify status returns to Idle |
| **Expected** | - Disconnect: status becomes Offline, simulator logs disconnect<br>- Connect: status returns to Idle, simulator logs new connection<br>- Audit trail records both events |
| **Pass/Fail** | |

#### B-04: Disconnect Printer with Active Job

| Field | Value |
|-------|-------|
| **Objective** | Verify warning dialog appears when disconnecting a printer with active jobs |
| **Preconditions** | `Sim1` has a Ready or Printing job |
| **Steps** | 1. Create and prepare a job on `Sim1` (leave it in Ready state)<br>2. Try to disconnect `Sim1` |
| **Expected** | - Warning dialog: "Printer has active print jobs. Disconnecting will pause them."<br>- User can Cancel to keep connection<br>- User can Continue to disconnect (job should pause) |
| **Pass/Fail** | |

#### B-05: Delete Printer

| Field | Value |
|-------|-------|
| **Objective** | Delete a printer with no active jobs |
| **Preconditions** | `Sim2` connected, no active jobs on it |
| **Steps** | 1. Select `Sim2`<br>2. Click Delete<br>3. Confirm deletion |
| **Expected** | - Confirmation dialog appears<br>- Printer removed from list<br>- Adapter disconnected<br>- Audit log entry |
| **Pass/Fail** | |

#### B-06: Delete Printer with Active Jobs (Blocked)

| Field | Value |
|-------|-------|
| **Objective** | Verify deletion is blocked when printer has active jobs |
| **Preconditions** | `Sim1` has a Ready or Printing job |
| **Steps** | 1. Try to delete `Sim1` |
| **Expected** | - Error message: "Printer has active jobs"<br>- Printer NOT deleted |
| **Pass/Fail** | |

#### B-07: Printer Connection Failure (Unreachable)

| Field | Value |
|-------|-------|
| **Objective** | Add a printer that can't be reached |
| **Steps** | 1. Add printer: Name=`Fake`, IP=`127.0.0.1`, Port=`9999` (nothing listening)<br>2. Observe behavior |
| **Expected** | - Printer appears with Offline status<br>- Reconnect loop starts automatically (check logs for exponential backoff)<br>- Application remains responsive |
| **Pass/Fail** | |

#### B-08: Auto-Reconnect After Simulator Restart

| Field | Value |
|-------|-------|
| **Objective** | Verify auto-reconnect when simulator comes back |
| **Preconditions** | `Sim1` connected and Idle |
| **Steps** | 1. Stop the simulator (Ctrl+C)<br>2. Observe printer status changes to Offline<br>3. Wait 5-10 seconds<br>4. Restart the simulator on the same port<br>5. Observe status |
| **Expected** | - Status changes to Offline when simulator stops<br>- Reconnect loop starts (check logs for attempt count and delay escalation)<br>- Status returns to Idle when simulator restarts<br>- Serial number re-verified on reconnect |
| **Pass/Fail** | |

---

### C. Product & Folder Management

#### C-01: Create Folder

| Field | Value |
|-------|-------|
| **Objective** | Create a folder in the product tree |
| **Steps** | 1. Go to Products tab<br>2. Click "Add Folder"<br>3. Enter name: `Pharma` |
| **Expected** | - Folder appears in tree with folder icon<br>- Folder is NOT a leaf (cannot have codes/template) |
| **Pass/Fail** | |

#### C-02: Create Product (Leaf Node)

| Field | Value |
|-------|-------|
| **Objective** | Create a printable product under a folder |
| **Preconditions** | `Pharma` folder exists |
| **Steps** | 1. Select `Pharma` folder<br>2. Click "Add Product"<br>3. Enter name: `Aspirin 500mg` |
| **Expected** | - Product appears under `Pharma` as a leaf node<br>- Product has template and CSV name fields (empty initially)<br>- Code count shows 0 |
| **Pass/Fail** | |

#### C-03: Create Nested Folders

| Field | Value |
|-------|-------|
| **Objective** | Verify unlimited-depth folder nesting |
| **Steps** | 1. Select `Pharma`<br>2. Add subfolder `Antibiotics`<br>3. Select `Antibiotics`<br>4. Add product `Amoxicillin 250mg` |
| **Expected** | - Tree shows: Pharma > Antibiotics > Amoxicillin 250mg<br>- Amoxicillin is a leaf, Antibiotics and Pharma are folders |
| **Pass/Fail** | |

#### C-04: Rename Product/Folder

| Field | Value |
|-------|-------|
| **Objective** | Rename a product and a folder |
| **Steps** | 1. Right-click `Aspirin 500mg` > Rename<br>2. Change to `Aspirin 500mg Tablets`<br>3. Right-click `Pharma` > Rename<br>4. Change to `Pharmaceuticals` |
| **Expected** | - Both names updated in the tree<br>- Rename dialog with current name pre-filled |
| **Pass/Fail** | |

#### C-05: Assign Template to Product

| Field | Value |
|-------|-------|
| **Objective** | Assign a .rox template file to a product |
| **Preconditions** | Product `Aspirin 500mg` exists |
| **Steps** | 1. Select `Aspirin 500mg`<br>2. Click "Change Template"<br>3. Browse to `demo/test32_32.rox`<br>4. Select the file |
| **Expected** | - Template field shows the file path<br>- Template file path stored in database |
| **Pass/Fail** | |

#### C-06: Set Printer CSV Filename

| Field | Value |
|-------|-------|
| **Objective** | Configure the CSV filename the printer expects |
| **Preconditions** | Product `Aspirin 500mg` exists |
| **Steps** | 1. Select `Aspirin 500mg`<br>2. Set Printer CSV Name to `gold_0.5_10000 (1).csv` |
| **Expected** | - CSV name stored<br>- This name will be used when uploading codes to the printer |
| **Pass/Fail** | |

#### C-07: Delete Empty Product

| Field | Value |
|-------|-------|
| **Objective** | Delete a product with no codes |
| **Steps** | 1. Create a temporary product `Temp Product`<br>2. Delete it |
| **Expected** | - Confirmation dialog<br>- Product removed from tree |
| **Pass/Fail** | |

#### C-08: Delete Product with Codes

| Field | Value |
|-------|-------|
| **Objective** | Delete a product that has codes imported |
| **Preconditions** | Product has codes imported (no active jobs) |
| **Steps** | 1. Select product with codes<br>2. Click Delete |
| **Expected** | - Special dialog: "This product has N codes"<br>- Three options: Yes (keep codes in Unassigned), No (archive codes), Cancel<br>- Try each option on different products |
| **Safety invariant** | Codes are never silently destroyed |
| **Pass/Fail** | |

#### C-09: Delete Product with Active Jobs (Blocked)

| Field | Value |
|-------|-------|
| **Objective** | Verify deletion is blocked when product has active/reserved codes |
| **Preconditions** | Product has a Ready or Printing job with reserved codes |
| **Steps** | 1. Try to delete the product |
| **Expected** | - Error: "Cannot delete product with active jobs or reserved codes"<br>- Product NOT deleted |
| **Safety invariant** | Reserved codes are protected |
| **Pass/Fail** | |

#### C-10: Delete Non-Empty Folder (Blocked)

| Field | Value |
|-------|-------|
| **Objective** | Verify folders with children can't be deleted |
| **Preconditions** | Folder has child products or subfolders |
| **Steps** | 1. Try to delete the folder |
| **Expected** | - Error: "Cannot delete folder because it contains items"<br>- Folder NOT deleted |
| **Pass/Fail** | |

---

### D. CSV Import & Code Pool

#### D-01: Import Small CSV (No Header)

| Field | Value |
|-------|-------|
| **Objective** | Import a CSV file without a header row |
| **Preconditions** | Product `Aspirin 500mg` selected |
| **Steps** | 1. Click "Import Codes"<br>2. Select `demo/test_5_codes.csv`<br>3. Observe result |
| **Expected** | - Status: "Imported 5 codes"<br>- Code count on product updated to 5<br>- All codes have status Available<br>- Audit log entry for import |
| **Pass/Fail** | |

#### D-02: Import CSV with Header Row

| Field | Value |
|-------|-------|
| **Objective** | Import a CSV that has a "QR" header row |
| **Preconditions** | Different product selected (to avoid duplicate conflicts) |
| **Steps** | 1. Create product `Test Product B`<br>2. Import `demo/test_gs_with_header.csv` |
| **Expected** | - Observe whether header row "QR" is imported as a code or skipped<br>- Note: Current implementation imports all non-empty lines as codes; the header "QR" will be imported as a code value. This is expected behavior -- the app treats all lines as codes. |
| **Pass/Fail** | |

#### D-03: Import Large CSV (10,000 codes)

| Field | Value |
|-------|-------|
| **Objective** | Verify large import completes without freezing the UI |
| **Preconditions** | Product with no codes |
| **Steps** | 1. Import `demo/gold_0.5_10000 (1).csv`<br>2. Observe UI responsiveness during import<br>3. Verify final count |
| **Expected** | - Import completes (may take a few seconds)<br>- UI remains responsive during import<br>- Status shows "Imported N codes" (some may be duplicates if quoted variants differ)<br>- Code count updated correctly |
| **Pass/Fail** | |

#### D-04: Import with Duplicates (Same Product)

| Field | Value |
|-------|-------|
| **Objective** | Verify duplicate codes within the same product are skipped |
| **Preconditions** | Product already has codes from D-01 |
| **Steps** | 1. Import `demo/test_5_codes.csv` again into the same product |
| **Expected** | - Status: "Imported 0 codes, 5 duplicates skipped"<br>- No duplicate codes created<br>- Code count unchanged |
| **Safety invariant** | Global uniqueness enforced |
| **Pass/Fail** | |

#### D-05: Import with Cross-Product Duplicates

| Field | Value |
|-------|-------|
| **Objective** | Verify duplicate codes across products are rejected |
| **Preconditions** | Product A has `test_5_codes.csv` imported |
| **Steps** | 1. Create Product B<br>2. Import `test_5_codes.csv` into Product B |
| **Expected** | - All 5 codes skipped as duplicates<br>- Status: "Imported 0 codes, 5 duplicates skipped"<br>- Duplicate detection is GLOBAL, not per-product |
| **Safety invariant** | Global duplicate prevention |
| **Pass/Fail** | |

#### D-06: Import with SPPL-Forbidden Characters

| Field | Value |
|-------|-------|
| **Objective** | Verify codes containing `^`, `~`, `~gt~`, `~sc~` are rejected |
| **Steps** | 1. Create a temporary CSV with codes containing these characters:<br>`TEST^CODE001`<br>`TEST~CODE002`<br>`TEST~gt~CODE003`<br>`VALID_CODE_004`<br>2. Import into a product |
| **Expected** | - 3 codes rejected with validation errors<br>- 1 valid code imported<br>- Error details shown with row numbers<br>- Import status shows warnings |
| **Safety invariant** | SPPL protocol safety |
| **Pass/Fail** | |

#### D-07: Import FIFO Order Verification

| Field | Value |
|-------|-------|
| **Objective** | Verify codes maintain import order (FIFO) for reservation |
| **Preconditions** | Empty product |
| **Steps** | 1. Import `test_5_codes.csv`<br>2. Go to Codes tab, filter by this product<br>3. Verify ordering |
| **Expected** | - Codes displayed in same order as the CSV file<br>- ImportOrder field increments sequentially<br>- When reserved for printing, first-imported codes are taken first |
| **Pass/Fail** | |

---

### E. Printer Storage (Templates & CSV)

#### E-01: View Printer Storage

| Field | Value |
|-------|-------|
| **Objective** | List templates and CSV files on the simulated printer |
| **Preconditions** | `Sim1` connected (simulator started with default `--templates gs1label_32.rox`) |
| **Steps** | 1. Go to Printers tab<br>2. Select `Sim1`<br>3. Click "Refresh Storage" (or navigate to Storage tab) |
| **Expected** | - Templates list shows `gs1label_32.rox`<br>- CSV files list is empty initially<br>- Simulator logs `SPLGST` and `SPLGSD` commands |
| **Pass/Fail** | |

#### E-02: Upload Template via Storage Tab

| Field | Value |
|-------|-------|
| **Objective** | Manually upload a .rox template to the printer |
| **Steps** | 1. Click "Upload Template"<br>2. Select `demo/test32_32.rox`<br>3. Refresh storage |
| **Expected** | - Template `test32_32.rox` appears in the list<br>- Simulator logs `SPLRTF` command with base64 data<br>- Simulator template list now includes `test32_32.rox` |
| **Pass/Fail** | |

#### E-03: Upload CSV via Storage Tab

| Field | Value |
|-------|-------|
| **Objective** | Manually upload a CSV file to the printer |
| **Steps** | 1. Click "Upload CSV"<br>2. Select a CSV file<br>3. Refresh storage |
| **Expected** | - CSV file appears in printer storage list<br>- Simulator logs `SPLCDF` command |
| **Pass/Fail** | |

#### E-04: Delete Files from Printer Storage

| Field | Value |
|-------|-------|
| **Objective** | Delete templates and CSV files from printer |
| **Preconditions** | Printer has templates/CSVs not mapped to any product or active template |
| **Steps** | 1. Select a file in the storage list<br>2. Click Delete<br>3. Confirm |
| **Expected** | - Confirmation dialog appears<br>- File removed from list<br>- Simulator logs `SPLDTF` or `SPLDDF` |
| **Pass/Fail** | |

#### E-05: Delete Protected File (Mapped to Product)

| Field | Value |
|-------|-------|
| **Objective** | Verify files mapped to products can't be deleted from storage |
| **Preconditions** | A product has `PrinterCsvName` set to a file that exists on the printer |
| **Steps** | 1. Try to delete that CSV from printer storage |
| **Expected** | - File shows a "mapped" indicator<br>- Deletion is prevented or a warning is shown |
| **Safety invariant** | Mapped files protected from accidental deletion |
| **Pass/Fail** | |

#### E-06: Delete Active Template (Protected)

| Field | Value |
|-------|-------|
| **Objective** | Verify the active template can't be deleted |
| **Steps** | 1. Verify which template is active on the printer<br>2. Try to delete it from storage |
| **Expected** | - Active template has a visual indicator<br>- Deletion is blocked or warns |
| **Pass/Fail** | |

---

### F. Normal Print Lifecycle

#### F-01: Create New Job

| Field | Value |
|-------|-------|
| **Objective** | Create a print job through the New Job dialog |
| **Preconditions** | Product `Aspirin 500mg` has 5+ Available codes, template assigned, CSV name set; `Sim1` connected |
| **Steps** | 1. Go to Jobs tab or Dashboard<br>2. Click "New Job"<br>3. Select product: `Aspirin 500mg`<br>4. Select printer: `Sim1`<br>5. Enter quantity: `5`<br>6. Click Create |
| **Expected** | - Job created in `Preparing` status<br>- Progress indicators show preparation steps<br>- Simulator logs show: SPPSTA, SPLCDF (upload), SPLGSD (verify), SPLGST/SPLLTF (template), SPGGTP (baseline)<br>- Job transitions to `Ready` |
| **Safety invariant** | TotalBaseline captured after SPLLTF via SPGGTP |
| **Pass/Fail** | |

#### F-02: Verify Code Reservation

| Field | Value |
|-------|-------|
| **Objective** | Verify codes are reserved in FIFO order during job creation |
| **Preconditions** | Job from F-01 in Ready state |
| **Steps** | 1. Go to Codes tab<br>2. Filter by product `Aspirin 500mg`<br>3. Filter by status: Reserved |
| **Expected** | - Exactly 5 codes show Reserved status<br>- These are the FIRST 5 codes that were imported (by ImportOrder)<br>- Remaining codes still show Available |
| **Pass/Fail** | |

#### F-03: Start Job and Monitor Progress

| Field | Value |
|-------|-------|
| **Objective** | Start the Ready job and watch real-time counter polling |
| **Preconditions** | Job in Ready state from F-01 |
| **Steps** | 1. Select the job<br>2. Click "Start"<br>3. Watch progress update |
| **Expected** | - Simulator logs: SPPSLQ (set qty=5), SPPSAP (start)<br>- Job status changes to `Printing`<br>- Counter polling starts (~500ms intervals, see SPGGCP in simulator logs)<br>- Progress bar updates: 1/5, 2/5, ..., 5/5<br>- Codes transition from Reserved to Printed as counter advances<br>- Job completes, status becomes `Completed`<br>- Simulator shows SPPSTP (stop) on completion |
| **Pass/Fail** | |

#### F-04: Verify Codes After Completion

| Field | Value |
|-------|-------|
| **Objective** | Verify all codes are Printed after job completes |
| **Preconditions** | Job from F-03 completed |
| **Steps** | 1. Go to Codes tab<br>2. Filter by product, status: Printed |
| **Expected** | - All 5 codes show status `Printed`<br>- No codes in Reserved status for this product<br>- Audit log shows job completion |
| **Pass/Fail** | |

#### F-05: Full Lifecycle with 50 Codes

| Field | Value |
|-------|-------|
| **Objective** | Run a larger job to observe sustained progress tracking |
| **Preconditions** | Product with 50 codes imported from `test_50_codes.csv`, template and CSV name set |
| **Steps** | 1. Create job with quantity=50<br>2. Start the job<br>3. Watch progress through to completion |
| **Expected** | - Preparation completes (CSV upload, template check)<br>- Printing progresses steadily<br>- Cross-check polls visible every 5th cycle (SPGGTP in simulator logs)<br>- Job completes at 50/50<br>- All 50 codes marked Printed |
| **Simulator note** | Simulator auto-prints at ~0.5s/label, so 50 codes takes ~25 seconds |
| **Pass/Fail** | |

#### F-06: Job with Template Upload

| Field | Value |
|-------|-------|
| **Objective** | Verify template is uploaded if not already on printer |
| **Simulator** | Restart simulator with `--templates` (empty -- no preloaded templates) |
| **Preconditions** | Product has template set to `demo/test32_32.rox` |
| **Steps** | 1. Create and observe job preparation |
| **Expected** | - During preparation, app detects template missing<br>- Uploads .rox file (SPLRTF in simulator logs)<br>- Then activates it (SPLLTF)<br>- Job reaches Ready |
| **Pass/Fail** | |

#### F-07: Job with Missing Template File on Disk

| Field | Value |
|-------|-------|
| **Objective** | Verify error when .rox file path is invalid |
| **Preconditions** | Product template set to a non-existent path like `C:\nonexistent.rox`; template also not on printer |
| **Steps** | 1. Create job<br>2. Observe preparation failure |
| **Expected** | - Error: "Template not found on printer and .rox file not found on disk"<br>- Job cancelled and codes returned to Available |
| **Pass/Fail** | |

#### F-08: Job with No CSV Name Configured

| Field | Value |
|-------|-------|
| **Objective** | Verify error when product has no CSV filename |
| **Preconditions** | Product with codes but no PrinterCsvName set |
| **Steps** | 1. Try to create a job |
| **Expected** | - Error: "Product has no CSV filename configured"<br>- Job fails during preparation |
| **Pass/Fail** | |

#### F-09: Job with No Template Configured

| Field | Value |
|-------|-------|
| **Objective** | Verify error when product has no template |
| **Preconditions** | Product with codes and CSV name but no TemplateFile |
| **Steps** | 1. Try to create a job |
| **Expected** | - Error: "Product has no template configured"<br>- Job fails during preparation |
| **Pass/Fail** | |

#### F-10: Job with Insufficient Codes

| Field | Value |
|-------|-------|
| **Objective** | Verify error when requesting more codes than available |
| **Preconditions** | Product has 3 Available codes |
| **Steps** | 1. Create job with quantity=10 |
| **Expected** | - Error: "Not enough codes available. Requested: 10, Available: 3"<br>- Job not created |
| **Safety invariant** | Quantity must not exceed available codes |
| **Pass/Fail** | |

#### F-11: Job Preparation Shows Progress Steps

| Field | Value |
|-------|-------|
| **Objective** | Verify the UI shows preparation progress messages |
| **Steps** | 1. Create a job and watch the preparation steps |
| **Expected** | - Messages cycle through: "Checking printer state...", "Reserving codes...", "Uploading data file...", "Loading template..."<br>- Each step visible in the UI progress area |
| **Pass/Fail** | |

---

### G. Job Cancellation

#### G-01: Cancel Preparing Job

| Field | Value |
|-------|-------|
| **Objective** | Cancel a job that is still in Preparing state |
| **Steps** | 1. Create a job (it transitions through Preparing quickly, so you may need to use a slow network or a busy product)<br>2. Cancel during preparation |
| **Expected** | - Job status becomes Cancelled<br>- Reserved codes returned to Available<br>- No codes left in Reserved state |
| **Pass/Fail** | |

#### G-02: Cancel Ready Job

| Field | Value |
|-------|-------|
| **Objective** | Cancel a job that is in Ready state (not yet started) |
| **Preconditions** | Job in Ready state |
| **Steps** | 1. Select the Ready job<br>2. Click Cancel |
| **Expected** | - Job status becomes Cancelled<br>- All reserved codes returned to Available<br>- ReadyWatcher stopped (check logs)<br>- No codes burned or quarantined (no ambiguity -- nothing was printed) |
| **Safety invariant** | Ready job cancellation is clean; no quarantine needed |
| **Pass/Fail** | |

#### G-03: Cancel Printing Job

| Field | Value |
|-------|-------|
| **Objective** | Cancel a job that is actively printing |
| **Preconditions** | Job printing (use 50-code job for enough time to cancel) |
| **Steps** | 1. Start a 50-code job<br>2. Wait until progress shows ~10-20 printed<br>3. Click Cancel |
| **Expected** | - Executor poll loop stopped<br>- Final counter read from printer (SPGGCP)<br>- Printer stopped (SPPSTP)<br>- Codes up to final counter: Printed<br>- Code at final counter boundary: Quarantined (ambiguous)<br>- Remaining codes: Returned to Available<br>- Job status: Cancelled<br>- Simulator logs show SPPSTP |
| **Safety invariant** | Boundary code quarantined, not burned; remaining codes returned |
| **Pass/Fail** | |

#### G-04: Verify Quarantined Code After Cancel

| Field | Value |
|-------|-------|
| **Objective** | Confirm exactly one code is quarantined at the cancel boundary |
| **Preconditions** | Job cancelled during printing (G-03) |
| **Steps** | 1. Go to Codes tab<br>2. Filter by status: Quarantined |
| **Expected** | - Exactly 1 code in Quarantined status (the boundary code)<br>- This is the code at the position where the cancel occurred<br>- Operator can later resolve it via status change |
| **Safety invariant** | Ambiguous codes are quarantined, not burned |
| **Pass/Fail** | |

#### G-05: Cancel Completed/Cancelled Job (Blocked)

| Field | Value |
|-------|-------|
| **Objective** | Verify completed or already-cancelled jobs cannot be cancelled again |
| **Steps** | 1. Select a Completed job<br>2. Try to Cancel it |
| **Expected** | - Error: "Cannot cancel job in Completed state" |
| **Pass/Fail** | |

---

### H. Job Pause & Resume

#### H-01: Pause Printing Job

| Field | Value |
|-------|-------|
| **Objective** | Pause an actively printing job |
| **Preconditions** | Job printing (50-code job) |
| **Steps** | 1. Start a 50-code job<br>2. Wait for ~10 codes to print<br>3. Click Pause |
| **Expected** | - Executor stopped<br>- SPPSTP sent to printer<br>- Final counter reconciled<br>- Codes up to counter: Printed<br>- Job status: Paused<br>- Progress shows "Paused at N/50" |
| **Pass/Fail** | |

#### H-02: Resume Paused Job

| Field | Value |
|-------|-------|
| **Objective** | Resume a paused job with the full Resume Procedure |
| **Preconditions** | Job in Paused state from H-01 |
| **Steps** | 1. Select paused job<br>2. Click Resume<br>3. Observe preparation and printing |
| **Expected** | - Old CSV deleted from printer<br>- New CSV uploaded with ONLY remaining (unprinted) codes<br>- Template re-activated<br>- Fresh TotalBaseline recorded<br>- Quantity set to remaining count<br>- Printing resumes<br>- New executor spawned with correct counter offset<br>- Progress continues from where it paused<br>- Job completes successfully at 50/50 |
| **Simulator note** | Simulator logs show: SPLDDF, SPLCDF (with fewer codes), SPLLTF, SPGGTP, SPPSLQ, SPPSAP |
| **Pass/Fail** | |

#### H-03: Resume Ready Job

| Field | Value |
|-------|-------|
| **Objective** | Resume a Ready job (delegates to StartJobAsync) |
| **Preconditions** | Job in Ready state |
| **Steps** | 1. Click Resume on a Ready job |
| **Expected** | - Behaves like Start: sets quantity, starts print, spawns executor<br>- Job transitions to Printing |
| **Pass/Fail** | |

#### H-04: Cancel Paused Job

| Field | Value |
|-------|-------|
| **Objective** | Cancel a job from Paused state |
| **Preconditions** | Job Paused at N out of total |
| **Steps** | 1. Cancel the paused job |
| **Expected** | - No quarantine needed (pause already reconciled the counter)<br>- Remaining unprinted codes returned to Available<br>- Job Cancelled |
| **Safety invariant** | Paused jobs have accurate counters, no ambiguity |
| **Pass/Fail** | |

---

### I. Concurrency Guards

#### I-01: One Active Job Per Printer

| Field | Value |
|-------|-------|
| **Objective** | Verify only one active job allowed per printer |
| **Preconditions** | `Sim1` has a Ready or Printing job |
| **Steps** | 1. Try to create another job on `Sim1` |
| **Expected** | - Job creation fails (SQLite partial unique index or service-level check)<br>- Error message explains the constraint |
| **Safety invariant** | One active job per printer enforced |
| **Pass/Fail** | |

#### I-02: One Active Job Per Product

| Field | Value |
|-------|-------|
| **Objective** | Verify only one active job allowed per product |
| **Preconditions** | Product `Aspirin 500mg` has an active job |
| **Steps** | 1. Try to create another job for `Aspirin 500mg` on a different printer |
| **Expected** | - Job creation fails<br>- Error explains one active job per product |
| **Safety invariant** | One active job per product enforced |
| **Pass/Fail** | |

#### I-03: Two Independent Printers Running Simultaneously

| Field | Value |
|-------|-------|
| **Objective** | Verify two printers can run jobs concurrently |
| **Preconditions** | `Sim1` on port 9100, `Sim2` on port 9101; two different products with codes |
| **Steps** | 1. Create and start job on Product A / Sim1<br>2. Create and start job on Product B / Sim2<br>3. Watch both progress simultaneously |
| **Expected** | - Both jobs run concurrently<br>- Both dashboards/progress cards update independently<br>- Both complete successfully without interference<br>- Simulator terminals show independent command streams |
| **Pass/Fail** | |

#### I-04: Printer-Level Serialization

| Field | Value |
|-------|-------|
| **Objective** | Verify per-printer lock prevents concurrent adapter operations |
| **Steps** | 1. This is primarily a code-level invariant<br>2. Verify in logs that SPPL commands for a single printer don't overlap<br>3. With two printers, commands may interleave between printers but never within the same printer |
| **Expected** | - Log timestamps show sequential SPPL TX/RX for each printer<br>- No "semaphore" errors in logs |
| **Pass/Fail** | |

---

### J. Connection Loss & Reconnect

#### J-01: Connection Loss During Printing (Printer Continues)

| Field | Value |
|-------|-------|
| **Objective** | Simulate network loss while printer keeps printing |
| **Preconditions** | Job printing on `Sim1` (50 codes, ~25s window) |
| **Steps** | 1. Start a 50-code job<br>2. Wait until ~10 codes printed<br>3. Stop the simulator (Ctrl+C) -- simulates network loss<br>4. Wait 3-5 seconds<br>5. Restart the simulator on the same port |
| **Expected** | - Executor logs IOException / connection lost<br>- Alert raised: "Connection lost"<br>- `_needsInspection = true` flag set<br>- On reconnect, post-reconnect inspection runs:<br>  - Reads SPPSTA, SPGGCP, SPGGTP, active template, serial<br>  - Computes lifetime delta to detect any prints that happened during disconnect<br>  - Catches up missed progress<br>  - Resumes normal polling<br>- Job completes successfully |
| **Simulator note** | The simulator resets state on restart (counters to 0, status to WAITING). This will trigger power-cycle detection in the app. See J-02 for that scenario. For a "printer continues" test, you would need to use a real printer or modify the simulator. |
| **Pass/Fail** | |

#### J-02: Connection Loss + Simulator Restart (Power Cycle Simulation)

| Field | Value |
|-------|-------|
| **Objective** | Simulate printer power cycle during active job |
| **Preconditions** | Job printing on `Sim1` |
| **Steps** | 1. Start a 50-code job<br>2. Wait until ~10 codes printed<br>3. Stop the simulator<br>4. Restart the simulator (counters reset to 0) |
| **Expected** | - On reconnect, inspection detects SPGGCP reset to 0<br>- Power cycle detected<br>- Job set to Error status<br>- Alert raised about power cycle<br>- Codes may be quarantined depending on lifetime delta |
| **Safety invariant** | Power cycle detection prevents silent data loss |
| **Pass/Fail** | |

#### J-03: Reconnect Loop Timing

| Field | Value |
|-------|-------|
| **Objective** | Verify exponential backoff in reconnection |
| **Preconditions** | Printer connected |
| **Steps** | 1. Stop the simulator<br>2. Watch log entries for reconnect attempts<br>3. Note the delay between attempts |
| **Expected** | - Delays: 1s, 2s, 4s, 8s, 16s, 30s (capped at 30s)<br>- Each attempt logged with attempt number and disconnected duration<br>- On simulator restart, reconnect succeeds and logs the total offline time |
| **Pass/Fail** | |

---

### K. Startup Recovery

#### K-01: Recovery of Stale Preparing Job

| Field | Value |
|-------|-------|
| **Objective** | Verify Preparing jobs are auto-cancelled on restart |
| **Steps** | 1. Create a job (it will go Preparing -> Ready quickly, so this is hard to catch)<br>2. Alternative: modify DB directly to set a job to Preparing status<br>3. Restart the application |
| **Expected** | - Stale Preparing job auto-cancelled<br>- Log: "Recovery: auto-cancelled stale Preparing job"<br>- Reserved codes returned to Available<br>- NO recovery dialog shown for this job |
| **Safety invariant** | Only Preparing jobs may be auto-cancelled |
| **Pass/Fail** | |

#### K-02: Recovery Dialog for Ready Job

| Field | Value |
|-------|-------|
| **Objective** | Verify Ready jobs trigger the recovery dialog, NOT auto-cancellation |
| **Preconditions** | Simulator running |
| **Steps** | 1. Create a job, let it reach Ready state<br>2. Force-close the application (Task Manager or Alt+F4)<br>3. Restart the application |
| **Expected** | - Recovery dialog appears on startup<br>- Shows the stale Ready job with columns: Job #, Product, Printer, App Says, Printer Says, Delta, Status, Flags, Recommended<br>- If printer is connected: inspection runs showing counter state<br>- Recommendation: "No printing detected -- safe to Resume or Abort"<br>- User can click Resume or Abort |
| **Safety invariant** | Ready jobs are NEVER auto-cancelled |
| **Pass/Fail** | |

#### K-03: Recovery Dialog for Printing Job

| Field | Value |
|-------|-------|
| **Objective** | Verify Printing jobs trigger recovery dialog |
| **Steps** | 1. Start a 50-code job<br>2. While printing (~10 codes in), force-close the app<br>3. Restart the app |
| **Expected** | - Recovery dialog shows the stale Printing job<br>- Inspection reads current printer state<br>- Shows delta (prints that occurred while app was closed)<br>- If simulator was restarted: shows "Power Cycle" flag<br>- If simulator kept running: shows discrepancy count<br>- User can Resume (with full Resume Procedure) or Abort |
| **Pass/Fail** | |

#### K-04: Recovery with Printer Offline

| Field | Value |
|-------|-------|
| **Objective** | Verify recovery handles offline printers |
| **Steps** | 1. Create a Ready job<br>2. Stop the simulator<br>3. Force-close the app<br>4. Restart the app (simulator still stopped) |
| **Expected** | - Recovery dialog shows job with "Offline" flag<br>- Recommendation: "Connect printer to inspect"<br>- User should Abort or wait for printer |
| **Pass/Fail** | |

#### K-05: Recovery Resume Action

| Field | Value |
|-------|-------|
| **Objective** | Resume a stale job from the recovery dialog |
| **Preconditions** | Recovery dialog showing a resumable job, printer connected |
| **Steps** | 1. Select the job in recovery dialog<br>2. Click "Resume Selected"<br>3. Observe job behavior |
| **Expected** | - Job transitions through Resume Procedure<br>- Remaining codes re-uploaded<br>- Template re-activated<br>- Job continues printing and completes |
| **Pass/Fail** | |

#### K-06: Recovery Abort Action

| Field | Value |
|-------|-------|
| **Objective** | Abort a stale job from the recovery dialog |
| **Steps** | 1. Select the job in recovery dialog<br>2. Click "Abort Selected" |
| **Expected** | - Job cancelled<br>- Codes handled appropriately (returned or quarantined based on state)<br>- Recovery dialog closes when all items resolved |
| **Pass/Fail** | |

#### K-07: Multiple Stale Jobs in Recovery

| Field | Value |
|-------|-------|
| **Objective** | Verify recovery handles multiple stale jobs |
| **Steps** | 1. Create jobs on two different printers<br>2. Force-close the app<br>3. Restart |
| **Expected** | - Recovery dialog lists all stale jobs<br>- Each can be independently Resumed or Aborted<br>- Dialog closes only after ALL items resolved |
| **Pass/Fail** | |

---

### L. Anomaly Detection & Quarantine

#### L-01: Counter Cross-Check Mismatch

| Field | Value |
|-------|-------|
| **Objective** | Verify anomaly alert when SPGGCP and SPGGTP diverge |
| **Notes** | This is difficult to reproduce with the standard simulator since both counters always match. Would require a modified simulator or mocking. |
| **Steps** | 1. If possible, modify the simulator to return different values for SPGGCP and SPGGTP<br>2. Start a job and observe |
| **Expected** | - Alert raised: counter mismatch warning<br>- Anomaly does NOT stop the printer (warning only)<br>- Job continues printing |
| **Safety invariant** | Never auto-stop a running printer on anomaly |
| **Pass/Fail** | |

#### L-02: Counter Jump Detection

| Field | Value |
|-------|-------|
| **Objective** | Verify alert when counter advances more than 10 in a single poll |
| **Notes** | Requires modified simulator or very fast print speed setting |
| **Steps** | 1. If counter jumps more than 10 between polls, verify alert is raised |
| **Expected** | - Warning alert: counter jump detected<br>- Job continues (warning only) |
| **Pass/Fail** | |

#### L-03: SPGGCP Backward Movement

| Field | Value |
|-------|-------|
| **Objective** | Verify job halts when SPGGCP goes backward during normal polling |
| **Notes** | Requires modified simulator |
| **Steps** | 1. If SPGGCP returns a value less than previous poll<br>2. Observe behavior |
| **Expected** | - Job set to Error status<br>- Alert: "SPGGCP went backward"<br>- Halted immediately |
| **Pass/Fail** | |

#### L-04: Template Mismatch After Reconnect

| Field | Value |
|-------|-------|
| **Objective** | Verify template mismatch detection after connection loss |
| **Notes** | Requires simulator restart with a different active template |
| **Steps** | 1. Start a job<br>2. Disconnect (stop simulator)<br>3. Restart simulator with `--templates different_template.rox`<br>4. Observe reconnect inspection |
| **Expected** | - Post-reconnect inspection detects template mismatch<br>- Job set to Error<br>- Remaining codes QUARANTINED (not returned to Available)<br>- Alert explains the mismatch |
| **Safety invariant** | Quarantine on template mismatch |
| **Pass/Fail** | |

#### L-05: Lifetime Counter Rollback

| Field | Value |
|-------|-------|
| **Objective** | Verify detection when SPGGTP goes backward (hardware swap indicator) |
| **Notes** | Requires modified simulator returning lower SPGGTP |
| **Steps** | 1. During reconnect inspection, if SPGGTP < TotalBaseline |
| **Expected** | - Job set to Error<br>- Codes quarantined<br>- Alert: "Lifetime counter went backward -- possible hardware swap" |
| **Safety invariant** | Quarantine on counter rollback |
| **Pass/Fail** | |

---

### M. External Print Detection (ReadyWatcher)

#### M-01: External Print Start on Ready Job

| Field | Value |
|-------|-------|
| **Objective** | Verify ReadyWatcher detects when someone starts the printer externally |
| **Notes** | The standard simulator doesn't support external print triggering easily. This test requires manual simulation or a modified simulator. |
| **Approach** | 1. Create a job, let it reach Ready<br>2. Use a second TCP client to send `~SPPSAP^` directly to the simulator (or modify the simulator to change status to RUNNING after a delay) |
| **Expected** | - ReadyWatcher detects SPPSTA=RUNNING or SPGGCP > baseline<br>- Alert: "Printing started externally"<br>- Job automatically transitions to Printing<br>- JobExecutor spawned to track progress<br>- ReadyWatcher stops itself |
| **Safety invariant** | Ready jobs are never auto-cancelled; external prints are tracked |
| **Real printer** | On a real printer, press Start on the touchscreen while job is Ready |
| **Pass/Fail** | |

#### M-02: ReadyWatcher During Connection Loss

| Field | Value |
|-------|-------|
| **Objective** | Verify ReadyWatcher handles connection errors gracefully |
| **Steps** | 1. Create a Ready job (ReadyWatcher running)<br>2. Stop the simulator briefly<br>3. Restart it |
| **Expected** | - ReadyWatcher logs IOException, waits 5s, retries<br>- Does NOT crash or stop watching<br>- Resumes monitoring after reconnect |
| **Pass/Fail** | |

---

### N. Codes Tab Administration

#### N-01: View Codes with Pagination

| Field | Value |
|-------|-------|
| **Objective** | Verify paginated code display |
| **Preconditions** | Product with 50+ codes |
| **Steps** | 1. Go to Codes tab<br>2. Select a product<br>3. Navigate pages |
| **Expected** | - Codes shown in pages<br>- Page size selector available<br>- Next/Previous buttons work<br>- Page count shown |
| **Pass/Fail** | |

#### N-02: Filter by Status

| Field | Value |
|-------|-------|
| **Objective** | Verify status filtering |
| **Preconditions** | Product with codes in multiple statuses (Available, Printed, Quarantined) |
| **Steps** | 1. Select status filter: Available<br>2. Switch to Printed<br>3. Switch to Quarantined |
| **Expected** | - Each filter shows only codes with that status<br>- Counts update appropriately<br>- "All" shows all codes |
| **Pass/Fail** | |

#### N-03: Search Codes

| Field | Value |
|-------|-------|
| **Objective** | Verify code search with debounce |
| **Steps** | 1. Type a partial code value in the search box<br>2. Wait ~300ms for debounce |
| **Expected** | - Results filter to matching codes<br>- Search is debounced (not on every keystroke)<br>- Clearing search restores full list |
| **Pass/Fail** | |

#### N-04: Change Code Status (Available -> Burned)

| Field | Value |
|-------|-------|
| **Objective** | Change status of individual codes |
| **Preconditions** | Available codes exist |
| **Steps** | 1. Select one or more Available codes<br>2. Change status to Burned |
| **Expected** | - Confirmation dialog<br>- Codes change to Burned<br>- Status bar shows "Changed N code(s) to Burned"<br>- Undo available |
| **Pass/Fail** | |

#### N-05: Risky Status Change (Printed -> Available) with Warning

| Field | Value |
|-------|-------|
| **Objective** | Verify extra warning when changing Printed codes back to Available |
| **Steps** | 1. Select Printed codes<br>2. Try to change status to Available |
| **Expected** | - WARNING dialog: "If any of these codes were physically printed, marking them Available will allow them to be printed again, creating a DUPLICATE"<br>- User must confirm to proceed |
| **Safety invariant** | Duplicate-risk transitions require explicit confirmation |
| **Pass/Fail** | |

#### N-06: Reserved Codes Cannot Be Selected

| Field | Value |
|-------|-------|
| **Objective** | Verify reserved codes are protected from manual operations |
| **Preconditions** | Active job with reserved codes |
| **Steps** | 1. Filter by Reserved status<br>2. Try to select/check reserved codes |
| **Expected** | - Reserved codes are NOT selectable (checkbox disabled or not present)<br>- Cannot change status, move, or archive reserved codes<br>- "Select All" skips reserved codes |
| **Safety invariant** | Reserved codes cannot be modified through admin operations |
| **Pass/Fail** | |

#### N-07: Quarantine Resolution

| Field | Value |
|-------|-------|
| **Objective** | Resolve quarantined codes to Available, Printed, or Burned |
| **Preconditions** | Quarantined codes exist (from a cancelled printing job) |
| **Steps** | 1. Filter by Quarantined<br>2. Select a quarantined code<br>3. Change to Available (after investigation confirms it wasn't printed)<br>4. Select another, change to Printed (confirmed it was printed)<br>5. Select another, change to Burned (discard it) |
| **Expected** | - Each transition works with appropriate confirmation<br>- Quarantined -> Available: no extra warning (operator investigated)<br>- Audit trail records each resolution |
| **Pass/Fail** | |

#### N-08: Move Codes Between Products

| Field | Value |
|-------|-------|
| **Objective** | Move available codes from one product to another |
| **Preconditions** | Two leaf products, codes in source product |
| **Steps** | 1. Select Available codes<br>2. Click Move<br>3. Select target product |
| **Expected** | - Confirmation dialog: "Move N code(s) to [target]?"<br>- Codes now belong to target product<br>- Source product code count decreases<br>- Target product code count increases<br>- Undo available |
| **Pass/Fail** | |

#### N-09: Move Codes to Non-Leaf (Blocked)

| Field | Value |
|-------|-------|
| **Objective** | Verify codes can only be moved to leaf products |
| **Steps** | 1. Try to move codes to a folder |
| **Expected** | - Error: "Codes can only be moved to leaf products" |
| **Pass/Fail** | |

#### N-10: Archive Codes

| Field | Value |
|-------|-------|
| **Objective** | Archive codes from the active pool |
| **Steps** | 1. Select codes<br>2. Click Archive<br>3. Confirm |
| **Expected** | - Confirmation: "Archive N code(s)?"<br>- Codes removed from active pool<br>- Can be re-imported later<br>- Undo available |
| **Pass/Fail** | |

#### N-11: Bulk Status Change (All Filtered)

| Field | Value |
|-------|-------|
| **Objective** | Change status of ALL codes matching the current filter |
| **Steps** | 1. Filter by status: Available<br>2. Click "Change All" to Burned |
| **Expected** | - Warning: "Change ALL N Available code(s) to Burned?"<br>- Extra emphasis: affects all matching codes, not just current page<br>- All matching codes change status<br>- Undo available |
| **Pass/Fail** | |

#### N-12: Undo Operation

| Field | Value |
|-------|-------|
| **Objective** | Undo the last code operation |
| **Steps** | 1. Perform a status change on some codes<br>2. Click Undo |
| **Expected** | - Codes revert to previous status<br>- Status bar: "Reverted N code(s)"<br>- Undo stack goes back up to 10 operations |
| **Pass/Fail** | |

#### N-13: Bulk Move All Filtered

| Field | Value |
|-------|-------|
| **Objective** | Move all codes matching the filter to another product |
| **Steps** | 1. Filter codes<br>2. Click "Move All" to another product |
| **Expected** | - Warning: "Move ALL N codes to [target]? This affects every matching code, not just the current page."<br>- All matching codes moved |
| **Pass/Fail** | |

---

### O. Printer Verify Tab

#### O-01: Verify Printer (No Active Job)

| Field | Value |
|-------|-------|
| **Objective** | Run verification when no job is active |
| **Preconditions** | `Sim1` connected, no active jobs |
| **Steps** | 1. Go to Printers tab<br>2. Select `Sim1`<br>3. Click Verify |
| **Expected** | - Verification dialog appears<br>- Checks: Connection (Passed), Printer Status (shows state), Counter (shows lifetime value)<br>- Template and CSV checks show "no active job" info<br>- Overall: All OK |
| **Pass/Fail** | |

#### O-02: Verify Printer with Active Job

| Field | Value |
|-------|-------|
| **Objective** | Run verification while a job is Ready or Printing |
| **Preconditions** | Active job on `Sim1` |
| **Steps** | 1. Verify printer |
| **Expected** | - Connection: Passed<br>- CSV File: shows whether expected CSV exists on printer<br>- Active Template: shows whether active template matches expected<br>- Counter: shows lifetime counter and consistency with app's CodesConfirmed<br>- Printer Status: shows current state |
| **Pass/Fail** | |

#### O-03: Verify Printer in ERROR State

| Field | Value |
|-------|-------|
| **Objective** | Verify detection of printer errors |
| **Simulator** | Start with `--status error` |
| **Steps** | 1. Connect to the ERROR printer<br>2. Run Verify |
| **Expected** | - Printer Status check: FAILED - "Printer is in ERROR state"<br>- Overall: ISSUES FOUND |
| **Pass/Fail** | |

#### O-04: Verify Printer in BLOCKED State

| Field | Value |
|-------|-------|
| **Objective** | Verify detection of BLOCKED state |
| **Simulator** | Start with `--blocked` |
| **Steps** | 1. Connect and Verify |
| **Expected** | - Status: "Printer is BLOCKED"<br>- Overall: ISSUES FOUND |
| **Pass/Fail** | |

---

### P. Dashboard

#### P-01: Dashboard Printer Cards

| Field | Value |
|-------|-------|
| **Objective** | Verify dashboard shows printer cards with job info |
| **Preconditions** | At least one printer has had a job |
| **Steps** | 1. Navigate to Dashboard |
| **Expected** | - Printer cards visible for printers that have had jobs<br>- Active jobs show at top<br>- Card shows: printer name, product, progress, status<br>- Job controls available from card (Start, Cancel, Pause, Resume) |
| **Pass/Fail** | |

#### P-02: Real-Time Progress Updates on Dashboard

| Field | Value |
|-------|-------|
| **Objective** | Verify live progress updates via event bus |
| **Preconditions** | Job actively printing |
| **Steps** | 1. Watch the dashboard card for the printing job |
| **Expected** | - Progress updates in real-time<br>- Counter shown: N / Total (percentage)<br>- Updates even when navigating away from Jobs tab and back |
| **Pass/Fail** | |

#### P-03: Dashboard Job Controls

| Field | Value |
|-------|-------|
| **Objective** | Start/Cancel/Pause/Resume from dashboard cards |
| **Steps** | 1. Start a Ready job from the dashboard card<br>2. Pause it from the card<br>3. Resume it<br>4. Cancel a different job |
| **Expected** | - All job actions work from dashboard cards<br>- Card updates reflect new status immediately |
| **Pass/Fail** | |

#### P-04: Activity Feed

| Field | Value |
|-------|-------|
| **Objective** | Verify recent activity feed shows audit entries |
| **Steps** | 1. Perform various operations<br>2. Check the activity feed on Dashboard |
| **Expected** | - Recent operations appear in activity feed<br>- Color-coded by type (blue=started, green=completed, amber=paused, gray=cancelled, purple=import) |
| **Pass/Fail** | |

---

### Q. Logging & Audit Trail

#### Q-01: Audit Log Entries

| Field | Value |
|-------|-------|
| **Objective** | Verify all safety-critical operations are audited |
| **Steps** | 1. Perform a complete lifecycle: create product, import codes, create job, start, complete<br>2. Check audit entries (in Products activity history or Dashboard feed) |
| **Expected** | Audit entries for:<br>- `import` (code import with batch name, count, duplicates)<br>- `job_created` (with quantity)<br>- `job_started`<br>- `job_completed` or `job_cancelled`<br>- `job_paused` / `job_resumed` if applicable |
| **Pass/Fail** | |

#### Q-02: Log File Completeness

| Field | Value |
|-------|-------|
| **Objective** | Verify log file captures the full SPPL command stream |
| **Steps** | 1. Run a complete job lifecycle<br>2. Open the log file<br>3. Grep for `SPPL TX` and `SPPL RX` entries |
| **Expected** | - Every command sent to the printer is logged with `SPPL TX ->`<br>- Every response logged with `SPPL RX <-`<br>- CSV uploads show truncated preview (filename + first 5 codes)<br>- Template uploads show name + size |
| **Pass/Fail** | |

---

### R. Localization & UI

#### R-01: Language Switch

| Field | Value |
|-------|-------|
| **Objective** | Switch between English and Russian |
| **Steps** | 1. Find language selector (bottom of sidebar)<br>2. Switch to Russian<br>3. Navigate all tabs<br>4. Switch back to English |
| **Expected** | - All UI labels change language<br>- Error messages appear in selected language<br>- Dialog titles and content localized<br>- Language preference persists across restart |
| **Pass/Fail** | |

#### R-02: Zoom Level

| Field | Value |
|-------|-------|
| **Objective** | Verify zoom level setting |
| **Steps** | 1. Find zoom level control<br>2. Adjust zoom<br>3. Verify UI scales |
| **Expected** | - UI scales proportionally<br>- Setting persists across restart |
| **Pass/Fail** | |

---

### S. Persistence & Restart

#### S-01: Full State Persistence

| Field | Value |
|-------|-------|
| **Objective** | Verify all data persists across clean restart |
| **Steps** | 1. Create products, import codes, create some jobs (completed and cancelled)<br>2. Close the application normally<br>3. Restart |
| **Expected** | - All products and folders present<br>- Code counts and statuses preserved<br>- Job history preserved<br>- Printers present and auto-connect<br>- Audit log retained |
| **Pass/Fail** | |

#### S-02: Job Execution Survives Tab Navigation

| Field | Value |
|-------|-------|
| **Objective** | Verify jobs continue running when user navigates away from Jobs tab |
| **Steps** | 1. Start a 50-code job<br>2. Navigate to Products tab<br>3. Navigate to Dashboard<br>4. Return to Jobs tab |
| **Expected** | - Job continued printing in background<br>- Progress updated when returning to Jobs tab<br>- Dashboard showed live progress too |
| **Safety invariant** | Job execution is in the service layer, not the ViewModel |
| **Pass/Fail** | |

---

## 5. Real Printer Adaptation Notes

After completing all simulator tests, repeat applicable scenarios on a real Savema printer with these adaptations:

### Setup

- Use a controlled, non-production printer
- Connect the printer to the same network as the PC
- Add the printer with its real IP address and port 9100
- Use small quantities (5-10 codes) initially
- Have the printer loaded with ribbon/media for actual printing

### Key Differences from Simulator

| Area | Simulator | Real Printer |
|------|-----------|-------------|
| **Print speed** | 0.5s/label (fixed) | Depends on belt speed, typically faster |
| **Counter behavior** | SPGGCP may or may not reset on SPLLTF | Firmware-specific; may be cumulative |
| **Template activation** | Instant | May cause brief TCP disconnect |
| **Power cycle** | Stop/restart process | Physical power off/on; preserves serial, clears counters |
| **BLOCKED state** | CLI flag | Operator not on main screen |
| **ERROR state** | CLI flag | Ribbon empty, head up, etc. |
| **Physical output** | None | Actual printed labels to verify |
| **Serial number** | Always `SIM00001` | Real serial; validates hardware swap detection |

### Tests to Prioritize on Real Hardware

1. **F-03/F-05**: Normal print lifecycle -- verify actual labels are printed correctly
2. **F-06**: Template upload -- verify .rox renders correctly on the printer
3. **G-03**: Cancel during printing -- verify physical stop position matches counter
4. **H-01/H-02**: Pause/Resume -- verify seamless continuation of physical printing
5. **J-01**: Network loss during printing -- unplug/replug Ethernet cable
6. **Power cycle**: Turn printer off during printing, then back on; verify recovery dialog
7. **BLOCKED state**: Navigate away from main screen on the touchscreen; verify app detects BLOCKED
8. **External print**: Press Start on the touchscreen while job is Ready; verify ReadyWatcher
9. **Serial number**: Verify serial is recorded on first connect, verified on subsequent connects

### Tests NOT Applicable to Real Hardware

- Simulator-specific CLI flags (can't set `--status error` on real hardware)
- Instant state changes (real hardware has physical latency)
- Some anomaly scenarios that require counter manipulation

### Safety Precautions for Real Hardware

- **Never** test with production codes
- **Always** use small quantities first
- **Verify** the template renders correctly before running large batches
- **Record** the lifetime counter (SPGGTP) before and after each test
- **Physically inspect** printed labels when verifying cancel/pause boundary codes
- **Do not** power-cycle the printer during production; use only during controlled tests
- **Coordinate** with operators if the printer is shared
- **Keep test codes clearly labeled** so they are not mixed with production output

---

## Appendix: Quick Reference

### SPPL Commands to Watch in Simulator Logs

| Command | Description | When |
|---------|-------------|------|
| `SPPSTA` | Status query | Preparation, polling, verification |
| `SPLCDF` | CSV upload | Preparation |
| `SPLGSD` | List CSV files | Verification |
| `SPLDDF` | Delete CSV | Before upload, cleanup |
| `SPLRTF` | Upload template | If missing |
| `SPLLTF` | Activate template | Preparation |
| `SPLGAT` | Get active template | Verification |
| `SPLGST` | List templates | Storage, preparation |
| `SPGGTP` | Lifetime counter | Baseline, cross-check |
| `SPGGCP` | Current counter | Polling (every 500ms) |
| `SPPSLQ` | Set quantity | Before start |
| `SPPSAP` | Start print | Start |
| `SPPSTP` | Stop print | Cancel, pause, completion |
| `SPGGSN` | Serial number | Connection, recovery |

### Recommended Execution Order

1. **A** - Startup (fresh DB)
2. **B** - Printer Management (add `Sim1`)
3. **C** - Products & Folders (create test hierarchy)
4. **D** - CSV Import (populate code pools)
5. **E** - Printer Storage (verify storage management)
6. **F** - Normal Print Lifecycle (the core flow)
7. **G** - Cancellation (test abort paths)
8. **H** - Pause & Resume (test continuation)
9. **I** - Concurrency (multi-printer, guards)
10. **N** - Codes Administration (status changes, moves, undo)
11. **O** - Printer Verify (health checks)
12. **P** - Dashboard (UI verification)
13. **J** - Connection Loss (fault injection)
14. **K** - Startup Recovery (crash simulation)
15. **L** - Anomaly Detection (requires modified simulator)
16. **M** - External Print (requires second client or real printer)
17. **Q** - Logging & Audit (review accumulated records)
18. **R** - Localization & UI
19. **S** - Persistence & Restart (final clean-state verification)

---

## 6. Test Findings

### Finding #1 -- Phantom Entity Crash on Start Print (2026-08-26)

**Discovered during:** Manual testing (B/F/I area -- printer disconnect/reconnect + job start)

**Severity:** Critical (application crash + printer left running untracked)

**Reproduction steps:**

1. Create a product with codes, template, and CSV name configured
2. Add a printer (`Sim1`) connected to the simulator
3. Create a job (Job #44, qty=15) -- let it reach **Ready** state
4. Go to Printers tab, **disconnect** the printer
5. While disconnected, open New Job and try to create **another job** for the **same product** (qty=13) -- this fails with a UNIQUE constraint error (expected behavior)
6. **Reconnect** the printer
7. Go to Jobs tab, select Job #44 (Ready), click **Start**
8. **App crashes** with unhandled `DbUpdateException`

**Root cause chain (3 bugs):**

**Bug 1 -- DbContext change tracker poisoning (`CreateJobAsync`):**

In step 5, `CreateJobAsync` calls `_db.PrintJobs.Add(new PrintJob {...})` which adds the entity to the EF Core change tracker as `Added`. Then `SaveChangesAsync()` fails because the partial unique index on `ProductId` rejects the INSERT (Job #44 is still active for the same product). The exception propagates, but **the phantom entity remains in the change tracker** as `Added`. Every subsequent `SaveChangesAsync()` on the same `AppDbContext` instance will try to INSERT this phantom entity again and fail.

In the WPF host, `AppDbContext` is effectively a singleton (lives for the app's lifetime), so the phantom entity persists until the app is restarted.

**Bug 2 -- Missing pre-check in `CreateJobAsync`:**

`CreateJobAsync` checks for available codes but does **not** check whether the product (or printer) already has an active job before calling `_db.PrintJobs.Add()`. It relies solely on the database unique index to enforce the one-active-job-per-product constraint. By the time the index rejects the INSERT, the entity is already tracked. A pre-check query like `_db.PrintJobs.AnyAsync(j => j.ProductId == productId && activeStatuses.Contains(j.Status))` would prevent the entity from being added at all.

**Bug 3 -- Printer started before DB save in `StartJobAsync`:**

`StartJobAsync` sends `SPPSLQ` (set quantity) and `SPPSAP` (start print) to the printer **before** calling `SaveChangesAsync()` to persist `Status = Printing`. When the save fails (due to Bug 1), the printer is physically running but the job was never recorded as Printing. On restart, the recovery system would find Job #44 still in Ready state while the printer has already printed codes, creating a state discrepancy.

**Log evidence:**

```
13:15:29 -- Job #44 created (product=5, printer=2, qty=15) -> Ready
13:19:47 -- CreateJobAsync(product=5, printer=2, qty=13) -> FAILED
             SQLite Error 19: 'UNIQUE constraint failed: print_jobs.ProductId'
             Entity left in change tracker as Added
13:20:03 -- Printer reconnected
13:20:22 -- StartJobAsync(jobId=44)
             SPPSAP -> OK (printer started!)
             SaveChangesAsync -> FAILED (phantom entity re-triggers same UNIQUE violation)
             UNHANDLED DISPATCHER EXCEPTION -> crash
```

**Affected files:**

- `PrintJobService.cs` -- `CreateJobAsync` (line ~83), `StartJobAsync` (line ~319)

**Additional observations:**

- The ReadyWatcher for Job #44 continued polling with `InvalidOperationException: Not connected to printer` after the disconnect. This exception is caught as a generic `Exception` (not `IOException`), so the watcher logs a warning and retries after 5 seconds. The watcher correctly keeps watching but uses the old adapter instance rather than the new one created on reconnect.
- The NewJobViewModel's error handling caught and displayed the creation failure to the user, but the underlying DbContext corruption was invisible.
