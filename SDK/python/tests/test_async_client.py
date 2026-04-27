"""Tests for AsyncTcpRouterClient – unit tests and integration tests."""

from __future__ import annotations

import asyncio
import time
from typing import List

import pytest

from csm_tcp_router_client import (
    AsyncResponse,
    AsyncTcpRouterClient,
    Packet,
    PacketType,
    ServerError,
    StatusNotification,
    _parse_server_error,
)
from csm_tcp_router_client import ConnectionError as RouterConnectionError
from csm_tcp_router_client import TimeoutError as RouterTimeoutError

# ---------------------------------------------------------------------------
# Helpers
# ---------------------------------------------------------------------------


def _make_packet(ptype: PacketType, text: str = "") -> Packet:
    return Packet(type=ptype, data=text.encode("utf-8"))


def _client_with_queues() -> AsyncTcpRouterClient:
    """Return a client with asyncio objects pre-initialised (no TCP connection)."""
    client = AsyncTcpRouterClient()
    client._init_async_objects()
    return client


# ---------------------------------------------------------------------------
# Unit tests: _parse_server_error  (sync – no asyncio needed)
# ---------------------------------------------------------------------------


def test_parse_error_plain_text():
    pkt = _make_packet(PacketType.ERROR, "something went wrong")
    err = _parse_server_error(pkt)
    assert err.message == "something went wrong"
    assert err.code == ""


def test_parse_error_csm_format():
    pkt = _make_packet(PacketType.ERROR, "[Error: 42] module not found")
    err = _parse_server_error(pkt)
    assert err.code == "42"
    assert err.message == "module not found"


def test_parse_error_malformed_bracket():
    pkt = _make_packet(PacketType.ERROR, "[Error: missing close")
    err = _parse_server_error(pkt)
    assert err.message == "[Error: missing close"


# ---------------------------------------------------------------------------
# Unit tests: packet dispatch
# ---------------------------------------------------------------------------


async def test_dispatch_resp():
    client = _client_with_queues()
    pkt = _make_packet(PacketType.RESP, "ok")
    await client._dispatch_packet(pkt)
    item = client._resp_queue.get_nowait()
    assert isinstance(item, Packet)
    assert item.data == b"ok"


async def test_dispatch_cmd_resp():
    client = _client_with_queues()
    await client._dispatch_packet(_make_packet(PacketType.CMD_RESP))
    assert not client._cmd_resp_queue.empty()


async def test_dispatch_error_unblocks_both_queues():
    client = _client_with_queues()
    await client._dispatch_packet(_make_packet(PacketType.ERROR, "[Error: 7] bad"))
    r = client._resp_queue.get_nowait()
    c = client._cmd_resp_queue.get_nowait()
    assert isinstance(r, ServerError) and r.code == "7"
    assert isinstance(c, ServerError)


async def test_dispatch_async_resp_to_queue():
    client = _client_with_queues()
    await client._dispatch_packet(
        _make_packet(PacketType.ASYNC_RESP, "result <- API: Start -> DIO")
    )
    ar = client.async_response_queue.get_nowait()
    assert ar.text == "result"
    assert ar.original_command == "API: Start -> DIO"


async def test_dispatch_async_resp_sync_callback():
    client = _client_with_queues()
    received: List[AsyncResponse] = []
    client.register_async_callback("API: Start -> DIO", received.append)
    await client._dispatch_packet(
        _make_packet(PacketType.ASYNC_RESP, "result <- API: Start -> DIO")
    )
    assert len(received) == 1 and received[0].text == "result"


async def test_dispatch_async_resp_async_callback():
    client = _client_with_queues()
    received: List[AsyncResponse] = []

    async def async_cb(ar: AsyncResponse) -> None:
        received.append(ar)

    client.register_async_callback("cmd", async_cb)
    await client._dispatch_packet(_make_packet(PacketType.ASYNC_RESP, "val <- cmd"))
    assert len(received) == 1


async def test_dispatch_status_to_queue():
    client = _client_with_queues()
    await client._dispatch_packet(_make_packet(PacketType.STATUS, "Status >> 42 <- AI"))
    notif = client.status_queue.get_nowait()
    assert notif.status_name == "Status"
    assert notif.data == "42"
    assert notif.module_name == "AI"


