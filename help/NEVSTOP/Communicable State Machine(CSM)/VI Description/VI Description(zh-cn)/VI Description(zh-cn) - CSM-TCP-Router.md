# CSM-TCP-Router

## API

CSM-TCP-Router 的 API 可分为两部分：

- Server 侧 VI：用于构建并运行 TCP Router 服务端。
- Client 侧 VI：用于在 LabVIEW 应用中连接 Router，并发送/接收指令。

> [!NOTE]
> 下述清单依据当前仓库结构、VI 命名和示例工程推断整理。

## Server 侧 VI

### CSM-TCP-Router.vi
位于 `src/_addons/TCP-Router/` 的核心 Router VI。

该 VI 用于在 CSM 之上启动 TCP Router 通讯层，处理 TCP 数据包路由，并提供 Router 内建指令处理能力。

### CSM-TCP-Router(Server).vi
位于 `src/Server/` 的示例服务端启动 VI。

它是示例工程中用于承载 CSM 模块并对外提供 CSM-TCP-Router 服务的标准入口 VI。

### Router 内建指令（服务端）
服务端还会通过 TCP 指令暴露内建管理功能：

- `List`：列出可用的 CSM 模块。
- `List API`：列出指定模块暴露的 API。
- `List State`：列出指定模块可用的 CSM 状态。
- `Help`：读取并返回模块的 VI Description 帮助信息。
- `Refresh lvcsm`：刷新 lvcsm 缓存数据。

## Client 侧 VI

Client API 位于 `src/_addons/TCP-Router/ClientAPI/`。

### Obtain.vi
创建/建立到 TCP Router 服务端的客户端会话。

### Release.vi
释放客户端会话及相关资源。

### Send Message and Wait for Reply.vi
发送同步指令并等待最终响应。

### Post Message.vi
发送异步指令（本次调用不等待最终响应，由服务端异步执行）。

### Post No-Rep Message.vi
发送无需回复的异步指令。

### Ping.vi
检查服务端连通性并获取通讯耗时。

### Wait for Server.vi
等待 Router 服务可连接，直到成功或超时。

### Register Status Change.vi / Unregister Status Change.vi
状态订阅/取消订阅 API。

> [!NOTE]
> 该组接口用于兼容旧用法，工程中同时保留了面向 Client 的新版本接口。

### Register Status for Client.vi / Unregister Status for Client.vi
面向指定客户端上下文的状态订阅/取消订阅接口。

### Status Queue.vi
以队列方式获取状态更新。

### ASync-Response Queue.vi
以队列方式获取异步指令响应。

### ASync-Response User Event.vi
以 LabVIEW User Event 方式获取异步指令响应。

### Register Broadcast.vi / Unregister Broadcast.vi
广播消息订阅/取消订阅接口。

### Register Broadcast for Client.vi / Unregister Broadcast for Client.vi
面向客户端上下文的广播订阅/取消订阅接口。
