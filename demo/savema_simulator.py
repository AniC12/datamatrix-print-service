"""Savema SPPL protocol simulator for development and testing.

Emulates a Savema thermal transfer overprinter responding to SPPL commands
over TCP. Supports template management, modification commands, print
commands, and status queries.

Usage:
    python savema_simulator.py [--port 9100] [--status ready|error]
    python savema_simulator.py --status error --error-message "Ribbon not found"
"""

from __future__ import annotations

import argparse
import logging
import os
import re
import signal
import socketserver
import sys
import threading
from dataclasses import dataclass, field
from pathlib import Path

logging.basicConfig(
    level=logging.INFO,
    format="%(asctime)s [%(levelname)s] %(name)s: %(message)s",
    datefmt="%Y-%m-%d %H:%M:%S",
)
logger = logging.getLogger("simulator")

# SPPL command pattern: ~COMMAND{params}^ or ~COMMAND^
CMD_PATTERN = re.compile(r"~\s*([A-Z0-9]+)(?:\{([^}]*)\})?\^")

# Human-readable command descriptions for log output
CMD_DESCRIPTIONS: dict[str, str] = {
    "SPPSTA": "query status",
    "SPLLTF": "load/activate template",
    "SPLGAT": "get active template",
    "SPLGST": "list stored templates",
    "SPLGFN": "get field names",
    "SPLGFV": "get field value",
    "SPLRTF": "upload template (.rox)",
    "SPLTDS": "upload template (XML)",
    "SPLDTF": "delete template",
    "SPLDTA": "delete all templates",
    "SPMC2D": "modify 2D barcode",
    "SPMCTV": "modify text value",
    "SPMCBV": "modify 1D barcode",
    "SPMCSV": "modify multiple values",
    "SPLAQD": "append queue data",
    "SPLGQC": "get queue count",
    "SPLCQD": "clear queue",
    "SPLCDB": "clear data buffer",
    "SPPSAP": "start auto print",
    "SPPSTP": "stop print",
    "SPPOTP": "one test print",
    "SPPSLQ": "set print quantity",
    "SPPGLQ": "get remaining quantity",
    "SPLCDF": "upload CSV data",
    "SPLGSD": "list stored data files",
    "SPLDDF": "delete data file",
    "SPLDDA": "delete all data files",
    "SPGGCP": "get current counter",
    "SPGGTP": "get total/lifetime counter",
    "SPGGFV": "get firmware version",
    "SPGGSN": "get serial number",
    "SPGGRR": "get remaining ribbon",
    "SPCGNC": "get network config",
    "SPGSUM": "send user message",
    "SPGSLI": "lock interface",
    "SPGGLI": "get lock state",
}


def cmd_label(cmd: str) -> str:
    """Return 'COMMAND (description)' for log readability."""
    desc = CMD_DESCRIPTIONS.get(cmd)
    return f"{cmd} ({desc})" if desc else cmd


def build_response(cmd: str, result: str) -> str:
    """Build an SPGRES response string."""
    return f"~ SPGRES{{{cmd}:{result}}}^"