async def test_dispatch_status_sync_callback():
    client = _client_with_queues()
    received: List[StatusNotification] = []
    client._status_callbacks[("Status", "AI")] = received.append
    await client._dispatch_packet(_make_packet(PacketType.STATUS, "Status >> v1 <- AI"))
    assert len(received) == 1 and received[0].data == "v1"


async def test_dispatch_status_async_callback():
    client = _client_with_queues()
    received: List[StatusNotification] = []

    async def async_cb(notif: StatusNotification) -> None:
        received.append(notif)

    client._status_callbacks[("Temp", "Sensor")] = async_cb
    await client._dispatch_packet(_make_packet(PacketType.STATUS, "Temp >> 25.5 <- Sensor"))
    assert len(received) == 1 and received[0].data == "25.5"


async def test_dispatch_interrupt_to_queue():
    client = _client_with_queues()
    await client._dispatch_packet(
        _make_packet(PacketType.INTERRUPT, "Stop >> 1 <- AI")
    )
    notif = client.status_queue.get_nowait()
    assert notif.packet_type == PacketType.INTERRUPT


async def test_dispatch_info_silently_discarded():
    client = _client_with_queues()
    await client._dispatch_packet(_make_packet(PacketType.INFO, "Welcome"))
    assert client._resp_queue.empty()
    assert client._cmd_resp_queue.empty()


async def test_notify_disconnect_puts_sentinels():
    client = _client_with_queues()
    client._notify_disconnect()
    r = client._resp_queue.get_nowait()
    c = client._cmd_resp_queue.get_nowait()
    assert isinstance(r, RouterConnectionError)
    assert isinstance(c, RouterConnectionError)


# ---------------------------------------------------------------------------
# Unit tests: callback management
# ---------------------------------------------------------------------------


def test_unregister_async_callback_noop_if_missing():
    client = AsyncTcpRouterClient()
    client.unregister_async_callback("nonexistent")  # must not raise


# ---------------------------------------------------------------------------
# Unit tests: timeout waiters
# ---------------------------------------------------------------------------


async def test_wait_for_resp_timeout():
    client = _client_with_queues()
    with pytest.raises(RouterTimeoutError, match=r"0\.1s"):
        await client._wait_for_resp(timeout=0.1)


async def test_wait_for_cmd_resp_timeout():
    client = _client_with_queues()
    with pytest.raises(RouterTimeoutError, match=r"0\.1s"):
        await client._wait_for_cmd_resp(timeout=0.1)


async def test_wait_for_resp_raises_server_error():
    client = _client_with_queues()
    client._resp_queue.put_nowait(ServerError("boom", "5"))
    with pytest.raises(ServerError, match="boom"):
        await client._wait_for_resp(timeout=1.0)


async def test_wait_for_resp_raises_connection_error():
    client = _client_with_queues()
    client._resp_queue.put_nowait(RouterConnectionError("lost"))
    with pytest.raises(RouterConnectionError):
        await client._wait_for_resp(timeout=1.0)


# ---------------------------------------------------------------------------
# Integration tests (real TCP via MockServer fixture)
# ---------------------------------------------------------------------------


class TestConnection:
    async def test_connect_and_disconnect(self, mock_server):
        client = AsyncTcpRouterClient()
        await client.connect(mock_server.host, mock_server.port)
        assert client.connected
        await client.disconnect()
        assert not client.connected

    async def test_async_context_manager(self, mock_server):
        async with AsyncTcpRouterClient() as client:
            await client.connect(mock_server.host, mock_server.port)
            assert client.connected
        assert not client.connected

    async def test_connect_already_connected_raises(self, mock_server):
        async with AsyncTcpRouterClient() as client:
            await client.connect(mock_server.host, mock_server.port)
            with pytest.raises(RouterConnectionError, match="Already connected"):
                await client.connect(mock_server.host, mock_server.port)

    async def test_connect_bad_port_raises(self):
        client = AsyncTcpRouterClient()
        with pytest.raises(RouterConnectionError):
            await client.connect("127.0.0.1", 1, timeout=0.5)

    async def test_wait_for_server_success(self, mock_server):
        client = AsyncTcpRouterClient()
        ok = await client.wait_for_server(
            mock_server.host, mock_server.port, timeout=5.0, retry_interval=0.1
        )
        assert ok is True

    async def test_wait_for_server_timeout(self):
        client = AsyncTcpRouterClient()
        ok = await client.wait_for_server("127.0.0.1", 1, timeout=0.3, retry_interval=0.1)
        assert ok is False


