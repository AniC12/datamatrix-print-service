"""Savema TTO printer client using SPPL (Savema Printer Programming Language).

Communicates with Savema thermal transfer overprinters via TCP/Ethernet
or RS-232 using the SPPL command protocol (Rev.12).

Unlike Zebra printers (which accept full label layouts per print), Savema
printers use pre-designed templates stored on the printer. Variable data
(text, barcodes, 2D barcodes) is injected into named template fields via
modification commands such as SPMC2D.

Typical workflow:
    1. Design template in Sayasis S20 with Source=External fields
    2. Upload template to printer (.rox file)
    3. Use this client to inject GS1 data via SPMC2D and trigger prints
"""

from __future__ import annotations

import logging
import re
import socket
import time
from dataclasses import dataclass, field

logger = logging.getLogger(__name__)

DEFAULT_PORT = 9100

# SPPL framing characters
CMD_START = "~"
CMD_END = "^"
CMD_SEP = "|"
PARAM_START = "{"
PARAM_END = "}"
SET_SEP = ">"         # separator for set-command parameters
GET_SEP = "<"         # separator in get-response values
MOD_SEP = "~gt~"     # separator for modification commands
CSV_SEP = "~sc~"     # separator for CSV column data

# XML entity escapes required in template/data content
XML_ESCAPES = {
    '"': "&quot;",
    "'": "&apos;",
    "<": "&lt;",
    ">": "&gt;",
    "&": "&amp;",
}

# Response pattern: ~ SPGRES{COMMAND:RESULT}^
RESPONSE_PATTERN = re.compile(
    r"~\s*SPGRES\{([^:}]+):([^}]*)\}\^", re.DOTALL
)


class PrinterError(Exception):
    pass


# ---------------------------------------------------------------------------
# Status
# ---------------------------------------------------------------------------

@dataclass
class PrinterStatus:
    """Parsed Savema printer status from SPPSTA response."""
    raw: str = ""
    state: str = ""           # INIT, WAITING, RUNNING, ERROR
    blocked: bool = False     # operator not on main window
    error_message: str = ""
    errors: list = field(default_factory=list)

    @property
    def ready(self) -> bool:
        return self.state == "WAITING" and not self.blocked

    @property
    def running(self) -> bool:
        return self.state == "RUNNING"

    def summary(self) -> str:
        parts = []
        if self.state == "ERROR":
            parts.append(f"ERROR: {self.error_message}")
        elif self.state:
            parts.append(self.state)
        if self.blocked:
            parts.append("(BLOCKED)")
        if self.errors:
            parts.append(f"parse warnings: {self.errors}")
        return " ".join(parts) if parts else "UNKNOWN"


def parse_status(raw: str) -> PrinterStatus:
    """Parse a SPPSTA response into a PrinterStatus.

    Possible responses:
        ~ SPGRES{SPPSTA:INIT<}^
        ~ SPGRES{SPPSTA:WAITING<}^
        ~ SPGRES{SPPSTA:RUNNING<}^
        ~ SPGRES{SPPSTA:ERROR<Ribbon not found.Please insert ribbon}^
        ~ SPGRES{SPPSTA:WAITING<BLOCKED}^
        ~ SPGRES{SPPSTA:ERROR<BLOCKED Ribbon not found...}^
    """
    status = PrinterStatus(raw=raw)
    m = RESPONSE_PATTERN.search(raw)
    if not m:
        status.errors.append("Could not parse SPGRES response")
        return status
    cmd_name = m.group(1).strip()
    result = m.group(2).strip()
    if cmd_name != "SPPSTA":
        status.errors.append(f"Expected SPPSTA, got {cmd_name}")

    # Split on first '<'
    parts = result.split(GET_SEP, 1)
    status.state = parts[0].strip()
    tail = parts[1].strip() if len(parts) > 1 else ""

    if tail.startswith("BLOCKED"):
        status.blocked = True
        remainder = tail[len("BLOCKED"):].strip()
        if remainder:
            status.error_message = remainder
    elif tail:
        status.error_message = tail

    return status


# ---------------------------------------------------------------------------
# Response parsing helpers
# ---------------------------------------------------------------------------

