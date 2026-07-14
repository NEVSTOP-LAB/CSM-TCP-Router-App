# csm-tcp-router-client (C SDK)

[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)
[![CI](https://github.com/NEVSTOP-LAB/CSM-TCP-Router-App/actions/workflows/C_SDK.yml/badge.svg)](https://github.com/NEVSTOP-LAB/CSM-TCP-Router-App/actions/workflows/C_SDK.yml)

[CSM-TCP-Router](https://github.com/NEVSTOP-LAB/CSM-TCP-Router-App) LabVIEW 服务端的 C 语言客户端 SDK。

CSM-TCP-Router 通过 TCP 暴露一个基于 LabVIEW [Communicable State Machine (CSM)](https://github.com/NEVSTOP-LAB/Communicable-State-Machine) 框架的应用程序，使任何 TCP 客户端 —— 包括原生 C/C++ 程序、嵌入式设备、测试夹具或 CI 流水线 —— 都可以发送命令并接收响应，无需修改任何 LabVIEW 代码。

> 📖 [English README.md](README.md)

---

## 特性

- **同步命令** (`-@`) – `csm_client_send_and_wait()` 阻塞直到服务器返回响应。
- **异步命令** (`->`) – `csm_client_post()` 等待 `cmd-resp` 握手；最终响应通过回调或轮询队列送达。
- **无回复命令** (`->|`) – `csm_client_post_no_reply()` 等待 `cmd-resp` 握手；不再有后续响应。
- **状态订阅** – `csm_client_subscribe_status()` / `csm_client_unsubscribe_status()`，可选回调或轮询队列。
- **路由器管理辅助函数** – `csm_client_list_modules()`、`csm_client_list_api()`、`csm_client_list_states()`、`csm_client_help()`。
- **连接工具** – 应用启动期间使用 `csm_client_wait_for_server()` 进行轮询。
- **线程安全的客户端** – 所有公开函数均可由多线程并发调用。
- **多平台移植** – Windows（Winsock2 + Win32 线程）和 POSIX（BSD sockets + pthreads）；单源文件实现。
- **无运行时依赖** – 仅依赖 C99 标准库与操作系统的 sockets/线程 API。

---

## 目录结构

```
SDK/c/
├── include/
│   └── csm_tcp_router_client.h          # 公开 API 头文件
├── src/
│   └── csm_tcp_router_client.c          # 跨平台实现
├── examples/
│   ├── basic_usage.c                    # 对应 examples/basic_usage.py
│   └── subscribe_status.c               # 对应 examples/subscribe_status.py
├── tests/
│   ├── test_harness.h                   # 进程内极简测试框架
│   ├── mock_server.[ch]                 # 进程内 MockServer 测试夹具
│   ├── test_protocol.c                  # 协议编解码单元测试
│   ├── test_client.c                    # 客户端生命周期单元测试
│   ├── test_integration.c               # 通过 MockServer 的端到端测试
│   └── test_main.c                      # 测试运行器/TESTS 表
├── vs2026/
│   ├── csm_tcp_router_client.sln
│   ├── csm_tcp_router_client/           # 静态库工程
│   └── csm_tcp_router_client.tests/     # 测试可执行工程
├── CMakeLists.txt                       # 跨平台 CMake 构建
├── CHANGELOG.md
├── LICENSE
├── README.md
└── README.zh-cn.md
```

整体结构与 `SDK/python/` 下的 Python SDK 保持一致。

---

## 编译

### CMake（Linux / macOS / Windows）

```bash
cd SDK/c
cmake -S . -B build -DCMAKE_BUILD_TYPE=Release
cmake --build build -j
ctest --test-dir build --output-on-failure -C Release
```

CMake 选项：

| 选项                    | 默认值  | 说明                              |
|-------------------------|---------|-----------------------------------|
| `CSM_BUILD_TESTS`       | `ON`    | 是否构建测试可执行文件。          |
| `CSM_BUILD_EXAMPLES`    | `ON`    | 是否构建示例程序。                |
| `CSM_BUILD_SHARED`      | `OFF`   | 是否编译为动态库（DLL/.so）。     |

### Visual Studio 2026

打开 `SDK/c/vs2026/csm_tcp_router_client.sln`，使用 Visual Studio 2026 直接构建（Ctrl+Shift+B）。该解决方案提供 Debug/Release × Win32/x64 四种配置，平台工具集为 `v144`，包含两个工程：

- `csm_tcp_router_client` – 静态库
- `csm_tcp_router_client.tests` – 控制台测试可执行文件（直接运行即可执行所有单元 + 集成测试，退出码为 0 即所有测试通过）。

详见 [`vs2026/README.md`](vs2026/README.md)。

---

## 快速开始

```c
#include "csm_tcp_router_client.h"
#include <stdio.h>

int main(void) {
    csm_client_t *c = csm_client_create();
    if (csm_client_connect(c, "localhost", 30007, 5000) != CSM_OK) {
        fprintf(stderr, "连接失败\n");
        csm_client_destroy(c);
        return 1;
    }

    char *modules = NULL;
    if (csm_client_list_modules(c, &modules, 5000) == CSM_OK) {
        printf("已加载模块:\n%s\n", modules);
        csm_string_free(modules);
    }

    csm_command_response_t resp = {0};
    if (csm_client_send_and_wait(c, "API: Read -@ DAQmx", 5000, &resp) == CSM_OK) {
        printf("响应: %s\n", (char *)resp.raw);
    }
    csm_command_response_dispose(&resp);

    double ms = 0;
    if (csm_client_ping(c, 2000, &ms) == CSM_OK) {
        printf("Ping 延迟: %.1f ms\n", ms);
    }

    csm_client_disconnect(c);
    csm_client_destroy(c);
    return 0;
}
```

---

## 协议

SDK 实现 CSM-TCP-Router **协议 v0**：

```
| 数据长度 (4B) | 版本 (1B) | TYPE (1B) | FLAG1 (1B) | FLAG2 (1B) | 文本数据 |
╰────────────────────────── 包头 (8B) ─────────────────────────────╯
```

| TYPE 字节 | 常量                | 方向            | 说明                                                |
|-----------|---------------------|----------------|----------------------------------------------------|
| `0x00`    | `CSM_PT_INFO`       | 服务器 → 客户端 | 欢迎/告别等信息消息                                |
| `0x01`    | `CSM_PT_ERROR`      | 服务器 → 客户端 | CSM 错误：`[Error: <code>] <message>`              |
| `0x02`    | `CSM_PT_CMD`        | 客户端 → 服务器 | 命令字符串                                         |
| `0x03`    | `CSM_PT_CMD_RESP`   | 服务器 → 客户端 | 异步/订阅命令的握手 ACK                            |
| `0x04`    | `CSM_PT_RESP`       | 服务器 → 客户端 | 同步响应负载                                       |
| `0x05`    | `CSM_PT_ASYNC_RESP` | 服务器 → 客户端 | 异步响应：`<data> <- <original-cmd>`               |
| `0x06`    | `CSM_PT_STATUS`     | 服务器 → 客户端 | 状态广播：`<name> >> <data> <- <module>`           |
| `0x07`    | `CSM_PT_INTERRUPT`  | 服务器 → 客户端 | 中断广播（与 STATUS 格式相同）                     |

---

## API 速览

### 生命周期

| 函数 | 说明 |
|---|---|
| `csm_client_create()` | 分配新的客户端。 |
| `csm_client_destroy(c)` | 如已连接则断开并释放资源。 |
| `csm_client_connect(c, host, port, timeout_ms)` | 建立 TCP 连接并启动接收线程。 |
| `csm_client_disconnect(c)` | 关闭连接；未连接时调用也安全。 |
| `csm_client_is_connected(c)` | 已连接时返回非零值。 |
| `csm_client_wait_for_server(host, port, timeout_ms, retry_ms)` | 轮询直到服务器可达。 |

### 命令

| 函数 | 说明 |
|---|---|
| `csm_client_send_and_wait(c, cmd, timeout, &resp)` | 同步命令 (`-@`)。 |
| `csm_client_post(c, cmd, timeout)` | 异步命令 (`->`)。 |
| `csm_client_post_no_reply(c, cmd, timeout)` | 无回复异步命令 (`->|`)。 |
| `csm_client_ping(c, timeout, &elapsed_ms)` | 往返延迟检测。 |

### 路由器管理辅助函数

| 函数 | 说明 |
|---|---|
| `csm_client_list_modules(c, &out_text, timeout)` | `List` 命令。 |
| `csm_client_list_api(c, module, &out_text, timeout)` | `List API <module>`。 |
| `csm_client_list_states(c, module, &out_text, timeout)` | `List State <module>`。 |
| `csm_client_help(c, module, &out_text, timeout)` | `Help <module>`。 |

请使用 `csm_string_free()` 释放 `*out_text`。

### 订阅

| 函数 | 说明 |
|---|---|
| `csm_client_subscribe_status(c, status, module, cb, ud, timeout)` | 订阅；回调由接收线程调用。 |
| `csm_client_unsubscribe_status(c, status, module, timeout)` | 取消订阅。 |
| `csm_client_register_async_callback(c, cmd, cb, ud)` | 为 `ASYNC_RESP` 包注册回调。 |
| `csm_client_unregister_async_callback(c, cmd)` | 移除回调。 |

### 轮询队列（回调的替代方式）

| 函数 | 说明 |
|---|---|
| `csm_client_poll_status(c, &out_notif, timeout)` | 阻塞直到下一条 STATUS / INTERRUPT。 |
| `csm_client_poll_async_response(c, &out_resp, timeout)` | 阻塞直到下一条 `ASYNC_RESP`。 |

---

## 返回值

所有公开函数均返回 `csm_result_t`：

| 代码                  | 含义                                            |
|-----------------------|-------------------------------------------------|
| `CSM_OK`              | 操作成功。                                      |
| `CSM_ERR_INVALID`     | 参数无效或为 NULL。                             |
| `CSM_ERR_CONNECTION`  | 连接失败或连接丢失。                            |
| `CSM_ERR_TIMEOUT`     | 操作超时。                                      |
| `CSM_ERR_PROTOCOL`    | 协议帧无效或损坏。                              |
| `CSM_ERR_SERVER`      | 服务器返回了 `ERROR` 数据包（见下文）。         |
| `CSM_ERR_NOMEM`       | 内存分配失败。                                  |
| `CSM_ERR_STATE`       | 当前状态下操作无效。                            |
| `CSM_ERR_IO`          | 底层 socket / 操作系统 I/O 错误。               |

收到 `CSM_ERR_SERVER` 后，可通过以下方式获取错误码与消息：

```c
csm_server_error_t err;
csm_client_last_server_error(c, &err);
fprintf(stderr, "[%s] %s\n", err.code, err.message);
```

`csm_result_str(code)` 返回静态可读字符串。

---

## 测试

测试套件（`SDK/c/tests/`）使用进程内极简测试框架与内嵌的 `MockServer`（详见 `tests/mock_server.h`），后者在 `127.0.0.1` 上模拟 LabVIEW CSM-TCP-Router。同一套测试在 Linux、macOS 与 Windows 上均可运行。

```bash
cmake --build build -j
ctest --test-dir build --output-on-failure
```

也可直接运行可执行文件以查看每条测试的进度：

```bash
./build/csm_tcp_router_client_tests
```

---

## 许可证

[MIT](LICENSE) — © NEVSTOP-LAB
