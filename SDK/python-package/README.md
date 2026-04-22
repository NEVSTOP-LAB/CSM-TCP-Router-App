# csm-tcp-router-client

[![PyPI](https://img.shields.io/pypi/v/csm-tcp-router-client)](https://pypi.org/project/csm-tcp-router-client/)
[![Python](https://img.shields.io/pypi/pyversions/csm-tcp-router-client)](https://pypi.org/project/csm-tcp-router-client/)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)
[![CI](https://github.com/NEVSTOP-LAB/CSM-TCP-Router-App/actions/workflows/Python_SDK.yml/badge.svg)](https://github.com/NEVSTOP-LAB/CSM-TCP-Router-App/actions/workflows/Python_SDK.yml)

Python client SDK for the [CSM-TCP-Router](https://github.com/NEVSTOP-LAB/CSM-TCP-Router-App) LabVIEW server.

CSM-TCP-Router exposes a LabVIEW [Communicable State Machine (CSM)](https://github.com/NEVSTOP-LAB/Communicable-State-Machine) application over TCP so that any TCP client—including Python scripts, test harnesses, or CI pipelines—can send commands and receive responses without touching the LabVIEW code.

---

## Installation

```bash
pip install csm-tcp-router-client
```

Requires Python 3.8 or later.  No third-party dependencies—only the Python standard library.

---

## Quickstart

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

---

## Features

- **Synchronous commands** (`-@`) – `send_and_wait()` blocks until the server returns the response.
- **Asynchronous commands** (`->`) – `post()` waits for the `cmd-resp` handshake; the eventual response is delivered via callback or queue.
- **No-reply commands** (`->|`) – `post_no_reply()` waits for the `cmd-resp` handshake; no further response expected.
- **Status subscriptions** – `subscribe_status()` / `unsubscribe_status()` with optional callback or polling queue.
- **Router management helpers** – `list_modules()`, `list_api()`, `list_states()`, `help()`.
- **Connection utilities** – `wait_for_server()` for polling during app startup.
- **Thread-safe** – all methods may be called from multiple threads concurrently.
- **Zero dependencies** – pure Python standard library.
- **Context manager** support (`with TcpRouterClient() as client:`).

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

### `TcpRouterClient`

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

- [`basic_usage.py`](examples/basic_usage.py) – connect, ping, list modules, send commands.
- [`subscribe_status.py`](examples/subscribe_status.py) – real-time status subscription with callback.

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
pip install hatchling pytest ruff

# Run tests
pytest

# Lint
ruff check src/ tests/
```

---

## License

[MIT](LICENSE) — © NEVSTOP-LAB
