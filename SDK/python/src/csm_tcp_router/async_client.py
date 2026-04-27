"""Asyncio-based CSM-TCP-Router client."""

from __future__ import annotations

import asyncio
import inspect
import struct
import time
from typing import Any, Callable, Coroutine, Dict, Optional, Tuple, Union

from ._errors import _parse_server_error
from ._protocol import HEADER_SIZE, encode_packet, parse_packet
from .exceptions import ConnectionError as RouterConnectionError
from .exceptions import ProtocolError, ServerError
from .exceptions import TimeoutError as RouterTimeoutError
from .models import (
    AsyncResponse,
    CommandResponse,
    Packet,
    PacketType,
    StatusNotification,
)

__all__ = ["AsyncTcpRouterClient"]

# ---------------------------------------------------------------------------
# Callback type aliases – both plain callables and async coroutines are accepted
# ---------------------------------------------------------------------------

_SyncStatusCb = Callable[[StatusNotification], None]
_AsyncStatusCb = Callable[[StatusNotification], "Coroutine[Any, Any, None]"]
StatusCallback = Union[_SyncStatusCb, _AsyncStatusCb]

_SyncAsyncRespCb = Callable[[AsyncResponse], None]
_AsyncAsyncRespCb = Callable[[AsyncResponse], "Coroutine[Any, Any, None]"]
AsyncRespCallback = Union[_SyncAsyncRespCb, _AsyncAsyncRespCb]

_SubKey = Tuple[str, str]


