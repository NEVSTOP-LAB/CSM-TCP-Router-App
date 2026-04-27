# csm-tcp-router-client (C SDK)

[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)
[![CI](https://github.com/NEVSTOP-LAB/CSM-TCP-Router-App/actions/workflows/C_SDK.yml/badge.svg)](https://github.com/NEVSTOP-LAB/CSM-TCP-Router-App/actions/workflows/C_SDK.yml)

C client SDK for the [CSM-TCP-Router](https://github.com/NEVSTOP-LAB/CSM-TCP-Router-App) LabVIEW server.

CSM-TCP-Router exposes a LabVIEW [Communicable State Machine (CSM)](https://github.com/NEVSTOP-LAB/Communicable-State-Machine) application over TCP so that any TCP client — including native C/C++ programs, embedded devices, test harnesses, or CI pipelines — can send commands and receive responses without touching the LabVIEW code.

> 📖 [中文文档 README.zh-cn.md](README.zh-cn.md)

---

## Features

- **Synchronous commands** (`-@`) – `csm_client_send_and_wait()` blocks until the server returns the response.
- **Asynchronous commands** (`->`) – `csm_client_post()` waits for the `cmd-resp` handshake; the eventual response is delivered via callback or polling queue.
- **No-reply commands** (`->|`) – `csm_client_post_no_reply()` waits for the `cmd-resp` handshake; no further response expected.
- **Status subscriptions** – `csm_client_subscribe_status()` / `csm_client_unsubscribe_status()` with optional callback or polling queue.
- **Router management helpers** – `csm_client_list_modules()`, `csm_client_list_api()`, `csm_client_list_states()`, `csm_client_help()`.
- **Connection utilities** – `csm_client_wait_for_server()` for polling during app startup.
- **Thread-safe client** – every public function is safe to call from multiple threads concurrently.
- **Multi-platform** – Windows (Winsock2 + Win32 threads) and POSIX (BSD sockets + pthreads); single source file.
- **Zero runtime dependencies** – C99 standard library + the OS sockets/threading APIs only.

---

## Layout

```
SDK/c/
├── include/
│   └── csm_tcp_router_client.h          # public API
├── src/
│   └── csm_tcp_router_client.c          # cross-platform implementation
├── examples/
│   ├── basic_usage.c                    # mirrors examples/basic_usage.py
│   └── subscribe_status.c               # mirrors examples/subscribe_status.py
├── tests/
│   ├── test_harness.h                   # tiny in-process test harness
│   ├── mock_server.[ch]                 # in-process MockServer fixture
│   ├── test_protocol.c                  # codec unit tests
│   ├── test_client.c                    # client-lifecycle unit tests
│   ├── test_integration.c               # end-to-end tests via MockServer
│   └── test_main.c                      # runner / TESTS table
├── vs2026/
│   ├── csm_tcp_router_client.sln
│   ├── csm_tcp_router_client/           # static-library project
│   └── csm_tcp_router_client.tests/     # test-executable project
├── CMakeLists.txt                       # cross-platform CMake build
├── CHANGELOG.md
├── LICENSE
├── README.md
└── README.zh-cn.md
```

This mirrors the layout of the Python SDK at `SDK/python/`.

---

## Building

### CMake (Linux / macOS / Windows)

```bash
cd SDK/c
cmake -S . -B build -DCMAKE_BUILD_TYPE=Release
cmake --build build -j
ctest --test-dir build --output-on-failure -C Release
```

CMake options:

| Option                  | Default | Description                          |
|-------------------------|---------|--------------------------------------|
| `CSM_BUILD_TESTS`       | `ON`    | Build the test executable.           |
| `CSM_BUILD_EXAMPLES`    | `ON`    | Build the example apps.              |
| `CSM_BUILD_SHARED`      | `OFF`   | Build a shared library (DLL/.so).    |

### Visual Studio 2026

Open `SDK/c/vs2026/csm_tcp_router_client.sln` in Visual Studio 2026 and
build (Ctrl+Shift+B). The solution provides Debug/Release × Win32/x64
configurations using the `v144` platform toolset. Two projects are
included:

- `csm_tcp_router_client` – static library
- `csm_tcp_router_client.tests` – console test executable (run it
  directly to execute all unit + integration tests; exit code 0 on
  success).

See [`vs2026/README.md`](vs2026/README.md) for details.

---

## Quickstart

```c
#include "csm_tcp_router_client.h"
#include <stdio.h>

int main(void) {
    csm_client_t *c = csm_client_create();
    if (csm_client_connect(c, "localhost", 30007, 5000) != CSM_OK) {
        fprintf(stderr, "Connect failed\n");
        csm_client_destroy(c);
        return 1;
    }

    char *modules = NULL;
    if (csm_client_list_modules(c, &modules, 5000) == CSM_OK) {
        printf("Modules:\n%s\n", modules);
        csm_string_free(modules);
    }

    csm_command_response_t resp = {0};
    if (csm_client_send_and_wait(c, "API: Read -@ DAQmx", 5000, &resp) == CSM_OK) {
        printf("Response: %s\n", (char *)resp.raw);
    }
    csm_command_response_dispose(&resp);

    double ms = 0;
    if (csm_client_ping(c, 2000, &ms) == CSM_OK) {
        printf("Ping latency: %.1f ms\n", ms);
    }

    csm_client_disconnect(c);
    csm_client_destroy(c);
    return 0;
}
```

---

## Protocol

The SDK implements the CSM-TCP-Router **protocol v0**.

```
| Data Length (4B) | Version (1B) | TYPE (1B) | FLAG1 (1B) | FLAG2 (1B) | Text Data |
╰────────────────────────── Header (8B) ─────────────────────────────╯
```

| TYPE byte | Constant            | Direction      | Description                                    |
|-----------|---------------------|----------------|------------------------------------------------|
| `0x00`    | `CSM_PT_INFO`       | Server → Client| Welcome / goodbye informational message        |
| `0x01`    | `CSM_PT_ERROR`      | Server → Client| CSM error: `[Error: <code>] <message>`         |
| `0x02`    | `CSM_PT_CMD`        | Client → Server| Command string                                 |
| `0x03`    | `CSM_PT_CMD_RESP`   | Server → Client| Handshake ACK for async / subscribe commands   |
| `0x04`    | `CSM_PT_RESP`       | Server → Client| Synchronous response payload                   |
| `0x05`    | `CSM_PT_ASYNC_RESP` | Server → Client| Async response: `<data> <- <original-cmd>`     |
| `0x06`    | `CSM_PT_STATUS`     | Server → Client| Status broadcast: `<name> >> <data> <- <module>` |
| `0x07`    | `CSM_PT_INTERRUPT`  | Server → Client| Interrupt broadcast (same format as STATUS)    |

---

## API at a glance

### Lifecycle

| Function | Description |
|---|---|
| `csm_client_create()` | Allocate a new client. |
| `csm_client_destroy(c)` | Disconnect (if connected) and free resources. |
| `csm_client_connect(c, host, port, timeout_ms)` | Open a TCP connection and start the receive thread. |
| `csm_client_disconnect(c)` | Close the connection; safe even when not connected. |
| `csm_client_is_connected(c)` | Non-zero while connected. |
| `csm_client_wait_for_server(host, port, timeout_ms, retry_ms)` | Poll until the server is reachable. |

### Commands

| Function | Description |
|---|---|
| `csm_client_send_and_wait(c, cmd, timeout, &resp)` | Synchronous command (`-@`). |
| `csm_client_post(c, cmd, timeout)` | Async command (`->`). |
| `csm_client_post_no_reply(c, cmd, timeout)` | No-reply async command (`->|`). |
| `csm_client_ping(c, timeout, &elapsed_ms)` | Round-trip latency check. |

### Router management helpers

| Function | Description |
|---|---|
| `csm_client_list_modules(c, &out_text, timeout)` | `List` command. |
| `csm_client_list_api(c, module, &out_text, timeout)` | `List API <module>`. |
| `csm_client_list_states(c, module, &out_text, timeout)` | `List State <module>`. |
| `csm_client_help(c, module, &out_text, timeout)` | `Help <module>`. |

Free `*out_text` with `csm_string_free()`.

### Subscriptions

| Function | Description |
|---|---|
| `csm_client_subscribe_status(c, status, module, cb, ud, timeout)` | Subscribe; callback invoked from receive thread. |
| `csm_client_unsubscribe_status(c, status, module, timeout)` | Cancel a subscription. |
| `csm_client_register_async_callback(c, cmd, cb, ud)` | Register callback for `ASYNC_RESP` packets. |
| `csm_client_unregister_async_callback(c, cmd)` | Remove a callback. |

### Polling queues (alternative to callbacks)

| Function | Description |
|---|---|
| `csm_client_poll_status(c, &out_notif, timeout)` | Block until next STATUS / INTERRUPT. |
| `csm_client_poll_async_response(c, &out_resp, timeout)` | Block until next `ASYNC_RESP`. |

---

## Result codes

All public functions return a `csm_result_t`:

| Code                  | Meaning                                         |
|-----------------------|-------------------------------------------------|
| `CSM_OK`              | Operation succeeded.                            |
| `CSM_ERR_INVALID`     | Invalid argument or NULL pointer.               |
| `CSM_ERR_CONNECTION`  | Connection failed or was lost.                  |
| `CSM_ERR_TIMEOUT`     | Operation exceeded its timeout.                 |
| `CSM_ERR_PROTOCOL`    | Invalid / malformed protocol frame.             |
| `CSM_ERR_SERVER`      | Server returned an `ERROR` packet (see below).  |
| `CSM_ERR_NOMEM`       | Memory allocation failure.                      |
| `CSM_ERR_STATE`       | Operation invalid in the current state.         |
| `CSM_ERR_IO`          | Underlying socket / OS I/O error.               |

After a `CSM_ERR_SERVER`, retrieve the error code/message via:

```c
csm_server_error_t err;
csm_client_last_server_error(c, &err);
fprintf(stderr, "[%s] %s\n", err.code, err.message);
```

`csm_result_str(code)` returns a static, human-readable string.

---

## Tests

The test suite (`SDK/c/tests/`) uses a tiny in-process harness and an
embedded `MockServer` (see `tests/mock_server.h`) that emulates the
LabVIEW CSM-TCP-Router on `127.0.0.1`. The same tests run on Linux,
macOS, and Windows.

```bash
cmake --build build -j
ctest --test-dir build --output-on-failure
```

Or run the executable directly to see per-test progress:

```bash
./build/csm_tcp_router_client_tests
```

---

## License

[MIT](LICENSE) — © NEVSTOP-LAB
