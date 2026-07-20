# DataMatrix Print Service

Industrial GS1 DataMatrix printing pipeline for Savema thermal transfer overprinters using SPPL over TCP.

Built for production environments where serialization codes from CSV exports need to be encoded as GS1 DataMatrix barcodes and printed on Savema TTO printers (SVM 32x70 I, SVM 32 CK, and compatible models) — using the SPPL (Savema Printer Programming Language) protocol over raw TCP on port 9100.

## Architecture

```
CSV File
  │
  ▼
CSV Processor ──→ validates rows (valid / skipped / invalid)
  │
  ▼
SPPL Client ────→ encodes GS1 data, sends SPMC2D modification commands
  │
  ▼
TCP Client ─────→ sends SPPL over raw TCP to port 9100
  │
  ├──→ Savema Printer (production)
  └──→ Simulator (development)
         │
         ▼
       Status Handling ──→ parses SPPSTA responses (WAITING, RUNNING, ERROR)
```

### How Savema Printing Works

Unlike Zebra printers where the host builds the entire label layout, Savema uses a **template-based** approach:

1. **Design** a template in Sayasis S20 software with a GS1-DataMatrix field set to `Source = External`
2. **Upload** the template to the printer as a `.rox` file
3. **Inject data** at runtime via `SPMC2D` commands over TCP
4. **Trigger print** via `SPPOTP` (single) or `SPPSAP` (continuous)

## Features

- **GS1 DataMatrix encoding** — converts `\x1d` group separators for SPPL transport, escapes XML-reserved characters (`&`, `<`, `>`, `"`, `'`) per SPPL spec
- **Batch CSV processing** — reads serialization codes from CSV files with configurable column selection, delimiter, and header handling
- **CSV validation** — classifies every row as valid, skipped, or invalid with clear reasons (empty code, duplicate code, missing column, malformed escape). Bad row → skip and record reason. Bad file structure → fail early with clear message.
- **Batch result report** — clean summary after every batch: total rows, skipped, invalid, extracted, sent OK / failed, plus detailed sections for invalid rows, CSV warnings, and print failures
- **Template management** — load templates, list stored templates, query field names, get active template
- **Printer status** — queries `SPPSTA` and parses responses into structured status (INIT, WAITING, RUNNING, ERROR, BLOCKED)
- **SPPL simulator** — threaded TCP server that emulates a Savema printer, responds to all SPPL commands, saves label data to files, and supports configurable status modes
- **Structured logging** — configurable log level, module-specific loggers, clear debug output for connection attempts, retries, and print jobs
- **Retry logic** — configurable retries and delay for TCP communication with the printer
- **Dry-run mode** — outputs SPPL commands to console instead of sending to printer, for development and review
- **Web UI** — Flask-based browser interface with printer status panel, single/batch print controls, SPPL command log, and built-in simulator toggle

## Project Structure

```
datamatrix-print-service/
├── main.py                CLI entry point (print, batch, status, clear, info, templates, fields)
├── web_ui.py              Flask web UI for demo and operation
├── templates/index.html   Web UI frontend (Tailwind CSS)
├── savema_printer.py      SavemaPrinterClient, SPPL command builders, GS1 encoder, status parser
├── csv_processor.py       CSV reader with row validation
├── config.py              Settings dataclass + config.ini loader
├── config.ini             Default configuration
├── savema_simulator.py    Savema SPPL protocol simulator (TCP server)
├── docs/                  SPPL Rev.12 manual, hardware documentation
└── tests/
    ├── test_savema_printer.py     SPPL commands, status parsing, GS1 encoding
    ├── test_savema_simulator.py   Live TCP tests for all SPPL commands
    ├── test_csv_processor.py      CSV reading + all validation cases
    ├── test_config.py             Config loading and defaults
    └── test_batch_result.py       Report formatting
```

## How to Run

### Install

```bash
# Python 3.11+ required
git clone <repo-url>
cd datamatrix-print-service
pip install flask
```

### Run the Simulator

Start the simulator to develop and test without a real printer:

```bash
# Default: WAITING state (ready to print)
python savema_simulator.py

# Simulate error states
python savema_simulator.py --status error --error-message "Ribbon not found"

# Simulate BLOCKED mode (operator not on main window)
python savema_simulator.py --blocked

# Pre-load specific templates
python savema_simulator.py --templates gs1label_32.rox backup_32.rox

# Custom port
python savema_simulator.py --port 9101
```

### Print a Single Code

```bash
# Dry-run (print SPPL commands to console)
python main.py print "010485000607001121ABC" --dry-run

# Send to printer/simulator
python main.py print "010485000607001121ABC"

# Override the datamatrix field name
python main.py print "010485000607001121ABC" --field my_barcode_field
```

### Batch Print from CSV

```bash
# Dry-run
python main.py batch codes.csv --column code --dry-run

# Send to printer
python main.py batch codes.csv --column code

# Custom delimiter and no header
python main.py batch codes.csv --column 0 --delimiter ";" --no-header

# Use a different config
python main.py --config config_line2.ini batch codes.csv --column code
```

### Check Printer Status

```bash
python main.py status
# Output: WAITING
# or:     ERROR: Ribbon not found.Please insert ribbon
# or:     RUNNING (BLOCKED)
```

### Clear Data Buffer

```bash
python main.py clear
```

### Printer Info

