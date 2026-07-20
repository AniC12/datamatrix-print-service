"""Flask web UI for DataMatrix Print Service demo.

Provides a browser-based interface to demonstrate:
- Printer status checking
- Single label printing (with dry-run)
- Batch printing from CSV
- SPPL command visualization
- Simulator control

Usage:
    python web_ui.py [--port 5000] [--simulator]
"""

from __future__ import annotations

import argparse
import io
import logging
import subprocess
import sys
import threading
import time
from dataclasses import dataclass, field
from pathlib import Path

from flask import Flask, jsonify, render_template, request

from config import load_settings
from csv_processor import read_codes_from_csv
from savema_printer import (
    PrinterError,
    SavemaPrinterClient,
    build_modify_2d,
    encode_gs1_for_savema,
    parse_status,
)

logging.basicConfig(
    level=logging.INFO,
    format="%(asctime)s [%(levelname)s] %(name)s: %(message)s",
    datefmt="%Y-%m-%d %H:%M:%S",
)
logger = logging.getLogger(__name__)

app = Flask(__name__, template_folder="templates")

# ---------------------------------------------------------------------------
# Command log (in-memory ring buffer)
# ---------------------------------------------------------------------------

MAX_LOG_ENTRIES = 100


@dataclass
class LogEntry:
    timestamp: str
    direction: str  # "sent" or "received"
    content: str


command_log: list[dict] = []
log_lock = threading.Lock()


def add_log(direction: str, content: str) -> None:
    import datetime
    entry = {
        "timestamp": datetime.datetime.now().strftime("%H:%M:%S"),
        "direction": direction,
        "content": content,
    }
    with log_lock:
        command_log.append(entry)
        if len(command_log) > MAX_LOG_ENTRIES:
            command_log.pop(0)


# ---------------------------------------------------------------------------
# Simulator process management
# ---------------------------------------------------------------------------

simulator_process: subprocess.Popen | None = None
simulator_lock = threading.Lock()


def start_simulator(port: int = 9100) -> bool:
    global simulator_process
    with simulator_lock:
        if simulator_process and simulator_process.poll() is None:
            return True  # already running
        try:
            simulator_process = subprocess.Popen(
                [sys.executable, "savema_simulator.py", "--port", str(port)],
                cwd=str(Path(__file__).parent),
                stdout=subprocess.PIPE,
                stderr=subprocess.PIPE,
            )
            time.sleep(0.5)
            if simulator_process.poll() is not None:
                return False
            logger.info("Simulator started on port %d (PID %d)", port, simulator_process.pid)
            return True
        except Exception as exc:
            logger.error("Failed to start simulator: %s", exc)
            return False


def stop_simulator() -> bool:
    global simulator_process
    with simulator_lock:
        if simulator_process is None or simulator_process.poll() is not None:
            simulator_process = None
            return True
        simulator_process.terminate()
        try:
            simulator_process.wait(timeout=3)
        except subprocess.TimeoutExpired:
            simulator_process.kill()
        logger.info("Simulator stopped")
        simulator_process = None
        return True


def simulator_running() -> bool:
    with simulator_lock:
        return simulator_process is not None and simulator_process.poll() is None


# ---------------------------------------------------------------------------
# Helper: get a client instance from current config
# ---------------------------------------------------------------------------

def get_client() -> SavemaPrinterClient:
    cfg = load_settings()
    return SavemaPrinterClient(
        host=cfg.printer_host,
        port=cfg.printer_port,
        timeout=cfg.timeout,
        retries=1,  # faster feedback for UI
        retry_delay=0.5,
        template_name=cfg.template_name,
        datamatrix_field=cfg.datamatrix_field,
    )


# ---------------------------------------------------------------------------
# Routes
# ---------------------------------------------------------------------------

@app.route("/")
def index():
    cfg = load_settings()
    return render_template("index.html", config=cfg)


@app.route("/api/status")
def api_status():
    """Get printer status."""
    try:
        client = get_client()
        add_log("sent", "~SPPSTA^")
        status = client.get_status()
        add_log("received", status.raw)
        return jsonify({
            "connected": True,
            "state": status.state,
            "blocked": status.blocked,
            "error_message": status.error_message,
            "ready": status.ready,
            "summary": status.summary(),
        })
    except PrinterError as exc:
        return jsonify({
            "connected": False,
            "state": "DISCONNECTED",
            "blocked": False,
            "error_message": str(exc),
            "ready": False,
            "summary": f"Connection failed: {exc}",
        })


@app.route("/api/info")
def api_info():
    """Get printer info."""
    try:
        client = get_client()
        fw = client.get_firmware_version()
        sn = client.get_serial_number()
        tpl = client.get_active_template()
        cnt = client.get_current_print_count()
        return jsonify({
            "connected": True,
            "firmware": fw,
            "serial_number": sn,
            "active_template": tpl,
            "print_count": cnt,
        })
    except PrinterError as exc:
        return jsonify({
            "connected": False,
            "error": str(exc),
        })


