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

        try:
            data = b""
            while True:
                chunk = self.request.recv(4096)
                if not chunk:
                    break
                data += chunk
                # SPPL commands end with ^, check if we have at least one
                if b"^" in data:
                    break
        except OSError:
            return

        if not data:
            return

        raw = data.decode("utf-8", errors="replace")
        logger.info("Received from %s: %s", self.client_address[0], raw[:200])

        # Find all SPPL commands in the data (supports chained commands with |)
        # First, normalize: the raw string may use | to separate commands
        # but they share ~ and ^ framing
        responses = []
        commands = CMD_PATTERN.findall(raw)

        for cmd_name, params in commands:
            resp = self._handle_command(cmd_name, params, state, server)
            if resp:
                responses.append(resp)

        if responses:
            full_response = "".join(responses)
            self.request.sendall(full_response.encode("utf-8"))
            logger.info("Sent: %s", full_response[:200])

    def _handle_command(
        self,
        cmd: str,
        params: str,
        state: PrinterState,
        server: SPPLSimulator,
    ) -> str:
        """Process a single SPPL command and return the response string."""

        # ---- STATUS ----
        if cmd == "SPPSTA":
            logger.info("Status query received")
            return build_response("SPPSTA", state.get_status_response())

        # ---- TEMPLATE MANAGEMENT ----
        if cmd == "SPLLTF":
            template = params.strip()
            if template in state.stored_templates:
                state.active_template = template
                state.current_print_count = 0
                logger.info("Template loaded: %s", template)
                return build_response("SPLLTF", "OK")
            logger.warning("Template not found: %s", template)
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
                logger.info("2D barcode '%s' set to: %s", field_name, value[:60])
                return build_response("SPMC2D", "OK")
            return build_response("SPMC2D", "FAIL")

        if cmd == "SPMCTV":
            parts = params.split("~gt~", 1)
            if len(parts) == 2:
                field_name, value = parts[0].strip(), parts[1].strip()
                state.field_values[field_name] = value
                logger.info("Text '%s' set to: %s", field_name, value[:60])
                return build_response("SPMCTV", "OK")
            return build_response("SPMCTV", "FAIL")

        if cmd == "SPMCBV":
            parts = params.split("~gt~", 1)
            if len(parts) == 2:
                field_name, value = parts[0].strip(), parts[1].strip()
                state.field_values[field_name] = value
                logger.info("Barcode '%s' set to: %s", field_name, value[:60])
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
                    logger.info("Field '%s' set to: %s", fname, fval[:60])
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
                logger.info(
                    "Queued %d values for '%s' (total: %d)",
                    len(values), field_name, len(state.queues[field_name]),
                )
                return build_response("SPLAQD", "OK")
            return build_response("SPLAQD", "FAIL")

        if cmd == "SPLGQC":
            field_name = params.strip()
            count = len(state.queues.get(field_name, []))
            return build_response("SPLGQC", str(count))

        if cmd == "SPLCQD":
            field_name = params.strip()
            state.queues.pop(field_name, None)
            logger.info("Queue cleared for '%s'", field_name)
            return build_response("SPLCQD", "OK")

        if cmd == "SPLCDB":
            state.queues.clear()
            state.field_values.clear()
            logger.info("Data buffer cleared")
            return build_response("SPLCDB", "OK")

        # ---- PRINT ----
        if cmd == "SPPSAP":
            if state.status == "WAITING":
                state.status = "RUNNING"
                logger.info("Automatic printing started")
                return build_response("SPPSAP", "OK")
            return build_response("SPPSAP", "FAIL")

        if cmd == "SPPSTP":
            if state.status == "RUNNING":
                state.status = "WAITING"
                logger.info("Printing stopped")
                return build_response("SPPSTP", "OK")
            return build_response("SPPSTP", "FAIL")

        if cmd == "SPPOTP":
            state.label_count += 1
            state.current_print_count += 1
            state.total_print_count += 1
            logger.info(
                "Test print #%d (fields: %s)",
                state.label_count,
                {k: v[:30] for k, v in state.field_values.items()},
            )
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
                logger.info("Limited print count set to %d", state.limited_print_count)
                return build_response("SPPSLQ", "OK")
            except ValueError:
                return build_response("SPPSLQ", "FAIL")

        if cmd == "SPPGLQ":
            return build_response("SPPGLQ", str(state.limited_print_count))

        # ---- DATA FILES ----
        if cmd == "SPLCDF":
            parts = params.split("~gt~", 1)
            if len(parts) == 2:
                filename = parts[0].strip()
                state.data_files.append(filename)
                logger.info("Data file created: %s", filename)
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
                logger.info("Template created and loaded: %s", tname)
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
            logger.info("User message: %s", params)
            return build_response("SPGSUM", "OK")

        if cmd == "SPGSLI":
            return build_response("SPGSLI", "OK")

        if cmd == "SPGGLI":
            return build_response("SPGGLI", "0")

        # ---- UNKNOWN ----
        logger.warning("Unknown command: %s", cmd)
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
