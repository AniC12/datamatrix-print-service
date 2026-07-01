"""Tests for savema_printer module: SPPL command building, status parsing, GS1 encoding."""

import pytest

from savema_printer import (
    PrinterError,
    PrinterStatus,
    build_chain,
    build_command,
    build_load_template,
    build_modify_2d,
    build_modify_barcode,
    build_modify_selected,
    build_modify_text,
    build_queue_data,
    encode_gs1_for_savema,
    is_ok,
    parse_response,
    parse_status,
)


# ---------------------------------------------------------------------------
# Command building
# ---------------------------------------------------------------------------

class TestBuildCommand:
    def test_simple_command_no_params(self):
        assert build_command("SPPSTA") == "~SPPSTA^"

    def test_command_with_params(self):
        assert build_command("SPPSLQ", "1000") == "~SPPSLQ{1000}^"

    def test_set_network_config(self):
        cmd = build_command("SPCSNC", "192.168.1.100>255.255.255.0>192.168.1.1>9100")
        assert cmd == "~SPCSNC{192.168.1.100>255.255.255.0>192.168.1.1>9100}^"


class TestBuildModify2D:
    def test_basic(self):
        cmd = build_modify_2d("gs1_code", "savema12345")
        assert cmd == "~SPMC2D{gs1_code~gt~savema12345}^"

    def test_with_gs1_data(self):
        cmd = build_modify_2d("qr1", "0104850006070011")
        assert cmd == "~SPMC2D{qr1~gt~0104850006070011}^"


class TestBuildModifyText:
    def test_basic(self):
        cmd = build_modify_text("brand_txt", "SAVEMA")
        assert cmd == "~SPMCTV{brand_txt~gt~SAVEMA}^"


class TestBuildModifyBarcode:
    def test_basic(self):
        cmd = build_modify_barcode("bar1", "8691234567890")
        assert cmd == "~SPMCBV{bar1~gt~8691234567890}^"


class TestBuildModifySelected:
    def test_two_fields(self):
        cmd = build_modify_selected([
            ("brand_txt", "SAVEMA"),
            ("qrcodeno", "savema12345"),
        ])
        assert cmd == "~SPMCSV{brand_txt~gt~SAVEMA~gt~qrcodeno~gt~savema12345}^"

    def test_single_field(self):
        cmd = build_modify_selected([("text1", "hello")])
        assert cmd == "~SPMCSV{text1~gt~hello}^"


class TestBuildLoadTemplate:
    def test_basic(self):
        cmd = build_load_template("gs1label_32.rox")
        assert cmd == "~SPLLTF{gs1label_32.rox}^"


class TestBuildQueueData:
    def test_multiple_values(self):
        cmd = build_queue_data("gs1_code", ["AB001", "AB002", "AB003"])
        assert cmd == "~SPLAQD{gs1_code~gt~AB001\nAB002\nAB003}^"

    def test_single_value(self):
        cmd = build_queue_data("gs1_code", ["SINGLE"])
        assert cmd == "~SPLAQD{gs1_code~gt~SINGLE}^"


class TestBuildChain:
    def test_two_commands(self):
        cmd1 = build_command("SPPSLQ", "1000")
        cmd2 = build_command("SPPSAP")
        chained = build_chain(cmd1, cmd2)
        assert chained == "~SPPSLQ{1000}|SPPSAP^"

    def test_three_commands(self):
        c1 = build_modify_2d("gs1_code", "data1")
        c2 = build_modify_text("batch", "LOT01")
        c3 = build_command("SPPOTP")
        chained = build_chain(c1, c2, c3)
        assert "SPMC2D{gs1_code~gt~data1}" in chained
        assert "|SPMCTV{batch~gt~LOT01}|" in chained
        assert chained.endswith("SPPOTP^")


# ---------------------------------------------------------------------------
# Response parsing
# ---------------------------------------------------------------------------

class TestParseResponse:
    def test_ok_response(self):
        cmd, result = parse_response("~ SPGRES{SPMCTV:OK}^")
        assert cmd == "SPMCTV"
        assert result == "OK"

    def test_fail_response(self):
        cmd, result = parse_response("~ SPGRES{SPLLTF:FAIL}^")
        assert cmd == "SPLLTF"
        assert result == "FAIL"

    def test_data_response(self):
        cmd, result = parse_response("~ SPGRES{SPLGAT:gs1label_32.rox}^")
        assert cmd == "SPLGAT"
        assert result == "gs1label_32.rox"

    def test_not_found_response(self):
        cmd, result = parse_response("~ SPGRES{SPMC2D:< ProductQRCode> not found}^")
        assert cmd == "SPMC2D"
        assert "not found" in result

    def test_get_params_response(self):
        raw = "~ SPGRES{SPCGNC:192.168.1.123<255.255.255.0<192.168.1.1<9100}^"
        cmd, result = parse_response(raw)
        assert cmd == "SPCGNC"
        parts = result.split("<")
        assert parts[0] == "192.168.1.123"
        assert parts[3] == "9100"

    def test_invalid_response(self):
        cmd, result = parse_response("garbage data")
        assert cmd == ""
        assert result == ""

    def test_stored_templates(self):
        raw = "~ SPGRES{SPLGST:temp1_53.rox<abc_53.rox<temp2_53.rox}^"
        cmd, result = parse_response(raw)
        templates = result.split("<")
        assert len(templates) == 3
        assert templates[0] == "temp1_53.rox"


