from __future__ import annotations

import pytest

from zebra_printer import (
    PrinterError,
    PrinterStatus,
    ZebraPrinterClient,
    build_datamatrix_zpl,
    encode_gs1_datamatrix_data,
    parse_status,
)


class TestEncodeGS1DatamatrixData:
    def test_literal_backslash_x1d(self):
        assert encode_gs1_datamatrix_data("ABC\\x1dDEF") == "ABC_1DDEF"

    def test_actual_ascii_29(self):
        assert encode_gs1_datamatrix_data("ABC\x1dDEF") == "ABC_1DDEF"

    def test_no_gs_characters(self):
        assert encode_gs1_datamatrix_data("HELLO123") == "HELLO123"

    def test_underscore_escaped(self):
        assert encode_gs1_datamatrix_data("A_B") == "A_5FB"

    def test_underscore_and_gs(self):
        assert encode_gs1_datamatrix_data("A_B\x1dC") == "A_5FB_1DC"

    def test_multiple_gs(self):
        assert encode_gs1_datamatrix_data("\x1dA\x1dB\x1d") == "_1DA_1DB_1D"

    def test_empty_string(self):
        assert encode_gs1_datamatrix_data("") == ""

    def test_brief_example(self):
        raw = "0104850006070011211:e:Bp.\x1d9396gt"
        assert encode_gs1_datamatrix_data(raw) == "0104850006070011211:e:Bp._1D9396gt"


class TestBuildDatamatrixZPL:
    def test_default_params(self):
        zpl = build_datamatrix_zpl("TEST123")
        assert zpl.startswith("^XA\n")
        assert zpl.endswith("^XZ")
        assert "^FO50,50" in zpl
        assert "^BXN,6,200" in zpl
        assert "^FDTEST123" in zpl
        assert "^FS" in zpl

    def test_custom_position(self):
        zpl = build_datamatrix_zpl("CODE", x=100, y=200)
        assert "^FO100,200" in zpl

    def test_custom_orientation(self):
        zpl = build_datamatrix_zpl("CODE", orientation="R")
        assert "^BXR," in zpl

    def test_custom_module_size(self):
        zpl = build_datamatrix_zpl("CODE", module_size=10)
        assert "^BXN,10," in zpl

    def test_custom_quality(self):
        zpl = build_datamatrix_zpl("CODE", quality=100)
        assert ",100\n" in zpl

    def test_invalid_orientation(self):
        with pytest.raises(ValueError, match="orientation"):
            build_datamatrix_zpl("CODE", orientation="X")

    def test_invalid_module_size(self):
        with pytest.raises(ValueError, match="module_size"):
            build_datamatrix_zpl("CODE", module_size=0)

    def test_invalid_quality(self):
        with pytest.raises(ValueError, match="quality"):
            build_datamatrix_zpl("CODE", quality=99)

    def test_all_valid_qualities(self):
        for q in [0, 50, 80, 100, 140, 200]:
            zpl = build_datamatrix_zpl("CODE", quality=q)
            assert f",{q}\n" in zpl