@dataclass
class PrinterState:
    """Simulated printer state."""
    # Status
    status: str = "WAITING"          # INIT, WAITING, RUNNING, ERROR
    blocked: bool = False
    error_message: str = ""

    # Templates
    active_template: str = ""
    stored_templates: list = field(default_factory=list)

    # Data
    queues: dict = field(default_factory=dict)   # field_name -> [values]
    field_values: dict = field(default_factory=dict)  # field_name -> current_value
    data_files: list = field(default_factory=list)

    # Counters
    current_print_count: int = 0
    total_print_count: int = 0
    limited_print_count: int = 0

    # Info
    firmware_version: str = "6.3.001.600.R"
    serial_number: str = "SIM00001"
    remaining_ribbon: int = 95

    # Network
    ip_address: str = "127.0.0.1"
    subnet_mask: str = "255.255.255.0"
    gateway: str = "127.0.0.1"
    port: int = 9100

    # Output
    output_dir: Path | None = None
    label_count: int = 0

    # Auto-print simulation
    auto_print_speed: float = 0.5    # seconds per label when RUNNING
    _print_thread: threading.Thread | None = field(default=None, repr=False)
    _stop_print: threading.Event = field(default_factory=threading.Event, repr=False)

    def start_auto_print(self) -> None:
        """Start a background thread that increments counters while RUNNING."""
        self._stop_print.clear()
        self._print_thread = threading.Thread(target=self._auto_print_loop, daemon=True)
        self._print_thread.start()

    def stop_auto_print(self) -> None:
        """Stop the auto-print thread."""
        self._stop_print.set()
        if self._print_thread:
            self._print_thread.join(timeout=2)
            self._print_thread = None

    def _auto_print_loop(self) -> None:
        """Background loop: increment counter until limited_print_count is reached."""
        while not self._stop_print.is_set():
            if self.status != "RUNNING":
                break
            if self.limited_print_count > 0 and self.current_print_count >= self.limited_print_count:
                self.status = "WAITING"
                logger.info("Auto-print finished: %d/%d", self.current_print_count, self.limited_print_count)
                break
            self._stop_print.wait(self.auto_print_speed)
            if self._stop_print.is_set():
                break
            self.current_print_count += 1
            self.total_print_count += 1
            self.label_count += 1
            logger.debug("Auto-print: counter=%d/%d", self.current_print_count, self.limited_print_count)

    def get_status_response(self) -> str:
        """Build SPPSTA response based on current state."""
        if self.blocked:
            if self.status == "ERROR":
                tail = f"BLOCKED {self.error_message}"
            else:
                tail = "BLOCKED"
        else:
            tail = self.error_message if self.status == "ERROR" else ""
        return f"{self.status}<{tail}"


