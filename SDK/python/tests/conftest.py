"""Shared pytest fixtures – mock TCP server for integration tests."""

from __future__ import annotations

import queue
import socket
import struct
import threading
from typing import Dict, Optional, Tuple

import pytest

from csm_tcp_router_client import HEADER_SIZE, PacketType, encode_packet

# ---------------------------------------------------------------------------
# Internal helpers
# ---------------------------------------------------------------------------

def _recv_all(sock: socket.socket, size: int) -> bytes:
    """Read exactly *size* bytes from *sock*; returns ``b""`` on EOF."""
    buf = bytearray(size)
    view = memoryview(buf)
    received = 0
    while received < size:
        try:
            n = sock.recv_into(view[received:], size - received)
        except OSError:
            return b""
        if n == 0:
            return b""
        received += n
    return bytes(buf)


# ---------------------------------------------------------------------------
# MockServer
# ---------------------------------------------------------------------------

class MockServer:
    """Minimal TCP server that emulates a CSM-TCP-Router for testing.

    Usage::

        server = MockServer()
        server.start()
        # ... connect a TcpRouterClient to server.port ...
        server.stop()

    Custom responses can be registered before the client sends commands::

        server.set_response("My Command", "My Reply")
        server.set_error_response("Bad Command", "[Error: 42] bad command")
    """

    def __init__(self) -> None:
        self._server_sock: Optional[socket.socket] = None
        self._thread: Optional[threading.Thread] = None
        self._stop = threading.Event()
        self.host: str = "127.0.0.1"
        self.port: int = 0

        #: All raw command strings received from the client, in order.
        self.received_commands: queue.Queue[str] = queue.Queue()

        # custom response map: command text -> (PacketType, bytes)
        self._responses: Dict[str, Tuple[PacketType, bytes]] = {}

        # Connected client sockets (for push operations like STATUS)
        self._client_sockets: list = []
        self._clients_lock = threading.Lock()

    def start(self) -> None:
        self._server_sock = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
        self._server_sock.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
        self._server_sock.bind((self.host, 0))
        self.port = self._server_sock.getsockname()[1]
        self._server_sock.listen(5)
        self._stop.clear()
        self._thread = threading.Thread(
            target=self._accept_loop, daemon=True, name="mock-server"
        )
        self._thread.start()

    def stop(self) -> None:
        self._stop.set()
        if self._server_sock:
            try:
                self._server_sock.close()
            except OSError:
                pass
        if self._thread:
            self._thread.join(timeout=2)

    def set_response(self, cmd_text: str, resp_text: str) -> None:
        """Register a custom ``RESP`` reply for *cmd_text*."""
        self._responses[cmd_text] = (PacketType.RESP, resp_text.encode("utf-8"))

    def set_error_response(self, cmd_text: str, error_text: str) -> None:
        """Register an ``ERROR`` reply for *cmd_text*."""
        self._responses[cmd_text] = (PacketType.ERROR, error_text.encode("utf-8"))

    def push_status(self, payload: str) -> None:
        """Push a ``STATUS`` packet to all currently connected clients."""
        wire = encode_packet(payload.encode("utf-8"), PacketType.STATUS)
        with self._clients_lock:
            for conn in list(self._client_sockets):
                try:
                    conn.sendall(wire)
                except OSError:
                    pass

    def get_received(self, timeout: float = 1.0) -> Optional[str]:
        """Return the next received command string, or ``None`` on timeout."""
        try:
            return self.received_commands.get(timeout=timeout)
        except queue.Empty:
            return None

    # ------------------------------------------------------------------
    # Internal
    # ------------------------------------------------------------------

    def _accept_loop(self) -> None:
        assert self._server_sock is not None
        self._server_sock.settimeout(0.5)
        while not self._stop.is_set():
            try:
                conn, _ = self._server_sock.accept()
            except (OSError, socket.timeout):
                continue
            with self._clients_lock:
                self._client_sockets.append(conn)
            t = threading.Thread(
                target=self._handle_client, args=(conn,), daemon=True
            )
            t.start()

    def _handle_client(self, conn: socket.socket) -> None:
        # Send welcome info packet on connect
        conn.sendall(encode_packet(b"Welcome to mock server", PacketType.INFO))
        conn.settimeout(1.0)
        try:
            while not self._stop.is_set():
                header = _recv_all(conn, HEADER_SIZE)
                if not header:
                    break
                try:
                    (data_len,) = struct.unpack("!I", header[:4])
                    body = _recv_all(conn, data_len)
                except (OSError, struct.error):
                    break
                if len(body) != data_len:
                    break

                type_byte = header[5]  # offset 5 == TYPE byte
                if type_byte == PacketType.CMD.value:
                    cmd_text = body.decode("utf-8", errors="replace").strip()
                    self.received_commands.put(cmd_text)
                    self._handle_command(conn, cmd_text)
        except OSError:
            pass
        finally:
            with self._clients_lock:
                try:
                    self._client_sockets.remove(conn)
                except ValueError:
                    pass
            try:
                conn.close()
            except OSError:
                pass

    def _handle_command(self, conn: socket.socket, cmd: str) -> None:
        """Respond to a received command."""
        if cmd in self._responses:
            ptype, data = self._responses[cmd]
            conn.sendall(encode_packet(data, ptype))
            return

        # Built-in defaults
        if cmd == "Ping":
            conn.sendall(encode_packet(b"Pong", PacketType.RESP))
        elif cmd == "List":
            conn.sendall(encode_packet(b"AI\nDIO\nSystem", PacketType.RESP))
        elif cmd.startswith("List API "):
            module = cmd[len("List API "):].strip()
            payload = f"API: Start -> {module}\nAPI: Stop -> {module}"
            conn.sendall(encode_packet(payload.encode(), PacketType.RESP))
        elif cmd.startswith("List State "):
            module = cmd[len("List State "):].strip()
            payload = f"Idle <- {module}\nRunning <- {module}"
            conn.sendall(encode_packet(payload.encode(), PacketType.RESP))
        elif "-><register>" in cmd or "-><unregister>" in cmd:
            conn.sendall(encode_packet(b"", PacketType.CMD_RESP))
        else:
            # Generic async handshake for any other command
            conn.sendall(encode_packet(b"", PacketType.CMD_RESP))


# ---------------------------------------------------------------------------
# Pytest fixture
# ---------------------------------------------------------------------------

@pytest.fixture
def mock_server():
    """Provide a running :class:`MockServer`; stop it after the test."""
    server = MockServer()
    server.start()
    yield server
    server.stop()
