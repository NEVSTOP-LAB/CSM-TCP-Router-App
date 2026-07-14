"""csm-tcp-router-client – CSM-TCP-Router 服务器的单文件 Python 客户端 SDK。

本模块将完整的客户端实现（同步与异步）、线路协议编解码器、
异常层次结构以及公共数据模型打包到一个可直接导入的文件中。

同步用法::

    from csm_tcp_router_client import TcpRouterClient

    with TcpRouterClient() as client:
        client.connect("localhost", 30007)
        print(client.list_modules())

异步用法::

    import asyncio
    from csm_tcp_router_client import AsyncTcpRouterClient

    async def main():
        async with AsyncTcpRouterClient() as client:
            await client.connect("localhost", 30007)
            print(await client.list_modules())

    asyncio.run(main())

线路格式（8 字节报头，大端序）::

    | Data Length (4B) | Version (1B=0x01) | Type (1B) | FLAG1 (1B) | FLAG2 (1B) |
    ╰────────────────────────── Header (8B) ──────────────────────────╯

后跟恰好 ``Data Length`` 字节的有效载荷。
"""

from __future__ import annotations

import asyncio
import inspect
import queue
import socket
import struct
import threading
import time
from dataclasses import dataclass
from enum import IntEnum
from typing import Any, Callable, Coroutine, Dict, Optional, Tuple, Union

__all__ = [
    # 客户端
    "TcpRouterClient",
    "AsyncTcpRouterClient",
    # 异常
    "TcpRouterError",
    "ConnectionError",
    "TimeoutError",
    "ProtocolError",
    "ServerError",
    # 数据模型
    "PacketType",
    "Packet",
    "CommandResponse",
    "AsyncResponse",
    "StatusNotification",
    # 版本
    "__version__",
]

__version__ = "0.3.0"


# ===========================================================================
# 异常
# ===========================================================================


class TcpRouterError(Exception):
    """所有 CSM-TCP-Router 客户端错误的基异常。"""


class ConnectionError(TcpRouterError):
    """当连接无法建立或已断开时抛出。"""


class TimeoutError(TcpRouterError):
    """当同步操作超过其超时时间时抛出。"""


class ProtocolError(TcpRouterError):
    """当接收到无效或意外的协议帧时抛出。"""


class ServerError(TcpRouterError):
    """当服务器返回错误数据包时抛出。

    Attributes:
        message: 来自服务器的可读错误文本。
        code: 从 CSM 错误格式 ``[Error: <code>] <message>`` 中提取的可选错误代码。
    """

    def __init__(self, message: str, code: str = "") -> None:
        super().__init__(message)
        self.message = message
        self.code = code

    def __str__(self) -> str:
        if self.code:
            return f"[Error: {self.code}] {self.message}"
        return self.message


# 传输/接收代码中使用的内部别名，用于避免与模块级遮蔽的内置名称产生歧义。
_RouterConnectionError = ConnectionError
_RouterTimeoutError = TimeoutError


# ===========================================================================
# 公共数据模型
# ===========================================================================


class PacketType(IntEnum):
    """CSM-TCP-Router 协议 v0 中定义的数据包类型常量。

    线路值
    -----------
    ``INFO``       0x00 – 信息消息（欢迎/再见）
    ``ERROR``      0x01 – 来自服务器的错误消息
    ``CMD``        0x02 – 客户端发送的命令
    ``CMD_RESP``   0x03 – 服务器对异步/无回复/订阅的握手确认
    ``RESP``       0x04 – 同步响应有效载荷
    ``ASYNC_RESP`` 0x05 – 异步响应有效载荷
    ``STATUS``     0x06 – 来自已订阅 CSM 模块的状态广播
    ``INTERRUPT``  0x07 – 来自已订阅 CSM 模块的中断广播
    """

    INFO = 0x00
    ERROR = 0x01
    CMD = 0x02
    CMD_RESP = 0x03
    RESP = 0x04
    ASYNC_RESP = 0x05
    STATUS = 0x06
    INTERRUPT = 0x07


@dataclass(frozen=True)
class Packet:
    """从服务器接收到的已解码数据包（内部表示）。"""

    type: PacketType
    data: bytes
    version: int = 1
    flag1: int = 0
    flag2: int = 0