class SPPLRequestHandler(socketserver.BaseRequestHandler):
    """Handles a single TCP connection with SPPL commands."""

    def handle(self) -> None:
        server: SPPLSimulator = self.server  # type: ignore[assignment]
        state = server.state
        logger.info("Client connected: %s", self.client_address[0])

        try:
            while True:
                data = b""
                while True:
                    chunk = self.request.recv(4096)
                    if not chunk:
                        logger.info("Client disconnected: %s", self.client_address[0])
                        return
                    data += chunk
                    if b"^" in data:
                        break

                raw = data.decode("utf-8", errors="replace")

                responses = []
                commands = CMD_PATTERN.findall(raw)

                for cmd_name, params in commands:
                    resp = self._handle_command(cmd_name, params, state, server)
                    if resp:
                        responses.append(resp)

                if responses:
                    full_response = "".join(responses)
                    self.request.sendall(full_response.encode("utf-8"))
        except OSError:
            logger.info("Connection closed: %s", self.client_address[0])

    def _handle_command(
        self,
        cmd: str,
        params: str,
        state: PrinterState,
        server: SPPLSimulator,
    ) -> str:
        """Process a single SPPL command and return the response string."""
        label = cmd_label(cmd)
        param_summary = f" {{{params[:80]}}}" if params else ""
        logger.info("← RX  %s%s", label, param_summary)

        result = self._dispatch(cmd, params, state, server)

        # Log the response with the same label
        # Extract just the result payload from the SPGRES wrapper for readability
        logger.info("→ TX  %s  ⇒  %s", label, result.split(":", 1)[-1].rstrip("}^") if ":" in result else result[:80])
        return result

    def _dispatch(
        self,
        cmd: str,
        params: str,
        state: PrinterState,
        server: SPPLSimulator,
    ) -> str:
        """Route a single SPPL command to its handler."""

        # ---- BLOCKED CHECK ----
        # Per §5.2: when BLOCKED, all commands except SPPSTA return FAIL
        if state.blocked and cmd != "SPPSTA":
            logger.warning("  BLOCKED — rejecting")
            return build_response(cmd, "FAIL")

        # ---- STATUS ----
        if cmd == "SPPSTA":
            return build_response("SPPSTA", state.get_status_response())

        # ---- TEMPLATE MANAGEMENT ----
        if cmd == "SPLLTF":
            # Stop Position only (§5.2)
            if state.status != "WAITING":
                logger.warning("  rejected: not in WAITING state (is %s)", state.status)
                return build_response("SPLLTF", "FAIL")
            template = params.strip()
            if template in state.stored_templates:
                state.active_template = template
                state.current_print_count = 0
                logger.info("  counter reset to 0, active='%s'", template)
                return build_response("SPLLTF", "OK")
            logger.warning("  template not found: %s", template)
            return build_response("SPLLTF", "FAIL")

        if cmd == "SPLGAT":
            return build_response("SPLGAT", state.active_template)

        if cmd == "SPLGST":
            return build_response("SPLGST", "<".join(state.stored_templates))

        if cmd == "SPLGFN":
            # Return field names for the template
            # In simulation, return some default fields
            return build_response("SPLGFN", "gs1_code<batch_txt<date_txt")

        if cmd == "SPLGFV":
            field_name = params.strip()
            value = state.field_values.get(field_name, "")
            return build_response("SPLGFV", value)

        # ---- MODIFICATION ----
        if cmd == "SPMC2D":
            parts = params.split("~gt~", 1)
            if len(parts) == 2:
                field_name, value = parts[0].strip(), parts[1].strip()
                state.field_values[field_name] = value
                return build_response("SPMC2D", "OK")
            return build_response("SPMC2D", "FAIL")

        if cmd == "SPMCTV":
            parts = params.split("~gt~", 1)
            if len(parts) == 2:
                field_name, value = parts[0].strip(), parts[1].strip()
                state.field_values[field_name] = value
                return build_response("SPMCTV", "OK")
            return build_response("SPMCTV", "FAIL")

        if cmd == "SPMCBV":
            parts = params.split("~gt~", 1)
            if len(parts) == 2:
                field_name, value = parts[0].strip(), parts[1].strip()
                state.field_values[field_name] = value
                return build_response("SPMCBV", "OK")
            return build_response("SPMCBV", "FAIL")

        if cmd == "SPMCSV":
            # Multiple field updates: name1~gt~val1~gt~name2~gt~val2
            parts = params.split("~gt~")
            if len(parts) >= 2 and len(parts) % 2 == 0:
                for i in range(0, len(parts), 2):
                    fname = parts[i].strip()
                    fval = parts[i + 1].strip()
                    state.field_values[fname] = fval
                return build_response("SPMCSV", "OK")
            return build_response("SPMCSV", "FAIL")

        # ---- QUEUE ----
        if cmd == "SPLAQD":
            parts = params.split("~gt~", 1)
            if len(parts) == 2:
                field_name = parts[0].strip()
                values = parts[1].strip().split("\n")
                if field_name not in state.queues:
                    state.queues[field_name] = []
                state.queues[field_name].extend(values)
                logger.info("  queued %d values for '%s' (total: %d)",
                    len(values), field_name, len(state.queues[field_name]))
                return build_response("SPLAQD", "OK")
            return build_response("SPLAQD", "FAIL")

        if cmd == "SPLGQC":
            field_name = params.strip()
            count = len(state.queues.get(field_name, []))
            return build_response("SPLGQC", str(count))

        if cmd == "SPLCQD":
            field_name = params.strip()
            state.queues.pop(field_name, None)
            return build_response("SPLCQD", "OK")

        if cmd == "SPLCDB":
            state.queues.clear()
            state.field_values.clear()
            return build_response("SPLCDB", "OK")

        # ---- PRINT ----
        if cmd == "SPPSAP":
            if state.status == "WAITING":
                state.status = "RUNNING"
                state.start_auto_print()
                logger.info("  printing started (qty=%d)", state.limited_print_count)
                return build_response("SPPSAP", "OK")
            return build_response("SPPSAP", "FAIL")

        if cmd == "SPPSTP":
            if state.status == "RUNNING":
                state.stop_auto_print()
                state.status = "WAITING"
                logger.info("  stopped at counter=%d", state.current_print_count)
                return build_response("SPPSTP", "OK")
            return build_response("SPPSTP", "FAIL")

        if cmd == "SPPOTP":
            state.label_count += 1
            state.current_print_count += 1
            state.total_print_count += 1
            # Save label data to file if output dir is set
            if state.output_dir:
                filepath = state.output_dir / f"label_{state.label_count:04d}.txt"
                lines = [f"{k}={v}" for k, v in state.field_values.items()]
                filepath.write_text("\n".join(lines), encoding="utf-8")
                logger.info("Saved label data to %s", filepath)
            return build_response("SPPOTP", "OK")

        if cmd == "SPPSLQ":
            try:
                state.limited_print_count = int(params.strip())
                return build_response("SPPSLQ", "OK")
            except ValueError:
                return build_response("SPPSLQ", "FAIL")

        if cmd == "SPPGLQ":
            # Return remaining quantity (§5.3: quantity - SPPGLQ == SPGGCP)
            remaining = max(0, state.limited_print_count - state.current_print_count)
            return build_response("SPPGLQ", str(remaining))

        # ---- DATA FILES ----
        if cmd == "SPLCDF":
            # Stop Position only (§5.2)
            if state.status != "WAITING":
                logger.warning("  rejected: not in WAITING state")
                return build_response("SPLCDF", "FAIL")
            parts = params.split("~gt~", 1)
            if len(parts) == 2:
                filename = parts[0].strip()
                # Overwrite if already exists (prevent duplicates in list)
                if filename not in state.data_files:
                    state.data_files.append(filename)
                return build_response("SPLCDF", "OK")
            return build_response("SPLCDF", "FAIL")

        if cmd == "SPLGSD":
            return build_response("SPLGSD", "<".join(state.data_files))

        if cmd == "SPLDDF":
            filename = params.strip()
            if filename in state.data_files:
                state.data_files.remove(filename)
                return build_response("SPLDDF", "OK")
            return build_response("SPLDDF", "FAIL")

        if cmd == "SPLDDA":
            state.data_files.clear()
            return build_response("SPLDDA", "OK")

        # ---- TEMPLATE UPLOAD (binary .rox via base64) ----
        if cmd == "SPLRTF":
            # Stop Position only (§5.2)
            if state.status != "WAITING":
                logger.warning("  rejected: not in WAITING state")
                return build_response("SPLRTF", "FAIL")
            # Format: name>base64data
            parts = params.split(">", 1)
            if len(parts) == 2:
                tname = parts[0].strip()
                if tname not in state.stored_templates:
                    state.stored_templates.append(tname)
                logger.info("  uploaded '%s' (%d bytes)", tname, len(parts[1]))
                return build_response("SPLRTF", "OK")
            return build_response("SPLRTF", "FAIL")

        # ---- TEMPLATE DESIGN ----
        if cmd == "SPLTDS":
            # Extract template name from XML
            name_match = re.search(r"<Name>([^<]+)</Name>", params)
            if name_match:
                tname = name_match.group(1)
                if tname not in state.stored_templates:
                    state.stored_templates.append(tname)
                state.active_template = tname
                state.current_print_count = 0
                return build_response("SPLTDS", "OK")
            return build_response("SPLTDS", "FAIL")

        if cmd == "SPLDTF":
            tname = params.strip()
            if tname in state.stored_templates:
                state.stored_templates.remove(tname)
                if state.active_template == tname:
                    state.active_template = ""
                return build_response("SPLDTF", "OK")
            return build_response("SPLDTF", "FAIL")

        if cmd == "SPLDTA":
            state.stored_templates.clear()
            state.active_template = ""
            return build_response("SPLDTA", "OK")

        # ---- GENERAL ----
        if cmd == "SPGGCP":
            return build_response("SPGGCP", str(state.current_print_count))

        if cmd == "SPGGTP":
            return build_response("SPGGTP", str(state.total_print_count))

        if cmd == "SPGGFV":
            return build_response("SPGGFV", state.firmware_version)

        if cmd == "SPGGSN":
            return build_response("SPGGSN", state.serial_number)

        if cmd == "SPGGRR":
            return build_response("SPGGRR", str(state.remaining_ribbon))

        if cmd == "SPCGNC":
            nc = f"{state.ip_address}<{state.subnet_mask}<{state.gateway}<{state.port}"
            return build_response("SPCGNC", nc)

        if cmd == "SPGSUM":
            return build_response("SPGSUM", "OK")

        if cmd == "SPGSLI":
            return build_response("SPGSLI", "OK")

        if cmd == "SPGGLI":
            return build_response("SPGGLI", "0")

        # ---- UNKNOWN ----
        logger.warning("  unknown command")
        return build_response(cmd, "FAIL")


