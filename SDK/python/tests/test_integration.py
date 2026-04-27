"""Integration tests: real TcpRouterClient talking to MockServer over localhost TCP."""

from __future__ import annotations

import time
from typing import List

import pytest

from csm_tcp_router_client import ServerError, StatusNotification, TcpRouterClient
from csm_tcp_router_client import TimeoutError as RouterTimeoutError

# All tests in this module use the `mock_server` fixture from conftest.py.


# ---------------------------------------------------------------------------
# Connection lifecycle
# ---------------------------------------------------------------------------

class TestConnection:
    def test_connect_and_disconnect(self, mock_server):
        client = TcpRouterClient()
        client.connect(mock_server.host, mock_server.port)
        assert client.connected
        client.disconnect()
        assert not client.connected

    def test_context_manager(self, mock_server):
        with TcpRouterClient() as client:
            client.connect(mock_server.host, mock_server.port)
            assert client.connected
        assert not client.connected

    def test_connect_bad_port_raises(self):
        client = TcpRouterClient()
        from csm_tcp_router_client import ConnectionError as RouterConnectionError
        with pytest.raises(RouterConnectionError):
            client.connect("127.0.0.1", 1, timeout=0.5)

    def test_wait_for_server_success(self, mock_server):
        client = TcpRouterClient()
        ok = client.wait_for_server(
            mock_server.host, mock_server.port, timeout=5.0, retry_interval=0.1
        )
        assert ok is True

    def test_wait_for_server_timeout(self):
        client = TcpRouterClient()
        ok = client.wait_for_server("127.0.0.1", 1, timeout=0.3, retry_interval=0.1)
        assert ok is False


# ---------------------------------------------------------------------------
# Ping
# ---------------------------------------------------------------------------

class TestPing:
    def test_ping_returns_true_and_elapsed(self, mock_server):
        with TcpRouterClient() as client:
            client.connect(mock_server.host, mock_server.port)
            ok, elapsed = client.ping(timeout=2.0)
        assert ok is True
        assert elapsed > 0


# ---------------------------------------------------------------------------
# Synchronous command (send_and_wait)
# ---------------------------------------------------------------------------

class TestSendAndWait:
    def test_list_modules(self, mock_server):
        with TcpRouterClient() as client:
            client.connect(mock_server.host, mock_server.port)
            resp = client.send_and_wait("List", timeout=2.0)
        assert "AI" in resp.text
        assert "DIO" in resp.text

    def test_list_api(self, mock_server):
        with TcpRouterClient() as client:
            client.connect(mock_server.host, mock_server.port)
            text = client.list_api("DAQmx", timeout=2.0)
        assert "DAQmx" in text

    def test_list_states(self, mock_server):
        with TcpRouterClient() as client:
            client.connect(mock_server.host, mock_server.port)
            text = client.list_states("DAQmx", timeout=2.0)
        assert "Idle" in text or "Running" in text

    def test_custom_command_response(self, mock_server):
        mock_server.set_response("My Command", "My Reply")
        with TcpRouterClient() as client:
            client.connect(mock_server.host, mock_server.port)
            resp = client.send_and_wait("My Command", timeout=2.0)
        assert resp.text == "My Reply"

    def test_server_error_raises(self, mock_server):
        mock_server.set_error_response("Bad Command", "[Error: 7] not found")
        with TcpRouterClient() as client:
            client.connect(mock_server.host, mock_server.port)
            with pytest.raises(ServerError) as exc_info:
                client.send_and_wait("Bad Command", timeout=2.0)
        assert exc_info.value.code == "7"

    def test_list_modules_helper(self, mock_server):
        with TcpRouterClient() as client:
            client.connect(mock_server.host, mock_server.port)
            text = client.list_modules(timeout=2.0)
        assert "AI" in text

    def test_command_received_by_server(self, mock_server):
        mock_server.set_response("API: Probe -@ Sensor", "ok")
        with TcpRouterClient() as client:
            client.connect(mock_server.host, mock_server.port)
            client.send_and_wait("API: Probe -@ Sensor", timeout=2.0)
        received = mock_server.get_received(timeout=0.5)
        assert received == "API: Probe -@ Sensor"