class TestPing:
    async def test_ping_returns_true_and_elapsed(self, mock_server):
        async with AsyncTcpRouterClient() as client:
            await client.connect(mock_server.host, mock_server.port)
            ok, elapsed = await client.ping(timeout=2.0)
        assert ok is True
        assert elapsed > 0


class TestSendAndWait:
    async def test_list_modules(self, mock_server):
        async with AsyncTcpRouterClient() as client:
            await client.connect(mock_server.host, mock_server.port)
            text = await client.list_modules(timeout=2.0)
        assert "AI" in text and "DIO" in text

    async def test_list_api(self, mock_server):
        async with AsyncTcpRouterClient() as client:
            await client.connect(mock_server.host, mock_server.port)
            text = await client.list_api("DAQmx", timeout=2.0)
        assert "DAQmx" in text

    async def test_list_states(self, mock_server):
        async with AsyncTcpRouterClient() as client:
            await client.connect(mock_server.host, mock_server.port)
            text = await client.list_states("AI", timeout=2.0)
        assert "AI" in text

    async def test_custom_command(self, mock_server):
        mock_server.set_response("My Cmd", "My Reply")
        async with AsyncTcpRouterClient() as client:
            await client.connect(mock_server.host, mock_server.port)
            resp = await client.send_and_wait("My Cmd", timeout=2.0)
        assert resp.text == "My Reply"
        assert resp.raw == b"My Reply"

    async def test_server_error_raises(self, mock_server):
        mock_server.set_error_response("Bad Cmd", "[Error: 9] nope")
        async with AsyncTcpRouterClient() as client:
            await client.connect(mock_server.host, mock_server.port)
            with pytest.raises(ServerError) as exc_info:
                await client.send_and_wait("Bad Cmd", timeout=2.0)
        assert exc_info.value.code == "9"
        assert exc_info.value.message == "nope"

    async def test_timeout_when_server_sends_only_cmd_resp(self, mock_server):
        """send_and_wait should time out when server sends CMD_RESP instead of RESP."""
        async with AsyncTcpRouterClient() as client:
            await client.connect(mock_server.host, mock_server.port)
            with pytest.raises(RouterTimeoutError):
                # Default mock sends CMD_RESP for unknown commands, never RESP
                await client.send_and_wait("Unknown Async", timeout=0.3)

    async def test_concurrent_commands(self, mock_server):
        """Two sequential send_and_wait calls on the same client both succeed."""
        mock_server.set_response("Cmd1", "R1")
        mock_server.set_response("Cmd2", "R2")
        async with AsyncTcpRouterClient() as client:
            await client.connect(mock_server.host, mock_server.port)
            r1 = await client.send_and_wait("Cmd1", timeout=2.0)
            r2 = await client.send_and_wait("Cmd2", timeout=2.0)
        assert r1.text == "R1"
        assert r2.text == "R2"


class TestPost:
    async def test_post_command(self, mock_server):
        async with AsyncTcpRouterClient() as client:
            await client.connect(mock_server.host, mock_server.port)
            await client.post("API: Start -> DIO", timeout=2.0)

    async def test_post_no_reply(self, mock_server):
        async with AsyncTcpRouterClient() as client:
            await client.connect(mock_server.host, mock_server.port)
            await client.post_no_reply("API: Reset ->| DIO", timeout=2.0)

    async def test_post_error_raises(self, mock_server):
        mock_server.set_error_response("API: Start -> DIO", "module missing")
        async with AsyncTcpRouterClient() as client:
            await client.connect(mock_server.host, mock_server.port)
            with pytest.raises(ServerError):
                await client.post("API: Start -> DIO", timeout=2.0)


