from __future__ import annotations

import socket
import threading
import time

import pytest

from simulator import PrinterSimulator, build_hs_response
from zebra_printer import parse_status


class TestBuildHsResponse:
    def test_ready_default(self):
        resp = build_hs_response()
        status = parse_status(resp)
        assert status.ready
        assert not status.paused
        assert not status.paper_out
        assert not status.ribbon_out
        assert not status.head_open
        assert status.label_length == 1245

    def test_paused(self):
        resp = build_hs_response(paused=True)
        status = parse_status(resp)
        assert not status.ready
        assert status.paused
        assert not status.paper_out

    def test_paper_out(self):
        resp = build_hs_response(paper_out=True)
        status = parse_status(resp)
        assert not status.ready
        assert status.paper_out

    def test_ribbon_out(self):
        resp = build_hs_response(ribbon_out=True)
        status = parse_status(resp)
        assert not status.ready
        assert status.ribbon_out

    def test_head_open(self):
        resp = build_hs_response(head_open=True)
        status = parse_status(resp)
        assert not status.ready
        assert status.head_open

    def test_multiple_errors(self):
        resp = build_hs_response(paused=True, paper_out=True, head_open=True)
        status = parse_status(resp)
        assert not status.ready
        assert status.paused
        assert status.paper_out
        assert status.head_open
        assert not status.ribbon_out

    def test_custom_label_length(self):
        resp = build_hs_response(label_length=800)
        status = parse_status(resp)
        assert status.label_length == 800

    def test_all_errors(self):
        resp = build_hs_response(
            paused=True, paper_out=True, ribbon_out=True, head_open=True,
        )
        status = parse_status(resp)
        assert status.paused
        assert status.paper_out
        assert status.ribbon_out
        assert status.head_open
        summary = status.summary()
        assert "paper out" in summary
        assert "paused" in summary
        assert "ribbon out" in summary
        assert "head open" in summary


def _free_port() -> int:
    with socket.socket() as s:
        s.bind(("127.0.0.1", 0))
        return s.getsockname()[1]


def _query_status(port: int) -> str:
    with socket.create_connection(("127.0.0.1", port), timeout=3) as conn:
        conn.sendall(b"~HS")
        conn.shutdown(socket.SHUT_WR)
        chunks = []
        while True:
            data = conn.recv(4096)
            if not data:
                break
            chunks.append(data)
    return b"".join(chunks).decode("ascii")


class TestSimulatorStatusModes:
    def _start_server(self, port, modes=None):
        server = PrinterSimulator(
            host="127.0.0.1", port=port,
            output_dir=None, status_modes=modes,
        )
        t = threading.Thread(target=server.serve_forever, daemon=True)
        t.start()
        time.sleep(0.1)
        return server

    def test_ready_mode(self):
        port = _free_port()
        server = self._start_server(port)
        try:
            raw = _query_status(port)
            status = parse_status(raw)
            assert status.ready
        finally:
            server.shutdown()
            server.server_close()

    def test_paused_mode(self):
        port = _free_port()
        server = self._start_server(port, modes=["paused"])
        try:
            raw = _query_status(port)
            status = parse_status(raw)
            assert status.paused
            assert not status.ready
        finally:
            server.shutdown()
            server.server_close()

    def test_paper_out_mode(self):
        port = _free_port()
        server = self._start_server(port, modes=["paper_out"])
        try:
            raw = _query_status(port)
            status = parse_status(raw)
            assert status.paper_out
        finally:
            server.shutdown()
            server.server_close()

    def test_head_open_mode(self):
        port = _free_port()
        server = self._start_server(port, modes=["head_open"])
        try:
            raw = _query_status(port)
            status = parse_status(raw)
            assert status.head_open
        finally:
            server.shutdown()
            server.server_close()

    def test_ribbon_out_mode(self):
        port = _free_port()
        server = self._start_server(port, modes=["ribbon_out"])
        try:
            raw = _query_status(port)
            status = parse_status(raw)
            assert status.ribbon_out
        finally:
            server.shutdown()
            server.server_close()

    def test_combined_modes(self):
        port = _free_port()
        server = self._start_server(port, modes=["paused", "paper_out"])
        try:
            raw = _query_status(port)
            status = parse_status(raw)
            assert status.paused
            assert status.paper_out
            assert not status.head_open
        finally:
            server.shutdown()
            server.server_close()

    def test_set_status_runtime(self):
        port = _free_port()
        server = self._start_server(port)
        try:
            raw = _query_status(port)
            assert parse_status(raw).ready

            server.set_status(["head_open"])
            raw = _query_status(port)
            status = parse_status(raw)
            assert status.head_open
            assert not status.ready

            server.set_status([])
            raw = _query_status(port)
            assert parse_status(raw).ready
        finally:
            server.shutdown()
            server.server_close()
