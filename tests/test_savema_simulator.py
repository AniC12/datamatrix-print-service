"""Tests for savema_simulator: SPPL protocol handling via TCP."""

import socket
import threading
import time

import pytest

from savema_simulator import PrinterState, SPPLSimulator, build_response


def get_free_port() -> int:
    with socket.socket() as s:
        s.bind(("", 0))
        return s.getsockname()[1]


@pytest.fixture
def sim():
    """Start a simulator on a free port and tear it down after the test."""
    port = get_free_port()
    state = PrinterState(
        stored_templates=["gs1label_32.rox", "other_32.rox"],
        active_template="gs1label_32.rox",
    )
    server = SPPLSimulator(("127.0.0.1", port), state)
    thread = threading.Thread(target=server.serve_forever, daemon=True)
    thread.start()
    time.sleep(0.1)
    yield server, port, state
    server.shutdown()


def send_sppl(port: int, command: str) -> str:
    """Send an SPPL command to the simulator and return the response."""
    with socket.create_connection(("127.0.0.1", port), timeout=3) as conn:
        conn.sendall(command.encode("utf-8"))
        conn.shutdown(socket.SHUT_WR)
        chunks = []
        while True:
            chunk = conn.recv(4096)
            if not chunk:
                break
            chunks.append(chunk)
        return b"".join(chunks).decode("utf-8")


class TestBuildResponse:
    def test_ok(self):
        assert build_response("SPMC2D", "OK") == "~ SPGRES{SPMC2D:OK}^"

    def test_fail(self):
        assert build_response("SPLLTF", "FAIL") == "~ SPGRES{SPLLTF:FAIL}^"


class TestStatusCommand:
    def test_status_waiting(self, sim):
        _, port, state = sim
        resp = send_sppl(port, "~SPPSTA^")
        assert "WAITING" in resp

    def test_status_running(self, sim):
        _, port, state = sim
        state.status = "RUNNING"
        resp = send_sppl(port, "~SPPSTA^")
        assert "RUNNING" in resp

    def test_status_error(self, sim):
        _, port, state = sim
        state.status = "ERROR"
        state.error_message = "Ribbon not found"
        resp = send_sppl(port, "~SPPSTA^")
        assert "ERROR" in resp
        assert "Ribbon not found" in resp

    def test_status_blocked(self, sim):
        _, port, state = sim
        state.blocked = True
        resp = send_sppl(port, "~SPPSTA^")
        assert "BLOCKED" in resp


class TestTemplateCommands:
    def test_get_active_template(self, sim):
        _, port, _ = sim
        resp = send_sppl(port, "~SPLGAT^")
        assert "gs1label_32.rox" in resp

    def test_get_stored_templates(self, sim):
        _, port, _ = sim
        resp = send_sppl(port, "~SPLGST^")
        assert "gs1label_32.rox" in resp
        assert "other_32.rox" in resp

    def test_load_existing_template(self, sim):
        _, port, state = sim
        resp = send_sppl(port, "~SPLLTF{other_32.rox}^")
        assert "OK" in resp
        assert state.active_template == "other_32.rox"

    def test_load_nonexistent_template(self, sim):
        _, port, _ = sim
        resp = send_sppl(port, "~SPLLTF{missing.rox}^")
        assert "FAIL" in resp

    def test_get_field_names(self, sim):
        _, port, _ = sim
        resp = send_sppl(port, "~SPLGFN{gs1label_32.rox}^")
        assert "gs1_code" in resp


class TestModificationCommands:
    def test_modify_2d(self, sim):
        _, port, state = sim
        resp = send_sppl(port, "~SPMC2D{gs1_code~gt~0104850006070011}^")
        assert "OK" in resp
        assert state.field_values["gs1_code"] == "0104850006070011"

    def test_modify_text(self, sim):
        _, port, state = sim
        resp = send_sppl(port, "~SPMCTV{brand_txt~gt~SAVEMA}^")
        assert "OK" in resp
        assert state.field_values["brand_txt"] == "SAVEMA"

    def test_modify_barcode(self, sim):
        _, port, state = sim
        resp = send_sppl(port, "~SPMCBV{bar1~gt~8691234567890}^")
        assert "OK" in resp
        assert state.field_values["bar1"] == "8691234567890"

    def test_modify_selected(self, sim):
        _, port, state = sim
        resp = send_sppl(port, "~SPMCSV{brand_txt~gt~SAVEMA~gt~qr1~gt~12345}^")
        assert "OK" in resp
        assert state.field_values["brand_txt"] == "SAVEMA"
        assert state.field_values["qr1"] == "12345"


class TestPrintCommands:
    def test_start_print(self, sim):
        _, port, state = sim
        resp = send_sppl(port, "~SPPSAP^")
        assert "OK" in resp
        assert state.status == "RUNNING"

    def test_stop_print(self, sim):
        _, port, state = sim
        state.status = "RUNNING"
        resp = send_sppl(port, "~SPPSTP^")
        assert "OK" in resp
        assert state.status == "WAITING"

    def test_one_test_print(self, sim):
        _, port, state = sim
        resp = send_sppl(port, "~SPPOTP^")
        assert "OK" in resp
        assert state.label_count == 1
        assert state.current_print_count == 1

    def test_set_limited_print_count(self, sim):
        _, port, state = sim
        resp = send_sppl(port, "~SPPSLQ{500}^")
        assert "OK" in resp
        assert state.limited_print_count == 500


class TestQueueCommands:
    def test_append_queue(self, sim):
        _, port, state = sim
        resp = send_sppl(port, "~SPLAQD{gs1_code~gt~AB001\nAB002\nAB003}^")
        assert "OK" in resp
        assert len(state.queues["gs1_code"]) == 3

    def test_get_queue_capacity(self, sim):
        _, port, state = sim
        state.queues["gs1_code"] = ["A", "B", "C"]
        resp = send_sppl(port, "~SPLGQC{gs1_code}^")
        assert "3" in resp

    def test_clear_queue(self, sim):
        _, port, state = sim
        state.queues["gs1_code"] = ["A", "B"]
        resp = send_sppl(port, "~SPLCQD{gs1_code}^")
        assert "OK" in resp
        assert "gs1_code" not in state.queues

    def test_clear_data_buffer(self, sim):
        _, port, state = sim
        state.queues["gs1_code"] = ["A"]
        state.field_values["text1"] = "hello"
        resp = send_sppl(port, "~SPLCDB^")
        assert "OK" in resp
        assert len(state.queues) == 0
        assert len(state.field_values) == 0


class TestGeneralCommands:
    def test_get_print_count(self, sim):
        _, port, state = sim
        state.current_print_count = 42
        resp = send_sppl(port, "~SPGGCP^")
        assert "42" in resp

    def test_get_total_print_count(self, sim):
        _, port, state = sim
        state.total_print_count = 99999
        resp = send_sppl(port, "~SPGGTP^")
        assert "99999" in resp

    def test_get_firmware(self, sim):
        _, port, _ = sim
        resp = send_sppl(port, "~SPGGFV^")
        assert "6.3.001.600.R" in resp

    def test_get_serial(self, sim):
        _, port, _ = sim
        resp = send_sppl(port, "~SPGGSN^")
        assert "SIM00001" in resp

    def test_get_network_config(self, sim):
        _, port, _ = sim
        resp = send_sppl(port, "~SPCGNC^")
        assert "127.0.0.1" in resp
        assert "9100" in resp

    def test_unknown_command(self, sim):
        _, port, _ = sim
        resp = send_sppl(port, "~XYZABC^")
        assert "FAIL" in resp