@dataclass(frozen=True)
class CommandResponse:
    """同步命令（:meth:`TcpRouterClient.send_and_wait`）的结果。"""

    raw: bytes

    @property
    def text(self) -> str:
        """响应有效载荷的 UTF-8 解码文本。"""
        return self.raw.decode("utf-8", errors="replace")

    def __repr__(self) -> str:
        return f"CommandResponse({self.text!r})"


@dataclass(frozen=True)
class AsyncResponse:
    """通过 ``async-resp`` 数据包传递的异步响应有效载荷。

    Attributes:
        raw: 原始响应字节（`` <- `` 分隔符*之前*的部分）。
        original_command: 服务器回显的原始命令文本
                          （`` <- `` 分隔符*之后*的部分）。
    """

    raw: bytes
    original_command: str = ""

    @property
    def text(self) -> str:
        """响应有效载荷的 UTF-8 解码文本。"""
        return self.raw.decode("utf-8", errors="replace")

    @classmethod
    def from_packet(cls, packet: Packet) -> AsyncResponse:
        """解析一个 ``ASYNC_RESP`` 数据包。

        服务器格式：``"<response-data> <- <original-command>"``。
        """
        text = packet.data.decode("utf-8", errors="replace")
        parts = text.split(" <- ", 1)
        if len(parts) == 2:
            return cls(raw=parts[0].encode("utf-8"), original_command=parts[1])
        return cls(raw=packet.data)

    def __repr__(self) -> str:
        return f"AsyncResponse({self.text!r}, cmd={self.original_command!r})"


@dataclass(frozen=True)
class StatusNotification:
    """通过 ``status`` 或 ``interrupt`` 数据包传递的状态广播。

    Attributes:
        raw: 完整的原始有效载荷字节。
        packet_type: :attr:`PacketType.STATUS` 或
                     :attr:`PacketType.INTERRUPT` 之一。
        status_name: 广播的状态名称（``>>`` 左侧）。
        data: 状态有效载荷（``>>`` 与 ``<-`` 之间）。
        module_name: 发送该状态的 CSM 模块名称（``<-`` 右侧）。
    """

    raw: bytes
    packet_type: PacketType = PacketType.STATUS
    status_name: str = ""
    data: str = ""
    module_name: str = ""

    @classmethod
    def from_packet(cls, packet: Packet) -> StatusNotification:
        """解析一个 ``STATUS`` 或 ``INTERRUPT`` 数据包。

        服务器格式：``"<status-name> >> <data> <- <module>"``。
        """
        text = packet.data.decode("utf-8", errors="replace")
        module = ""
        left = text
        if " <- " in text:
            left, module = text.rsplit(" <- ", 1)
            module = module.strip()
        status_name = ""
        data = left.strip()
        if " >> " in left:
            status_name, data = left.split(" >> ", 1)
            status_name = status_name.strip()
            data = data.strip()
        return cls(
            raw=packet.data,
            packet_type=packet.type,
            status_name=status_name,
            data=data,
            module_name=module,
        )

    def __repr__(self) -> str:
        return (
            f"StatusNotification(status={self.status_name!r}, "
            f"data={self.data!r}, module={self.module_name!r})"
        )


# ===========================================================================
# 协议编解码器（内部使用，但可导入供高级用途/测试）
# ===========================================================================

# 报头布局：大端序 uint32 data_len + 4 x uint8（version, type, flag1, flag2）
_HEADER_FORMAT = "!IBBBB"

#: 固定数据包报头的字节数。
HEADER_SIZE: int = struct.calcsize(_HEADER_FORMAT)  # == 8

#: 每个出站数据包中发送的协议版本字节。
PROTOCOL_VERSION: int = 0x01


def encode_packet(
    data: bytes,
    packet_type: PacketType,
    flag1: int = 0,
    flag2: int = 0,
) -> bytes:
    """将 *data* 编码为完整的线路格式数据包（报头 + 正文）。

    :param data: 原始有效载荷字节。
    :param packet_type: 报头中使用的 :class:`PacketType`。
    :param flag1: FLAG1 字节（当前未使用；默认为 0）。
    :param flag2: FLAG2 字节（当前未使用；默认为 0）。
    :returns: 已拼接的报头 + 有效载荷字节，可直接传递给 ``sendall()``。
    """
    header = struct.pack(
        _HEADER_FORMAT,
        len(data),
        PROTOCOL_VERSION,
        packet_type.value,
        flag1,
        flag2,
    )
    return header + data