```bash
python main.py info
# Firmware:        6.3.001.600.R
# Serial number:   17013012
# Active template: gs1label_32.rox
# Print count:     1250
```

### List Templates

```bash
python main.py templates
#   gs1label_32.rox (active)
#   backup_32.rox
```

### Get Field Names

```bash
python main.py fields
#   gs1_code
#   batch_txt
#   date_txt
```

### Web UI

```bash
# Start web UI only (connect to real printer via config.ini)
python web_ui.py

# Start web UI with built-in simulator (no real printer needed)
python web_ui.py --simulator

# Custom web UI port
python web_ui.py --port 8080
```

Open http://127.0.0.1:5000 in your browser. The UI provides:
- Printer status and connection monitoring
- Single label printing with dry-run toggle
- CSV batch upload and processing
- Live SPPL command log showing all sent/received traffic
- One-click simulator start/stop

### Run Tests

```bash
python -m pytest tests/ -v
```

## Configuration

All parameters live in `config.ini` and can be overridden via CLI flags:

```ini
[printer]
host = 127.0.0.1       # Printer IP address
port = 9100             # SPPL TCP port
timeout = 5.0           # Connection timeout (seconds)
retries = 3             # Retry attempts on failure
retry_delay = 1.0       # Delay between retries (seconds)

[savema]
template_name = gs1label_32.rox   # Template file on the printer (.rox)
datamatrix_field = gs1_code       # 2D barcode field name (Source must be External)

[csv]
column =                # Column name or index (empty = first column)
delimiter = ,           # CSV field delimiter
has_header = true       # Whether first row is a header

[mode]
dry_run = false         # Output SPPL to console instead of sending
log_level = INFO        # DEBUG, INFO, WARNING, ERROR
```

## Example Workflow

A step-by-step demo from CSV to printed label:

```bash
# 1. Start the simulator
python savema_simulator.py
# → Savema SPPL Simulator listening on port 9100
# → Status: WAITING
# → Active template: gs1label_32.rox

# 2. Check that the "printer" is ready
python main.py status
# → WAITING

# 3. Batch print from a CSV file (dry-run first)
python main.py batch sample_data.csv --column code --dry-run
# → Shows SPPL commands for each label + batch report

# 4. Send to the simulator
python main.py batch sample_data.csv --column code
# → ============================================
#     BATCH RESULT REPORT
#   ============================================
#     CSV file:          sample_data.csv
#     Total rows:        3
#     Skipped rows:      0
#     Invalid rows:      0
#     Codes extracted:   3
#     Sent OK:           3
#     Failed:            0
#   ============================================

# 5. Inspect saved label data
cat received_labels/label_0001.txt
# → gs1_code=0104850006070011211:e:Bp

# 6. Check printer info
python main.py info
# → Firmware:        6.3.001.600.R
# → Serial number:   SIM00001
# → Active template: gs1label_32.rox
# → Print count:     3

# 7. Test error handling: restart simulator in error mode
python savema_simulator.py --status error --error-message "Ribbon not found"
python main.py status
# → ERROR: Ribbon not found
```

## SPPL Protocol Reference

This project implements the Savema Printer Programming Language (SPPL) Rev.12. Key commands used:

| Command | Description | Example |
|---------|-------------|---------|
| `SPMC2D` | Update 2D barcode field | `~SPMC2D{gs1_code~gt~01048500...}^` |
| `SPMCTV` | Update text field | `~SPMCTV{batch_txt~gt~LOT001}^` |
| `SPPOTP` | One test print | `~SPPOTP^` |
| `SPPSAP` | Start automatic print | `~SPPSAP^` |
| `SPPSTP` | Stop print | `~SPPSTP^` |
| `SPPSTA` | Query status | `~SPPSTA^` → `WAITING` / `RUNNING` / `ERROR` |
| `SPLLTF` | Load template | `~SPLLTF{gs1label_32.rox}^` |
| `SPLGFN` | Get field names | `~SPLGFN{gs1label_32.rox}^` |

All commands start with `~` and end with `^`. All responses use the format `~ SPGRES{COMMAND:RESULT}^`.

Full protocol documentation: [`docs/savema_language_-_rev12.md`](docs/savema_language_-_rev12.md)

## Template Setup (Sayasis S20)

Before using this tool, you must create a template on the printer:

1. Open **Sayasis S20** software
2. Create a new template for your printer width (32mm, 53mm, or 107mm)
3. Add a **2D Barcode** object with:
   - `TwoDBarcodeType` = `GS1-Datamatrix`
   - `Source` = **External**
   - Give it a name (e.g., `gs1_code`)
4. Save and upload to the printer as a `.rox` file
5. Set `template_name` and `datamatrix_field` in `config.ini`

## Tests

125 tests covering:

- SPPL command builders (SPMC2D, SPMCTV, SPMCBV, SPMCSV, SPLLTF, SPLAQD, chaining)
- Response parsing (OK, FAIL, not found, data responses)
- Printer status parsing (INIT, WAITING, RUNNING, ERROR, BLOCKED, all combinations)
- GS1 encoding for Savema (GS character handling, XML entity escaping)
- SPPL simulator (live TCP tests for all commands: status, templates, modifications, print, queue, general)
- CSV processing (blank rows, duplicates, empty codes, malformed escapes, missing columns, extra columns)
- Configuration loading (defaults, savema section, partial config, full config)
- Batch result reporting (all report sections, properties)

```bash
python -m pytest tests/ -v
# 125 passed
```
