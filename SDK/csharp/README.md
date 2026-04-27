# csm-tcp-router-client (C#)

[![CI](https://github.com/NEVSTOP-LAB/CSM-TCP-Router-App/actions/workflows/CSharp_SDK.yml/badge.svg)](https://github.com/NEVSTOP-LAB/CSM-TCP-Router-App/actions/workflows/CSharp_SDK.yml)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)

C# / .NET client SDK for the [CSM-TCP-Router](https://github.com/NEVSTOP-LAB/CSM-TCP-Router-App) LabVIEW server.

CSM-TCP-Router exposes a LabVIEW [Communicable State Machine (CSM)](https://github.com/NEVSTOP-LAB/Communicable-State-Machine) application over TCP so any TCP client — including .NET applications, test harnesses, or CI pipelines — can send commands and receive responses without touching the LabVIEW code.

> 📖 [中文文档 README.zh-cn.md](README.zh-cn.md)

---

## Highlights

- **Single-file implementation** — the whole library lives in `src/CsmTcpRouterClient.cs`.
- **Sync + async API** — both blocking (`Connect`, `SendAndWait`, …) and `Task`-based (`ConnectAsync`, `SendAndWaitAsync`, …) methods on the same `TcpRouterClient`.
- **Zero third-party dependencies** at runtime — pure BCL.
- **VS 2026 ready** — SDK-style projects, builds in Visual Studio 2022 17.8+ and Visual Studio 2026.
- **Cross-platform** — Linux, macOS, Windows (anywhere .NET 8 runs).

---

## Installation

The package is distributed as a NuGet package (planned: `CsmTcpRouterClient`). To use it locally before publication, add a project reference to `src/CsmTcpRouterClient.csproj` or copy the single `src/CsmTcpRouterClient.cs` file into your project.

```xml
<ItemGroup>
  <ProjectReference Include="path/to/SDK/csharp/src/CsmTcpRouterClient.csproj" />
</ItemGroup>
```

Target frameworks: `net8.0`.

---

## Quickstart

### Synchronous

```csharp
using CsmTcpRouter;

using var client = new TcpRouterClient();
client.Connect("localhost", 30007);

// List all loaded CSM modules
Console.WriteLine(client.ListModules());

// Send a synchronous command and wait for the response
var resp = client.SendAndWait("API: Read -@ DAQmx");
Console.WriteLine(resp.Text);

// Ping the server
var (ok, elapsed) = client.Ping();
Console.WriteLine($"Ping: {ok}, latency={elapsed * 1000:F1} ms");
```

### Asynchronous

```csharp
using CsmTcpRouter;

await using var client = new TcpRouterClient();
await client.ConnectAsync("localhost", 30007);
Console.WriteLine(await client.ListModulesAsync());
var resp = await client.SendAndWaitAsync("API: Read -@ DAQmx");
Console.WriteLine(resp.Text);
```

---

## Protocol overview (v0)

CSM-TCP-Router uses an 8-byte big-endian header followed by a UTF-8 payload:

```
| Data Length (4B) | Version (1B = 0x01) | Type (1B) | FLAG1 (1B) | FLAG2 (1B) | <payload bytes> |
```

Packet types:

| Hex  | Name         | Direction | Description                                    |
|------|--------------|-----------|------------------------------------------------|
| 0x00 | `INFO`       | S → C     | Welcome / goodbye text                         |
| 0x01 | `ERROR`      | S → C     | Server error (`[Error: code] message`)         |
| 0x02 | `CMD`        | C → S     | Command sent by the client                     |
| 0x03 | `CMD_RESP`   | S → C     | Handshake for async / no-reply / subscribe     |
| 0x04 | `RESP`       | S → C     | Synchronous command response                   |
| 0x05 | `ASYNC_RESP` | S → C     | Asynchronous command response                  |
| 0x06 | `STATUS`     | S → C     | Status broadcast from a subscribed CSM module  |
| 0x07 | `INTERRUPT`  | S → C     | Interrupt broadcast from a subscribed module   |

---

## API reference

### Connection

| Sync                          | Async                              | Description                                       |
|-------------------------------|------------------------------------|---------------------------------------------------|
| `Connect(host, port, t?)`     | `ConnectAsync(host, port, t?)`     | Open a TCP connection.                            |
| `Disconnect()`                | `DisconnectAsync()`                | Close the connection and release resources.      |
| `WaitForServer(host, port, …)`| `WaitForServerAsync(host, port, …)`| Poll until the server accepts a connection.       |
| `Connected`                   | `Connected`                        | `bool` — currently connected?                    |

### Commands

| Sync                                   | Async                                       | Description                                |
|----------------------------------------|---------------------------------------------|--------------------------------------------|
| `SendAndWait(cmd, t?)` → `CommandResponse` | `SendAndWaitAsync(cmd, t?)` → `Task<CommandResponse>` | Send a sync command (`-@`).                |
| `Post(cmd, t?)`                        | `PostAsync(cmd, t?)`                        | Send an async command (`->`).              |
| `PostNoReply(cmd, t?)`                 | `PostNoReplyAsync(cmd, t?)`                 | Send a no-reply async command (`->|`).     |
| `Ping(t?)` → `(bool, double)`          | `PingAsync(t?)` → `Task<(bool, double)>`    | Round-trip latency check.                  |

### Router management helpers

| Sync                  | Async                       | Description                       |
|-----------------------|-----------------------------|-----------------------------------|
| `ListModules()`       | `ListModulesAsync()`        | Plain-text list of CSM modules.   |
| `ListApi(module)`     | `ListApiAsync(module)`      | Plain-text API list for a module. |
| `ListStates(module)`  | `ListStatesAsync(module)`   | Plain-text CSM state list.        |
| `Help(module)`        | `HelpAsync(module)`         | Plain-text help for a module.     |

### Status / interrupt subscriptions

| Sync                                       | Async                                            | Description                          |
|--------------------------------------------|--------------------------------------------------|--------------------------------------|
| `SubscribeStatus(name, module, cb?, t?)`   | `SubscribeStatusAsync(name, module, cb?, t?)`    | Subscribe to a module status broadcast. |
| `UnsubscribeStatus(name, module, t?)`      | `UnsubscribeStatusAsync(name, module, t?)`       | Cancel a subscription.               |
| `RegisterAsyncCallback(cmd, cb)`           | (same)                                           | Register an `async-resp` callback.   |
| `UnregisterAsyncCallback(cmd)`             | (same)                                           | Remove an `async-resp` callback.     |
| `StatusQueue` (`BlockingCollection<…>`)    | (same)                                           | Polling queue for status broadcasts. |
| `AsyncResponseQueue` (`BlockingCollection<…>`) | (same)                                       | Polling queue for `async-resp`.      |

### Exceptions

| Type                     | Raised when                                          |
|--------------------------|------------------------------------------------------|
| `TcpRouterError`         | Base type for all SDK exceptions.                    |
| `RouterConnectionError`  | Connection cannot be established or is lost.         |
| `RouterTimeoutError`     | A blocking / awaited operation times out.            |
| `ProtocolError`          | An invalid protocol frame is received.               |
| `ServerError`            | The server returns an `ERROR` packet (with `Code`).  |

---

## Building and testing

```bash
cd SDK/csharp

# Restore + build
dotnet build CsmTcpRouterClient.slnx -c Release

# Run unit + integration tests
dotnet test CsmTcpRouterClient.slnx -c Release

# Run the basic example (requires a running CSM-TCP-Router server on :30007)
dotnet run --project examples/BasicUsage
```

The repository also contains a Visual Studio 2026 / 2022 solution
(`CsmTcpRouterClient.slnx`) that opens directly in the IDE.

---

## Examples

| Path                              | Description                                                   |
|-----------------------------------|---------------------------------------------------------------|
| `examples/BasicUsage/`            | Connect, ping, list modules, list APIs.                       |
| `examples/SubscribeStatus/`       | Subscribe to a module status with both callback and queue.    |

---

## License

[MIT](LICENSE) © NEVSTOP-LAB
