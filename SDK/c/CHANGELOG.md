# Changelog

All notable changes to the C `csm-tcp-router-client` SDK will be
documented in this file. The format is based on
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and this
project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [0.1.0] - 2026-04-27

Initial public release of the C SDK.

### Added

- Cross-platform TCP client implementation (Windows + POSIX).
  - Single source file `src/csm_tcp_router_client.c`.
  - Single header `include/csm_tcp_router_client.h`.
  - Background receive thread with thread-safe public API.
- CSM-TCP-Router protocol v0 codec (`csm_encode_packet`,
  `csm_decode_header`, `csm_parse_packet`).
- Synchronous command API (`csm_client_send_and_wait`).
- Asynchronous command API (`csm_client_post`,
  `csm_client_post_no_reply`) with callback + polling-queue delivery.
- Status / interrupt subscription API
  (`csm_client_subscribe_status` / `csm_client_unsubscribe_status`,
  `csm_client_poll_status`).
- Router management helpers: `csm_client_list_modules`,
  `csm_client_list_api`, `csm_client_list_states`, `csm_client_help`.
- Connection utilities: `csm_client_ping`,
  `csm_client_wait_for_server`, `csm_client_is_connected`.
- Server error inspection via `csm_client_last_server_error`.
- Examples: `examples/basic_usage.c`, `examples/subscribe_status.c`.
- Test suite using an in-process `MockServer` fixture
  (`tests/mock_server.[ch]`) and a tiny custom test harness
  (`tests/test_harness.h` + `tests/test_main.c`):
  protocol codec tests, client-lifecycle tests, end-to-end integration
  tests.
- CMake build (`CMakeLists.txt`) with options for tests, examples, and
  shared-library output; `ctest` integration.
- Visual Studio 2026 (toolset `v144`) solution + projects under
  `vs2026/` for IDE-driven build & test on Windows.
- GitHub Actions CI workflow (`.github/workflows/C_SDK.yml`) building
  and running tests on Ubuntu, Windows and macOS.
