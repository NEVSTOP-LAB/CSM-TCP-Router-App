"""High-level CSM-TCP-Router client."""

from __future__ import annotations

import queue
import threading
import time
from typing import Callable, Dict, Optional, Tuple

from ._errors import _parse_server_error
from ._protocol import encode_packet
from ._transport import Transport
from .exceptions import ConnectionError as RouterConnectionError
from .exceptions import ServerError
from .exceptions import TimeoutError as RouterTimeoutError
from .models import (
    AsyncResponse,
    CommandResponse,
    Packet,
    PacketType,
    StatusNotification,
)

__all__ = ["TcpRouterClient"]

# Type aliases
_SubKey = Tuple[str, str]
StatusCallback = Callable[[StatusNotification], None]
AsyncCallback = Callable[[AsyncResponse], None]

# Items held in the internal queues are either Packet or Exception instances.
_QueueItem = object


class TcpRouterClient:
    """Python client for a CSM-TCP-Router server.

    This class mirrors the LabVIEW ClientAPI VIs and speaks the
    CSM-TCP-Router protocol v0.  It is thread-safe in that its internal
    state is protected by locks; however, the protocol allows at most one
    in-flight *synchronous* command at a time and at most one in-flight
    *async* command / subscription at a time.  Concurrent callers are
    serialised by ``_resp_lock`` and ``_cmd_resp_lock`` respectively.

    **Quickstart**::

        from csm_tcp_router import TcpRouterClient

        with TcpRouterClient() as client:
            client.connect("localhost", 30007)
            print(client.list_modules())

    **Protocol flows**:

    - *Synchronous* command (``-@``): :meth:`send_and_wait` – sends a ``CMD``
      packet and blocks until a ``RESP`` (or ``ERROR``) is received.
    - *Asynchronous* command (``->``): :meth:`post` – sends a ``CMD`` packet
      and blocks until the ``CMD_RESP`` handshake is received; the eventual
      ``ASYNC_RESP`` is delivered asynchronously.
    - *No-reply async* command (``->|``): :meth:`post_no_reply` – same as
      :meth:`post` but no ``ASYNC_RESP`` will ever arrive.
    - *Subscribe / unsubscribe*: :meth:`subscribe_status` /
      :meth:`unsubscribe_status` – sends a ``<register>`` / ``<unregister>``
      command and waits for the ``CMD_RESP`` handshake.

    **Received-packet routing** (on the background receive thread):

    - ``RESP`` (0x04) – unblocks the caller of :meth:`send_and_wait`.
    - ``CMD_RESP`` (0x03) – unblocks callers of :meth:`post`,
      :meth:`post_no_reply`, :meth:`subscribe_status`, and
      :meth:`unsubscribe_status`.
    - ``ASYNC_RESP`` (0x05) – added to :attr:`async_response_queue` and
      dispatched to any matching :meth:`register_async_callback`.
    - ``STATUS`` / ``INTERRUPT`` (0x06 / 0x07) – added to
      :attr:`status_queue` and dispatched to any matching
      :meth:`subscribe_status` callback.
    - ``ERROR`` (0x01) – unblocks any pending synchronous waiter with a
      :exc:`~csm_tcp_router.exceptions.ServerError`.
    - ``INFO`` (0x00) – silently discarded (welcome / goodbye messages).
    """

    def __init__(self) -> None:
        self._transport = Transport(
            on_packet=self._on_packet,
            on_disconnect=self._on_disconnect,
        )

        # One-item-deep queues for synchronised waits.
        # Items are either Packet or Exception instances.
        self._resp_queue: queue.Queue[_QueueItem] = queue.Queue()
        self._cmd_resp_queue: queue.Queue[_QueueItem] = queue.Queue()

        #: Polling queue for :class:`~csm_tcp_router.models.AsyncResponse`
        #: objects received from the server.
        self.async_response_queue: queue.Queue[AsyncResponse] = queue.Queue()

        #: Polling queue for :class:`~csm_tcp_router.models.StatusNotification`
        #: objects received from the server.
        self.status_queue: queue.Queue[StatusNotification] = queue.Queue()

        # Callback registries (protected by _lock)
        self._status_callbacks: Dict[_SubKey, Optional[StatusCallback]] = {}
        self._async_callbacks: Dict[str, AsyncCallback] = {}
        self._lock = threading.Lock()

        # Serialisation locks – at most one in-flight RESP / CMD_RESP waiter
        # at a time.  This prevents concurrent callers from consuming each
        # other's response packets.
        self._resp_lock = threading.Lock()
        self._cmd_resp_lock = threading.Lock()

    # ------------------------------------------------------------------
    # Connection management
    # ------------------------------------------------------------------

    def connect(self, host: str, port: int, timeout: float = 5.0) -> None:
        """Connect to a CSM-TCP-Router server.

        :param host: Server hostname or IP address.
        :param port: Server TCP port (the reference app defaults to 30007).
        :param timeout: Connect timeout in seconds.
        :raises ConnectionError: if the connection cannot be established.
        """
        self._transport.connect(host, port, timeout=timeout)

    def disconnect(self) -> None:
        """Disconnect from the server and release all resources.

        Safe to call even if not currently connected.  Any threads currently
        blocked inside :meth:`send_and_wait`, :meth:`post`, or similar methods
        will receive a :exc:`~csm_tcp_router.exceptions.ConnectionError`
        immediately rather than waiting for their timeout to expire.
        """
        # Wake blocked waiters *before* tearing down the transport.
        sentinel = RouterConnectionError("Disconnected from server.")
        self._resp_queue.put(sentinel)
        self._cmd_resp_queue.put(sentinel)
        self._transport.disconnect()

    @property
    def connected(self) -> bool:
        """``True`` if the underlying transport is currently connected."""
        return self._transport.connected

    def wait_for_server(
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
            probe = Transport(
                on_packet=lambda _p: None,
                on_disconnect=lambda: None,
            )
            try:
                probe.connect(host, port, timeout=1.0)
                probe.disconnect()
                return True
            except RouterConnectionError:
                pass
            time.sleep(retry_interval)
        return False

    # ------------------------------------------------------------------
    # Core command methods
    # ------------------------------------------------------------------

    def send_and_wait(self, command: str, timeout: float = 5.0) -> CommandResponse:
        """Send a **synchronous** command and block until the response arrives.

        Use the CSM synchronous message suffix ``-@`` in *command*::

            resp = client.send_and_wait("API: Read -@ DAQmx")
            print(resp.text)

        The built-in router management commands (``List``, ``Ping``, …) are
        also synchronous and do not require a suffix.

        :param command: CSM command string.
        :param timeout: Seconds to wait for the ``resp`` packet.
        :raises ConnectionError: if not connected.
        :raises TimeoutError: if no response arrives within *timeout*.
        :raises ServerError: if the server returns an error packet.
        """
        wire = encode_packet(command.encode("utf-8"), PacketType.CMD)
        with self._resp_lock:
            self._transport.send_raw(wire)
            return self._wait_for_resp(timeout)

    def post(self, command: str, timeout: float = 5.0) -> None:
        """Send an **asynchronous** command and wait for the ``cmd-resp`` handshake.

        Use the CSM async message suffix ``->`` in *command*::

            client.post("API: Start Sampling -> DAQmx")

        The eventual ``async-resp`` payload will be delivered to any callback
        registered with :meth:`register_async_callback` and added to
        :attr:`async_response_queue`.

        :param command: CSM command string including the ``->`` suffix.
        :param timeout: Seconds to wait for the ``cmd-resp`` handshake.
        :raises ConnectionError: if not connected.
        :raises TimeoutError: if no handshake arrives within *timeout*.
        :raises ServerError: if the server rejects the command.
        """
        wire = encode_packet(command.encode("utf-8"), PacketType.CMD)
        with self._cmd_resp_lock:
            self._transport.send_raw(wire)
            self._wait_for_cmd_resp(timeout)

    def post_no_reply(self, command: str, timeout: float = 5.0) -> None:
        """Send an **async no-reply** command and wait for the ``cmd-resp`` handshake.

        Use the CSM no-reply suffix ``->|`` in *command*::

            client.post_no_reply("API: Reset ->| DAQmx")

        After the handshake the server will not send any further response.

        :param command: CSM command string including the ``->|`` suffix.
        :param timeout: Seconds to wait for the ``cmd-resp`` handshake.
        :raises ConnectionError: if not connected.
        :raises TimeoutError: if no handshake arrives within *timeout*.
        :raises ServerError: if the server rejects the command.
        """
        wire = encode_packet(command.encode("utf-8"), PacketType.CMD)
        with self._cmd_resp_lock:
            self._transport.send_raw(wire)
            self._wait_for_cmd_resp(timeout)

    def ping(self, timeout: float = 2.0) -> Tuple[bool, float]:
        """Send a ``Ping`` command and measure round-trip latency.

        :param timeout: Seconds to wait for the reply.
        :returns: ``(True, elapsed_seconds)`` on success,
                  ``(False, 0.0)`` on failure or error.
        """
        try:
            t0 = time.monotonic()
            self.send_and_wait("Ping", timeout=timeout)
            return True, time.monotonic() - t0
        except (RouterConnectionError, RouterTimeoutError, ServerError):
            return False, 0.0

    # ------------------------------------------------------------------
    # Router management helpers
    # ------------------------------------------------------------------

    def list_modules(self, timeout: float = 5.0) -> str:
        """Return the server's loaded CSM module list as plain text.

        Equivalent to the ``List`` router management command.
        """
        return self.send_and_wait("List", timeout=timeout).text

    def list_api(self, module: str, timeout: float = 5.0) -> str:
        """Return the API list for *module* as plain text."""
        return self.send_and_wait(f"List API {module}", timeout=timeout).text

    def list_states(self, module: str, timeout: float = 5.0) -> str:
        """Return the CSM state list for *module* as plain text."""
        return self.send_and_wait(f"List State {module}", timeout=timeout).text

    def help(self, module: str, timeout: float = 5.0) -> str:
        """Return the help text for *module* as plain text."""
        return self.send_and_wait(f"Help {module}", timeout=timeout).text

    # ------------------------------------------------------------------
    # Status / interrupt subscriptions
    # ------------------------------------------------------------------

    def subscribe_status(
        self,
        status_name: str,
        module_name: str,
        callback: Optional[StatusCallback] = None,
        timeout: float = 5.0,
    ) -> None:
        """Subscribe to a CSM module's status broadcast.

        Sends ``"<status_name>@<module_name> -><register>"`` and waits for
        the ``cmd-resp`` handshake.  Once subscribed,
        :class:`~csm_tcp_router.models.StatusNotification` objects will be:

        * delivered to *callback* (if provided), and
        * added to :attr:`status_queue`.

        :param status_name: Name of the status to subscribe to (e.g. ``"Status"``).
        :param module_name: Name of the CSM module (e.g. ``"AI"``).
        :param callback: Optional callable invoked on each notification.
                         Must be fast and non-blocking (runs in the recv thread).
        :param timeout: Seconds to wait for the ``cmd-resp`` handshake.
        :raises ConnectionError: if not connected.
        :raises TimeoutError: if no handshake arrives within *timeout*.
        :raises ServerError: if the server rejects the subscription.
        """
        # Register the callback *before* sending to eliminate the race where
        # a STATUS packet could arrive before the callback is stored.
        with self._lock:
            self._status_callbacks[(status_name, module_name)] = callback
        cmd = f"{status_name}@{module_name} -><register>"
        wire = encode_packet(cmd.encode("utf-8"), PacketType.CMD)
        try:
            with self._cmd_resp_lock:
                self._transport.send_raw(wire)
                self._wait_for_cmd_resp(timeout)
        except Exception:
            with self._lock:
                self._status_callbacks.pop((status_name, module_name), None)
            raise

    def unsubscribe_status(
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
        with self._cmd_resp_lock:
            self._transport.send_raw(wire)
            self._wait_for_cmd_resp(timeout)
        with self._lock:
            self._status_callbacks.pop((status_name, module_name), None)

    def register_async_callback(
        self,
        original_command: str,
        callback: AsyncCallback,
    ) -> None:
        """Register a callback for ``async-resp`` packets.

        The callback is matched by *original_command* (the command text
        echoed in the ``async-resp`` payload after the `` <- `` separator).

        :param original_command: The command text that will appear in the
                                 ``async-resp`` echo
                                 (e.g. ``"API: Read -> DAQmx"``).
        :param callback: Callable receiving an
                         :class:`~csm_tcp_router.models.AsyncResponse`.
        """
        with self._lock:
            self._async_callbacks[original_command] = callback

    def unregister_async_callback(self, original_command: str) -> None:
        """Remove a previously registered async callback."""
        with self._lock:
            self._async_callbacks.pop(original_command, None)

    # ------------------------------------------------------------------
    # Context-manager support
    # ------------------------------------------------------------------

    def __enter__(self) -> TcpRouterClient:
        return self

    def __exit__(self, *_args: object) -> None:
        self.disconnect()

    # ------------------------------------------------------------------
    # Internal: packet dispatch  (runs in the receive thread)
    # ------------------------------------------------------------------

    def _on_packet(self, packet: Packet) -> None:
        ptype = packet.type
        if ptype == PacketType.RESP:
            self._resp_queue.put(packet)

        elif ptype == PacketType.CMD_RESP:
            self._cmd_resp_queue.put(packet)

        elif ptype == PacketType.ASYNC_RESP:
            resp = AsyncResponse.from_packet(packet)
            self.async_response_queue.put(resp)
            with self._lock:
                cb = self._async_callbacks.get(resp.original_command)
            if cb is not None:
                try:
                    cb(resp)
                except Exception:
                    pass

        elif ptype in (PacketType.STATUS, PacketType.INTERRUPT):
            notif = StatusNotification.from_packet(packet)
            self.status_queue.put(notif)
            with self._lock:
                cb = self._status_callbacks.get(  # type: ignore[assignment]
                    (notif.status_name, notif.module_name)
                )
            if cb is not None:
                try:
                    cb(notif)  # type: ignore[call-arg]
                except Exception:
                    pass

        elif ptype == PacketType.ERROR:
            err = _parse_server_error(packet)
            # Unblock any pending synchronous waiter
            self._resp_queue.put(err)
            self._cmd_resp_queue.put(err)

        # PacketType.INFO is silently discarded (welcome / goodbye messages)

    def _on_disconnect(self) -> None:
        """Called from the receive thread when the connection drops unexpectedly."""
        sentinel = RouterConnectionError("Connection lost unexpectedly.")
        self._resp_queue.put(sentinel)
        self._cmd_resp_queue.put(sentinel)

    # ------------------------------------------------------------------
    # Internal: synchronised waiters
    # ------------------------------------------------------------------

    def _wait_for_resp(self, timeout: float) -> CommandResponse:
        try:
            item = self._resp_queue.get(timeout=timeout)
        except queue.Empty:
            raise RouterTimeoutError(
                f"No response received within {timeout:.1f}s."
            ) from None
        if isinstance(item, Exception):
            raise item
        assert isinstance(item, Packet)
        return CommandResponse(raw=item.data)

    def _wait_for_cmd_resp(self, timeout: float) -> None:
        try:
            item = self._cmd_resp_queue.get(timeout=timeout)
        except queue.Empty:
            raise RouterTimeoutError(
                f"No cmd-resp received within {timeout:.1f}s."
            ) from None
        if isinstance(item, Exception):
            raise item
        # CMD_RESP payload is a handshake acknowledgment; discard it
