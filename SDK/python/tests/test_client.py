"""Unit tests for TcpRouterClient using a mock transport."""

from __future__ import annotations

import threading
import time
from typing import List
from unittest.mock import MagicMock, patch

import pytest

from csm_tcp_router_client import (
    AsyncResponse,
    Packet,
    PacketType,
    ServerError,
    StatusNotification,
    TcpRouterClient,
    _parse_server_error,
)
from csm_tcp_router_client import ConnectionError as RouterConnectionError
from csm_tcp_router_client import TimeoutError as RouterTimeoutError

# ---------------------------------------------------------------------------
# Helpers: inject packets directly into the client's dispatch method
# ---------------------------------------------------------------------------

def make_packet(ptype: PacketType, text: str = "") -> Packet:
    return Packet(type=ptype, data=text.encode("utf-8"))


def inject(client: TcpRouterClient, packet: Packet) -> None:
    """Simulate the receive thread delivering a packet."""
    client._on_packet(packet)


# ---------------------------------------------------------------------------
# _parse_server_error
# ---------------------------------------------------------------------------

class TestParseServerError:
    def test_plain_message(self):
        pkt = make_packet(PacketType.ERROR, "something went wrong")
        err = _parse_server_error(pkt)
        assert err.message == "something went wrong"
        assert err.code == ""

    def test_csm_format(self):
        pkt = make_packet(PacketType.ERROR, "[Error: 42] module not found")
        err = _parse_server_error(pkt)
        assert err.code == "42"
        assert err.message == "module not found"
        assert str(err) == "[Error: 42] module not found"

    def test_csm_format_no_message(self):
        pkt = make_packet(PacketType.ERROR, "[Error: 0]")
        err = _parse_server_error(pkt)
        assert err.code == "0"
        assert err.message == ""

    def test_malformed_bracket_no_crash(self):
        pkt = make_packet(PacketType.ERROR, "[Error: no closing bracket")
        err = _parse_server_error(pkt)
        assert err.code == ""
        assert "no closing bracket" in err.message


# ---------------------------------------------------------------------------
# TcpRouterClient internal dispatch
# ---------------------------------------------------------------------------

class TestPacketDispatch:
    def _client_no_transport(self) -> TcpRouterClient:
        """Return a client with a mocked (never-connecting) transport."""
        client = TcpRouterClient()
        client._transport = MagicMock()
        client._transport.connected = True
        client._transport.send_raw = MagicMock()
        return client

    def test_resp_unblocks_wait_for_resp(self):
        client = self._client_no_transport()
        pkt = make_packet(PacketType.RESP, "ok")
        threading.Timer(0.05, inject, args=(client, pkt)).start()
        resp = client._wait_for_resp(timeout=1.0)
        assert resp.text == "ok"

    def test_cmd_resp_unblocks_wait_for_cmd_resp(self):
        client = self._client_no_transport()
        pkt = make_packet(PacketType.CMD_RESP)
        threading.Timer(0.05, inject, args=(client, pkt)).start()
        client._wait_for_cmd_resp(timeout=1.0)  # should not raise

    def test_error_unblocks_resp_waiter_with_exception(self):
        client = self._client_no_transport()
        pkt = make_packet(PacketType.ERROR, "[Error: 1] bad")
        threading.Timer(0.05, inject, args=(client, pkt)).start()
        with pytest.raises(ServerError):
            client._wait_for_resp(timeout=1.0)

    def test_error_unblocks_cmd_resp_waiter_with_exception(self):
        client = self._client_no_transport()
        pkt = make_packet(PacketType.ERROR, "no module")
        threading.Timer(0.05, inject, args=(client, pkt)).start()
        with pytest.raises(ServerError):
            client._wait_for_cmd_resp(timeout=1.0)

    def test_async_resp_added_to_queue(self):
        client = self._client_no_transport()
        pkt = make_packet(PacketType.ASYNC_RESP, "result <- API: Start -> DIO")
        inject(client, pkt)
        ar = client.async_response_queue.get(timeout=0.5)
        assert ar.text == "result"
        assert ar.original_command == "API: Start -> DIO"

    def test_async_resp_calls_callback(self):
        client = self._client_no_transport()
        received: List[AsyncResponse] = []
        client.register_async_callback("API: Start -> DIO", received.append)
        pkt = make_packet(PacketType.ASYNC_RESP, "result <- API: Start -> DIO")
        inject(client, pkt)
        time.sleep(0.05)
        assert len(received) == 1
        assert received[0].text == "result"

    def test_status_added_to_queue(self):
        client = self._client_no_transport()
        pkt = make_packet(PacketType.STATUS, "Status >> value42 <- AI")
        inject(client, pkt)
        notif = client.status_queue.get(timeout=0.5)
        assert notif.status_name == "Status"
        assert notif.data == "value42"
        assert notif.module_name == "AI"

    def test_status_calls_registered_callback(self):
        client = self._client_no_transport()
        received: List[StatusNotification] = []

        with patch.object(client._transport, "send_raw"), patch.object(client, "_wait_for_cmd_resp"):
            client._status_callbacks[("Status", "AI")] = received.append

        pkt = make_packet(PacketType.STATUS, "Status >> v1 <- AI")
        inject(client, pkt)
        time.sleep(0.05)
        assert len(received) == 1
        assert received[0].data == "v1"

    def test_interrupt_added_to_status_queue(self):
        client = self._client_no_transport()
        pkt = make_packet(PacketType.INTERRUPT, "Alarm >> fire <- Safety")
        inject(client, pkt)
        notif = client.status_queue.get(timeout=0.5)
        assert notif.packet_type == PacketType.INTERRUPT
        assert notif.status_name == "Alarm"

    def test_info_packet_silently_discarded(self):
        client = self._client_no_transport()
        pkt = make_packet(PacketType.INFO, "Welcome to the server")
        inject(client, pkt)
        assert client._resp_queue.empty()
        assert client._cmd_resp_queue.empty()

    def test_on_disconnect_unblocks_waiters(self):
        client = self._client_no_transport()
        threading.Timer(0.05, client._on_disconnect).start()
        with pytest.raises(RouterConnectionError):
            client._wait_for_resp(timeout=1.0)


