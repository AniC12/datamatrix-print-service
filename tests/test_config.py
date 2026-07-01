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
        assert s.template_name == ""
        assert s.datamatrix_field == ""
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

    def test_savema_section(self, tmp_path):
        p = tmp_path / "test.ini"
        p.write_text(
            "[savema]\ntemplate_name = gs1label_32.rox\ndatamatrix_field = gs1_code\n",
            encoding="utf-8",
        )
        s = load_settings(p)
        assert s.template_name == "gs1label_32.rox"
        assert s.datamatrix_field == "gs1_code"

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
        assert s.template_name == ""  # default preserved

    def test_full_config(self, tmp_path):
        p = tmp_path / "test.ini"
        p.write_text(
            "[printer]\nhost = 10.0.0.1\nport = 9100\ntimeout = 3.0\nretries = 2\nretry_delay = 0.2\n"
            "[savema]\ntemplate_name = label_53.rox\ndatamatrix_field = dm_field\n"
            "[csv]\ncolumn = serial\ndelimiter = ,\nhas_header = true\n"
            "[mode]\ndry_run = true\n",
            encoding="utf-8",
        )
        s = load_settings(p)
        assert s.printer_host == "10.0.0.1"
        assert s.template_name == "label_53.rox"
        assert s.datamatrix_field == "dm_field"
        assert s.csv_column == "serial"
        assert s.dry_run is True

    def test_savema_defaults_when_missing(self, tmp_path):
        p = tmp_path / "test.ini"
        p.write_text("[printer]\nhost = 10.0.0.1\n", encoding="utf-8")
        s = load_settings(p)
        assert s.template_name == ""
        assert s.datamatrix_field == ""