def parse_response(raw: str) -> tuple[str, str]:
    """Parse a generic SPGRES response, returning (command, result).

    Returns ('', '') if parsing fails.
    """
    m = RESPONSE_PATTERN.search(raw)
    if not m:
        return ("", "")
    return (m.group(1).strip(), m.group(2).strip())


def is_ok(raw: str) -> bool:
    """Check if a response indicates success."""
    _, result = parse_response(raw)
    return result == "OK"


# ---------------------------------------------------------------------------
# SPPL command builders
# ---------------------------------------------------------------------------

def build_command(cmd: str, params: str | None = None) -> str:
    """Build a single SPPL command string."""
    if params is not None:
        return f"{CMD_START}{cmd}{PARAM_START}{params}{PARAM_END}{CMD_END}"
    return f"{CMD_START}{cmd}{CMD_END}"


def build_modify_2d(field_name: str, value: str) -> str:
    """Build SPMC2D command to change a 2D barcode field value."""
    return build_command("SPMC2D", f"{field_name}{MOD_SEP}{value}")


def build_modify_text(field_name: str, value: str) -> str:
    """Build SPMCTV command to change a text field value."""
    return build_command("SPMCTV", f"{field_name}{MOD_SEP}{value}")


def build_modify_barcode(field_name: str, value: str) -> str:
    """Build SPMCBV command to change a 1D barcode field value."""
    return build_command("SPMCBV", f"{field_name}{MOD_SEP}{value}")


def build_modify_selected(pairs: list[tuple[str, str]]) -> str:
    """Build SPMCSV command to change multiple field values at once.

    pairs: list of (field_name, value) tuples
    """
    parts = MOD_SEP.join(f"{name}{MOD_SEP}{val}" for name, val in pairs)
    return build_command("SPMCSV", parts)


def build_load_template(template_name: str) -> str:
    """Build SPLLTF command to load a template from printer storage."""
    return build_command("SPLLTF", template_name)


def build_queue_data(field_name: str, values: list[str]) -> str:
    """Build SPLAQD command to append queue data for batch printing.

    Values are joined with newlines as per SPPL spec.
    """
    data = "\n".join(values)
    return build_command("SPLAQD", f"{field_name}{MOD_SEP}{data}")


def build_chain(*commands: str) -> str:
    """Chain multiple SPPL commands with | separator.

    Input commands should be fully formed (with ~ and ^).
    This strips the outer ~ and ^ and joins with |.
    """
    stripped = []
    for cmd in commands:
        c = cmd.strip()
        if c.startswith(CMD_START):
            c = c[1:]
        if c.endswith(CMD_END):
            c = c[:-1]
        stripped.append(c)
    return CMD_START + CMD_SEP.join(stripped) + CMD_END


# ---------------------------------------------------------------------------
# GS1 encoding
# ---------------------------------------------------------------------------

GS_CHARACTER = "\x1d"

def encode_gs1_for_savema(raw_code: str) -> str:
    """Encode a GS1 data string for use in Savema SPMC2D commands.

    The SPPL manual states DataMatrix accepts standard ASCII (0x20-0x7E).
    The GS character (0x1D) is a control character outside this range.

    Strategy: send the raw GS character as-is and let the printer's
    GS1-DataMatrix engine interpret it. If this fails on the real printer,
    this function should be updated to use the correct escape sequence.

    The \\x1d literal escape (from CLI input) is converted to the actual
    GS byte, same as the Zebra version.

    NOTE: XML-reserved characters in the data must be escaped per the
    SPPL character limitations table.
    """
    # Convert literal \x1d from CLI to actual GS byte
    result = raw_code.replace("\\x1d", GS_CHARACTER)
    # Escape XML-reserved characters per SPPL spec (& must be first)
    result = result.replace("&", "&amp;")
    result = result.replace('"', "&quot;")
    result = result.replace("'", "&apos;")
    result = result.replace("<", "&lt;")
    result = result.replace(">", "&gt;")
    return result


# ---------------------------------------------------------------------------
# Client
# ---------------------------------------------------------------------------

