# Changelog

All notable changes to `csm-tcp-router-client` are documented here.

The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/).
This project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

---

## [Unreleased]

---

## [0.1.0] – 2026-04-22

### Added

- Initial release of the `csm-tcp-router-client` Python SDK.
- `TcpRouterClient` class with full thread-safe implementation of the
  CSM-TCP-Router protocol v0.
- Connection lifecycle: `connect()`, `disconnect()`, `wait_for_server()`,
  `connected` property, context-manager support.
- Synchronous command: `send_and_wait()`.
- Asynchronous command: `post()` with `CMD_RESP` handshake.
- No-reply async command: `post_no_reply()` with `CMD_RESP` handshake.
- Round-trip ping: `ping()`.
- Router management helpers: `list_modules()`, `list_api()`, `list_states()`,
  `help()`.
- Status / interrupt subscriptions: `subscribe_status()`,
  `unsubscribe_status()`, `register_async_callback()`,
  `unregister_async_callback()`.
- Polling queues: `status_queue`, `async_response_queue`.
- Typed exception hierarchy: `TcpRouterError`, `ConnectionError`,
  `TimeoutError`, `ProtocolError`, `ServerError` (with `.code` and `.message`).
- Public data models: `PacketType`, `Packet`, `CommandResponse`,
  `AsyncResponse`, `StatusNotification`.
- Internal protocol v0 codec (`_protocol.py`) with `encode_packet()`,
  `decode_header()`, `parse_packet()`; unknown packet types mapped to
  `INFO` for forward compatibility.
- Internal TCP transport layer (`_transport.py`) with background daemon
  receive thread, `memoryview`-based zero-copy reads, and clean shutdown.
- Comprehensive test suite: unit tests for protocol codec, unit tests for
  client dispatch logic (mock transport), and integration tests against a
  `MockServer` fixture.
- Examples: `basic_usage.py`, `subscribe_status.py`.
- `pyproject.toml` with `hatchling` build backend; ready for `pip install`
  and upload to PyPI.
- GitHub Actions workflow `Python_SDK.yml`: lint (ruff), test (pytest) on
  Python 3.8–3.12, build, and optional publish to PyPI on tag.

[Unreleased]: https://github.com/NEVSTOP-LAB/CSM-TCP-Router-App/compare/python-sdk-v0.1.0...HEAD
[0.1.0]: https://github.com/NEVSTOP-LAB/CSM-TCP-Router-App/releases/tag/python-sdk-v0.1.0
