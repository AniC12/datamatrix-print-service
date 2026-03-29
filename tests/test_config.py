from __future__ import annotations

import pytest

from config import Settings, load_settings


class TestSettingsDefaults:
    def test_default_values(self):
        s = Settings()
        assert s.printer_host == "127.0.0.1"
        assert s.printer_port == 9100
        assert s.timeout == 5.0
        assert s.retries == 3
        assert s.retry_delay == 1.0
        assert s.label_x == 50
        assert s.label_y == 50
        assert s.orientation == "N"
        assert s.module_size == 6
        assert s.quality == 200
        assert s.label_width == 4.0
        assert s.label_height == 6.0
        assert s.dpmm == 8
        assert s.csv_column == ""
        assert s.csv_delimiter == ","
        assert s.csv_has_header is True
        assert s.dry_run is False


class TestLoadSettings:
    def test_missing_file_returns_defaults(self, tmp_path):
        s = load_settings(tmp_path / "nonexistent.ini")
        assert s.printer_host == "127.0.0.1"
        assert s.printer_port == 9100

    def test_empty_file_returns_defaults(self, tmp_path):
        p = tmp_path / "empty.ini"
        p.write_text("", encoding="utf-8")
        s = load_settings(p)
        assert s.printer_host == "127.0.0.1"

    def test_printer_section(self, tmp_path):
        p = tmp_path / "test.ini"
        p.write_text(
            "[printer]\nhost = 10.0.0.5\nport = 6101\ntimeout = 2.5\nretries = 5\nretry_delay = 0.5\n",
            encoding="utf-8",
        )
        s = load_settings(p)
        assert s.printer_host == "10.0.0.5"
        assert s.printer_port == 6101
        assert s.timeout == 2.5
        assert s.retries == 5
        assert s.retry_delay == 0.5

    def test_label_section(self, tmp_path):
        p = tmp_path / "test.ini"
        p.write_text(
            "[label]\nx = 100\ny = 200\norientation = R\nmodule_size = 10\nquality = 140\n"
            "label_width = 2.0\nlabel_height = 1.5\ndpmm = 12\n",
            encoding="utf-8",
        )
        s = load_settings(p)
        assert s.label_x == 100
        assert s.label_y == 200
        assert s.orientation == "R"
        assert s.module_size == 10
        assert s.quality == 140
        assert s.label_width == 2.0
        assert s.label_height == 1.5
        assert s.dpmm == 12

    def test_csv_section(self, tmp_path):
        p = tmp_path / "test.ini"
        p.write_text(
            "[csv]\ncolumn = code\ndelimiter = ;\nhas_header = false\n",
            encoding="utf-8",
        )
        s = load_settings(p)
        assert s.csv_column == "code"
        assert s.csv_delimiter == ";"
        assert s.csv_has_header is False

    def test_mode_section(self, tmp_path):
        p = tmp_path / "test.ini"
        p.write_text("[mode]\ndry_run = true\n", encoding="utf-8")
        s = load_settings(p)
        assert s.dry_run is True

    def test_partial_config(self, tmp_path):
        p = tmp_path / "test.ini"
        p.write_text("[printer]\nhost = 192.168.1.50\n", encoding="utf-8")
        s = load_settings(p)
        assert s.printer_host == "192.168.1.50"
        assert s.printer_port == 9100  # default preserved
        assert s.label_x == 50  # default preserved

    def test_full_config(self, tmp_path):
        p = tmp_path / "test.ini"
        p.write_text(
            "[printer]\nhost = 10.0.0.1\nport = 9100\ntimeout = 3.0\nretries = 2\nretry_delay = 0.2\n"
            "[label]\nx = 30\ny = 40\norientation = I\nmodule_size = 8\nquality = 80\n"
            "label_width = 3.0\nlabel_height = 2.0\ndpmm = 12\n"
            "[csv]\ncolumn = serial\ndelimiter = ,\nhas_header = true\n"
            "[mode]\ndry_run = true\n",
            encoding="utf-8",
        )
        s = load_settings(p)
        assert s.printer_host == "10.0.0.1"
        assert s.label_x == 30
        assert s.orientation == "I"
        assert s.label_width == 3.0
        assert s.label_height == 2.0
        assert s.dpmm == 12
        assert s.csv_column == "serial"
        assert s.dry_run is True

    def test_label_dimensions_default_when_missing(self, tmp_path):
        p = tmp_path / "test.ini"
        p.write_text("[label]\nx = 100\n", encoding="utf-8")
        s = load_settings(p)
        assert s.label_x == 100
        assert s.label_width == 4.0
        assert s.label_height == 6.0
        assert s.dpmm == 8
