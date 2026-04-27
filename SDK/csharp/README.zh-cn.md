# CsmTcpRouter.Client

[![NuGet](https://img.shields.io/nuget/v/CsmTcpRouter.Client.svg)](https://www.nuget.org/packages/CsmTcpRouter.Client/)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)
[![CI](https://github.com/NEVSTOP-LAB/CSM-TCP-Router-App/actions/workflows/CSharp_SDK.yml/badge.svg)](https://github.com/NEVSTOP-LAB/CSM-TCP-Router-App/actions/workflows/CSharp_SDK.yml)

[CSM-TCP-Router](https://github.com/NEVSTOP-LAB/CSM-TCP-Router-App) LabVIEW 服务器的 C# / .NET 客户端 SDK。

CSM-TCP-Router 通过 TCP 暴露一个 LabVIEW [可通信状态机 (CSM)](https://github.com/NEVSTOP-LAB/Communicable-State-Machine) 应用程序，因此任何 TCP 客户端 — 包括 .NET 应用程序、测试夹具或 CI 流水线 — 都可以发送命令并接收响应，而无需修改 LabVIEW 代码。

> 📖 [English README.md](README.md)

整个 SDK 实现位于单个源文件中：[`src/CsmTcpRouter/CsmTcpRouter.cs`](src/CsmTcpRouter/CsmTcpRouter.cs)。

---

## 安装

```bash
dotnet add package CsmTcpRouter.Client
```

目标框架：`netstandard2.0` 与 `net8.0`。无第三方运行时依赖 — 仅使用 .NET BCL。

支持的平台包括 .NET Framework 4.6.2+、.NET Core 3.1+ 以及 .NET 5/6/7/8/9。

---

## 快速上手

### 同步客户端

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
Console.WriteLine($"Ping: {ok}, latency={elapsed.TotalMilliseconds:F1} ms");
```

### 异步客户端

```csharp
using CsmTcpRouter;

using var client = new TcpRouterClient();
await client.ConnectAsync("localhost", 30007);

Console.WriteLine(await client.ListModulesAsync());
var resp = await client.SendAndWaitAsync("API: Read -@ DAQmx");
Console.WriteLine(resp.Text);
```

### 订阅状态广播

```csharp
client.SubscribeStatus("Status", "AI", notif =>
{
    Console.WriteLine($"{notif.StatusName} = {notif.Data}");
});

// ... 之后
client.UnsubscribeStatus("Status", "AI");
```

### 异步响应回调

```csharp
client.RegisterAsyncCallback("API: Start Sampling -> DAQmx", ar =>
{
    Console.WriteLine($"异步响应: {ar.Text}");
});

client.Post("API: Start Sampling -> DAQmx");
```

完整可运行示例见 [`examples/BasicUsage/Program.cs`](examples/BasicUsage/Program.cs)。

---

## API 参考

### `TcpRouterClient`

| 方法                                                    | 说明                                                                                |
| ------------------------------------------------------- | ----------------------------------------------------------------------------------- |
| `Connect / ConnectAsync(host, port, timeout?)`          | 建立 TCP 连接。                                                                     |
| `Disconnect()`                                          | 关闭连接，并以 `RouterConnectionException` 立即唤醒所有阻塞中的等待者。            |
| `Connected`                                             | 套接字打开期间为 `true`。                                                          |
| `WaitForServer / WaitForServerAsync(...)`               | 轮询 `host:port` 直到连接成功或超时。                                              |
| `SendAndWait / SendAndWaitAsync(cmd, timeout?)`         | 发送同步命令 (`-@`) 并阻塞直到 `RESP` 返回。返回 `CommandResponse`。               |
| `Post / PostAsync(cmd, timeout?)`                       | 发送异步命令 (`->`) 并等待 `CMD_RESP` 握手。                                       |
| `PostNoReply / PostNoReplyAsync(cmd, timeout?)`         | 发送无响应异步命令 (`->\|`) 并等待 `CMD_RESP` 握手。                               |
| `Ping / PingAsync(timeout?)`                            | 测量往返延迟；返回 `(bool ok, TimeSpan elapsed)`。                                 |
| `ListModules / ListApi / ListStates / Help(...)`        | 内置的路由管理辅助命令。                                                           |
| `SubscribeStatus / UnsubscribeStatus(...)`              | 订阅 / 取消 `STATUS` (或 `INTERRUPT`) 广播。                                       |
| `RegisterAsyncCallback / UnregisterAsyncCallback(...)`  | 注册 / 移除按原始命令匹配的 `ASYNC_RESP` 回调。                                    |
| `AsyncResponseQueue`、`StatusQueue`                     | 与回调互补的轮询队列 (`ConcurrentQueue<>`)。                                       |

### 数据模型

* `PacketType` (枚举) — 协议 v0 中定义的报文类型。
* `Packet`、`CommandResponse`、`AsyncResponse`、`StatusNotification` — 解析后的负载。

### 异常

* `CsmTcpRouterException` — 基类。
* `RouterConnectionException` — 连接 / 发送 / 断开失败。
* `RouterTimeoutException` — 同步等待超时。
* `ProtocolException` — 报文格式非法。
* `ServerException` — 服务器返回 `ERROR` 报文 (`Code` 与 `ServerMessage`)。

---

## 协议

CSM-TCP-Router 协议 v0 使用 8 字节大端头部加上任意长度的负载：

```
| Data Length (4B) | Version (1B = 0x01) | Type (1B) | FLAG1 (1B) | FLAG2 (1B) | <payload...>
+------------------------ Header (8B) ----------------------------+
```

报文类型：

| 值    | 名称         | 方向          | 用途                                       |
| ----- | ------------ | ------------- | ------------------------------------------ |
| 0x00  | `Info`       | 服务器 → 客户 | 欢迎 / 告别消息。                          |
| 0x01  | `Error`      | 服务器 → 客户 | 错误响应 (`[Error: <code>] <msg>`)。       |
| 0x02  | `Cmd`        | 客户 → 服务器 | 客户端命令。                               |
| 0x03  | `CmdResp`    | 服务器 → 客户 | 异步 / 无响应 / 订阅命令的握手。           |
| 0x04  | `Resp`       | 服务器 → 客户 | 同步命令响应。                             |
| 0x05  | `AsyncResp`  | 服务器 → 客户 | 异步命令响应 (回显原始命令)。              |
| 0x06  | `Status`     | 服务器 → 客户 | 已订阅模块的状态广播。                     |
| 0x07  | `Interrupt`  | 服务器 → 客户 | 已订阅模块的中断广播。                     |

---

## 从源码构建

```bash
# 还原并构建整个解决方案
dotnet build SDK/csharp/CsmTcpRouter.sln -c Release

# 运行单元 + 集成测试
dotnet test SDK/csharp/tests/CsmTcpRouter.Tests/CsmTcpRouter.Tests.csproj -c Release

# 打包 NuGet
dotnet pack SDK/csharp/src/CsmTcpRouter/CsmTcpRouter.csproj -c Release -o nupkg
```

解决方案文件可直接用 **Visual Studio 2022 / 2026**（或 **JetBrains Rider**）打开；SDK 风格项目向前兼容所有当前 Visual Studio 版本。

---

## 项目结构

```
SDK/csharp/
├── CsmTcpRouter.sln
├── README.md / README.zh-cn.md / CHANGELOG.md / LICENSE
├── src/CsmTcpRouter/                ← 单文件 SDK
│   ├── CsmTcpRouter.cs
│   └── CsmTcpRouter.csproj
├── tests/CsmTcpRouter.Tests/        ← xUnit 测试工程
│   ├── ProtocolTests.cs
│   ├── ClientIntegrationTests.cs
│   ├── MockServer.cs
│   └── CsmTcpRouter.Tests.csproj
└── examples/BasicUsage/             ← 可运行控制台示例
    ├── Program.cs
    └── BasicUsage.csproj
```

---

## 许可证

基于 [MIT 许可证](LICENSE) 发布 — © 2026 NEVSTOP-LAB。
