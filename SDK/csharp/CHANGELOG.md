# Changelog

All notable changes to `CsmTcpRouterClient` (the C# SDK) are documented here.

The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/).
This project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

---

## [Unreleased]

---

## [0.1.0] – 2026-04-27

### Added

- Initial release of the `CsmTcpRouterClient` C# SDK.
- Single-file implementation in `src/CsmTcpRouterClient.cs` containing:
  - Wire-protocol v0 codec (`Protocol.EncodePacket`, `Protocol.DecodeHeader`,
    `Protocol.ParsePacket`) with 8-byte big-endian header.
  - `PacketType` enum mirroring the LabVIEW server's packet types
    (`Info`, `Error`, `Cmd`, `CmdResp`, `Resp`, `AsyncResp`, `Status`,
    `Interrupt`).
  - Public data models: `Packet`, `CommandResponse`, `AsyncResponse`,
    `StatusNotification`.
  - Typed exception hierarchy: `TcpRouterError`, `RouterConnectionError`,
    `RouterTimeoutError`, `ProtocolError`, `ServerError` (with `Code` and
    `Message`).
  - `TcpRouterClient` providing both a synchronous and a `Task`-based
    asynchronous API over the same TCP connection:
    - `Connect` / `ConnectAsync`, `Disconnect` / `DisconnectAsync`,
      `WaitForServer` / `WaitForServerAsync`, `Connected` property.
    - `SendAndWait` / `SendAndWaitAsync` (synchronous `-@` commands).
    - `Post` / `PostAsync` (asynchronous `->` commands with `CMD_RESP`
      handshake).
    - `PostNoReply` / `PostNoReplyAsync` (no-reply `->|` commands).
    - `Ping` / `PingAsync` (round-trip latency check).
    - Router management helpers: `ListModules`, `ListApi`, `ListStates`,
      `Help` (and `*Async` variants).
    - `SubscribeStatus` / `UnsubscribeStatus` (and `*Async` variants),
      `RegisterAsyncCallback` / `UnregisterAsyncCallback`.
    - Polling queues: `StatusQueue`, `AsyncResponseQueue`
      (`BlockingCollection<T>`).
    - `IDisposable` and `IAsyncDisposable` support for `using` /
      `await using` lifetime management.
- VS 2026 / VS 2022 SDK-style project layout:
  - Library project `src/CsmTcpRouterClient.csproj` (target `net8.0`,
    packable, with NuGet package metadata).
  - xUnit test project `tests/CsmTcpRouterClient.Tests/`.
  - Example projects `examples/BasicUsage/`, `examples/SubscribeStatus/`.
  - Solution file `CsmTcpRouterClient.slnx` (modern `.slnx` format
    supported by Visual Studio 2022 17.10+ and Visual Studio 2026).
- Comprehensive test suite (60 tests):
  - Unit tests for the protocol codec.
  - Unit tests for client packet dispatch logic.
  - Integration tests against a `MockServer` fixture (mirrors the Python
    SDK's `conftest.py:MockServer`).
- Documentation: English `README.md` and Chinese `README.zh-cn.md`,
  matching the bilingual structure of the Python SDK docs.
- GitHub Actions workflow `.github/workflows/CSharp_SDK.yml`: restore,
  build, test, and pack the NuGet artifact on every push / PR touching
  `SDK/csharp/**`.

[Unreleased]: https://github.com/NEVSTOP-LAB/CSM-TCP-Router-App/compare/csharp-sdk-v0.1.0...HEAD
[0.1.0]: https://github.com/NEVSTOP-LAB/CSM-TCP-Router-App/releases/tag/csharp-sdk-v0.1.0
