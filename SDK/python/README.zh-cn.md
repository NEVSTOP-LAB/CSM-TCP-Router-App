# csm-tcp-router-client

[![PyPI](https://img.shields.io/pypi/v/csm-tcp-router-client)](https://pypi.org/project/csm-tcp-router-client/)
[![Python](https://img.shields.io/pypi/pyversions/csm-tcp-router-client)](https://pypi.org/project/csm-tcp-router-client/)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)
[![CI](https://github.com/NEVSTOP-LAB/CSM-TCP-Router-App/actions/workflows/Python_SDK.yml/badge.svg)](https://github.com/NEVSTOP-LAB/CSM-TCP-Router-App/actions/workflows/Python_SDK.yml)

[CSM-TCP-Router](https://github.com/NEVSTOP-LAB/CSM-TCP-Router-App) LabVIEW 服务器的 Python 客户端 SDK。

CSM-TCP-Router 将 LabVIEW [可通信状态机（CSM）](https://github.com/NEVSTOP-LAB/Communicable-State-Machine) 应用通过 TCP 对外暴露，使任意 TCP 客户端（Python 脚本、测试框架、CI 流水线等）无需修改 LabVIEW 代码即可发送指令并接收响应。

> 📖 [English README](README.md)

---

## 安装

```bash
pip install csm-tcp-router-client
```

要求 Python 3.8 或更高版本，无第三方依赖——仅依赖 Python 标准库。

---

## 快速入门

### 同步客户端

```python
from csm_tcp_router_client import TcpRouterClient

with TcpRouterClient() as client:
    client.connect("localhost", 30007)

    # 获取已加载的 CSM 模块列表
    print(client.list_modules())

    # 发送同步指令并等待响应
    resp = client.send_and_wait("API: Read -@ DAQmx")
    print(resp.text)

    # Ping 服务器
    ok, elapsed_s = client.ping()
    print(f"Ping: {ok}, 延迟={elapsed_s*1000:.1f} ms")
```

### 异步客户端（asyncio）

```python
import asyncio
from csm_tcp_router_client import AsyncTcpRouterClient

async def main():
    async with AsyncTcpRouterClient() as client:
        await client.connect("localhost", 30007)
        print(await client.list_modules())
        resp = await client.send_and_wait("API: Read -@ DAQmx")
        print(resp.text)

asyncio.run(main())
```

---

## 功能特性

- **同步指令**（`-@`）——`send_and_wait()` 阻塞直到服务器返回响应。
- **异步指令**（`->`）——`post()` 等待 `cmd-resp` 握手包；最终响应通过回调或队列传递。
- **无响应指令**（`->|`）——`post_no_reply()` 等待 `cmd-resp` 握手包；不再有后续响应。
- **状态订阅**——`subscribe_status()` / `unsubscribe_status()`，支持可选回调或轮询队列。
- **路由器管理助手**——`list_modules()`、`list_api()`、`list_states()`、`help()`。
- **连接工具**——`wait_for_server()` 在应用启动期间轮询等待服务器就绪。
- **线程安全的同步客户端**——`TcpRouterClient`：所有方法均可从多个线程并发调用。
- **异步客户端**——`AsyncTcpRouterClient`：完整的 `async def` API，支持同步和异步回调。
- **零第三方依赖**——纯 Python 标准库实现。
- **上下文管理器**支持（`with TcpRouterClient()` / `async with AsyncTcpRouterClient()`）。

---

## 通信协议

本 SDK 实现了 CSM-TCP-Router **v0 协议**。

```
| 数据长度 (4B) | 版本 (1B) | TYPE (1B) | FLAG1 (1B) | FLAG2 (1B) | 文本数据 |
╰────────────────────────── 头部 (8B) ────────────────────────────────╯
```

| TYPE 字节 | 名称          | 方向           | 描述                                              |
|-----------|---------------|----------------|---------------------------------------------------|
| `0x00`    | `INFO`        | 服务器 → 客户端 | 欢迎 / 再见等信息报文                              |
| `0x01`    | `ERROR`       | 服务器 → 客户端 | CSM 错误：`[Error: <code>] <message>`             |
| `0x02`    | `CMD`         | 客户端 → 服务器 | 指令字符串                                        |
| `0x03`    | `CMD_RESP`    | 服务器 → 客户端 | 异步 / 订阅指令的握手确认包                        |
| `0x04`    | `RESP`        | 服务器 → 客户端 | 同步响应负载                                      |
| `0x05`    | `ASYNC_RESP`  | 服务器 → 客户端 | 异步响应：`<数据> <- <原始指令>`                   |
| `0x06`    | `STATUS`      | 服务器 → 客户端 | 状态广播：`<名称> >> <数据> <- <模块>`             |
| `0x07`    | `INTERRUPT`   | 服务器 → 客户端 | 中断广播（格式与 STATUS 相同）                     |

### 通信流程

**同步（`-@`）**

```
客户端 ─── CMD ──────────────────► 服务器
客户端 ◄── RESP（或 ERROR）─────── 服务器
```

**异步（`->`）**

```
客户端 ─── CMD ──────────────────► 服务器
客户端 ◄── CMD_RESP（或 ERROR）─── 服务器   ← 握手
客户端 ◄── ASYNC_RESP ──────────── 服务器   ← 稍后，异步结果
```

**无响应（`->|`）**

```
客户端 ─── CMD ──────────────────► 服务器
客户端 ◄── CMD_RESP（或 ERROR）─── 服务器   ← 握手；无后续响应
```

**订阅 / 取消订阅**

```
客户端 ─── CMD (<register>) ─────► 服务器
客户端 ◄── CMD_RESP（或 ERROR）─── 服务器
  …（CSM 模块每次发出状态时）…
客户端 ◄── STATUS ──────────────── 服务器
客户端 ─── CMD (<unregister>) ───► 服务器
客户端 ◄── CMD_RESP ─────────────── 服务器
```

---

## API 参考

### `TcpRouterClient`（同步）

#### 连接管理

| 方法 | 描述 |
|---|---|
| `connect(host, port, timeout=5.0)` | 连接服务器；失败时抛出 `ConnectionError`。 |
| `disconnect()` | 关闭连接；即使未连接也可安全调用。 |
| `connected` | 已连接时为 `True`。 |
| `wait_for_server(host, port, timeout=30, retry_interval=0.5)` | 轮询直到服务器可达；返回 `True`/`False`。 |

#### 指令方法

| 方法 | 描述 |
|---|---|
| `send_and_wait(command, timeout=5.0) → CommandResponse` | 同步指令（`-@`）；阻塞直到 `RESP` 到达。 |
| `post(command, timeout=5.0)` | 异步指令（`->`）；等待 `CMD_RESP` 握手。 |
| `post_no_reply(command, timeout=5.0)` | 无响应指令（`->|`）；等待 `CMD_RESP` 握手。 |
| `ping(timeout=2.0) → (bool, float)` | 往返延迟检测。 |

#### 路由器管理助手

| 方法 | 描述 |
|---|---|
| `list_modules(timeout=5.0) → str` | 执行 `List` 指令，返回模块列表。 |
| `list_api(module, timeout=5.0) → str` | 执行 `List API <module>` 指令。 |
| `list_states(module, timeout=5.0) → str` | 执行 `List State <module>` 指令。 |
| `help(module, timeout=5.0) → str` | 执行 `Help <module>` 指令。 |

#### 订阅管理

| 方法 | 描述 |
|---|---|
| `subscribe_status(status_name, module_name, callback=None, timeout=5.0)` | 订阅；可选回调，每次收到通知时调用。 |
| `unsubscribe_status(status_name, module_name, timeout=5.0)` | 取消订阅。 |
| `register_async_callback(original_command, callback)` | 注册 `ASYNC_RESP` 回调。 |
| `unregister_async_callback(original_command)` | 移除异步响应回调。 |

#### 轮询队列（回调的替代方案）

| 属性 | 类型 | 描述 |
|---|---|---|
| `status_queue` | `Queue[StatusNotification]` | 通过轮询接收状态/中断广播。 |
| `async_response_queue` | `Queue[AsyncResponse]` | 通过轮询接收异步响应。 |

---

### `AsyncTcpRouterClient`（asyncio）

所有方法均为 `async def` 协程，需使用 `await` 调用。

#### 连接管理

| 方法 | 描述 |
|---|---|
| `await connect(host, port, timeout=5.0)` | 建立 TCP 连接；失败时抛出 `ConnectionError`。 |
| `await disconnect()` | 关闭连接；未连接时可安全调用。 |
| `connected` | 写入端开启时为 `True`。 |
| `await wait_for_server(host, port, timeout=30, retry_interval=0.5)` | 轮询直到服务器可达。 |

#### 指令方法

| 方法 | 描述 |
|---|---|
| `await send_and_wait(command, timeout=5.0) → CommandResponse` | 同步指令（`-@`）。 |
| `await post(command, timeout=5.0)` | 异步指令（`->`）。 |
| `await post_no_reply(command, timeout=5.0)` | 无响应指令（`->|`）。 |
| `await ping(timeout=2.0) → (bool, float)` | 往返延迟检测。 |

#### 路由器管理助手

与同步客户端相同，但所有方法均为 `async def`。

#### 订阅管理

| 方法 | 描述 |
|---|---|
| `await subscribe_status(status_name, module_name, callback=None, timeout=5.0)` | 订阅；回调可以是普通函数或 `async def` 协程。 |
| `await unsubscribe_status(status_name, module_name, timeout=5.0)` | 取消订阅。 |
| `register_async_callback(original_command, callback)` | 注册 `ASYNC_RESP` 回调；可以是普通函数或 `async def`。 |
| `unregister_async_callback(original_command)` | 移除回调。 |

#### 轮询队列

| 属性 | 类型 | 描述 |
|---|---|---|
| `status_queue` | `asyncio.Queue[StatusNotification]` | `connect()` 后可用；使用 `await queue.get()` 轮询。 |
| `async_response_queue` | `asyncio.Queue[AsyncResponse]` | `connect()` 后可用。 |

---

### 数据模型

#### `CommandResponse`
- `.raw: bytes` – 原始服务器负载
- `.text: str` – UTF-8 解码后的文本

#### `AsyncResponse`
- `.raw: bytes`, `.text: str`
- `.original_command: str` – 服务器回显的原始指令

#### `StatusNotification`
- `.raw: bytes`
- `.packet_type: PacketType` – `STATUS` 或 `INTERRUPT`
- `.status_name: str` – 例如 `"Status"`
- `.data: str` – 广播的值
- `.module_name: str` – 发送该状态的 CSM 模块名称

### 异常

| 异常 | 触发场景 |
|---|---|
| `TcpRouterError` | 所有 SDK 异常的基类 |
| `ConnectionError` | TCP 连接失败或断开 |
| `TimeoutError` | 在超时时间内未收到响应 |
| `ProtocolError` | 无效或意外的数据帧 |
| `ServerError` | 服务器返回 `ERROR` 包；可通过 `.code` 和 `.message` 属性获取错误详情 |

---

## 示例

详见 [`examples/`](examples/) 目录：

- [`basic_usage.py`](examples/basic_usage.py) – 同步客户端：连接、Ping、列出模块、发送指令。
- [`subscribe_status.py`](examples/subscribe_status.py) – 同步客户端：通过回调实时接收状态订阅。
- [`async_usage.py`](examples/async_usage.py) – 异步客户端：使用 `async def` / `await` 实现所有功能。

---

## 从旧版脚本 SDK 迁移

原有的单文件 SDK（`SDK/PythonClientAPI/tcp_router_client.py`）仍然可用，但无法通过 pip 安装，且其数据包类型编号采用的是 v1 草稿协议，而非已发布的 v0 规范。

| 旧方法 | 新方法 | 备注 |
|---|---|---|
| `connect()` | `connect()` | 返回 `None`；失败时抛出 `ConnectionError` 而非返回 `False` |
| `disconnect()` | `disconnect()` | 无变化 |
| `send_message_and_wait_for_reply(msg)` | `send_and_wait(cmd)` | 返回 `CommandResponse`；出错时抛出异常 |
| `post_message(msg)` | `post(cmd)` | 等待 `CMD_RESP` 握手 |
| `post_no_rep_message(msg)` | `post_no_reply(cmd)` | 等待 `CMD_RESP` 握手 |
| `ping()` | `ping()` | 签名不变 |
| `register_status_change(s, m, cb)` | `subscribe_status(s, m, callback=cb)` | 失败时抛出异常而非返回 `False` |
| `unregister_status_change(s, m)` | `unsubscribe_status(s, m)` | 失败时抛出异常 |
| `wait_for_server(h, p, t)` | `wait_for_server(h, p, timeout=t)` | 改为关键字参数 |
| `obtain()` / `release()` | 使用上下文管理器 `with TcpRouterClient() as c:` | — |

---

## 开发

```bash
# 安装开发依赖
pip install -e ".[dev]"
# 或
pip install hatchling pytest pytest-asyncio ruff

# 运行测试（同步 + 异步）
pytest

# 代码检查
ruff check src/ tests/
```

---

## 许可证

[MIT](LICENSE) — © NEVSTOP-LAB