@app.route("/api/print", methods=["POST"])
def api_print():
    """Print a single label."""
    data = request.get_json()
    code = data.get("code", "").strip()
    dry_run = data.get("dry_run", False)
    field_name = data.get("field_name", "").strip()

    if not code:
        return jsonify({"success": False, "error": "No code provided"}), 400

    cfg = load_settings()
    fname = field_name or cfg.datamatrix_field or "gs1_code"
    encoded = encode_gs1_for_savema(code)
    sppl_cmd = build_modify_2d(fname, encoded)

    add_log("sent", sppl_cmd)

    if dry_run:
        add_log("sent", "~SPPOTP^")
        return jsonify({
            "success": True,
            "dry_run": True,
            "commands": [sppl_cmd, "~SPPOTP^"],
            "encoded_data": encoded,
        })

    try:
        client = get_client()
        client.print_code(raw_code=code, field_name=fname or None)
        add_log("received", "~ SPGRES{SPMC2D:OK}^")
        add_log("received", "~ SPGRES{SPPOTP:OK}^")
        return jsonify({
            "success": True,
            "dry_run": False,
            "commands": [sppl_cmd, "~SPPOTP^"],
            "encoded_data": encoded,
        })
    except PrinterError as exc:
        add_log("received", f"ERROR: {exc}")
        return jsonify({"success": False, "error": str(exc)}), 500


@app.route("/api/batch", methods=["POST"])
def api_batch():
    """Process a batch CSV and print (or dry-run)."""
    dry_run = request.form.get("dry_run", "false").lower() == "true"
    field_name = request.form.get("field_name", "").strip()

    if "csv_file" not in request.files:
        return jsonify({"success": False, "error": "No CSV file uploaded"}), 400

    file = request.files["csv_file"]
    if not file.filename:
        return jsonify({"success": False, "error": "No file selected"}), 400

    # Save to temp and process
    content = file.read().decode("utf-8", errors="replace")
    temp_path = Path(__file__).parent / "received_labels" / file.filename
    temp_path.parent.mkdir(exist_ok=True)
    temp_path.write_text(content, encoding="utf-8")

    cfg = load_settings()
    fname = field_name or cfg.datamatrix_field or "gs1_code"
    col = cfg.csv_column or None
    if col and col.isdigit():
        col = int(col)

    csv_result = read_codes_from_csv(
        file_path=str(temp_path),
        column=col,
        delimiter=cfg.csv_delimiter,
        skip_header=cfg.csv_has_header,
    )

    results = []
    sent_ok = 0
    failed = []

    for i, code in enumerate(csv_result.codes, 1):
        encoded = encode_gs1_for_savema(code)
        sppl_cmd = build_modify_2d(fname, encoded)
        add_log("sent", sppl_cmd)

        if dry_run:
            add_log("sent", "~SPPOTP^")
            results.append({
                "index": i,
                "code": code[:60],
                "command": sppl_cmd,
                "status": "dry-run",
            })
        else:
            try:
                client = get_client()
                client.print_code(raw_code=code, field_name=fname)
                add_log("received", "~ SPGRES{SPMC2D:OK}^")
                results.append({
                    "index": i,
                    "code": code[:60],
                    "command": sppl_cmd,
                    "status": "sent",
                })
                sent_ok += 1
            except PrinterError as exc:
                add_log("received", f"ERROR: {exc}")
                results.append({
                    "index": i,
                    "code": code[:60],
                    "command": sppl_cmd,
                    "status": f"failed: {exc}",
                })
                failed.append({"index": i, "error": str(exc)})

    return jsonify({
        "success": True,
        "dry_run": dry_run,
        "total_rows": csv_result.total_rows,
        "codes_extracted": len(csv_result.codes),
        "skipped_rows": csv_result.skipped_rows,
        "invalid_rows": len(csv_result.invalid_rows),
        "sent_ok": sent_ok,
        "failed": len(failed),
        "results": results,
        "warnings": csv_result.warnings,
    })


@app.route("/api/log")
def api_log():
    """Get the command log."""
    with log_lock:
        return jsonify({"entries": list(command_log)})


@app.route("/api/log/clear", methods=["POST"])
def api_log_clear():
    """Clear the command log."""
    with log_lock:
        command_log.clear()
    return jsonify({"success": True})


@app.route("/api/simulator/start", methods=["POST"])
def api_simulator_start():
    cfg = load_settings()
    ok = start_simulator(port=cfg.printer_port)
    return jsonify({"success": ok, "running": simulator_running()})


@app.route("/api/simulator/stop", methods=["POST"])
def api_simulator_stop():
    ok = stop_simulator()
    return jsonify({"success": ok, "running": simulator_running()})


@app.route("/api/simulator/status")
def api_simulator_status():
    return jsonify({"running": simulator_running()})


@app.route("/api/config")
def api_config():
    """Get current configuration."""
    cfg = load_settings()
    return jsonify({
        "printer_host": cfg.printer_host,
        "printer_port": cfg.printer_port,
        "template_name": cfg.template_name,
        "datamatrix_field": cfg.datamatrix_field,
        "csv_column": cfg.csv_column,
        "csv_delimiter": cfg.csv_delimiter,
        "dry_run": cfg.dry_run,
    })


# ---------------------------------------------------------------------------
# Entry point
# ---------------------------------------------------------------------------

def main():
    parser = argparse.ArgumentParser(description="DataMatrix Print Service - Web UI")
    parser.add_argument("--port", type=int, default=5000, help="Web UI port (default: 5000)")
    parser.add_argument("--simulator", action="store_true", help="Auto-start simulator on launch")
    args = parser.parse_args()

    if args.simulator:
        cfg = load_settings()
        start_simulator(port=cfg.printer_port)

    print(f"\n  DataMatrix Print Service - Web UI")
    print(f"  http://127.0.0.1:{args.port}")
    print(f"  Press Ctrl+C to stop\n")

    app.run(host="127.0.0.1", port=args.port, debug=False)


if __name__ == "__main__":
    main()