def decode_header(header_bytes: bytes) -> Tuple[int, int, int, int, int]:
    """将 8 字节报头解码为其各组成字段。

    :returns: ``(data_len, version, type_byte, flag1, flag2)``
    :raises ProtocolError: 若 *header_bytes* 不恰好为 :data:`HEADER_SIZE` 字节。
    """
    if len(header_bytes) != HEADER_SIZE:
        raise ProtocolError(
            f"Expected {HEADER_SIZE}-byte header, got {len(header_bytes)} bytes."
        )
    return struct.unpack(_HEADER_FORMAT, header_bytes)  # type: ignore[return-value]


def parse_packet(header_bytes: bytes, body: bytes) -> Packet:
    """从原始报头 + 正文构建 :class:`Packet`。

    未知的数据包类型字节将映射到 :attr:`PacketType.INFO` 以保持
    前向兼容性（服务器在未来的协议修订中可能引入新类型）。

    :raises ProtocolError: 当报头大小不匹配或正文长度不匹配时。
    """
    data_len, version, type_byte, flag1, flag2 = decode_header(header_bytes)
    if len(body) != data_len:
        raise ProtocolError(
            f"Payload length mismatch: header says {data_len} bytes, "
            f"got {len(body)} bytes."
        )
    try:
        ptype = PacketType(type_byte)
    except ValueError:
        # 前向兼容：将未知类型视为 INFO
        ptype = PacketType.INFO
    return Packet(type=ptype, data=body, version=version, flag1=flag1, flag2=flag2)


# ===========================================================================
# 共享的服务器错误解析辅助函数
# ===========================================================================


def _parse_server_error(packet: Packet) -> ServerError:
    """从 CSM 错误格式 ``[Error: <code>] <msg>`` 中提取错误代码和消息。"""
    text = packet.data.decode("utf-8", errors="replace").strip()
    code = ""
    msg = text
    if text.startswith("[Error:"):
        try:
            end_idx = text.index("]")
            code = text[7:end_idx].strip()
            msg = text[end_idx + 1 :].strip()
        except ValueError:
            pass
    return ServerError(msg, code)


# ===========================================================================
# 内部：基于线程的 TCP 传输（由同步客户端使用）
# ===========================================================================