# ---------------------------------------------------------------------------
# Timeout behaviour
# ---------------------------------------------------------------------------

class TestTimeouts:
    def _client(self) -> TcpRouterClient:
        client = TcpRouterClient()
        client._transport = MagicMock()
        client._transport.connected = True
        client._transport.send_raw = MagicMock()
        return client

    def test_wait_for_resp_timeout(self):
        client = self._client()
        with pytest.raises(RouterTimeoutError, match=r"0\.1s"):
            client._wait_for_resp(timeout=0.1)

    def test_wait_for_cmd_resp_timeout(self):
        client = self._client()
        with pytest.raises(RouterTimeoutError, match=r"0\.1s"):
            client._wait_for_cmd_resp(timeout=0.1)


# ---------------------------------------------------------------------------
# ping convenience method
# ---------------------------------------------------------------------------

class TestPing:
    def test_ping_success_returns_true_and_elapsed(self):
        client = TcpRouterClient()
        client._transport = MagicMock()
        client._transport.connected = True
        client._transport.send_raw = MagicMock()

        threading.Timer(
            0.02,
            inject,
            args=(client, make_packet(PacketType.RESP, "Pong")),
        ).start()
        ok, elapsed = client.ping(timeout=1.0)
        assert ok is True
        assert elapsed > 0

    def test_ping_failure_returns_false(self):
        client = TcpRouterClient()
        client._transport = MagicMock()
        client._transport.connected = True
        client._transport.send_raw = MagicMock()
        # No packet injected → times out
        ok, elapsed = client.ping(timeout=0.05)
        assert ok is False
        assert elapsed == 0.0


# ---------------------------------------------------------------------------
# Context manager
# ---------------------------------------------------------------------------

def test_context_manager_calls_disconnect():
    client = TcpRouterClient()
    client._transport = MagicMock()
    client._transport.connected = False

    with client:
        pass

    client._transport.disconnect.assert_called_once()


# ---------------------------------------------------------------------------
# subscribe_status / unsubscribe_status
# ---------------------------------------------------------------------------

class TestSubscriptions:
    def _client_with_mock_handshake(self) -> TcpRouterClient:
        client = TcpRouterClient()
        client._transport = MagicMock()
        client._transport.connected = True
        client._transport.send_raw = MagicMock()
        # Patch _wait_for_cmd_resp to succeed immediately
        client._wait_for_cmd_resp = MagicMock()
        return client

    def test_subscribe_stores_callback(self):
        client = self._client_with_mock_handshake()
        cb = MagicMock()
        client.subscribe_status("Status", "AI", callback=cb)
        assert client._status_callbacks[("Status", "AI")] is cb

    def test_subscribe_sends_register_command(self):
        client = self._client_with_mock_handshake()
        client.subscribe_status("Status", "AI")
        wire = client._transport.send_raw.call_args[0][0]
        assert b"Status@AI -><register>" in wire

    def test_unsubscribe_removes_callback(self):
        client = self._client_with_mock_handshake()
        client._status_callbacks[("Status", "AI")] = MagicMock()
        client.unsubscribe_status("Status", "AI")
        assert ("Status", "AI") not in client._status_callbacks

    def test_unsubscribe_sends_unregister_command(self):
        client = self._client_with_mock_handshake()
        client.unsubscribe_status("Status", "AI")
        wire = client._transport.send_raw.call_args[0][0]
        assert b"Status@AI -><unregister>" in wire

    def test_subscribe_cleans_up_callback_on_error(self):
        client = TcpRouterClient()
        client._transport = MagicMock()
        client._transport.connected = True
        client._transport.send_raw = MagicMock()
        # Make _wait_for_cmd_resp raise
        client._wait_for_cmd_resp = MagicMock(side_effect=RouterTimeoutError("t/o"))

        cb = MagicMock()
        with pytest.raises(RouterTimeoutError):
            client.subscribe_status("Status", "AI", callback=cb)
        assert ("Status", "AI") not in client._status_callbacks


# ---------------------------------------------------------------------------
# register_async_callback / unregister_async_callback
# ---------------------------------------------------------------------------

class TestAsyncCallbacks:
    def test_register_and_unregister(self):
        client = TcpRouterClient()
        cb = MagicMock()
        client.register_async_callback("cmd", cb)
        assert client._async_callbacks["cmd"] is cb
        client.unregister_async_callback("cmd")
        assert "cmd" not in client._async_callbacks
