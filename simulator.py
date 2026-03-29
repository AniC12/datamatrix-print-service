"""Zebra ZT411 printer simulator.

A lightweight TCP server that emulates a Zebra printer on port 9100.
Accepts ZPL commands, responds to ~HS (status) and ~JA (clear queue),
and saves each received label ZPL to a file under received_labels/.

Usage:
    python simulator.py [--host HOST] [--port PORT] [--output-dir DIR]
    python simulator.py --status paused
    python simulator.py --status paper_out ribbon_out

Graceful shutdown with Ctrl+C.
"""
from __future__ import annotations

import argparse
import logging
import os
import signal
import socketserver
import sys
import threading
from datetime import datetime
from pathlib import Path

logger = logging.getLogger("simulator")

PRINTER_MODES = ("ready", "paused", "paper_out", "ribbon_out", "head_open")


def build_hs_response(
    paused: bool = False,
    paper_out: bool = False,
    ribbon_out: bool = False,
    head_open: bool = False,
    label_length: int = 1245,
) -> str:
    """Build a realistic ~HS response from flag values.

    Line 1 fields: diag, paper_out, paused, label_len, fmt_count,
                   buf_full, diag, partial_fmt, unused, corrupt_ram,
                   under_temp, over_temp
    Line 2 fields: func, unused, head_open, ribbon_out, unused,
                   print_mode, width_mode, label_waiting, labels_remaining,
                   fmt_count, unused
    Line 3 fields: password, unused (static)
    """
    line1 = (
        f"\x02030,{int(paper_out)},{int(paused)},{label_length},"
        f"000,0,0,0,000,0,0,0\x03\r\n"
    )
    line2 = (
        f"\x02001,0,{int(head_open)},{int(ribbon_out)},0,2,6,0,"
        f"00000000,1,000\x03\r\n"
    )
    line3 = "\x02001,0,0,0,0,0\x03\r\n"
    return line1 + line2 + line3


class PrinterRequestHandler(socketserver.BaseRequestHandler):
    """Handles a single TCP connection from the client."""

    def handle(self) -> None:
        server: PrinterSimulator = self.server  # type: ignore[assignment]
        chunks: list[bytes] = []
        try:
            while True:
                data = self.request.recv(4096)
                if not data:
                    break
                chunks.append(data)
        except OSError:
            pass

        if not chunks:
            return

        raw = b"".join(chunks).decode("ascii", errors="replace")
        logger.debug("Received %d bytes from %s", len(raw), self.client_address[0])

        # Check for status query
        if "~HS" in raw:
            logger.info("Status query (~HS) received [mode: %s]",
                        ", ".join(server.active_modes) or "ready")
            try:
                response = server.hs_response
                self.request.sendall(response.encode("ascii"))
                logger.debug("Sent status response (%d bytes)", len(response))
            except OSError as exc:
                logger.warning("Failed to send status response: %s", exc)
            return

        # Check for clear queue
        if "~JA" in raw:
            logger.info("Clear queue (~JA) received")
            return

        # Otherwise treat it as a ZPL label
        if "^XA" in raw:
            server.label_count += 1
            label_num = server.label_count
            logger.info("Label %d received (%d bytes)", label_num, len(raw))

            # Save to file
            if server.output_dir:
                filepath = server.output_dir / f"label_{label_num:04d}.zpl"
                filepath.write_text(raw, encoding="ascii")
                logger.info("Label %d saved to %s", label_num, filepath)
        else:
            logger.info("Non-ZPL data received (%d bytes): %s", len(raw), raw[:80])


class PrinterSimulator(socketserver.ThreadingTCPServer):
    """Threaded TCP server simulating a Zebra printer."""

    allow_reuse_address = True
    daemon_threads = True

    def __init__(
        self,
        host: str = "127.0.0.1",
        port: int = 9100,
        output_dir: Path | None = None,
        status_modes: list[str] | None = None,
    ) -> None:
        self.label_count = 0
        self.output_dir = output_dir
        if self.output_dir:
            self.output_dir.mkdir(parents=True, exist_ok=True)
        self.active_modes: list[str] = status_modes or []
        self.hs_response = build_hs_response(
            paused="paused" in self.active_modes,
            paper_out="paper_out" in self.active_modes,
            ribbon_out="ribbon_out" in self.active_modes,
            head_open="head_open" in self.active_modes,
        )
        super().__init__((host, port), PrinterRequestHandler)

    def set_status(self, modes: list[str]) -> None:
        """Change the active status modes at runtime."""
        self.active_modes = modes
        self.hs_response = build_hs_response(
            paused="paused" in modes,
            paper_out="paper_out" in modes,
            ribbon_out="ribbon_out" in modes,
            head_open="head_open" in modes,
        )
        logger.info("Status mode changed to: %s",
                    ", ".join(modes) if modes else "ready")


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(
        prog="simulator",
        description="Zebra ZT411 printer simulator",
    )
    parser.add_argument("--host", default="127.0.0.1", help="Bind address (default: 127.0.0.1)")
    parser.add_argument("--port", type=int, default=9100, help="Bind port (default: 9100)")
    parser.add_argument(
        "--output-dir",
        default="received_labels",
        help="Directory to save received ZPL files (default: received_labels)",
    )
    parser.add_argument(
        "--no-save",
        action="store_true",
        help="Do not save received ZPL to files",
    )
    parser.add_argument(
        "--log-level",
        default="INFO",
        choices=["DEBUG", "INFO", "WARNING", "ERROR"],
        help="Log level (default: INFO)",
    )
    parser.add_argument(
        "--status",
        nargs="+",
        default=[],
        choices=["ready", "paused", "paper_out", "ribbon_out", "head_open"],
        help="Simulated printer status (default: ready). Combine multiple: --status paused paper_out",
    )
    return parser


def main() -> None:
    args = build_parser().parse_args()

    logging.basicConfig(
        level=getattr(logging, args.log_level),
        format="%(asctime)s [%(levelname)s] %(name)s: %(message)s",
        datefmt="%Y-%m-%d %H:%M:%S",
    )

    output_dir = None if args.no_save else Path(args.output_dir)
    status_modes = [m for m in args.status if m != "ready"]

    server = PrinterSimulator(
        host=args.host, port=args.port,
        output_dir=output_dir, status_modes=status_modes,
    )

    # Graceful shutdown on Ctrl+C and SIGTERM
    shutdown_event = threading.Event()

    def _shutdown(signum: int, frame: object) -> None:
        sig_name = signal.Signals(signum).name
        logger.info("Received %s, shutting down...", sig_name)
        shutdown_event.set()
        server.shutdown()

    signal.signal(signal.SIGINT, _shutdown)
    signal.signal(signal.SIGTERM, _shutdown)

    save_msg = f", saving ZPL to {output_dir}/" if output_dir else ""
    mode_msg = ", ".join(status_modes) if status_modes else "ready"
    logger.info("Printer simulator started on %s:%d%s", args.host, args.port, save_msg)
    logger.info("Status mode: %s", mode_msg)
    logger.info("Press Ctrl+C to stop")

    try:
        server.serve_forever()
    finally:
        server.server_close()
        logger.info("Simulator stopped. %d labels received.", server.label_count)


if __name__ == "__main__":
    main()
