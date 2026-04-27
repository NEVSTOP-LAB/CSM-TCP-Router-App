# csm-tcp-router-client (C#)

[![CI](https://github.com/NEVSTOP-LAB/CSM-TCP-Router-App/actions/workflows/CSharp_SDK.yml/badge.svg)](https://github.com/NEVSTOP-LAB/CSM-TCP-Router-App/actions/workflows/CSharp_SDK.yml)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)

[CSM-TCP-Router](https://github.com/NEVSTOP-LAB/CSM-TCP-Router-App) LabVIEW 服务器的 C# / .NET 客户端 SDK。

CSM-TCP-Router 通过 TCP 暴露 LabVIEW [Communicable State Machine (CSM)](https://github.com/NEVSTOP-LAB/Communicable-State-Machine) 应用，使任何 TCP 客户端 —— 包括 .NET 应用、测试夹具或 CI 流水线 —— 都能在不接触 LabVIEW 代码的情况下发送命令、接收响应。

> 📖 [English README.md](README.md)

---

## 特性

- **单文件实现** —— 整个库都在 `src/CsmTcpRouterClient.cs`。
- **同步 + 异步 API** —— 同一个 `TcpRouterClient` 同时提供阻塞式（`Connect`、`SendAndWait`、…）和基于 `Task` 的（`ConnectAsync`、`SendAndWaitAsync`、…）方法。
- **运行时零第三方依赖** —— 仅使用 BCL。
- **支持 VS 2026** —— SDK 风格项目，可在 Visual Studio 2022 17.8+ 与 Visual Studio 2026 中编译。
- **跨平台** —— Linux、macOS、Windows（凡是 .NET 8 能跑的地方）。

---

## 安装

本包计划以 `CsmTcpRouterClient` 名称发布到 NuGet。在发布之前可在本地工程中通过项目引用 `src/CsmTcpRouterClient.csproj`，或将单一文件 `src/CsmTcpRouterClient.cs` 复制到您的项目中即可使用：

```xml
<ItemGroup>
  <ProjectReference Include="path/to/SDK/csharp/src/CsmTcpRouterClient.csproj" />
</ItemGroup>
```

目标框架：`net8.0`。

---

## 快速上手

### 同步用法

```csharp
using CsmTcpRouter;

using var client = new TcpRouterClient();
client.Connect("localhost", 30007);

// 列出已加载的所有 CSM 模块
Console.WriteLine(client.ListModules());

// 发送同步命令并等待响应
var resp = client.SendAndWait("API: Read -@ DAQmx");
Console.WriteLine(resp.Text);

// Ping 服务器
var (ok, elapsed) = client.Ping();
Console.WriteLine($"Ping: {ok}, latency={elapsed * 1000:F1} ms");
```

### 异步用法

```csharp
using CsmTcpRouter;

await using var client = new TcpRouterClient();
await client.ConnectAsync("localhost", 30007);
Console.WriteLine(await client.ListModulesAsync());
var resp = await client.SendAndWaitAsync("API: Read -@ DAQmx");
Console.WriteLine(resp.Text);
```

---

## 协议概览（v0）

CSM-TCP-Router 使用 8 字节大端头部 + UTF-8 负载：

```
| 数据长度 (4B) | 版本 (1B = 0x01) | 类型 (1B) | FLAG1 (1B) | FLAG2 (1B) | <负载字节> |
```

包类型：

| 十六进制 | 名称           | 方向        | 描述                                  |
|----------|----------------|-------------|---------------------------------------|
| 0x00     | `INFO`         | 服务器→客户端 | 欢迎/告别消息                         |
| 0x01     | `ERROR`        | 服务器→客户端 | 服务器错误（`[Error: 码] 消息`）       |
| 0x02     | `CMD`          | 客户端→服务器 | 客户端发送的命令                       |
| 0x03     | `CMD_RESP`     | 服务器→客户端 | 异步/无回复/订阅的握手响应             |
| 0x04     | `RESP`         | 服务器→客户端 | 同步命令响应                           |
| 0x05     | `ASYNC_RESP`   | 服务器→客户端 | 异步命令响应                           |
| 0x06     | `STATUS`       | 服务器→客户端 | 已订阅 CSM 模块的状态广播              |
| 0x07     | `INTERRUPT`    | 服务器→客户端 | 已订阅 CSM 模块的中断广播              |

---

## API 参考

### 连接管理

| 同步                          | 异步                                   | 说明                              |
|-------------------------------|----------------------------------------|-----------------------------------|
| `Connect(host, port, t?)`     | `ConnectAsync(host, port, t?)`         | 打开 TCP 连接                     |
| `Disconnect()`                | `DisconnectAsync()`                    | 关闭连接、释放资源                |
| `WaitForServer(host, port,…)` | `WaitForServerAsync(host, port, …)`    | 轮询直到服务器接受连接            |
| `Connected`                   | `Connected`                            | `bool`，当前是否已连接            |

### 命令

| 同步                                       | 异步                                            | 说明                          |
|--------------------------------------------|-------------------------------------------------|-------------------------------|
| `SendAndWait(cmd, t?)` → `CommandResponse` | `SendAndWaitAsync(cmd, t?)` → `Task<CommandResponse>` | 发送同步命令（`-@`）          |
| `Post(cmd, t?)`                            | `PostAsync(cmd, t?)`                            | 发送异步命令（`->`）          |
| `PostNoReply(cmd, t?)`                     | `PostNoReplyAsync(cmd, t?)`                     | 发送无回复异步命令（`->|`）   |
| `Ping(t?)` → `(bool, double)`              | `PingAsync(t?)` → `Task<(bool, double)>`        | 测量往返延迟                  |

### 路由器管理辅助方法

| 同步                  | 异步                       | 说明                              |
|-----------------------|----------------------------|-----------------------------------|
| `ListModules()`       | `ListModulesAsync()`       | 以纯文本返回 CSM 模块列表          |
| `ListApi(module)`     | `ListApiAsync(module)`     | 以纯文本返回模块的 API 列表        |
| `ListStates(module)`  | `ListStatesAsync(module)`  | 以纯文本返回 CSM 状态列表          |
| `Help(module)`        | `HelpAsync(module)`        | 以纯文本返回模块帮助               |

### 状态/中断订阅

| 同步                                       | 异步                                              | 说明                              |
|--------------------------------------------|---------------------------------------------------|-----------------------------------|
| `SubscribeStatus(name, module, cb?, t?)`   | `SubscribeStatusAsync(name, module, cb?, t?)`     | 订阅模块状态广播                  |
| `UnsubscribeStatus(name, module, t?)`      | `UnsubscribeStatusAsync(name, module, t?)`        | 取消订阅                          |
| `RegisterAsyncCallback(cmd, cb)`           | （同上）                                          | 为 `async-resp` 注册回调          |
| `UnregisterAsyncCallback(cmd)`             | （同上）                                          | 移除 `async-resp` 回调            |
| `StatusQueue` (`BlockingCollection<…>`)    | （同上）                                          | 状态广播轮询队列                  |
| `AsyncResponseQueue` (`BlockingCollection<…>`) | （同上）                                      | `async-resp` 轮询队列             |

### 异常

| 类型                     | 何时抛出                                              |
|--------------------------|-------------------------------------------------------|
| `TcpRouterError`         | 所有 SDK 异常的基类                                   |
| `RouterConnectionError`  | 无法建立或意外丢失连接                                |
| `RouterTimeoutError`     | 阻塞/await 操作超时                                   |
| `ProtocolError`          | 收到非法的协议帧                                      |
| `ServerError`            | 服务器返回 `ERROR` 包（含 `Code` 字段）               |

---

## 编译与测试

```bash
cd SDK/csharp

# 还原 + 编译
dotnet build CsmTcpRouterClient.slnx -c Release

# 运行单元 + 集成测试
dotnet test CsmTcpRouterClient.slnx -c Release

# 运行基本示例（需要有一个运行中的 CSM-TCP-Router 服务器在 :30007）
dotnet run --project examples/BasicUsage
```

仓库还包含一个可直接在 Visual Studio 2026 / 2022 中打开的解决方案文件
（`CsmTcpRouterClient.slnx`）。

---

## 示例

| 路径                              | 描述                                              |
|-----------------------------------|---------------------------------------------------|
| `examples/BasicUsage/`            | 连接、ping、列模块、列 API                        |
| `examples/SubscribeStatus/`       | 订阅模块状态，演示回调和队列两种用法              |

---

## 许可证

[MIT](LICENSE) © NEVSTOP-LAB