class AsyncTcpRouterClient:
    """Asyncio client for a CSM-TCP-Router server.

    Provides the same interface as :class:`~csm_tcp_router.TcpRouterClient` but
    as ``async def`` coroutines, suitable for use inside an asyncio event loop.

    **Quickstart**::

        import asyncio
        from csm_tcp_router import AsyncTcpRouterClient

        async def main():
            async with AsyncTcpRouterClient() as client:
                await client.connect("localhost", 30007)
                print(await client.list_modules())
                resp = await client.send_and_wait("API: Read -@ DAQmx")
                print(resp.text)

        asyncio.run(main())

    **Protocol flows** are identical to :class:`~csm_tcp_router.TcpRouterClient`.

    **Callbacks** passed to :meth:`subscribe_status` and
    :meth:`register_async_callback` may be either a plain callable *or* an
    ``async def`` coroutine — both are supported.

    **Polling queues** (:attr:`async_response_queue`, :attr:`status_queue`) are
    created when :meth:`connect` is called and are bound to the running event
    loop.  Access them only after :meth:`connect` has been awaited.
    """

    def __init__(self) -> None:
        self._reader: Optional[asyncio.StreamReader] = None
        self._writer: Optional[asyncio.StreamWriter] = None
        self._recv_task: Optional[asyncio.Task[None]] = None

        # Asyncio objects created lazily in connect() to bind to the running loop
        self._resp_queue: Optional[asyncio.Queue[object]] = None
        self._cmd_resp_queue: Optional[asyncio.Queue[object]] = None
        self._send_lock: Optional[asyncio.Lock] = None
        # Serialisation locks – at most one in-flight RESP / CMD_RESP waiter
        self._resp_lock: Optional[asyncio.Lock] = None
        self._cmd_resp_lock: Optional[asyncio.Lock] = None

        #: Polling queue for :class:`~csm_tcp_router.models.AsyncResponse` objects
        #: received from the server.  Available after :meth:`connect` is called.
        self.async_response_queue: Optional[asyncio.Queue[AsyncResponse]] = None

        #: Polling queue for :class:`~csm_tcp_router.models.StatusNotification`
        #: objects received from the server.  Available after :meth:`connect`.
        self.status_queue: Optional[asyncio.Queue[StatusNotification]] = None

        # Callback registries – plain dicts (asyncio is single-threaded)
        self._status_callbacks: Dict[_SubKey, Optional[StatusCallback]] = {}
        self._async_callbacks: Dict[str, AsyncRespCallback] = {}

    # ------------------------------------------------------------------
    # Connection management
    # ------------------------------------------------------------------

    def _init_async_objects(self) -> None:
        """(Re)create asyncio objects bound to the current running loop."""
        self._resp_queue = asyncio.Queue()
        self._cmd_resp_queue = asyncio.Queue()
        self._send_lock = asyncio.Lock()
        self._resp_lock = asyncio.Lock()
        self._cmd_resp_lock = asyncio.Lock()
        self.async_response_queue = asyncio.Queue()
        self.status_queue = asyncio.Queue()

    @property
    def connected(self) -> bool:
        """``True`` while the writer is open and not being closed."""
        return self._writer is not None and not self._writer.is_closing()

    async def connect(self, host: str, port: int, timeout: float = 5.0) -> None:
        """Open a TCP connection and start the background receive task.

        :param host: Server hostname or IP address.
        :param port: Server TCP port (the reference app defaults to 30007).
        :param timeout: Connection timeout in seconds.
        :raises ConnectionError: if already connected or the OS refuses.
        """
        if self.connected:
            raise RouterConnectionError(
                "Already connected; call disconnect() first."
            )
        self._init_async_objects()
        try:
            self._reader, self._writer = await asyncio.wait_for(
                asyncio.open_connection(host, port), timeout=timeout
            )
        except asyncio.TimeoutError:
            raise RouterConnectionError(
                f"Connection to {host}:{port} timed out after {timeout:.1f}s."
            ) from None
        except OSError as exc:
            raise RouterConnectionError(
                f"Cannot connect to {host}:{port}: {exc}"
            ) from exc
        self._recv_task = asyncio.ensure_future(self._recv_loop())

    async def disconnect(self) -> None:
        """Close the connection and stop the background receive task.

        Safe to call even if not currently connected.  Any coroutines currently
        blocked inside :meth:`send_and_wait`, :meth:`post`, or similar methods
        will receive a :exc:`~csm_tcp_router.exceptions.ConnectionError`
        immediately rather than waiting for their timeout to expire.
        """
        # Wake blocked waiters *before* cancelling the recv task.
        sentinel = RouterConnectionError("Disconnected from server.")
        if self._resp_queue is not None:
            self._resp_queue.put_nowait(sentinel)
        if self._cmd_resp_queue is not None:
            self._cmd_resp_queue.put_nowait(sentinel)
        # Cancel the recv task first; its finally block notifies pending waiters
        if self._recv_task is not None and not self._recv_task.done():
            self._recv_task.cancel()
            try:
                await self._recv_task
            except (asyncio.CancelledError, Exception):
                pass
        self._recv_task = None

        if self._writer is not None:
            try:
                self._writer.close()
                await self._writer.wait_closed()
            except OSError:
                pass
            self._writer = None
        self._reader = None

    async def wait_for_server(
        self,
        host: str,
        port: int,
        timeout: float = 30.0,
        retry_interval: float = 0.5,
    ) -> bool:
        """Poll until *host*:*port* accepts a connection or *timeout* elapses.

        :param host: Server hostname or IP address.
        :param port: Server TCP port.
        :param timeout: Maximum time to wait in seconds.
        :param retry_interval: Pause between retries in seconds.
        :returns: ``True`` when the server is reachable; ``False`` on timeout.
        """
        deadline = time.monotonic() + timeout
        while time.monotonic() < deadline:
            try:
                _, writer = await asyncio.wait_for(
                    asyncio.open_connection(host, port), timeout=1.0
                )
                writer.close()
                try:
                    await writer.wait_closed()
                except OSError:
                    pass
                return True
            except (OSError, asyncio.TimeoutError):
                pass
            await asyncio.sleep(retry_interval)
        return False

    # ------------------------------------------------------------------
    # Core command methods
    # ------------------------------------------------------------------

    async def send_and_wait(
        self, command: str, timeout: float = 5.0
    ) -> CommandResponse:
        """Send a **synchronous** command and await the response.

        Use the CSM synchronous suffix ``-@`` in *command*::

            resp = await client.send_and_wait("API: Read -@ DAQmx")
            print(resp.text)

        :param command: CSM command string.
        :param timeout: Seconds to wait for the ``resp`` packet.
        :raises ConnectionError: if not connected.
        :raises TimeoutError: if no response arrives within *timeout*.
        :raises ServerError: if the server returns an error packet.
        """
        wire = encode_packet(command.encode("utf-8"), PacketType.CMD)
        assert self._resp_lock is not None
        async with self._resp_lock:
            await self._send_raw(wire)
            return await self._wait_for_resp(timeout)

    async def post(self, command: str, timeout: float = 5.0) -> None:
        """Send an **asynchronous** command and await the ``cmd-resp`` handshake.

        Use the CSM async suffix ``->`` in *command*::

            await client.post("API: Start Sampling -> DAQmx")

        :param command: CSM command string including the ``->`` suffix.
        :param timeout: Seconds to wait for the ``cmd-resp`` handshake.
        :raises ConnectionError: if not connected.
        :raises TimeoutError: if no handshake arrives within *timeout*.
        :raises ServerError: if the server rejects the command.
        """
        wire = encode_packet(command.encode("utf-8"), PacketType.CMD)
        assert self._cmd_resp_lock is not None
        async with self._cmd_resp_lock:
            await self._send_raw(wire)
            await self._wait_for_cmd_resp(timeout)

    async def post_no_reply(self, command: str, timeout: float = 5.0) -> None:
        """Send an **async no-reply** command and await the ``cmd-resp`` handshake.

        Use the CSM no-reply suffix ``->|`` in *command*::

            await client.post_no_reply("API: Reset ->| DAQmx")

        :param command: CSM command string including the ``->|`` suffix.
        :param timeout: Seconds to wait for the ``cmd-resp`` handshake.
        :raises ConnectionError: if not connected.
        :raises TimeoutError: if no handshake arrives within *timeout*.
        :raises ServerError: if the server rejects the command.
        """
        wire = encode_packet(command.encode("utf-8"), PacketType.CMD)
        assert self._cmd_resp_lock is not None
        async with self._cmd_resp_lock:
            await self._send_raw(wire)
            await self._wait_for_cmd_resp(timeout)

    async def ping(self, timeout: float = 2.0) -> Tuple[bool, float]:
        """Send a ``Ping`` command and measure round-trip latency.

        :returns: ``(True, elapsed_seconds)`` on success,
                  ``(False, 0.0)`` on failure.
        """
        try:
            t0 = time.monotonic()
            await self.send_and_wait("Ping", timeout=timeout)
            return True, time.monotonic() - t0
        except (RouterConnectionError, RouterTimeoutError, ServerError):
            return False, 0.0

    # ------------------------------------------------------------------
    # Router management helpers
    # ------------------------------------------------------------------

    async def list_modules(self, timeout: float = 5.0) -> str:
        """Return the server's loaded CSM module list as plain text."""
        return (await self.send_and_wait("List", timeout=timeout)).text

    async def list_api(self, module: str, timeout: float = 5.0) -> str:
        """Return the API list for *module* as plain text."""
        return (await self.send_and_wait(f"List API {module}", timeout=timeout)).text

    async def list_states(self, module: str, timeout: float = 5.0) -> str:
        """Return the CSM state list for *module* as plain text."""
        return (await self.send_and_wait(f"List State {module}", timeout=timeout)).text

    async def help(self, module: str, timeout: float = 5.0) -> str:
        """Return the help text for *module* as plain text."""
        return (await self.send_and_wait(f"Help {module}", timeout=timeout)).text

    # ------------------------------------------------------------------
    # Status / interrupt subscriptions
    # ------------------------------------------------------------------

    async def subscribe_status(
        self,
        status_name: str,
        module_name: str,
        callback: Optional[StatusCallback] = None,
        timeout: float = 5.0,
    ) -> None:
        """Subscribe to a CSM module's status broadcast.

        Sends ``"<status_name>@<module_name> -><register>"`` and awaits the
        ``cmd-resp`` handshake.  Once subscribed,
        :class:`~csm_tcp_router.models.StatusNotification` objects will be:

        * delivered to *callback* (if provided – sync or async both accepted), and
        * added to :attr:`status_queue`.

        :param status_name: Name of the status (e.g. ``"Status"``).
        :param module_name: Name of the CSM module (e.g. ``"AI"``).
        :param callback: Optional callable or coroutine invoked per notification.
        :param timeout: Seconds to wait for the ``cmd-resp`` handshake.
        :raises ConnectionError: if not connected.
        :raises TimeoutError: if no handshake arrives within *timeout*.
        :raises ServerError: if the server rejects the subscription.
        """
        # Register the callback before sending to eliminate the race where a
        # STATUS packet could arrive before the callback is stored.
        self._status_callbacks[(status_name, module_name)] = callback
        cmd = f"{status_name}@{module_name} -><register>"
        wire = encode_packet(cmd.encode("utf-8"), PacketType.CMD)
        assert self._cmd_resp_lock is not None
        try:
            async with self._cmd_resp_lock:
                await self._send_raw(wire)
                await self._wait_for_cmd_resp(timeout)
        except Exception:
            self._status_callbacks.pop((status_name, module_name), None)
            raise

    async def unsubscribe_status(
        self,
        status_name: str,
        module_name: str,
        timeout: float = 5.0,
    ) -> None:
        """Cancel a status subscription.

        :param status_name: Name of the subscribed status.
        :param module_name: Name of the CSM module.
        :param timeout: Seconds to wait for the ``cmd-resp`` handshake.
        :raises ConnectionError: if not connected.
        :raises TimeoutError: if no handshake arrives within *timeout*.
        :raises ServerError: if the server rejects the request.
        """
        cmd = f"{status_name}@{module_name} -><unregister>"
        wire = encode_packet(cmd.encode("utf-8"), PacketType.CMD)
        assert self._cmd_resp_lock is not None
        async with self._cmd_resp_lock:
            await self._send_raw(wire)
            await self._wait_for_cmd_resp(timeout)
        self._status_callbacks.pop((status_name, module_name), None)

    def register_async_callback(
        self,
        original_command: str,
        callback: AsyncRespCallback,
    ) -> None:
        """Register a callback for ``async-resp`` packets.

        The callback is matched by *original_command* (the command text
        echoed in the ``async-resp`` payload after the `` <- `` separator).

        Callbacks may be either a plain callable or an ``async def`` coroutine.

        :param original_command: The command text echoed in the ``async-resp``.
        :param callback: Callable or coroutine receiving an
                         :class:`~csm_tcp_router.models.AsyncResponse`.
        """
        self._async_callbacks[original_command] = callback

    def unregister_async_callback(self, original_command: str) -> None:
        """Remove a previously registered async-response callback."""
        self._async_callbacks.pop(original_command, None)

    # ------------------------------------------------------------------
    # Async context-manager support
    # ------------------------------------------------------------------

    async def __aenter__(self) -> AsyncTcpRouterClient:
        return self

    async def __aexit__(self, *_args: object) -> None:
        await self.disconnect()

    # ------------------------------------------------------------------
    # Internal: send
    # ------------------------------------------------------------------

    async def _send_raw(self, data: bytes) -> None:
        if not self.connected:
            raise RouterConnectionError("Not connected.")
        assert self._writer is not None
        assert self._send_lock is not None
        async with self._send_lock:
            self._writer.write(data)
            await self._writer.drain()

    # ------------------------------------------------------------------
    # Internal: receive loop (background task)
    # ------------------------------------------------------------------

    async def _recv_loop(self) -> None:
        """Background task: read frames and dispatch them."""
        assert self._reader is not None
        try:
            while True:
                header = await self._reader.readexactly(HEADER_SIZE)
                (data_len,) = struct.unpack("!I", header[:4])
                body = (
                    await self._reader.readexactly(data_len) if data_len else b""
                )
                try:
                    packet = parse_packet(header, body)
                except ProtocolError:
                    continue  # skip corrupted frame; keep connection alive
                await self._dispatch_packet(packet)
        except (asyncio.IncompleteReadError, asyncio.CancelledError, OSError):
            pass
        finally:
            self._notify_disconnect()

    async def _dispatch_packet(self, packet: Packet) -> None:
        """Route a received packet to the correct queue and/or callback."""
        assert self._resp_queue is not None
        assert self._cmd_resp_queue is not None
        assert self.async_response_queue is not None
        assert self.status_queue is not None

        ptype = packet.type

        if ptype == PacketType.RESP:
            self._resp_queue.put_nowait(packet)

        elif ptype == PacketType.CMD_RESP:
            self._cmd_resp_queue.put_nowait(packet)

        elif ptype == PacketType.ASYNC_RESP:
            resp = AsyncResponse.from_packet(packet)
            self.async_response_queue.put_nowait(resp)
            cb = self._async_callbacks.get(resp.original_command)
            if cb is not None:
                try:
                    result = cb(resp)  # type: ignore[arg-type]
                    if inspect.isawaitable(result):
                        await result
                except Exception:
                    pass

        elif ptype in (PacketType.STATUS, PacketType.INTERRUPT):
            notif = StatusNotification.from_packet(packet)
            self.status_queue.put_nowait(notif)
            cb = self._status_callbacks.get(  # type: ignore[assignment]
                (notif.status_name, notif.module_name)
            )
            if cb is not None:
                try:
                    result = cb(notif)  # type: ignore[arg-type]
                    if inspect.isawaitable(result):
                        await result
                except Exception:
                    pass

        elif ptype == PacketType.ERROR:
            err = _parse_server_error(packet)
            self._resp_queue.put_nowait(err)
            self._cmd_resp_queue.put_nowait(err)

        # PacketType.INFO is silently discarded (welcome / goodbye messages)

    def _notify_disconnect(self) -> None:
        """Put sentinels in waiter queues when the connection is lost."""
        if self._resp_queue is None:
            return
        sentinel = RouterConnectionError("Connection lost unexpectedly.")
        self._resp_queue.put_nowait(sentinel)
        self._cmd_resp_queue.put_nowait(sentinel)

    # ------------------------------------------------------------------
    # Internal: synchronised waiters
    # ------------------------------------------------------------------

    async def _wait_for_resp(self, timeout: float) -> CommandResponse:
        assert self._resp_queue is not None
        try:
            item = await asyncio.wait_for(self._resp_queue.get(), timeout=timeout)
        except asyncio.TimeoutError:
            raise RouterTimeoutError(
                f"No response received within {timeout:.1f}s."
            ) from None
        if isinstance(item, Exception):
            raise item
        assert isinstance(item, Packet)
        return CommandResponse(raw=item.data)

    async def _wait_for_cmd_resp(self, timeout: float) -> None:
        assert self._cmd_resp_queue is not None
        try:
            item = await asyncio.wait_for(
                self._cmd_resp_queue.get(), timeout=timeout
            )
        except asyncio.TimeoutError:
            raise RouterTimeoutError(
                f"No cmd-resp received within {timeout:.1f}s."
            ) from None
        if isinstance(item, Exception):
            raise item
        # CMD_RESP payload is a handshake acknowledgment; discard it
