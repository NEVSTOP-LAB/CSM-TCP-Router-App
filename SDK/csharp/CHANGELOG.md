# Changelog

All notable changes to `CsmTcpRouter.Client` are documented here.

The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/).
This project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

---

## [Unreleased]

---

## [0.1.0] – 2026-04-27

### Added

- Initial release of the C# / .NET client SDK for the CSM-TCP-Router LabVIEW
  server.
- Single-file implementation in `src/CsmTcpRouter/CsmTcpRouter.cs`.
- Multi-targets `netstandard2.0` (for .NET Framework 4.6.2+, .NET Core 3.1+,
  .NET 5/6/7/8/9) and `net8.0`. Zero third-party runtime dependencies.
- Public types:
  - `TcpRouterClient` – synchronous and asynchronous APIs:
    `Connect/ConnectAsync`, `Disconnect`, `WaitForServer/WaitForServerAsync`,
    `SendAndWait/SendAndWaitAsync`, `Post/PostAsync`,
    `PostNoReply/PostNoReplyAsync`, `Ping/PingAsync`, `ListModules`,
    `ListApi`, `ListStates`, `Help`, `SubscribeStatus/UnsubscribeStatus`,
    `RegisterAsyncCallback/UnregisterAsyncCallback`.
  - `PacketType` enum and data models `Packet`, `CommandResponse`,
    `AsyncResponse`, `StatusNotification`.
  - Exception hierarchy: `CsmTcpRouterException`, `RouterConnectionException`,
    `RouterTimeoutException`, `ProtocolException`, `ServerException`.
- xUnit test suite (51 tests) covering protocol codec, error parsing, model
  parsing, and end-to-end client behaviour against an in-process MockServer.
- Runnable console example `examples/BasicUsage/`.
- VS 2022 / 2026 compatible solution `CsmTcpRouter.sln`.
- GitHub Actions workflow `.github/workflows/CSharp_SDK.yml` for build, test,
  pack, and conditional NuGet publish on `csharp-sdk-v*` tag pushes.
- English (`README.md`) and Chinese (`README.zh-cn.md`) documentation.