class TestIsOk:
    def test_ok(self):
        assert is_ok("~ SPGRES{SPMC2D:OK}^") is True

    def test_fail(self):
        assert is_ok("~ SPGRES{SPMC2D:FAIL}^") is False

    def test_garbage(self):
        assert is_ok("garbage") is False


# ---------------------------------------------------------------------------
# Status parsing
# ---------------------------------------------------------------------------

class TestParseStatus:
    def test_waiting(self):
        status = parse_status("~ SPGRES{SPPSTA:WAITING<}^")
        assert status.state == "WAITING"
        assert status.ready is True
        assert status.running is False
        assert status.blocked is False
        assert status.error_message == ""

    def test_running(self):
        status = parse_status("~ SPGRES{SPPSTA:RUNNING<}^")
        assert status.state == "RUNNING"
        assert status.ready is False
        assert status.running is True

    def test_init(self):
        status = parse_status("~ SPGRES{SPPSTA:INIT<}^")
        assert status.state == "INIT"
        assert status.ready is False

    def test_error_with_message(self):
        status = parse_status(
            "~ SPGRES{SPPSTA:ERROR<Ribbon not found.Please insert ribbon}^"
        )
        assert status.state == "ERROR"
        assert status.ready is False
        assert "Ribbon not found" in status.error_message

    def test_waiting_blocked(self):
        status = parse_status("~ SPGRES{SPPSTA:WAITING<BLOCKED}^")
        assert status.state == "WAITING"
        assert status.blocked is True
        assert status.ready is False

    def test_running_blocked(self):
        status = parse_status("~ SPGRES{SPPSTA:RUNNING<BLOCKED}^")
        assert status.state == "RUNNING"
        assert status.blocked is True

    def test_error_blocked(self):
        status = parse_status(
            "~ SPGRES{SPPSTA:ERROR<BLOCKED Ribbon not found.Please insert ribbon}^"
        )
        assert status.state == "ERROR"
        assert status.blocked is True
        assert "Ribbon not found" in status.error_message

    def test_invalid_response(self):
        status = parse_status("garbage")
        assert status.state == ""
        assert len(status.errors) > 0

    def test_summary_waiting(self):
        status = parse_status("~ SPGRES{SPPSTA:WAITING<}^")
        assert "WAITING" in status.summary()

    def test_summary_error(self):
        status = parse_status("~ SPGRES{SPPSTA:ERROR<Something broke}^")
        assert "ERROR" in status.summary()
        assert "Something broke" in status.summary()

    def test_summary_blocked(self):
        status = parse_status("~ SPGRES{SPPSTA:RUNNING<BLOCKED}^")
        assert "BLOCKED" in status.summary()


# ---------------------------------------------------------------------------
# GS1 encoding for Savema
# ---------------------------------------------------------------------------

class TestEncodeGS1ForSavema:
    def test_plain_text(self):
        assert encode_gs1_for_savema("0104850006070011") == "0104850006070011"

    def test_literal_escape_converted(self):
        result = encode_gs1_for_savema("01048500\\x1d21ABC")
        assert "\x1d" in result
        assert "\\x1d" not in result

    def test_actual_gs_byte_preserved(self):
        result = encode_gs1_for_savema("01048500\x1d21ABC")
        assert "\x1d" in result

    def test_xml_ampersand_escaped(self):
        result = encode_gs1_for_savema("A&B")
        assert result == "A&amp;B"

    def test_xml_lt_gt_escaped(self):
        result = encode_gs1_for_savema("A<B>C")
        assert result == "A&lt;B&gt;C"

    def test_xml_quotes_escaped(self):
        result = encode_gs1_for_savema('A"B\'C')
        assert result == "A&quot;B&apos;C"

    def test_ampersand_not_double_escaped(self):
        result = encode_gs1_for_savema("A&B<C")
        assert result == "A&amp;B&lt;C"
        # Ensure & in &lt; is not double-escaped
        assert "&amp;lt;" not in result


# ---------------------------------------------------------------------------
# PrinterStatus dataclass
# ---------------------------------------------------------------------------

class TestPrinterStatus:
    def test_default_not_ready(self):
        s = PrinterStatus()
        assert s.ready is False
        assert s.running is False

    def test_waiting_is_ready(self):
        s = PrinterStatus(state="WAITING")
        assert s.ready is True

    def test_waiting_blocked_not_ready(self):
        s = PrinterStatus(state="WAITING", blocked=True)
        assert s.ready is False

    def test_running_not_ready(self):
        s = PrinterStatus(state="RUNNING")
        assert s.ready is False
        assert s.running is True

    def test_error_summary(self):
        s = PrinterStatus(state="ERROR", error_message="Ribbon out")
        assert "ERROR" in s.summary()
        assert "Ribbon out" in s.summary()

    def test_unknown_summary(self):
        s = PrinterStatus()
        assert s.summary() == "UNKNOWN"
