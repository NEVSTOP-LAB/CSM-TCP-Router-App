# csm-tcp-router-client

[![PyPI](https://img.shields.io/pypi/v/csm-tcp-router-client)](https://pypi.org/project/csm-tcp-router-client/)
[![Python](https://img.shields.io/pypi/pyversions/csm-tcp-router-client)](https://pypi.org/project/csm-tcp-router-client/)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)
[![CI](https://github.com/NEVSTOP-LAB/CSM-TCP-Router-App/actions/workflows/Python_SDK.yml/badge.svg)](https://github.com/NEVSTOP-LAB/CSM-TCP-Router-App/actions/workflows/Python_SDK.yml)

Python client SDK for the [CSM-TCP-Router](https://github.com/NEVSTOP-LAB/CSM-TCP-Router-App) LabVIEW server.

CSM-TCP-Router exposes a LabVIEW [Communicable State Machine (CSM)](https://github.com/NEVSTOP-LAB/Communicable-State-Machine) application over TCP so that any TCP client—including Python scripts, test harnesses, or CI pipelines—can send commands and receive responses without touching the LabVIEW code.

> 📖 [中文文档 README.zh-cn.md](README.zh-cn.md)

---

## Installation

```bash
pip install csm-tcp-router-client
```

Requires Python 3.8 or later.  No third-party dependencies—only the Python standard library.

---

## Quickstart

### Synchronous client

```python
from csm_tcp_router import TcpRouterClient

with TcpRouterClient() as client:
    client.connect("localhost", 30007)

    # List all loaded CSM modules
    print(client.list_modules())

    # Send a synchronous command and wait for the response
    resp = client.send_and_wait("API: Read -@ DAQmx")
    print(resp.text)

    # Ping the server
    ok, elapsed_s = client.ping()
    print(f"Ping: {ok}, latency={elapsed_s*1000:.1f} ms")
```

### Asyncio client

```python
import asyncio
from csm_tcp_router import AsyncTcpRouterClient

async def main():
    async with AsyncTcpRouterClient() as client:
        await client.connect("localhost", 30007)
        print(await client.list_modules())
        resp = await client.send_and_wait("API: Read -@ DAQmx")
        print(resp.text)

asyncio.run(main())
```

---

## Features

- **Synchronous commands** (`-@`) – `send_and_wait()` blocks until the server returns the response.
- **Asynchronous commands** (`->`) – `post()` waits for the `cmd-resp` handshake; the eventual response is delivered via callback or queue.
- **No-reply commands** (`->|`) – `post_no_reply()` waits for the `cmd-resp` handshake; no further response expected.
- **Status subscriptions** – `subscribe_status()` / `unsubscribe_status()` with optional callback or polling queue.
- **Router management helpers** – `list_modules()`, `list_api()`, `list_states()`, `help()`.
- **Connection utilities** – `wait_for_server()` for polling during app startup.
- **Thread-safe sync client** – `TcpRouterClient`: all methods may be called from multiple threads concurrently.
- **Asyncio client** – `AsyncTcpRouterClient`: full `async def` API with both sync and async callbacks supported.
- **Zero dependencies** – pure Python standard library.
- **Context manager** support (`with TcpRouterClient()` / `async with AsyncTcpRouterClient()`).

---

## Protocol

The SDK implements the CSM-TCP-Router **protocol v0**.

```
| Data Length (4B) | Version (1B) | TYPE (1B) | FLAG1 (1B) | FLAG2 (1B) | Text Data |
╰────────────────────────── Header (8B) ─────────────────────────────╯
```

| TYPE byte | Name          | Direction      | Description                                    |
|-----------|---------------|----------------|------------------------------------------------|
| `0x00`    | `INFO`        | Server → Client| Welcome / goodbye informational message        |
| `0x01`    | `ERROR`       | Server → Client| CSM error: `[Error: <code>] <message>`         |
| `0x02`    | `CMD`         | Client → Server| Command string                                 |
| `0x03`    | `CMD_RESP`    | Server → Client| Handshake ACK for async / subscribe commands   |
| `0x04`    | `RESP`        | Server → Client| Synchronous response payload                   |
| `0x05`    | `ASYNC_RESP`  | Server → Client| Async response: `<data> <- <original-cmd>`     |
| `0x06`    | `STATUS`      | Server → Client| Status broadcast: `<name> >> <data> <- <module>` |
| `0x07`    | `INTERRUPT`   | Server → Client| Interrupt broadcast (same format as STATUS)    |

### Communication flows

**Synchronous (`-@`)**

```
Client ─── CMD ──────────────────► Server
Client ◄── RESP (or ERROR) ─────── Server
```

**Asynchronous (`->`)**

```
Client ─── CMD ──────────────────► Server
Client ◄── CMD_RESP (or ERROR) ─── Server   ← handshake
Client ◄── ASYNC_RESP ──────────── Server   ← later, async result
```

**No-reply (`->|`)**

```
Client ─── CMD ──────────────────► Server
Client ◄── CMD_RESP (or ERROR) ─── Server   ← handshake; no further reply
```

**Subscribe / unsubscribe**

```
Client ─── CMD (<register>) ─────► Server
Client ◄── CMD_RESP (or ERROR) ─── Server
  … (whenever the CSM module emits the status) …
Client ◄── STATUS ──────────────── Server
Client ─── CMD (<unregister>) ───► Server
Client ◄── CMD_RESP ─────────────── Server
```

---

## API Reference

### `TcpRouterClient` (sync)

#### Connection

| Method | Description |
|---|---|
| `connect(host, port, timeout=5.0)` | Connect to the server; raises `ConnectionError` on failure. |
| `disconnect()` | Close the connection; safe to call even when not connected. |
| `connected` | `True` when the transport is connected. |
| `wait_for_server(host, port, timeout=30, retry_interval=0.5)` | Poll until the server is reachable; returns `True`/`False`. |

#### Commands

| Method | Description |
|---|---|
| `send_and_wait(command, timeout=5.0) → CommandResponse` | Synchronous command (`-@`); blocks until `RESP` arrives. |
| `post(command, timeout=5.0)` | Async command (`->`); waits for `CMD_RESP` handshake. |
| `post_no_reply(command, timeout=5.0)` | No-reply command (`->|`); waits for `CMD_RESP` handshake. |
| `ping(timeout=2.0) → (bool, float)` | Round-trip latency check. |

#### Router management helpers

| Method | Description |
|---|---|
| `list_modules(timeout=5.0) → str` | `List` command result. |
| `list_api(module, timeout=5.0) → str` | `List API <module>` result. |
| `list_states(module, timeout=5.0) → str` | `List State <module>` result. |
| `help(module, timeout=5.0) → str` | `Help <module>` result. |

#### Subscriptions

| Method | Description |
|---|---|
| `subscribe_status(status_name, module_name, callback=None, timeout=5.0)` | Subscribe; optional callback invoked per notification. |
| `unsubscribe_status(status_name, module_name, timeout=5.0)` | Unsubscribe. |
| `register_async_callback(original_command, callback)` | Register a callback for `ASYNC_RESP` packets. |
| `unregister_async_callback(original_command)` | Remove an async callback. |

#### Queues (polling alternative to callbacks)

| Attribute | Type | Description |
|---|---|---|
| `status_queue` | `Queue[StatusNotification]` | Receive status/interrupt broadcasts by polling. |
| `async_response_queue` | `Queue[AsyncResponse]` | Receive async responses by polling. |

---

### `AsyncTcpRouterClient` (asyncio)

All methods are `async def` coroutines; use `await` to call them.

#### Connection

| Method | Description |
|---|---|
| `await connect(host, port, timeout=5.0)` | Open a TCP connection; raises `ConnectionError` on failure. |
| `await disconnect()` | Close the connection; safe to call when not connected. |
| `connected` | `True` when the writer is open. |
| `await wait_for_server(host, port, timeout=30, retry_interval=0.5)` | Poll until the server is reachable. |

#### Commands

| Method | Description |
|---|---|
| `await send_and_wait(command, timeout=5.0) → CommandResponse` | Synchronous command (`-@`). |
| `await post(command, timeout=5.0)` | Async command (`->`). |
| `await post_no_reply(command, timeout=5.0)` | No-reply command (`->|`). |
| `await ping(timeout=2.0) → (bool, float)` | Round-trip latency check. |

#### Router management helpers

Same as sync client but all methods are `async def`.

#### Subscriptions

| Method | Description |
|---|---|
| `await subscribe_status(status_name, module_name, callback=None, timeout=5.0)` | Subscribe; callback may be sync or `async def`. |
| `await unsubscribe_status(status_name, module_name, timeout=5.0)` | Unsubscribe. |
| `register_async_callback(original_command, callback)` | Register callback for `ASYNC_RESP`; may be sync or `async def`. |
| `unregister_async_callback(original_command)` | Remove callback. |

#### Queues

| Attribute | Type | Description |
|---|---|---|
| `status_queue` | `asyncio.Queue[StatusNotification]` | Available after `connect()`; poll with `await queue.get()`. |
| `async_response_queue` | `asyncio.Queue[AsyncResponse]` | Available after `connect()`. |

---

### Data models

#### `CommandResponse`
- `.raw: bytes` – raw server payload
- `.text: str` – UTF-8 decoded text

#### `AsyncResponse`
- `.raw: bytes`, `.text: str`
- `.original_command: str` – the command echoed by the server

#### `StatusNotification`
- `.raw: bytes`
- `.packet_type: PacketType` – `STATUS` or `INTERRUPT`
- `.status_name: str` – e.g. `"Status"`
- `.data: str` – the broadcasted value
- `.module_name: str` – the sending CSM module

### Exceptions

| Exception | Raised when |
|---|---|
| `TcpRouterError` | Base class for all SDK exceptions |
| `ConnectionError` | TCP connection fails or is lost |
| `TimeoutError` | No response within the timeout window |
| `ProtocolError` | Invalid or unexpected wire frame |
| `ServerError` | Server returns an `ERROR` packet; `.code` and `.message` attributes available |

---

## Examples

See the [`examples/`](examples/) directory:

- [`basic_usage.py`](examples/basic_usage.py) – sync client: connect, ping, list modules, send commands.
- [`subscribe_status.py`](examples/subscribe_status.py) – sync client: real-time status subscription with callback.
- [`async_usage.py`](examples/async_usage.py) – asyncio client: all features using `async def` / `await`.

---

## Migration from the script SDK

The previous single-file SDK (`SDK/PythonClientAPI/tcp_router_client.py`) is
still available but is not pip-installable and uses a different packet-type
numbering (aligned with protocol v1-draft rather than the published v0 spec).

| Old method | New method | Notes |
|---|---|---|
| `connect()` | `connect()` | Returns `None`; raises `ConnectionError` instead of returning `False` |
| `disconnect()` | `disconnect()` | Unchanged |
| `send_message_and_wait_for_reply(msg)` | `send_and_wait(cmd)` | Returns `CommandResponse`; raises on error |
| `post_message(msg)` | `post(cmd)` | Waits for `CMD_RESP` handshake |
| `post_no_rep_message(msg)` | `post_no_reply(cmd)` | Waits for `CMD_RESP` handshake |
| `ping()` | `ping()` | Same signature |
| `register_status_change(s, m, cb)` | `subscribe_status(s, m, callback=cb)` | Raises on error instead of returning `False` |
| `unregister_status_change(s, m)` | `unsubscribe_status(s, m)` | Raises on error |
| `wait_for_server(h, p, t)` | `wait_for_server(h, p, timeout=t)` | Keyword arg |
| `obtain()` / `release()` | Use context manager `with TcpRouterClient() as c:` | – |

---

## Development

```bash
# Install dev dependencies
pip install -e ".[dev]"
# or
pip install hatchling pytest pytest-asyncio ruff

# Run tests (sync + async)
pytest

# Lint
ruff check src/ tests/
```

---

## License

[MIT](LICENSE) — © NEVSTOP-LAB