class _Transport:
    """线程安全的阻塞式 TCP 传输。

    后台守护线程持续从套接字读取数据包，并通过 *on_packet* 进行分发。
    调用方负责保持回调函数快速且无阻塞，因为它们在接收线程中运行。
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

    @property
    def connected(self) -> bool:
        """``True`` 表示套接字已打开且停止事件尚未触发。"""
        return self._sock is not None and not self._stop_event.is_set()

    def connect(self, host: str, port: int, timeout: float = 5.0) -> None:
        """建立 TCP 连接并启动接收线程。"""
        if self.connected:
            raise _RouterConnectionError(
                "Already connected; call disconnect() first."
            )
        sock: Optional[socket.socket] = None
        try:
            sock = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
            sock.settimeout(timeout)
            sock.connect((host, port))
            sock.settimeout(None)  # 切换为阻塞模式以用于接收循环
        except OSError as exc:
            if sock is not None:
                try:
                    sock.close()
                except OSError:
                    pass
            raise _RouterConnectionError(
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
        """关闭连接并停止接收线程。"""
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
        """原子性地发送 *data*。线程安全。"""
        if not self.connected:
            raise _RouterConnectionError("Not connected.")
        with self._send_lock:
            try:
                self._sock.sendall(data)  # type: ignore[union-attr]
            except OSError as exc:
                self._stop_event.set()
                raise _RouterConnectionError(f"Send failed: {exc}") from exc

    def _recv_all(self, size: int) -> bytes:
        """精确读取 *size* 字节；在干净的 EOF 或断开连接时返回空字节。"""
        buf = bytearray(size)
        view = memoryview(buf)
        received = 0
        while received < size:
            sock = self._sock  # 本地捕获，避免与 disconnect() 产生 TOCTOU 竞争
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
        """后台线程：读取数据包并通过回调进行分发。"""
        try:
            while not self._stop_event.is_set():
                header = self._recv_all(HEADER_SIZE)
                if not header:
                    break

                # 从前 4 个字节提取 data_len，无需完整解码
                (data_len,) = struct.unpack("!I", header[:4])
                body = self._recv_all(data_len)
                if len(body) != data_len:
                    break

                try:
                    packet = parse_packet(header, body)
                except ProtocolError:
                    # 帧损坏 – 跳过并保持循环运行
                    continue

                self._on_packet(packet)

        except OSError:
            pass
        finally:
            if not self._stop_event.is_set():
                self._stop_event.set()
                self._on_disconnect()


# ===========================================================================
# TcpRouterClient – 基于线程的同步客户端
# ===========================================================================

# 类型别名
_SubKey = Tuple[str, str]
StatusCallback = Callable[[StatusNotification], None]
AsyncCallback = Callable[[AsyncResponse], None]

# 内部队列中存放的元素为 Packet 或 Exception 实例。
_QueueItem = object


class TcpRouterClient:
    """CSM-TCP-Router 服务器的同步客户端。

    本类镜像了 LabVIEW ClientAPI VI，并实现了 CSM-TCP-Router 协议 v0。
    其内部状态通过锁保护，因此是线程安全的；但协议同时只允许一个在途
    *同步* 命令和一个在途 *异步* 命令/订阅。并发调用者分别由
    ``_resp_lock`` 和 ``_cmd_resp_lock`` 串行化。

    **快速入门**::

        from csm_tcp_router_client import TcpRouterClient

        with TcpRouterClient() as client:
            client.connect("localhost", 30007)
            print(client.list_modules())

    **协议流程**：

    - *同步* 命令 (``-@``)：:meth:`send_and_wait` – 发送 ``CMD`` 包并阻塞
      直到收到 ``RESP``（或 ``ERROR``）。
    - *异步* 命令 (``->``)：:meth:`post` – 发送 ``CMD`` 包并阻塞直到收到
      ``CMD_RESP`` 握手；最终的 ``ASYNC_RESP`` 会异步投递。
    - *无回复异步* 命令 (``->|``)：:meth:`post_no_reply` – 与
      :meth:`post` 相同，但不会有 ``ASYNC_RESP`` 到来。
    - *订阅 / 取消订阅*：:meth:`subscribe_status` /
      :meth:`unsubscribe_status` – 发送 ``<register>`` / ``<unregister>``
      命令并等待 ``CMD_RESP`` 握手。

    **接收包路由**（在后台接收线程上）：

    - ``RESP`` (0x04) – 解除 :meth:`send_and_wait` 调用者的阻塞。
    - ``CMD_RESP`` (0x03) – 解除 :meth:`post`、:meth:`post_no_reply`、
      :meth:`subscribe_status` 和 :meth:`unsubscribe_status` 调用者的阻塞。
    - ``ASYNC_RESP`` (0x05) – 加入 :attr:`async_response_queue` 并
      分发给匹配的 :meth:`register_async_callback`。
    - ``STATUS`` / ``INTERRUPT`` (0x06 / 0x07) – 加入 :attr:`status_queue`
      并分发给匹配的 :meth:`subscribe_status` 回调。
    - ``ERROR`` (0x01) – 以 :exc:`ServerError` 解除任何待处理的同步等待者。
    - ``INFO`` (0x00) – 静默丢弃（欢迎 / 再见消息）。
    """

    def __init__(self) -> None:
        self._transport = _Transport(
            on_packet=self._on_packet,
            on_disconnect=self._on_disconnect,
        )

        # 用于同步等待的单项队列。
        # 队列元素为 Packet 或 Exception 实例。
        self._resp_queue: queue.Queue[_QueueItem] = queue.Queue()
        self._cmd_resp_queue: queue.Queue[_QueueItem] = queue.Queue()

        #: 用于轮询从服务器收到的 :class:`AsyncResponse` 对象的队列。
        self.async_response_queue: queue.Queue[AsyncResponse] = queue.Queue()

        #: 用于轮询从服务器收到的 :class:`StatusNotification` 对象的队列。
        self.status_queue: queue.Queue[StatusNotification] = queue.Queue()

        # 回调注册表（由 _lock 保护）
        self._status_callbacks: Dict[_SubKey, Optional[StatusCallback]] = {}
        self._async_callbacks: Dict[str, AsyncCallback] = {}
        self._lock = threading.Lock()

        # 串行化锁 – 同时最多只有一个在途的 RESP / CMD_RESP 等待者，
        # 防止并发调用者消费彼此的响应包。
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
        will receive a :exc:`ConnectionError` immediately rather than
        waiting for their timeout to expire.
        """
        # Wake blocked waiters *before* tearing down the transport.
        sentinel = _RouterConnectionError("Disconnected from server.")
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

        :returns: ``True`` when the server is reachable; ``False`` on timeout.
        """
        deadline = time.monotonic() + timeout
        while time.monotonic() < deadline:
            probe = _Transport(
                on_packet=lambda _p: None,
                on_disconnect=lambda: None,
            )
            try:
                probe.connect(host, port, timeout=1.0)
                probe.disconnect()
                return True
            except _RouterConnectionError:
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
        """
        wire = encode_packet(command.encode("utf-8"), PacketType.CMD)
        with self._cmd_resp_lock:
            self._transport.send_raw(wire)
            self._wait_for_cmd_resp(timeout)

    def ping(self, timeout: float = 2.0) -> Tuple[bool, float]:
        """Send a ``Ping`` command and measure round-trip latency.

        :returns: ``(True, elapsed_seconds)`` on success,
                  ``(False, 0.0)`` on failure or error.
        """
        try:
            t0 = time.monotonic()
            self.send_and_wait("Ping", timeout=timeout)
            return True, time.monotonic() - t0
        except (_RouterConnectionError, _RouterTimeoutError, ServerError):
            return False, 0.0

    # ------------------------------------------------------------------
    # Router management helpers
    # ------------------------------------------------------------------

    def list_modules(self, timeout: float = 5.0) -> str:
        """Return the server's loaded CSM module list as plain text."""
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
        :class:`StatusNotification` objects will be:

        * delivered to *callback* (if provided), and
        * added to :attr:`status_queue`.

        :param callback: Optional callable invoked on each notification.
                         Must be fast and non-blocking (runs in the recv thread).
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
        """Cancel a status subscription."""
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
        sentinel = _RouterConnectionError("Connection lost unexpectedly.")
        self._resp_queue.put(sentinel)
        self._cmd_resp_queue.put(sentinel)

    # ------------------------------------------------------------------
    # Internal: synchronised waiters
    # ------------------------------------------------------------------

    def _wait_for_resp(self, timeout: float) -> CommandResponse:
        try:
            item = self._resp_queue.get(timeout=timeout)
        except queue.Empty:
            raise _RouterTimeoutError(
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
            raise _RouterTimeoutError(
                f"No cmd-resp received within {timeout:.1f}s."
            ) from None
        if isinstance(item, Exception):
            raise item
        # CMD_RESP payload is a handshake acknowledgment; discard it


# ===========================================================================
# AsyncTcpRouterClient – asyncio-based client
# ===========================================================================

# Async callback type aliases – both plain callables and coroutines accepted
_SyncStatusCb = Callable[[StatusNotification], None]
_AsyncStatusCb = Callable[[StatusNotification], "Coroutine[Any, Any, None]"]
AsyncStatusCallback = Union[_SyncStatusCb, _AsyncStatusCb]

_SyncAsyncRespCb = Callable[[AsyncResponse], None]
_AsyncAsyncRespCb = Callable[[AsyncResponse], "Coroutine[Any, Any, None]"]
AsyncRespCallback = Union[_SyncAsyncRespCb, _AsyncAsyncRespCb]


class AsyncTcpRouterClient:
    """Asyncio client for a CSM-TCP-Router server.

    Provides the same interface as :class:`TcpRouterClient` but as
    ``async def`` coroutines, suitable for use inside an asyncio event loop.

    **Quickstart**::

        import asyncio
        from csm_tcp_router_client import AsyncTcpRouterClient

        async def main():
            async with AsyncTcpRouterClient() as client:
                await client.connect("localhost", 30007)
                print(await client.list_modules())
                resp = await client.send_and_wait("API: Read -@ DAQmx")
                print(resp.text)

        asyncio.run(main())

    **Protocol flows** are identical to :class:`TcpRouterClient`.

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

        #: Polling queue for :class:`AsyncResponse` objects received from the
        #: server.  Available after :meth:`connect` is called.
        self.async_response_queue: Optional[asyncio.Queue[AsyncResponse]] = None

        #: Polling queue for :class:`StatusNotification` objects received from
        #: the server.  Available after :meth:`connect`.
        self.status_queue: Optional[asyncio.Queue[StatusNotification]] = None

        # Callback registries – plain dicts (asyncio is single-threaded)
        self._status_callbacks: Dict[_SubKey, Optional[AsyncStatusCallback]] = {}
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
        """Open a TCP connection and start the background receive task."""
        if self.connected:
            raise _RouterConnectionError(
                "Already connected; call disconnect() first."
            )
        self._init_async_objects()
        try:
            self._reader, self._writer = await asyncio.wait_for(
                asyncio.open_connection(host, port), timeout=timeout
            )
        except asyncio.TimeoutError:
            raise _RouterConnectionError(
                f"Connection to {host}:{port} timed out after {timeout:.1f}s."
            ) from None
        except OSError as exc:
            raise _RouterConnectionError(
                f"Cannot connect to {host}:{port}: {exc}"
            ) from exc
        self._recv_task = asyncio.ensure_future(self._recv_loop())

    async def disconnect(self) -> None:
        """Close the connection and stop the background receive task.

        Safe to call even if not currently connected.  Any coroutines currently
        blocked inside :meth:`send_and_wait`, :meth:`post`, or similar methods
        will receive a :exc:`ConnectionError` immediately rather than waiting
        for their timeout to expire.
        """
        # Wake blocked waiters *before* cancelling the recv task.
        sentinel = _RouterConnectionError("Disconnected from server.")
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
        """Poll until *host*:*port* accepts a connection or *timeout* elapses."""
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
        """Send a **synchronous** command and await the response."""
        wire = encode_packet(command.encode("utf-8"), PacketType.CMD)
        assert self._resp_lock is not None
        async with self._resp_lock:
            await self._send_raw(wire)
            return await self._wait_for_resp(timeout)

    async def post(self, command: str, timeout: float = 5.0) -> None:
        """Send an **asynchronous** command and await the ``cmd-resp`` handshake."""
        wire = encode_packet(command.encode("utf-8"), PacketType.CMD)
        assert self._cmd_resp_lock is not None
        async with self._cmd_resp_lock:
            await self._send_raw(wire)
            await self._wait_for_cmd_resp(timeout)

    async def post_no_reply(self, command: str, timeout: float = 5.0) -> None:
        """Send an **async no-reply** command and await the ``cmd-resp`` handshake."""
        wire = encode_packet(command.encode("utf-8"), PacketType.CMD)
        assert self._cmd_resp_lock is not None
        async with self._cmd_resp_lock:
            await self._send_raw(wire)
            await self._wait_for_cmd_resp(timeout)

    async def ping(self, timeout: float = 2.0) -> Tuple[bool, float]:
        """Send a ``Ping`` command and measure round-trip latency.

        :returns: ``(True, elapsed_seconds)`` on success, ``(False, 0.0)`` on failure.
        """
        try:
            t0 = time.monotonic()
            await self.send_and_wait("Ping", timeout=timeout)
            return True, time.monotonic() - t0
        except (_RouterConnectionError, _RouterTimeoutError, ServerError):
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
        return (
            await self.send_and_wait(f"List State {module}", timeout=timeout)
        ).text

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
        callback: Optional[AsyncStatusCallback] = None,
        timeout: float = 5.0,
    ) -> None:
        """Subscribe to a CSM module's status broadcast.

        Sends ``"<status_name>@<module_name> -><register>"`` and awaits the
        ``cmd-resp`` handshake.  Once subscribed, :class:`StatusNotification`
        objects will be:

        * delivered to *callback* (if provided – sync or async both accepted), and
        * added to :attr:`status_queue`.
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
        """Cancel a status subscription."""
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

        Callbacks may be either a plain callable or an ``async def`` coroutine.
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
            raise _RouterConnectionError("Not connected.")
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
        sentinel = _RouterConnectionError("Connection lost unexpectedly.")
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
            raise _RouterTimeoutError(
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
            raise _RouterTimeoutError(
                f"No cmd-resp received within {timeout:.1f}s."
            ) from None
        if isinstance(item, Exception):
            raise item
        # CMD_RESP payload is a handshake acknowledgment; discard it