class SPPLSimulator(socketserver.ThreadingTCPServer):
    """Threaded TCP server simulating a Savema printer."""
    allow_reuse_address = True
    daemon_threads = True

    def __init__(
        self,
        address: tuple[str, int],
        state: PrinterState | None = None,
    ) -> None:
        super().__init__(address, SPPLRequestHandler)
        self.state = state or PrinterState()


def main() -> None:
    parser = argparse.ArgumentParser(
        description="Savema SPPL protocol simulator"
    )
    parser.add_argument(
        "--port", type=int, default=9100,
        help="TCP port to listen on (default: 9100)",
    )
    parser.add_argument(
        "--status", default="ready",
        choices=["ready", "running", "init", "error"],
        help="Initial printer status mode (default: ready)",
    )
    parser.add_argument(
        "--error-message", default="Ribbon not found.Please insert ribbon",
        help="Error message when status is 'error'",
    )
    parser.add_argument(
        "--blocked", action="store_true",
        help="Simulate operator not on main window (BLOCKED state)",
    )
    parser.add_argument(
        "--templates", nargs="*", default=["gs1label_32.rox"],
        help="Pre-loaded template names (default: gs1label_32.rox)",
    )
    parser.add_argument(
        "--output-dir", default="received_labels",
        help="Directory to save label data (default: received_labels)",
    )
    args = parser.parse_args()

    # Map status names
    status_map = {
        "ready": "WAITING",
        "running": "RUNNING",
        "init": "INIT",
        "error": "ERROR",
    }

    state = PrinterState(
        status=status_map[args.status],
        blocked=args.blocked,
        error_message=args.error_message if args.status == "error" else "",
        stored_templates=list(args.templates),
        active_template=args.templates[0] if args.templates else "",
        port=args.port,
    )

    # Create output directory
    output_dir = Path(args.output_dir)
    output_dir.mkdir(exist_ok=True)
    state.output_dir = output_dir

    server = SPPLSimulator(("0.0.0.0", args.port), state)

    logger.info("Savema SPPL Simulator listening on port %d", args.port)
    logger.info("Status: %s", state.status)
    if state.blocked:
        logger.info("BLOCKED mode active")
    logger.info("Templates: %s", state.stored_templates)
    logger.info("Active template: %s", state.active_template)
    logger.info("Press Ctrl+C to stop")

    def _shutdown(*_: object) -> None:
        logger.info("Received SIGINT, shutting down...")
        threading.Thread(target=server.shutdown, daemon=True).start()

    signal.signal(signal.SIGINT, _shutdown)

    server.serve_forever()
    logger.info(
        "Simulator stopped. %d labels received.", state.label_count
    )


if __name__ == "__main__":
    main()
