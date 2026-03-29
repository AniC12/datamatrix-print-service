# DataMatrix Print Service

Industrial GS1 DataMatrix printing pipeline for Zebra printers using raw ZPL over TCP.

Built for production environments where serialization codes from CSV exports need to be encoded as GS1 DataMatrix barcodes and printed on Zebra ZT411 (or compatible) label printers — without vendor SDKs, just raw TCP on port 9100.

## Architecture

```
CSV File
  │
  ▼
CSV Processor ──→ validates rows (valid / skipped / invalid)
  │
  ▼
ZPL Builder ────→ encodes GS1 data, builds ^BX DataMatrix commands
  │
  ▼
TCP Client ─────→ sends ZPL over raw TCP to port 9100
  │
  ├──→ Zebra Printer (production)
  └──→ Simulator (development)
         │
         ▼
       Status Handling ──→ parses ~HS responses (paper out, head open, etc.)
```

## Features

- **GS1 DataMatrix encoding** — converts `\x1d` group separators to ZPL `_1D` and escapes underscores to `_5F`, following Zebra's `^BX` field encoding rules
- **Batch CSV processing** — reads serialization codes from CSV files with configurable column selection, delimiter, and header handling
- **CSV validation** — classifies every row as valid, skipped, or invalid with clear reasons (empty code, duplicate code, missing column, malformed escape). Bad row → skip and record reason. Bad file structure → fail early with clear message.
- **Batch result report** — clean summary after every batch: total rows, skipped, invalid, extracted, sent OK / failed, plus detailed sections for invalid rows, CSV warnings, and print failures
- **Configurable label template** — origin x/y, DataMatrix module size, quality (ECC level), orientation, label dimensions, and dpmm — all in `config.ini`, all overridable via CLI
- **Printer status parsing** — sends `~HS`, parses the three-line response into a structured `PrinterStatus` with flags for paper out, paused, head open, ribbon out, temperature, corrupt RAM, buffer full, labels remaining
- **Printer simulator** — threaded TCP server on port 9100 that accepts ZPL, responds to `~HS` and `~JA`, saves received labels to files, and supports configurable status modes (`--status paused paper_out head_open`)
- **ZPL visual verification** — renders ZPL files to PNG via the Labelary API for visual inspection before committing to real hardware
- **Structured logging** — configurable log level, module-specific loggers, clear debug output for connection attempts, retries, and print jobs
- **Retry logic** — configurable retries and delay for TCP communication with the printer
- **Dry-run mode** — outputs ZPL to console instead of sending to printer, for development and review

## Project Structure

```
datamatrix-print-service/
├── main.py              CLI entry point (print, batch, status, clear)
├── zebra_printer.py     ZebraPrinterClient, ZPL builder, GS1 encoder, status parser
├── csv_processor.py     CSV reader with row validation
├── config.py            Settings dataclass + config.ini loader
├── config.ini           Default configuration
├── simulator.py         Zebra printer simulator (TCP server)
├── render_label.py      ZPL → PNG renderer via Labelary API
└── tests/
    ├── test_zebra_printer.py    Encoding, ZPL, status parsing, TCP errors
    ├── test_csv_processor.py    CSV reading + all validation cases
    ├── test_config.py           Config loading and defaults
    ├── test_batch_result.py     Report formatting
    └── test_simulator.py        Status response builder + live TCP tests
```

## How to Run

### Install

```bash
# Python 3.11+ required, no external dependencies
git clone <repo-url>
cd datamatrix-print-service
```

### Run the Simulator

Start the simulator to develop and test without a real printer:

```bash
# Default: ready state
python simulator.py

# Simulate error states
python simulator.py --status paper_out
python simulator.py --status paused head_open ribbon_out

# Don't save ZPL files
python simulator.py --no-save

# Custom port
python simulator.py --port 9101 --log-level DEBUG
```

### Print a Single Code

```bash
# Dry-run (print ZPL to console)
python main.py print "010485000607001121ABC" --dry-run

# Send to printer/simulator
python main.py print "010485000607001121ABC"

# Override label layout
python main.py print "010485000607001121ABC" --x 100 --y 100 --module-size 4
```

### Batch Print from CSV

```bash
# Dry-run
python main.py batch codes.csv --column code --dry-run

# Send to printer
python main.py batch codes.csv --column code

# Custom delimiter and no header
python main.py batch codes.csv --column 0 --delimiter ";" --no-header

# Use a different config for small labels
python main.py --config config_small.ini batch codes.csv --column code --dry-run
```

### Check Printer Status

```bash
python main.py status
# Output: NOT READY: paper out, head open
#   Label length: 1245
```

