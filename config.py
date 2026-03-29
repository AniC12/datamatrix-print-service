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

    # label
    label_x: int = 50
    label_y: int = 50
    orientation: str = "N"
    module_size: int = 6
    quality: int = 200
    label_width: float = 4.0
    label_height: float = 6.0
    dpmm: int = 8

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

    if cp.has_section("label"):
        s = cp["label"]
        settings.label_x = s.getint("x", settings.label_x)
        settings.label_y = s.getint("y", settings.label_y)
        settings.orientation = s.get("orientation", settings.orientation)
        settings.module_size = s.getint("module_size", settings.module_size)
        settings.quality = s.getint("quality", settings.quality)
        settings.label_width = s.getfloat("label_width", settings.label_width)
        settings.label_height = s.getfloat("label_height", settings.label_height)
        settings.dpmm = s.getint("dpmm", settings.dpmm)

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
