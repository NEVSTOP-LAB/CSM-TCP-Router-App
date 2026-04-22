"""Internal TCP transport layer with a background receive thread.

This module is internal; nothing is re-exported from the package.
"""

from __future__ import annotations

import socket
import struct
import threading
from typing import Callable, Optional

from ._protocol import HEADER_SIZE, parse_packet
from .exceptions import ConnectionError as RouterConnectionError
from .exceptions import ProtocolError
from .models import Packet

__all__: list = []  # internal; nothing re-exported


class Transport:
    """Thread-safe, blocking TCP transport.

    A background daemon thread continuously reads packets from the socket and
    dispatches them via *on_packet*.  Callers are responsible for keeping
    callbacks fast and non-blocking, as they run in the receive thread.

    Lifecycle::

        t = Transport(on_packet=..., on_disconnect=...)
        t.connect("localhost", 30007)
        t.send_raw(wire_bytes)
        t.disconnect()
    """

    def __init__(
        self,
        on_packet: Callable[[Packet], None],
        on_disconnect: Callable[[], None],
    ) -> None:
        self._sock: Optional[socket.socket] = None
        self._send_lock = threading.Lock()
        self._stop_event = threading.Event()
        self._recv_thread: Optional[threading.Thread] = None
        self._on_packet = on_packet
        self._on_disconnect = on_disconnect

    # ------------------------------------------------------------------
    # Public interface
    # ------------------------------------------------------------------

    @property
    def connected(self) -> bool:
        """``True`` while the socket is open and the stop event has not fired."""
        return self._sock is not None and not self._stop_event.is_set()

    def connect(self, host: str, port: int, timeout: float = 5.0) -> None:
        """Open a TCP connection and start the receive thread.

        :param host: Target hostname or IP address.
        :param port: Target TCP port.
        :param timeout: Connect timeout in seconds.
        :raises ConnectionError: if already connected or if the OS refuses.
        """
        if self.connected:
            raise RouterConnectionError(
                "Already connected; call disconnect() first."
            )
        try:
            sock = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
            sock.settimeout(timeout)
            sock.connect((host, port))
            sock.settimeout(None)  # switch to blocking for the recv loop
        except OSError as exc:
            raise RouterConnectionError(
                f"Cannot connect to {host}:{port}: {exc}"
            ) from exc

        self._sock = sock
        self._stop_event.clear()
        self._recv_thread = threading.Thread(
            target=self._recv_loop,
            daemon=True,
            name="csm-tcp-router-recv",
        )
        self._recv_thread.start()

    def disconnect(self, join_timeout: float = 2.0) -> None:
        """Close the connection and stop the receive thread.

        Safe to call even if not connected.
        """
        self._stop_event.set()
        if self._sock is not None:
            try:
                self._sock.shutdown(socket.SHUT_RDWR)
            except OSError:
                pass
            try:
                self._sock.close()
            except OSError:
                pass
            self._sock = None
        if self._recv_thread is not None and self._recv_thread.is_alive():
            self._recv_thread.join(timeout=join_timeout)

    def send_raw(self, data: bytes) -> None:
        """Send *data* atomically.  Thread-safe.

        :raises ConnectionError: if not connected or if the send fails.
        """
        if not self.connected:
            raise RouterConnectionError("Not connected.")
        with self._send_lock:
            try:
                self._sock.sendall(data)  # type: ignore[union-attr]
            except OSError as exc:
                self._stop_event.set()
                raise RouterConnectionError(f"Send failed: {exc}") from exc

    # ------------------------------------------------------------------
    # Private helpers
    # ------------------------------------------------------------------

    def _recv_all(self, size: int) -> bytes:
        """Read exactly *size* bytes; returns empty bytes on clean EOF or disconnect."""
        buf = bytearray(size)
        view = memoryview(buf)
        received = 0
        while received < size:
            sock = self._sock  # capture locally to avoid TOCTOU race with disconnect()
            if sock is None:
                return b""
            try:
                n = sock.recv_into(view[received:], size - received)
            except OSError:
                return b""
            if n == 0:
                return b""
            received += n
        return bytes(buf)

    def _recv_loop(self) -> None:
        """Background thread: read packets and dispatch via callback."""
        try:
            while not self._stop_event.is_set():
                header = self._recv_all(HEADER_SIZE)
                if not header:
                    break

                # Extract data_len from the first 4 bytes without full decode
                (data_len,) = struct.unpack("!I", header[:4])
                body = self._recv_all(data_len)
                if len(body) != data_len:
                    break

                try:
                    packet = parse_packet(header, body)
                except ProtocolError:
                    # Corrupted frame – skip it and keep the loop alive
                    continue

                self._on_packet(packet)

        except OSError:
            pass
        finally:
            if not self._stop_event.is_set():
                self._stop_event.set()
                self._on_disconnect()
