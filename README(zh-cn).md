# CSM-TCP-Router

[English](./README.md) | [中文](./README(zh-cn).md)

本仓库演示如何通过创建一个可复用的TCP通讯层 (CSM-TCP-Router)，将本地程序变成一个TCP服务器，实现远程控制。通过这个案例，展示CSM框架的隐形总线的优点。

## 功能介绍

![framework](.doc/CSM-TCP-Router%201.svg)

- 本地所有可以发送的CSM消息，都可以使用CSM同步、异步消息格式，通过TCP连接发送给本地程序。
- 基于JKI-TCP-Server库，支持多个TCP客户端同时连接。
- [client] 提供一个标准的TCP客户端，可以连接到服务器，验证远程连接、消息发送等功能。

## 通讯协议

CSM-TCP-Router 中 TCP 数据包格式定义如下：

```
| 数据长度(4B) | 版本(1B) | FLAG1(1B) | FLAG2(1B) | TYPE(1B) |      文本数据          |
╰─────────────────────────── 包头 ──────────────────────────╯╰──── 数据长度字范围 ────╯
```

数据包类型用于描述数据包的内容，为枚举类型，目前支持的数据包类型有：

- 信息数据包(info) - `0x00`
- 错误数据包(error) - `0x01`
- 指令数据包(cmd) - `0x02`
- 同步响应数据包(resp) - `0x03`
- 异步响应数据包(async-resp) - `0x04`
- 订阅返回数据包(status) - `0x05`

详细的通讯协议定义, 见 [协议设计](/Protocol%20Design.v0.(zh-cn).md)

## 指令集

![image](.doc/CSM-TCP-Router.drawio.png)

### 1. CSM 消息指令集

由原有基于CSM开发的代码定义。由于CSM框架通过隐形的总线进行消息传递，所有的通讯可以不用侵入代码的方式实现。

例如，本程序中的AI CSM模块提供了：

- `Channels`: 列出所有的通道
- `Read`：读取指定通道的值
- `read all`：读取所有通道的值

这些消息可以通过TCP连接发送给本地程序，实现远程控制。

### 2. CSM-TCP-Router 指令集

由TCP通讯层 (CSM-TCP-Router) 定义。CSM模块管理的功能，通过定义指令，可以实现远程控制。

- `List` - 列出所有的CSM模块
- `List API`: 列出指定模块的所有API
- `List State`: 列出指定模块的所有CSM状态
- `Help` - 显示模块的帮助文件，存储在CSM VI的Documentation字段
- `Refresh lvcsm`: 刷新缓存文件

### [Client Only] 3. CSM-TCP-Router Client 指令集

代码中提供一个标准的CSM-TCP-Router Client。它也内置了一些指令，这些指令如果基于指令集进行开发，无法使用。

- `Bye`: 断开连接
- `Switch`：切换模块，便于输入时省略模块名，不带参数时切换回默认模式
- TAB键: 自动定位到输入对话框

![CSM-TCP-Router Client Console](.doc/Client.png)

## 使用方法

1. 在VIPM中安装本工具及依赖
2. 在CSM的范例中打开范例工程CSM-TCP-Router.lvproj
3. 启动代码工程中的CSM-TCP-Router(Server).vi
4. 启动Client.vi，输入服务器的IP地址和端口号，点击连接
5. 输入指令，点击发送，可以在控制台看到返回的消息
6. 在Server程序的界面log中，可以看到执行过的历史消息
7. 在Client.vi中输入`Bye`断开连接
8. 关闭Server程序

### 下载

通过VIPM搜索CSM TCP Router，即可下载安装。

### 依赖

- Communicable State Machine(CSM) - NEVSTOP
- JKI TCP Server - JKI
- Global Stop - NEVSTOP
- OpenG
