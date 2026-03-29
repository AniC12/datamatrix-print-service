from __future__ import annotations

import argparse
import logging
from dataclasses import dataclass, field

from config import load_settings
from csv_processor import read_codes_from_csv
from zebra_printer import PrinterError, ZebraPrinterClient, build_datamatrix_zpl, encode_gs1_datamatrix_data

logger = logging.getLogger(__name__)


@dataclass
class BatchResult:
    csv_file: str = ""
    total_rows: int = 0
    skipped_rows: int = 0
    invalid_rows: list = field(default_factory=list)
    codes_extracted: int = 0
    sent_ok: int = 0
    dry_run_generated: int = 0
    failed: list = field(default_factory=list)
    csv_warnings: list = field(default_factory=list)

    @property
    def failed_count(self) -> int:
        return len(self.failed)

    @property
    def invalid_count(self) -> int:
        return len(self.invalid_rows)

    def format_report(self) -> str:
        lines = [
            "",
            "=" * 44,
            "  BATCH RESULT REPORT",
            "=" * 44,
            f"  CSV file:          {self.csv_file}",
            f"  Total rows:        {self.total_rows}",
            f"  Skipped rows:      {self.skipped_rows}",
            f"  Invalid rows:      {self.invalid_count}",
            f"  Codes extracted:   {self.codes_extracted}",
        ]
        if self.dry_run_generated:
            lines.append(f"  Dry-run generated: {self.dry_run_generated}")
        else:
            lines.append(f"  Sent OK:           {self.sent_ok}")
            lines.append(f"  Failed:            {self.failed_count}")
        if self.invalid_rows:
            lines.append("")
            lines.append("  INVALID CSV ROWS:")
            for inv in self.invalid_rows:
                lines.append(f"    Row {inv.row_num}: {inv.reason}")
        if self.csv_warnings:
            lines.append("")
            lines.append("  CSV WARNINGS:")
            for w in self.csv_warnings:
                lines.append(f"    {w}")
        if self.failed:
            lines.append("")
            lines.append("  PRINT FAILURES:")
            for idx, code, err in self.failed:
                lines.append(f"    Label {idx}: {err}")
                lines.append(f"             code: {code[:50]}")
        lines.append("=" * 44)
        return "\n".join(lines)


def setup_logging(level_name: str) -> None:
    level = getattr(logging, level_name, logging.INFO)
    logging.basicConfig(
        level=level,
        format="%(asctime)s [%(levelname)s] %(name)s: %(message)s",
        datefmt="%Y-%m-%d %H:%M:%S",
    )


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(prog="datamatrix-print-service")
    parser.add_argument("--config", default=None, help="Path to config.ini file")
    parser.add_argument("--host", default=None)
    parser.add_argument("--port", type=int, default=None)
    subparsers = parser.add_subparsers(dest="command", required=True)

    print_parser = subparsers.add_parser("print")
    print_parser.add_argument("code")
    print_parser.add_argument("--x", type=int, default=None)
    print_parser.add_argument("--y", type=int, default=None)
    print_parser.add_argument("--orientation", default=None)
    print_parser.add_argument("--module-size", type=int, default=None)
    print_parser.add_argument("--quality", type=int, default=None)
    print_parser.add_argument("--dry-run", action="store_true", default=None)

    batch_parser = subparsers.add_parser("batch")
    batch_parser.add_argument("csv_file")
    batch_parser.add_argument("--column", default=None)
    batch_parser.add_argument("--delimiter", default=None)
    batch_parser.add_argument("--no-header", action="store_true", default=None)
    batch_parser.add_argument("--x", type=int, default=None)
    batch_parser.add_argument("--y", type=int, default=None)
    batch_parser.add_argument("--orientation", default=None)
    batch_parser.add_argument("--module-size", type=int, default=None)
    batch_parser.add_argument("--quality", type=int, default=None)
    batch_parser.add_argument("--dry-run", action="store_true", default=None)

    subparsers.add_parser("status")
    subparsers.add_parser("clear")
    return parser


def _pick(cli_value, config_value):
    """Return CLI value if explicitly provided, otherwise config value."""
    if cli_value is not None:
        return cli_value
    return config_value


