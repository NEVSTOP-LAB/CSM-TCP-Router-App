# Changelog

All notable changes to `csm-tcp-router-client` are documented here.

The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/).
This project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

---

## [Unreleased]

---

## [0.2.0] – 2026-04-22

### Added

- `AsyncTcpRouterClient` class: full asyncio API mirroring every method of
  `TcpRouterClient`, using `asyncio.StreamReader`/`StreamWriter` and
  `asyncio.Queue` for non-blocking I/O.
- Async context-manager support: `async with AsyncTcpRouterClient() as client:`.
- Both sync and `async def` callbacks supported for `subscribe_status()` and
  `register_async_callback()` on the async client.
- `AsyncTcpRouterClient` exported from the top-level `csm_tcp_router` package.
- `examples/async_usage.py` – asyncio quickstart demonstrating all features.
- Test suite extended with `tests/test_async_client.py` (48 tests: unit +
  integration via `MockServer`); test runner now uses `asyncio_mode = "auto"`.
- `pytest-asyncio` added to CI test dependencies.
- Chinese documentation: `README.zh-cn.md` (full translation of `README.md`).
- `README.md` updated with asyncio quickstart, async API reference table, link
  to Chinese docs, and `async_usage.py` in the examples list.
- CI: added `publish-testpypi` job that publishes to TestPyPI *before*
  `publish` (production PyPI); production publish now depends on TestPyPI
  success; both use OIDC trusted publishing.
- `Framework :: AsyncIO` classifier added to package metadata.

### Changed

- Package version bumped to `0.2.0`.
- `asyncio_mode = "auto"` added to `pyproject.toml` pytest options; all async
  tests run automatically without explicit `@pytest.mark.asyncio` decorators.

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

[Unreleased]: https://github.com/NEVSTOP-LAB/CSM-TCP-Router-App/compare/python-sdk-v0.2.0...HEAD
[0.2.0]: https://github.com/NEVSTOP-LAB/CSM-TCP-Router-App/compare/python-sdk-v0.1.0...python-sdk-v0.2.0
[0.1.0]: https://github.com/NEVSTOP-LAB/CSM-TCP-Router-App/releases/tag/python-sdk-v0.1.0
