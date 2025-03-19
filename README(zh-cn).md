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

>
> | `数据长度(4字节)` | `数据包类型(10字节)` | `数据内容` |
>

### 数据长度

数据长度为4字节，表示整个 tcp 数据包的长度，包括数据包类型和数据包内容。

### 数据包类型

数据包类型用于描述数据包的内容，长度为10字节，不足10字节的用 `\0` 填充。

数据包类型相当于一个枚举类型，目前支持的数据包类型有：

- 信息数据包(info)
- 错误数据包(error)
- 指令数据包(cmd)
- 同步响应数据包(resp)
- 异步响应数据包(async-resp)
- 订阅返回数据包(status)

### 数据内容

#### 信息数据包(info)

info 数据包的数据内容为提示信息内容，纯文本格式。

#### 错误数据包(error)

error 数据包的数据内容为错误信息内容，为纯文本格式，文本格式定为 CSM Error 格式。

> [!NOTE]
> CSM Error 格式为："[Error: `错误代码`]`错误字符串`"。

#### 参数/返回数据格式描述

同步(-@)、异步(->)、无返回异步(->|)消息都可能携带参数，`status` 数据包也会携带状态参数。

#### 指令数据包(cmd)

指令数据包的数据内容为指令内容，格式为 CSM 本地指令格式，支持同步(-@)、异步(->)、无返回异步(->|)消息，注册(register)、注销(unregister)。 参数格式见 [参数/返回数据包](#参数返回数据格式描述)

> [!NOTE]
> 举例：假设本地程序存在名为DAQmx的CSM模块，具有一个接口为 "API: Start Sampling".
> 本地我们可以发送消息给这个模块，控制采集的启停：
>
> - API: Start Sampling -@ DAQmx // 同步消息
> - API: Start Sampling -> DAQmx // 异步消息
> - API: Start Sampling ->| DAQmx // 异步无返回消息
>
> 现在只要通过TCP连接，发送同样的文本消息，就可以实现远程消息。
>

> [!NOTE]
> 举例：假设本地程序存在名为A的CSM模块，不停的发送一个监控状态为 "Status", 另外一个模块B可以订阅这个状态。
>
> - status@a >> api@b -><register> // 订阅状态
> - status@a >> api@b -><unregister> // 取消订阅
>
> 现在只要通过TCP连接，发送同样的文本消息，就可以实现远程控制底层 csm 模块的订阅
>
> 但是如果发送中缺省了订阅方(api@b), 则表示连接到 tcp-router 的 client订阅状态
>
> - status@a -><register> // client 订阅 A 模块status
> - status@a >> api@b -><unregister> // 取消 client 订阅 A 模块status
>
> 当 A 模块发出 Status 后，client 将自动收到 `status` 数据包
>

#### 同步响应数据包(resp)

当执行完毕同步消息指令后，tcp-router 将 response 返回给 client.

- `Response数据` 格式见 [参数/返回数据包](#参数返回数据格式描述)

#### 异步响应数据包(async-resp)

当执行完毕同步消息指令后，tcp-router 将 response 返回给 client. 格式为："`Response数据` <- `异步消息原文`"

- `Response数据` 格式见 [参数/返回数据包](#参数返回数据格式描述)
- `异步消息原文` 异步消息原文

#### 订阅返回数据包(status)

Client 订阅了CSM模块的状态，当状态发生时，client 会自动收到此数据包。

数据包格式为 "状态名 >> `状态数据` <- 发送模块"。`状态数据`格式见 [参数/返回数据包](#参数返回数据格式描述)

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