def main() -> None:
    parser = build_parser()
    args = parser.parse_args()
    cfg = load_settings(args.config)
    setup_logging(cfg.log_level)
    logger.debug("Configuration loaded: host=%s port=%d dry_run=%s", cfg.printer_host, cfg.printer_port, cfg.dry_run)

    host = _pick(args.host, cfg.printer_host)
    port = _pick(args.port, cfg.printer_port)
    dry_run = _pick(getattr(args, "dry_run", None), cfg.dry_run)

    client = ZebraPrinterClient(
        host=host,
        port=port,
        timeout=cfg.timeout,
        retries=cfg.retries,
        retry_delay=cfg.retry_delay,
    )

    if args.command == "print":
        logger.info("Command: print single label")
        x = _pick(args.x, cfg.label_x)
        y = _pick(args.y, cfg.label_y)
        orientation = _pick(args.orientation, cfg.orientation)
        module_size = _pick(args.module_size, cfg.module_size)
        quality = _pick(args.quality, cfg.quality)

        encoded = encode_gs1_datamatrix_data(args.code)
        if dry_run:
            logger.info("Dry-run mode: printing ZPL to console")
            zpl = build_datamatrix_zpl(
                encoded_code=encoded,
                x=x,
                y=y,
                orientation=orientation,
                module_size=module_size,
                quality=quality,
            )
            print(zpl)
            return
        try:
            client.print_code(
                raw_code=args.code,
                x=x,
                y=y,
                orientation=orientation,
                module_size=module_size,
                quality=quality,
            )
            print("Print command sent.")
        except PrinterError as exc:
            logger.error("Print failed: %s", exc)
            print(f"Print FAILED: {exc}")
        return

    if args.command == "batch":
        logger.info("Command: batch print from CSV")
        x = _pick(args.x, cfg.label_x)
        y = _pick(args.y, cfg.label_y)
        orientation = _pick(args.orientation, cfg.orientation)
        module_size = _pick(args.module_size, cfg.module_size)
        quality = _pick(args.quality, cfg.quality)

        col = _pick(args.column, cfg.csv_column or None)
        if col is not None and isinstance(col, str) and col.isdigit():
            col = int(col)
        delimiter = _pick(args.delimiter, cfg.csv_delimiter)
        no_header = _pick(args.no_header, not cfg.csv_has_header)

        csv_result = read_codes_from_csv(
            file_path=args.csv_file,
            column=col,
            delimiter=delimiter,
            skip_header=not no_header,
        )
        total = len(csv_result.codes)

        report = BatchResult(
            csv_file=args.csv_file,
            total_rows=csv_result.total_rows,
            skipped_rows=csv_result.skipped_rows,
            invalid_rows=csv_result.invalid_rows,
            codes_extracted=total,
            csv_warnings=csv_result.warnings,
        )

        for i, code in enumerate(csv_result.codes, 1):
            encoded = encode_gs1_datamatrix_data(code)
            if dry_run:
                zpl = build_datamatrix_zpl(
                    encoded_code=encoded,
                    x=x,
                    y=y,
                    orientation=orientation,
                    module_size=module_size,
                    quality=quality,
                )
                print(f"--- Label {i} ---")
                print(zpl)
                report.dry_run_generated += 1
            else:
                try:
                    client.print_code(
                        raw_code=code,
                        x=x,
                        y=y,
                        orientation=orientation,
                        module_size=module_size,
                        quality=quality,
                    )
                    report.sent_ok += 1
                    logger.debug("Label %d/%d sent", i, total)
                except PrinterError as exc:
                    logger.error("Label %d failed: %s", i, exc)
                    report.failed.append((i, code, str(exc)))

        print(report.format_report())
        return

    if args.command == "status":
        logger.info("Command: check printer status")
        try:
            status = client.get_status()
            print(status.summary())
            if status.label_length:
                print(f"  Label length: {status.label_length}")
            if status.labels_remaining:
                print(f"  Labels remaining: {status.labels_remaining}")
            if status.label_waiting:
                print(f"  Label waiting: yes")
            if status.buffer_full:
                print(f"  Buffer full: yes")
            if status.errors:
                print(f"  Parse warnings: {status.errors}")
        except PrinterError as exc:
            logger.error("Status check failed: %s", exc)
            print(f"Failed to get status: {exc}")
        return

    if args.command == "clear":
        logger.info("Command: clear printer queue")
        client.clear_queue()
        print("Queue cleared.")
        return

    parser.error("Unknown command")


if __name__ == "__main__":
    main()