@dataclass
class SavemaPrinterClient:
    """SPPL client for Savema thermal transfer overprinters.

    Communicates over TCP (Ethernet) using the SPPL protocol.
    """
    host: str
    port: int = DEFAULT_PORT
    timeout: float = 5.0
    retries: int = 3
    retry_delay: float = 1.0

    # Template configuration
    template_name: str = ""
    datamatrix_field: str = ""

    def send_command(
        self,
        command: str,
        expect_response: bool = True,
        response_size: int = 4096,
    ) -> str:
        """Send an SPPL command and optionally read the response.

        All SPPL commands return a response (OK/FAIL/data), so
        expect_response defaults to True (unlike Zebra).
        """
        payload = command.encode("utf-8")
        last_error: Exception = Exception("No connection attempt made")
        for attempt in range(1, self.retries + 1):
            try:
                logger.debug(
                    "Connecting to %s:%d (attempt %d/%d)",
                    self.host, self.port, attempt, self.retries,
                )
                with socket.create_connection(
                    (self.host, self.port), timeout=self.timeout
                ) as conn:
                    conn.sendall(payload)
                    logger.debug("Sent %d bytes: %s", len(payload), command[:80])
                    if not expect_response:
                        return ""
                    conn.shutdown(socket.SHUT_WR)
                    chunks: list[bytes] = []
                    while True:
                        chunk = conn.recv(response_size)
                        if not chunk:
                            break
                        chunks.append(chunk)
                    response = b"".join(chunks).decode("utf-8", errors="replace")
                    logger.debug("Received: %s", response[:200])
                    return response
            except OSError as exc:
                last_error = exc
                logger.warning(
                    "Connection attempt %d/%d failed: %s",
                    attempt, self.retries, exc,
                )
                if attempt < self.retries:
                    time.sleep(self.retry_delay)
        logger.error(
            "Printer communication failed after %d attempts", self.retries
        )
        raise PrinterError(
            f"Printer communication failed after {self.retries} attempts: "
            f"{last_error}"
        ) from last_error

    def _send_and_check(self, command: str, operation: str) -> str:
        """Send a command, parse response, raise on FAIL."""
        raw = self.send_command(command)
        cmd_name, result = parse_response(raw)
        if result == "FAIL":
            raise PrinterError(f"{operation} failed: {raw}")
        if "not found" in result:
            raise PrinterError(f"{operation}: {result}")
        return raw

    # -- Status --

    def get_status(self) -> PrinterStatus:
        """Query printer status via SPPSTA."""
        logger.info("Requesting printer status")
        raw = self.send_command(build_command("SPPSTA"))
        status = parse_status(raw)
        logger.info("Printer status: %s", status.summary())
        return status

    # -- Template management --

    def load_template(self, template_name: str | None = None) -> None:
        """Load a template on the printer (SPLLTF). Uses configured name if not specified."""
        name = template_name or self.template_name
        if not name:
            raise PrinterError("No template name specified")
        logger.info("Loading template: %s", name)
        self._send_and_check(build_load_template(name), "Load template")
        logger.info("Template loaded: %s", name)

    def get_active_template(self) -> str:
        """Get the currently active template name (SPLGAT)."""
        raw = self.send_command(build_command("SPLGAT"))
        _, result = parse_response(raw)
        logger.info("Active template: %s", result)
        return result

    def get_stored_templates(self) -> list[str]:
        """Get list of all stored template names (SPLGST)."""
        raw = self.send_command(build_command("SPLGST"))
        _, result = parse_response(raw)
        if not result:
            return []
        return result.split(GET_SEP)

    def get_field_names(self, template_name: str | None = None) -> list[str]:
        """Get field names from a template (SPLGFN)."""
        name = template_name or self.template_name
        if not name:
            raise PrinterError("No template name specified")
        raw = self.send_command(build_command("SPLGFN", name))
        _, result = parse_response(raw)
        if not result:
            return []
        return result.split(GET_SEP)

    # -- Printing --

    def print_code(self, raw_code: str, field_name: str | None = None) -> None:
        """Inject GS1 data into the 2D barcode field and trigger one test print.

        This is the primary method for single-label printing:
        1. SPMC2D to update the barcode field
        2. SPPOTP to trigger one print
        """
        fname = field_name or self.datamatrix_field
        if not fname:
            raise PrinterError("No datamatrix_field specified")
        encoded = encode_gs1_for_savema(raw_code)
        logger.info("Print job started for code: %s", raw_code[:40])

        # Update barcode field
        cmd_modify = build_modify_2d(fname, encoded)
        self._send_and_check(cmd_modify, "Modify 2D barcode")

        # Trigger one print
        cmd_print = build_command("SPPOTP")
        self._send_and_check(cmd_print, "One test print")
        logger.info("Print job sent successfully")

    def queue_codes(
        self, codes: list[str], field_name: str | None = None
    ) -> None:
        """Queue multiple GS1 codes for batch printing (SPLAQD).

        After queuing, call start_print() to begin printing.
        The printer will consume one queued value per print trigger.
        """
        fname = field_name or self.datamatrix_field
        if not fname:
            raise PrinterError("No datamatrix_field specified")
        encoded = [encode_gs1_for_savema(c) for c in codes]
        logger.info("Queuing %d codes to field '%s'", len(codes), fname)
        cmd = build_queue_data(fname, encoded)
        self._send_and_check(cmd, "Queue data")
        logger.info("Queued %d codes", len(codes))

    def start_print(self) -> None:
        """Start automatic printing (SPPSAP)."""
        logger.info("Starting automatic print")
        self._send_and_check(build_command("SPPSAP"), "Start print")
        logger.info("Printing started")

    def stop_print(self) -> None:
        """Stop printing (SPPSTP)."""
        logger.info("Stopping print")
        self._send_and_check(build_command("SPPSTP"), "Stop print")
        logger.info("Printing stopped")

    def one_test_print(self) -> None:
        """Trigger a single test print (SPPOTP). Intermittent models only."""
        logger.info("Triggering one test print")
        self._send_and_check(build_command("SPPOTP"), "One test print")
        logger.info("Test print sent")

    def set_print_count(self, count: int) -> None:
        """Set limited print count (SPPSLQ). Printer stops after count prints."""
        logger.info("Setting limited print count: %d", count)
        self._send_and_check(
            build_command("SPPSLQ", str(count)), "Set print count"
        )

    # -- Data management --

    def clear_data_buffer(self) -> None:
        """Clear the printer's data buffer (SPLCDB)."""
        logger.info("Clearing data buffer")
        self._send_and_check(build_command("SPLCDB"), "Clear data buffer")

    def clear_queue(self, field_name: str | None = None) -> None:
        """Clear queue data for a field (SPLCQD)."""
        fname = field_name or self.datamatrix_field
        if not fname:
            raise PrinterError("No field name specified")
        logger.info("Clearing queue for field '%s'", fname)
        self._send_and_check(
            build_command("SPLCQD", fname), "Clear queue"
        )

    def get_queue_capacity(self, field_name: str | None = None) -> str:
        """Get queue capacity/count for a field (SPLGQC)."""
        fname = field_name or self.datamatrix_field
        if not fname:
            raise PrinterError("No field name specified")
        raw = self.send_command(build_command("SPLGQC", fname))
        _, result = parse_response(raw)
        return result

    # -- Info --

    def get_current_print_count(self) -> int:
        """Get current print count since last template load (SPGGCP)."""
        raw = self.send_command(build_command("SPGGCP"))
        _, result = parse_response(raw)
        try:
            return int(result)
        except ValueError:
            return 0

    def get_total_print_count(self) -> int:
        """Get total lifetime print count (SPGGTP)."""
        raw = self.send_command(build_command("SPGGTP"))
        _, result = parse_response(raw)
        try:
            return int(result)
        except ValueError:
            return 0

    def get_firmware_version(self) -> str:
        """Get printer firmware version (SPGGFV)."""
        raw = self.send_command(build_command("SPGGFV"))
        _, result = parse_response(raw)
        return result

    def get_serial_number(self) -> str:
        """Get printer serial number (SPGGSN)."""
        raw = self.send_command(build_command("SPGGSN"))
        _, result = parse_response(raw)
        return result

    def get_remaining_ribbon(self) -> str:
        """Get remaining ribbon percentage (SPGGRR). Cassette models only."""
        raw = self.send_command(build_command("SPGGRR"))
        _, result = parse_response(raw)
        return result

    # -- Configuration --

    def get_network_config(self) -> dict[str, str]:
        """Get network configuration (SPCGNC)."""
        raw = self.send_command(build_command("SPCGNC"))
        _, result = parse_response(raw)
        parts = result.split(GET_SEP)
        if len(parts) >= 4:
            return {
                "ip": parts[0],
                "subnet": parts[1],
                "gateway": parts[2],
                "port": parts[3],
            }
        return {"raw": result}