class TestSubscriptions:
    async def test_subscribe_receives_via_queue(self, mock_server):
        async with AsyncTcpRouterClient() as client:
            await client.connect(mock_server.host, mock_server.port)
            await client.subscribe_status("Status", "AI", timeout=2.0)
            mock_server.push_status("Status >> 99 <- AI")
            assert client.status_queue is not None
            notif = await asyncio.wait_for(client.status_queue.get(), timeout=2.0)
        assert notif.status_name == "Status"
        assert notif.data == "99"
        assert notif.module_name == "AI"

    async def test_subscribe_sync_callback(self, mock_server):
        received: List[StatusNotification] = []
        async with AsyncTcpRouterClient() as client:
            await client.connect(mock_server.host, mock_server.port)
            await client.subscribe_status(
                "Status", "AI", callback=received.append, timeout=2.0
            )
            mock_server.push_status("Status >> hello <- AI")
            await asyncio.sleep(0.3)
        assert len(received) == 1 and received[0].data == "hello"

    async def test_subscribe_async_callback(self, mock_server):
        received: List[StatusNotification] = []

        async def async_cb(notif: StatusNotification) -> None:
            received.append(notif)

        async with AsyncTcpRouterClient() as client:
            await client.connect(mock_server.host, mock_server.port)
            await client.subscribe_status("Status", "AI", callback=async_cb, timeout=2.0)
            mock_server.push_status("Status >> world <- AI")
            await asyncio.sleep(0.3)
        assert len(received) == 1 and received[0].data == "world"

    async def test_unsubscribe_stops_callback(self, mock_server):
        received: List[StatusNotification] = []
        async with AsyncTcpRouterClient() as client:
            await client.connect(mock_server.host, mock_server.port)
            await client.subscribe_status(
                "Status", "AI", callback=received.append, timeout=2.0
            )
            await client.unsubscribe_status("Status", "AI", timeout=2.0)
            mock_server.push_status("Status >> ignored <- AI")
            await asyncio.sleep(0.2)
        assert len(received) == 0

    async def test_multiple_notifications_in_order(self, mock_server):
        received: List[StatusNotification] = []
        async with AsyncTcpRouterClient() as client:
            await client.connect(mock_server.host, mock_server.port)
            await client.subscribe_status(
                "Temp", "Sensor", callback=received.append, timeout=2.0
            )
            for i in range(5):
                mock_server.push_status(f"Temp >> {i} <- Sensor")
            await asyncio.sleep(0.5)
        assert len(received) == 5
        assert [n.data for n in received] == ["0", "1", "2", "3", "4"]

    async def test_subscribe_error_rolls_back_callback(self, mock_server):
        mock_server.set_error_response(
            "Status@AI -><register>", "[Error: 1] denied"
        )
        received: List[StatusNotification] = []
        async with AsyncTcpRouterClient() as client:
            await client.connect(mock_server.host, mock_server.port)
            with pytest.raises(ServerError):
                await client.subscribe_status(
                    "Status", "AI", callback=received.append, timeout=2.0
                )
            # Callback must be removed on failure
            assert ("Status", "AI") not in client._status_callbacks


class TestConnectedProperty:
    async def test_not_connected_before_connect(self):
        client = AsyncTcpRouterClient()
        assert not client.connected

    async def test_connected_after_connect(self, mock_server):
        client = AsyncTcpRouterClient()
        await client.connect(mock_server.host, mock_server.port)
        assert client.connected
        await client.disconnect()

    async def test_not_connected_after_disconnect(self, mock_server):
        client = AsyncTcpRouterClient()
        await client.connect(mock_server.host, mock_server.port)
        await client.disconnect()
        assert not client.connected

    async def test_send_when_not_connected_raises(self):
        client = AsyncTcpRouterClient()
        client._init_async_objects()
        with pytest.raises(RouterConnectionError, match="Not connected"):
            await client.send_and_wait("Ping", timeout=0.1)


class TestTimingAndPerformance:
    async def test_elapsed_time_is_positive(self, mock_server):
        async with AsyncTcpRouterClient() as client:
            await client.connect(mock_server.host, mock_server.port)
            t0 = time.monotonic()
            await client.send_and_wait("Ping", timeout=2.0)
            elapsed = time.monotonic() - t0
        assert elapsed >= 0
