from __future__ import annotations

import logging
import socket
import time
from dataclasses import dataclass, field

logger = logging.getLogger(__name__)


DEFAULT_PORT = 9100
GS_CHARACTER = "\x1d"
ZPL_GS_HEX = "_1D"


class PrinterError(Exception):
    pass


@dataclass
class PrinterStatus:
    raw: str = ""
    paper_out: bool = False
    paused: bool = False
    head_open: bool = False
    ribbon_out: bool = False
    over_temperature: bool = False
    under_temperature: bool = False
    corrupt_ram: bool = False
    buffer_full: bool = False
    partial_format: bool = False
    label_waiting: bool = False
    labels_remaining: int = 0
    label_length: int = 0
    errors: list = field(default_factory=list)

    @property
    def ready(self) -> bool:
        return not any([
            self.paper_out,
            self.paused,
            self.head_open,
            self.ribbon_out,
            self.over_temperature,
            self.under_temperature,
            self.corrupt_ram,
        ])

    def summary(self) -> str:
        if self.ready:
            return "READY"
        problems = []
        if self.paper_out:
            problems.append("paper out")
        if self.paused:
            problems.append("paused")
        if self.head_open:
            problems.append("head open")
        if self.ribbon_out:
            problems.append("ribbon out")
        if self.over_temperature:
            problems.append("over temperature")
        if self.under_temperature:
            problems.append("under temperature")
        if self.corrupt_ram:
            problems.append("corrupt RAM")
        return "NOT READY: " + ", ".join(problems)


def parse_status(raw: str) -> PrinterStatus:
    status = PrinterStatus(raw=raw)
    cleaned = raw.replace("\x02", "").replace("\x03", "").strip()
    lines = [line.strip() for line in cleaned.split("\r\n") if line.strip()]
    if not lines:
        lines = [line.strip() for line in cleaned.split("\n") if line.strip()]
    if len(lines) < 1:
        status.errors.append("Empty status response")
        return status
    try:
        f1 = lines[0].split(",")
        if len(f1) >= 12:
            status.paper_out = f1[1].strip() == "1"
            status.paused = f1[2].strip() == "1"
            status.label_length = int(f1[3].strip()) if f1[3].strip().isdigit() else 0
            status.buffer_full = f1[5].strip() == "1"
            status.partial_format = f1[7].strip() == "1"
            status.corrupt_ram = f1[9].strip() == "1"
            status.under_temperature = f1[10].strip() == "1"
            status.over_temperature = f1[11].strip() == "1"
    except (IndexError, ValueError) as exc:
        status.errors.append(f"Failed to parse status line 1: {exc}")
    if len(lines) >= 2:
        try:
            f2 = lines[1].split(",")
            if len(f2) >= 9:
                status.head_open = f2[2].strip() == "1"
                status.ribbon_out = f2[3].strip() == "1"
                status.label_waiting = f2[7].strip() == "1"
                remaining = f2[8].strip()
                status.labels_remaining = int(remaining) if remaining.isdigit() else 0
        except (IndexError, ValueError) as exc:
            status.errors.append(f"Failed to parse status line 2: {exc}")
    return status


@dataclass
class ZebraPrinterClient:
    host: str
    port: int = DEFAULT_PORT
    timeout: float = 5.0

    retries: int = 3
    retry_delay: float = 1.0

    def send_command(self, command: str, expect_response: bool = False, response_size: int = 4096) -> str:
        payload = command.encode("ascii")
        last_error: Exception = Exception("No connection attempt made")
        for attempt in range(1, self.retries + 1):
            try:
                logger.debug("Connecting to %s:%d (attempt %d/%d)", self.host, self.port, attempt, self.retries)
                with socket.create_connection((self.host, self.port), timeout=self.timeout) as connection:
                    connection.sendall(payload)
                    logger.debug("Sent %d bytes to printer", len(payload))
                    if not expect_response:
                        return ""
                    connection.shutdown(socket.SHUT_WR)
                    chunks: list[bytes] = []
                    while True:
                        chunk = connection.recv(response_size)
                        if not chunk:
                            break
                        chunks.append(chunk)
                    response = b"".join(chunks).decode("ascii", errors="replace")
                    logger.debug("Received %d bytes from printer", len(response))
                    return response
            except OSError as exc:
                last_error = exc
                logger.warning("Connection attempt %d/%d failed: %s", attempt, self.retries, exc)
                if attempt < self.retries:
                    time.sleep(self.retry_delay)
        logger.error("Printer communication failed after %d attempts", self.retries)
        raise PrinterError(f"Printer communication failed after {self.retries} attempts: {last_error}") from last_error

    def print_code(self, raw_code: str, x: int = 50, y: int = 50, orientation: str = "N", module_size: int = 6, quality: int = 200) -> None:
        logger.info("Print job started for code: %s", raw_code[:40])
        encoded_code = encode_gs1_datamatrix_data(raw_code)
        zpl = build_datamatrix_zpl(
            encoded_code=encoded_code,
            x=x,
            y=y,
            orientation=orientation,
            module_size=module_size,
            quality=quality,
        )
        self.send_command(zpl)
        logger.info("Print job sent successfully")

    def clear_queue(self) -> None:
        logger.info("Clearing printer queue")
        self.send_command("~JA")
        logger.info("Printer queue cleared")

    def get_status(self) -> PrinterStatus:
        logger.info("Requesting printer status")
        raw = self.send_command("~HS", expect_response=True)
        status = parse_status(raw)
        logger.info("Printer status: %s", status.summary())
        return status

    def get_raw_status(self) -> str:
        return self.send_command("~HS", expect_response=True)


def encode_gs1_datamatrix_data(raw_code: str) -> str:
    result = raw_code.replace("_", "_5F")
    result = result.replace(GS_CHARACTER, ZPL_GS_HEX)
    result = result.replace("\\x1d", ZPL_GS_HEX)
    return result


def build_datamatrix_zpl(encoded_code: str, x: int = 50, y: int = 50, orientation: str = "N", module_size: int = 6, quality: int = 200) -> str:
    if orientation not in {"N", "R", "I", "B"}:
        raise ValueError("orientation must be one of N, R, I, B")
    if module_size <= 0:
        raise ValueError("module_size must be positive")
    if quality not in {0, 50, 80, 100, 140, 200}:
        raise ValueError("quality must be one of 0, 50, 80, 100, 140, 200")
    return (
        "^XA\n"
        f"^FO{x},{y}\n"
        f"^BX{orientation},{module_size},{quality}\n"
        f"^FD{encoded_code}\n"
        "^FS\n"
        "^XZ"
    )