# ---------------------------------------------------------------------------
# Async command (post)
# ---------------------------------------------------------------------------

class TestPost:
    def test_post_command_sends_and_receives_handshake(self, mock_server):
        # MockServer sends CMD_RESP for unknown commands by default
        with TcpRouterClient() as client:
            client.connect(mock_server.host, mock_server.port)
            client.post("API: Start -> DIO", timeout=2.0)  # should not raise

    def test_post_no_reply_sends_and_receives_handshake(self, mock_server):
        with TcpRouterClient() as client:
            client.connect(mock_server.host, mock_server.port)
            client.post_no_reply("API: Reset ->| DIO", timeout=2.0)

    def test_post_error_raises(self, mock_server):
        mock_server.set_error_response("API: Start -> DIO", "module missing")
        with TcpRouterClient() as client:
            client.connect(mock_server.host, mock_server.port)
            with pytest.raises(ServerError):
                client.post("API: Start -> DIO", timeout=2.0)


# ---------------------------------------------------------------------------
# Status subscriptions
# ---------------------------------------------------------------------------

class TestStatusSubscriptions:
    def test_subscribe_receives_status_via_queue(self, mock_server):
        with TcpRouterClient() as client:
            client.connect(mock_server.host, mock_server.port)
            client.subscribe_status("Status", "AI", timeout=2.0)

            # Server pushes a STATUS packet
            mock_server.push_status("Status >> 42.5 <- AI")

            notif = client.status_queue.get(timeout=2.0)
        assert notif.status_name == "Status"
        assert notif.data == "42.5"
        assert notif.module_name == "AI"

    def test_subscribe_callback_is_invoked(self, mock_server):
        received: List[StatusNotification] = []

        with TcpRouterClient() as client:
            client.connect(mock_server.host, mock_server.port)
            client.subscribe_status("Status", "AI", callback=received.append, timeout=2.0)
            mock_server.push_status("Status >> hello <- AI")
            time.sleep(0.3)

        assert len(received) == 1
        assert received[0].data == "hello"

    def test_unsubscribe_removes_callback(self, mock_server):
        received: List[StatusNotification] = []

        with TcpRouterClient() as client:
            client.connect(mock_server.host, mock_server.port)
            client.subscribe_status("Status", "AI", callback=received.append, timeout=2.0)
            client.unsubscribe_status("Status", "AI", timeout=2.0)
            mock_server.push_status("Status >> ignored <- AI")
            time.sleep(0.3)

        # Callback was removed so it should not have been called
        assert len(received) == 0

    def test_multiple_status_notifications(self, mock_server):
        received: List[StatusNotification] = []

        with TcpRouterClient() as client:
            client.connect(mock_server.host, mock_server.port)
            client.subscribe_status("Temp", "Sensor", callback=received.append, timeout=2.0)
            for i in range(5):
                mock_server.push_status(f"Temp >> {i} <- Sensor")
            time.sleep(0.5)

        assert len(received) == 5
        values = [n.data for n in received]
        assert values == ["0", "1", "2", "3", "4"]


# ---------------------------------------------------------------------------
# Timeout on disconnect
# ---------------------------------------------------------------------------

class TestDisconnectBehaviour:
    def test_wait_raises_timeout_when_no_resp(self, mock_server):
        """send_and_wait raises TimeoutError when the server sends CMD_RESP instead of RESP.

        The mock server's default handler for unknown commands sends a CMD_RESP
        handshake, which goes to the cmd_resp queue.  send_and_wait waits on
        the resp queue, so it must time out.
        """
        # "Unknown Sync Command" has no registered response → server sends CMD_RESP
        with TcpRouterClient() as client:
            client.connect(mock_server.host, mock_server.port)
            with pytest.raises(RouterTimeoutError):
                client.send_and_wait("Unknown Sync Command", timeout=0.3)