### Clear Printer Queue

```bash
python main.py clear
```

### Render Labels to PNG

```bash
# Render a single label
python render_label.py received_labels/label_0001.zpl

# Render with config-based label dimensions
python render_label.py received_labels/label_0001.zpl --config config.ini

# Custom render settings
python render_label.py received_labels/*.zpl --dpmm 12 --width 2 --height 1
```

### Run Tests

```bash
python -m pytest tests/ -v
```

## Configuration

All parameters live in `config.ini` and can be overridden via CLI flags:

```ini
[printer]
host = 127.0.0.1       # Printer IP address
port = 9100             # ZPL raw TCP port
timeout = 5.0           # Connection timeout (seconds)
retries = 3             # Retry attempts on failure
retry_delay = 1.0       # Delay between retries (seconds)

[label]
x = 50                  # DataMatrix origin X (dots)
y = 50                  # DataMatrix origin Y (dots)
orientation = N         # N = normal, R = rotated, I = inverted, B = bottom-up
module_size = 6         # DataMatrix module size (dots per module)
quality = 200           # ECC level (0, 50, 80, 100, 140, 200)
label_width = 4.0       # Label width (inches)
label_height = 6.0      # Label height (inches)
dpmm = 8                # Dots per mm (printer resolution)

[csv]
column =                # Column name or index (empty = first column)
delimiter = ,           # CSV field delimiter
has_header = true       # Whether first row is a header

[mode]
dry_run = false         # Output ZPL to console instead of sending
log_level = INFO        # DEBUG, INFO, WARNING, ERROR
```

Create alternate configs for different label stock:

```bash
python main.py --config config_small.ini batch codes.csv --dry-run
```

## Example Workflow

A step-by-step demo from CSV to printed label:

```bash
# 1. Start the simulator
python simulator.py
# → Printer simulator started on 127.0.0.1:9100, saving ZPL to received_labels/

# 2. Check that the "printer" is ready
python main.py status
# → READY
#   Label length: 1245

# 3. Batch print from a CSV file (dry-run first)
python main.py batch sample_data.csv --column code --dry-run
# → Shows ZPL for each label + batch report

# 4. Send to the simulator for real
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

# 5. Inspect saved ZPL files
cat received_labels/label_0001.zpl
# → ^XA
#   ^FO50,50
#   ^BXN,6,200
#   ^FD0104850006070011211:e:Bp_1Dabc
#   ^FS
#   ^XZ

# 6. Render to PNG for visual verification
python render_label.py received_labels/label_0001.zpl
# → OK: label_0001.zpl -> received_labels/label_0001.png

# 7. Test error handling: restart simulator in paper-out mode
# (stop simulator with Ctrl+C, then:)
python simulator.py --status paper_out
python main.py status
# → NOT READY: paper out
```

## Why This is Interesting

### GS1 Encoding

GS1 DataMatrix barcodes use the ASCII Group Separator character (`\x1d`, decimal 29) to delimit fields inside the barcode data. This invisible character is critical for scanners to correctly parse application identifiers like GTIN, serial number, batch, and expiry date. Zebra's ZPL language represents this as `_1D` inside `^BX` field data, and underscores themselves must be escaped to `_5F`. Getting this encoding wrong means the barcode scans but the data is unparseable — a silent, costly failure in production.

### Industrial Printing

Label printers in pharmaceutical, food, and logistics environments print thousands of serialized labels per batch. Each label carries a unique code that must be traceable through the supply chain. The CSV-to-printer pipeline must be strict enough to prevent bad labels (empty codes, duplicates) while forgiving enough to continue a batch when a single row is malformed. This tool implements that balance: bad row → skip and record, bad file → fail early.

### No SDK, Raw TCP

Zebra printers speak ZPL over raw TCP on port 9100. There is no HTTP API, no REST endpoint, no vendor SDK required. You open a socket, send ASCII text, and the printer prints. Status queries (`~HS`) return a structured but cryptic three-line response that must be parsed field-by-field. This project handles the full lifecycle: connect with retries, send ZPL, parse status responses, and handle every error state the printer can report.

## Tests

95 tests covering:

- GS1 DataMatrix encoding (underscore escaping, GS character conversion)
- ZPL command builder (all parameters, validation)
- Printer status parsing (all error flags, edge cases)
- CSV processing (blank rows, duplicates, empty codes, malformed escapes, missing columns, extra columns)
- Configuration loading (defaults, partial config, full config, label dimensions)
- Batch result reporting (all report sections, properties)
- Simulator status modes (unit tests for response builder + live TCP tests for each mode)

```bash
python -m pytest tests/ -v
# 95 passed
```
