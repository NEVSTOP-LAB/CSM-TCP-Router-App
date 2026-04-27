# CsmTcpRouter.Client

[![NuGet](https://img.shields.io/nuget/v/CsmTcpRouter.Client.svg)](https://www.nuget.org/packages/CsmTcpRouter.Client/)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)
[![CI](https://github.com/NEVSTOP-LAB/CSM-TCP-Router-App/actions/workflows/CSharp_SDK.yml/badge.svg)](https://github.com/NEVSTOP-LAB/CSM-TCP-Router-App/actions/workflows/CSharp_SDK.yml)

C# / .NET client SDK for the [CSM-TCP-Router](https://github.com/NEVSTOP-LAB/CSM-TCP-Router-App) LabVIEW server.

CSM-TCP-Router exposes a LabVIEW [Communicable State Machine (CSM)](https://github.com/NEVSTOP-LAB/Communicable-State-Machine) application over TCP so that any TCP client — including .NET applications, test harnesses, or CI pipelines — can send commands and receive responses without touching the LabVIEW code.

> 📖 [中文文档 README.zh-cn.md](README.zh-cn.md)

The entire SDK lives in a single source file: [`src/CsmTcpRouter/CsmTcpRouter.cs`](src/CsmTcpRouter/CsmTcpRouter.cs).

---

## Installation

```bash
dotnet add package CsmTcpRouter.Client
```

Targets `netstandard2.0` and `net8.0`. No third-party runtime dependencies — only the .NET BCL.

Supported platforms include .NET Framework 4.6.2+, .NET Core 3.1+, and .NET 5/6/7/8/9.

---

## Quickstart

### Synchronous client

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
Console.WriteLine($"Ping: {ok}, latency={elapsed.TotalMilliseconds:F1} ms");
```

### Asynchronous client

```csharp
using CsmTcpRouter;

using var client = new TcpRouterClient();
await client.ConnectAsync("localhost", 30007);

Console.WriteLine(await client.ListModulesAsync());
var resp = await client.SendAndWaitAsync("API: Read -@ DAQmx");
Console.WriteLine(resp.Text);
```

### Subscribe to status broadcasts

```csharp
client.SubscribeStatus("Status", "AI", notif =>
{
    Console.WriteLine($"{notif.StatusName} = {notif.Data}");
});

// ... later
client.UnsubscribeStatus("Status", "AI");
```

### Async-response callbacks

```csharp
client.RegisterAsyncCallback("API: Start Sampling -> DAQmx", ar =>
{
    Console.WriteLine($"Async-resp: {ar.Text}");
});

client.Post("API: Start Sampling -> DAQmx");
```

See [`examples/BasicUsage/Program.cs`](examples/BasicUsage/Program.cs) for a complete runnable example.

---

## API reference

### `TcpRouterClient`

| Method                                                  | Description                                                                                              |
| ------------------------------------------------------- | -------------------------------------------------------------------------------------------------------- |
| `Connect / ConnectAsync(host, port, timeout?)`          | Open a TCP connection.                                                                                   |
| `Disconnect()`                                          | Close the connection and unblock any pending waiters with `RouterConnectionException`.                   |
| `Connected`                                             | `true` while the underlying socket is open.                                                              |
| `WaitForServer / WaitForServerAsync(...)`               | Poll `host:port` until a connection succeeds or the timeout elapses.                                     |
| `SendAndWait / SendAndWaitAsync(cmd, timeout?)`         | Send a synchronous command (`-@`) and block until the `RESP` packet arrives. Returns `CommandResponse`.  |
| `Post / PostAsync(cmd, timeout?)`                       | Send an async command (`->`) and wait for the `CMD_RESP` handshake.                                      |
| `PostNoReply / PostNoReplyAsync(cmd, timeout?)`         | Send a no-reply async command (`->\|`) and wait for the `CMD_RESP` handshake.                            |
| `Ping / PingAsync(timeout?)`                            | Round-trip latency check; returns `(bool ok, TimeSpan elapsed)`.                                         |
| `ListModules / ListApi / ListStates / Help(...)`        | Built-in router-management helpers.                                                                      |
| `SubscribeStatus / UnsubscribeStatus(...)`              | Register / cancel a `STATUS` (or `INTERRUPT`) broadcast subscription.                                    |
| `RegisterAsyncCallback / UnregisterAsyncCallback(...)`  | Register / remove a callback dispatched for matching `ASYNC_RESP` packets.                               |
| `AsyncResponseQueue`, `StatusQueue`                     | Polling alternatives to callbacks (`ConcurrentQueue<>`).                                                 |

### Models

* `PacketType` (enum) — wire-level packet types as in the protocol v0 spec.
* `Packet`, `CommandResponse`, `AsyncResponse`, `StatusNotification` — decoded payloads.

### Exceptions

* `CsmTcpRouterException` — base class.
* `RouterConnectionException` — connect / send / disconnect failures.
* `RouterTimeoutException` — synchronous waiter timeout.
* `ProtocolException` — invalid wire framing.
* `ServerException` — server returned an `ERROR` packet (`Code` and `ServerMessage`).

---

## Protocol

CSM-TCP-Router protocol v0 uses an 8-byte header (big-endian) followed by an arbitrary payload:

```
| Data Length (4B) | Version (1B = 0x01) | Type (1B) | FLAG1 (1B) | FLAG2 (1B) | <payload...>
+------------------------ Header (8B) ----------------------------+
```

Packet types:

| Value | Name         | Direction         | Use                                            |
| ----- | ------------ | ----------------- | ---------------------------------------------- |
| 0x00  | `Info`       | server → client   | Welcome / goodbye message.                     |
| 0x01  | `Error`      | server → client   | Error reply (`[Error: <code>] <msg>`).         |
| 0x02  | `Cmd`        | client → server   | Command from the client.                       |
| 0x03  | `CmdResp`    | server → client   | Async / no-reply / subscribe handshake.        |
| 0x04  | `Resp`       | server → client   | Synchronous command response.                  |
| 0x05  | `AsyncResp`  | server → client   | Asynchronous command response (echoes cmd).    |
| 0x06  | `Status`     | server → client   | Status broadcast from a subscribed module.     |
| 0x07  | `Interrupt`  | server → client   | Interrupt broadcast from a subscribed module.  |

---

## Building from source

```bash
# Restore + build the whole solution
dotnet build SDK/csharp/CsmTcpRouter.sln -c Release

# Run the unit + integration test suite
dotnet test SDK/csharp/CsmTcpRouter.Tests/CsmTcpRouter.Tests.csproj -c Release

# Build the NuGet package
dotnet pack SDK/csharp/src/CsmTcpRouter/CsmTcpRouter.csproj -c Release -o nupkg
```

The solution opens directly in **Visual Studio 2022 / 2026** (or **JetBrains Rider**); SDK-style projects are forward-compatible with all current Visual Studio versions.

---

## Project layout

```
SDK/csharp/
├── CsmTcpRouter.sln
├── README.md / README.zh-cn.md / CHANGELOG.md / LICENSE
├── src/CsmTcpRouter/                ← single-file SDK
│   ├── CsmTcpRouter.cs
│   └── CsmTcpRouter.csproj
├── tests/CsmTcpRouter.Tests/        ← xUnit test project
│   ├── ProtocolTests.cs
│   ├── ClientIntegrationTests.cs
│   ├── MockServer.cs
│   └── CsmTcpRouter.Tests.csproj
└── examples/BasicUsage/             ← runnable console example
    ├── Program.cs
    └── BasicUsage.csproj
```

---

## License

Released under the [MIT License](LICENSE) — © 2026 NEVSTOP-LAB.
