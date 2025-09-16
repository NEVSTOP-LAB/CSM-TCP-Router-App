# CSM-TCP-Router Python Client API

这是一个Python版本的CSM-TCP-Router客户端API，实现了与LabVIEW版本相同的功能，可以连接到CSM-TCP-Router服务器，发送命令并接收响应。

## 功能特性

- 与CSM-TCP-Router服务器建立TCP连接
- 发送同步命令并等待回复
- 发送异步命令
- 发送无返回异步命令
- Ping服务器
- 订阅状态变化通知
- 等待服务器可用
- 完整的错误处理和线程安全设计

## 文件结构

- `tcp_router_client.py`: 主要的客户端API类实现
- `example_usage.py`: 使用示例代码
- `README.md`: 使用说明文档

## 使用方法

### 基本连接

```python
from tcp_router_client import TcpRouterClient

# 创建客户端实例
client = TcpRouterClient()

# 连接到服务器
if client.connect("localhost", 9999):
    print("连接成功")
    # 执行操作...
    
    # 断开连接
    client.disconnect()
else:
    print("连接失败")
```

### 发送同步命令

```python
# 发送命令并等待回复
response = client.send_message_and_wait_for_reply("List")
print(f"回复: {response}")
```

### 发送异步命令

```python
# 发送异步命令
async_cmd = "API: Read Channels -> AI"
client.post_message(async_cmd)

# 发送无返回异步命令
no_rep_cmd = "API: Refresh ->| System"
client.post_no_rep_message(no_rep_cmd)
```

### 订阅状态变化

```python
# 定义状态变化回调函数
def status_callback(status_data):
    print(f"收到状态更新: {status_data}")

# 注册状态变化通知
client.register_status_change("Status", "AI", status_callback)

# 取消订阅
client.unregister_status_change("Status", "AI")
```

### 等待服务器可用

```python
# 等待服务器可用，最多等待30秒
success = client.wait_for_server("localhost", 9999, timeout=30)
if success:
    print("服务器已可用")
    client.connect("localhost", 9999)
```

### Ping服务器

```python
# Ping服务器，检查连接状态
success, elapsed = client.ping()
if success:
    print(f"Ping成功，延迟: {elapsed*1000:.2f}ms")
```

## 通讯协议

Python客户端实现了与CSM-TCP-Router相同的通讯协议，数据包格式如下：

```
| 数据长度(4B) | 版本(1B) | TYPE(1B) | FLAG1(1B) | FLAG2(1B) |      文本数据          |
╰─────────────────────────── 包头 ──────────────────────────╯╰──── 数据长度字范围 ────╯
```

支持的数据包类型：
- 信息数据包(info) - `0x00`
- 错误数据包(error) - `0x01`
- 指令数据包(cmd) - `0x02`
- 同步响应数据包(resp) - `0x03`
- 异步响应数据包(async-resp) - `0x04`
- 订阅返回数据包(status) - `0x05`

## 支持的指令集

### 1. CSM 消息指令集
由原有基于CSM开发的代码定义，支持：
- 同步消息 (-@)
- 异步消息 (->)
- 无返回异步消息 (->|)

### 2. CSM-TCP-Router 指令集
- `List` - 列出所有的CSM模块
- `List API`: 列出指定模块的所有API
- `List State`: 列出指定模块的所有CSM状态
- `Help` - 显示模块的帮助文件
- `Refresh lvcsm`: 刷新缓存文件
- `Ping` - 测试服务器连接

## 注意事项

1. 确保在使用完客户端后调用`disconnect()`或`release()`方法释放资源
2. 回调函数将在接收线程中执行，避免在回调函数中执行长时间阻塞操作
3. 当网络连接异常断开时，客户端会自动将`connected`标志设为False
4. 对于频繁发送消息的场景，建议使用连接池或重用同一个客户端实例

## 示例程序

请参考`example_usage.py`文件，其中包含了详细的使用示例。

## 依赖项

本客户端API仅使用Python标准库，无需安装额外依赖：
- `socket`: 用于TCP通信
- `struct`: 用于解析数据包
- `threading`: 用于多线程处理
- `queue`: 用于线程间通信
- `json`: 用于数据序列化（预留）
- `time`: 用于超时和延时
- `enum`: 用于定义数据包类型枚举

## 与LabVIEW版本对比

此Python版本实现了LabVIEW版本ClientAPI的所有核心功能：
- `Obtain.vi` -> `__init__()` 和 `obtain()`
- `Release.vi` -> `release()`
- `Send Message and Wait for Reply.vi` -> `send_message_and_wait_for_reply()`
- `Post Message.vi` -> `post_message()`
- `Post No-Rep Message.vi` -> `post_no_rep_message()`
- `Ping.vi` -> `ping()`
- `Register Status Change.vi` -> `register_status_change()`
- `Unregister Status Change.vi` -> `unregister_status_change()`
- `Wait for Server.vi` -> `wait_for_server()`

## 版本历史

- v1.0.0: 初始版本，实现基本功能