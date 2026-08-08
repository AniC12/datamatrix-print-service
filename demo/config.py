from __future__ import annotations

import configparser
from dataclasses import dataclass
from pathlib import Path


DEFAULT_CONFIG_PATH = Path(__file__).parent / "config.ini"


@dataclass
class Settings:
    # printer
    printer_host: str = "127.0.0.1"
    printer_port: int = 9100
    timeout: float = 5.0
    retries: int = 3
    retry_delay: float = 1.0

    # savema template
    template_name: str = ""
    datamatrix_field: str = ""

    # csv
    csv_column: str = ""
    csv_delimiter: str = ","
    csv_has_header: bool = True

    # mode
    dry_run: bool = False
    log_level: str = "INFO"


def load_settings(config_path: str | Path | None = None) -> Settings:
    path = Path(config_path) if config_path else DEFAULT_CONFIG_PATH
    settings = Settings()

    if not path.exists():
        return settings

    cp = configparser.ConfigParser()
    cp.read(str(path), encoding="utf-8")

    if cp.has_section("printer"):
        s = cp["printer"]
        settings.printer_host = s.get("host", settings.printer_host)
        settings.printer_port = s.getint("port", settings.printer_port)
        settings.timeout = s.getfloat("timeout", settings.timeout)
        settings.retries = s.getint("retries", settings.retries)
        settings.retry_delay = s.getfloat("retry_delay", settings.retry_delay)

    if cp.has_section("savema"):
        s = cp["savema"]
        settings.template_name = s.get("template_name", settings.template_name)
        settings.datamatrix_field = s.get("datamatrix_field", settings.datamatrix_field)

    if cp.has_section("csv"):
        s = cp["csv"]
        settings.csv_column = s.get("column", settings.csv_column)
        settings.csv_delimiter = s.get("delimiter", settings.csv_delimiter)
        settings.csv_has_header = s.getboolean("has_header", settings.csv_has_header)

    if cp.has_section("mode"):
        s = cp["mode"]
        settings.dry_run = s.getboolean("dry_run", settings.dry_run)
        settings.log_level = s.get("log_level", settings.log_level).upper()

    return settings