class TestParseStatus:
    READY_RESPONSE = (
        "\x02030,0,0,1245,000,0,0,0,000,0,0,0\x03\r\n"
        "\x02001,0,0,0,0,2,6,0,00000000,1,000\x03\r\n"
        "\x02001,0,0,0,0,0\x03\r\n"
    )

    PAPER_OUT_RESPONSE = (
        "\x02030,1,0,1245,000,0,0,0,000,0,0,0\x03\r\n"
        "\x02001,0,0,0,0,2,6,0,00000000,1,000\x03\r\n"
        "\x02001,0,0,0,0,0\x03\r\n"
    )

    HEAD_OPEN_RESPONSE = (
        "\x02030,0,0,1245,000,0,0,0,000,0,0,0\x03\r\n"
        "\x02001,0,1,0,0,2,6,0,00000000,1,000\x03\r\n"
        "\x02001,0,0,0,0,0\x03\r\n"
    )

    MULTIPLE_ERRORS_RESPONSE = (
        "\x02030,1,1,1245,000,0,0,0,000,0,1,1\x03\r\n"
        "\x02001,0,1,1,0,2,6,0,00000000,1,000\x03\r\n"
        "\x02001,0,0,0,0,0\x03\r\n"
    )

    def test_ready(self):
        status = parse_status(self.READY_RESPONSE)
        assert status.ready is True
        assert status.summary() == "READY"
        assert status.paper_out is False
        assert status.paused is False
        assert status.head_open is False
        assert status.ribbon_out is False
        assert status.label_length == 1245
        assert status.errors == []

    def test_paper_out(self):
        status = parse_status(self.PAPER_OUT_RESPONSE)
        assert status.ready is False
        assert status.paper_out is True
        assert "paper out" in status.summary()

    def test_head_open(self):
        status = parse_status(self.HEAD_OPEN_RESPONSE)
        assert status.ready is False
        assert status.head_open is True
        assert "head open" in status.summary()

    def test_multiple_errors(self):
        status = parse_status(self.MULTIPLE_ERRORS_RESPONSE)
        assert status.ready is False
        assert status.paper_out is True
        assert status.paused is True
        assert status.head_open is True
        assert status.ribbon_out is True
        assert status.under_temperature is True
        assert status.over_temperature is True
        summary = status.summary()
        assert "paper out" in summary
        assert "paused" in summary
        assert "head open" in summary
        assert "ribbon out" in summary

    def test_empty_response(self):
        status = parse_status("")
        assert status.errors == ["Empty status response"]
        assert status.ready is True  # no flags set, defaults

    def test_malformed_short_line(self):
        status = parse_status("\x02030,0,0\x03\r\n")
        assert status.ready is True  # not enough fields to set any flag

    def test_lf_only_line_endings(self):
        raw = (
            "\x02030,0,0,1245,000,0,0,0,000,0,0,0\x03\n"
            "\x02001,0,0,0,0,2,6,0,00000000,1,000\x03\n"
        )
        status = parse_status(raw)
        assert status.ready is True
        assert status.label_length == 1245

    def test_raw_preserved(self):
        status = parse_status(self.READY_RESPONSE)
        assert status.raw == self.READY_RESPONSE

    def test_labels_remaining(self):
        raw = (
            "\x02030,0,0,1245,000,0,0,0,000,0,0,0\x03\r\n"
            "\x02001,0,0,0,0,2,6,0,00000050,1,000\x03\r\n"
        )
        status = parse_status(raw)
        assert status.labels_remaining == 50

    def test_label_waiting(self):
        raw = (
            "\x02030,0,0,1245,000,0,0,0,000,0,0,0\x03\r\n"
            "\x02001,0,0,0,0,2,6,1,00000000,1,000\x03\r\n"
        )
        status = parse_status(raw)
        assert status.label_waiting is True


class TestPrinterStatusSummary:
    def test_ready_summary(self):
        status = PrinterStatus()
        assert status.summary() == "READY"

    def test_single_problem(self):
        status = PrinterStatus(ribbon_out=True)
        assert status.summary() == "NOT READY: ribbon out"

    def test_multiple_problems(self):
        status = PrinterStatus(paper_out=True, head_open=True)
        summary = status.summary()
        assert summary.startswith("NOT READY: ")
        assert "paper out" in summary
        assert "head open" in summary


class TestZebraPrinterClientSendCommand:
    def test_connection_refused_raises_printer_error(self):
        client = ZebraPrinterClient(host="127.0.0.1", port=1, timeout=0.5, retries=1)
        with pytest.raises(PrinterError, match="Printer communication failed"):
            client.send_command("^XA^XZ")

    def test_retries_in_error_message(self):
        client = ZebraPrinterClient(host="127.0.0.1", port=1, timeout=0.2, retries=2, retry_delay=0.1)
        with pytest.raises(PrinterError, match="after 2 attempts"):
            client.send_command("^XA^XZ")
