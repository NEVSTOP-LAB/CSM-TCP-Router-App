# CSM-TCP-Router

## API

> [!NOTE]
> <b>CSM-TCP-Router API 范围</b>
>
> CSM-TCP-Router 的 API 分为两部分：
> - <b>Server 侧 VI</b>：启动并承载 CSM 模块的 TCP Router 服务。
> - <b>Client 侧 VI</b>：连接 Router 并发送/接收指令。
>
> 下述 API 清单依据当前项目结构、VI 命名和示例工程推断整理。

## Server 侧 VI

### CSM-TCP-Router.vi
位于 `src/_addons/TCP-Router/` 的核心 Router VI。

用于启动 CSM TCP Router 通讯层，处理数据包路由，并提供 Router 内建管理指令。

### CSM-TCP-Router(Server).vi
位于 `src/Server/` 的服务端启动 VI。

作为示例工程的标准入口 VI，用于通过 CSM-TCP-Router 对外承载 CSM 模块。

### Router 内建指令
由 Router 服务端提供的内建指令：

- `List`：列出所有可用 CSM 模块。
- `List API`：列出指定模块暴露的 API。
- `List State`：列出指定模块可用的 CSM 状态。
- `Help`：返回模块 VI Description 中的帮助文本。
- `Refresh lvcsm`：刷新 lvcsm 缓存数据。

## Client 侧 VI

Client API 位于 `src/_addons/TCP-Router/ClientAPI/`。

### Obtain.vi
创建并连接到 TCP Router 服务端的客户端会话。

### Release.vi
释放客户端会话及相关资源。

### Send Message and Wait for Reply.vi
发送同步指令并等待最终响应。

### Post Message.vi
发送异步指令（调用不阻塞等待最终响应）。

### Post No-Rep Message.vi
发送无需最终响应的异步指令。

### Ping.vi
检查服务端可达性并返回通讯耗时。

### Wait for Server.vi
等待服务端可连接，直到成功或超时。

### Register Status Change.vi
注册状态变更订阅回调。

### Unregister Status Change.vi
取消状态变更订阅回调。

### Register Status for Client.vi
面向指定客户端上下文注册状态订阅。

### Unregister Status for Client.vi
面向指定客户端上下文取消状态订阅。

### Status Queue.vi
通过队列方式获取状态更新。

### ASync-Response Queue.vi
通过队列方式获取异步指令响应。

### ASync-Response User Event.vi
通过 User Event 方式获取异步指令响应。

### Register Broadcast.vi
注册广播消息订阅。

### Unregister Broadcast.vi
取消广播消息订阅。

### Register Broadcast for Client.vi
面向指定客户端上下文注册广播订阅。

### Unregister Broadcast for Client.vi
面向指定客户端上下文取消广播订阅。
