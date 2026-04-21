# CSM-TCP-Router

[English](./README.md) | [中文](./README(zh-cn).md)

CSM-TCP-Router 是一个可复用的 CSM TCP 通讯层。  
它通过 CSM 隐形总线机制，将本地 CSM 程序转换为可远程控制的 TCP 服务器。

## 功能特性

```mermaid
flowchart LR
    A[远程 TCP 客户端]:::client --> B[CSM-TCP-Router]:::router
    B --> C[CSM 隐形总线]:::bus
    C --> D[本地 CSM 模块]:::module
    D --> C
    C --> B
    B --> A

    classDef client fill:#e8f4ff,stroke:#1d4ed8,color:#0f172a,stroke-width:1.5px;
    classDef router fill:#ecfeff,stroke:#0e7490,color:#0f172a,stroke-width:2px;
    classDef bus fill:#f5f3ff,stroke:#7c3aed,color:#0f172a,stroke-width:1.5px;
    classDef module fill:#f0fdf4,stroke:#15803d,color:#0f172a,stroke-width:1.5px;
```

- 所有本地可发送的 CSM 消息都可通过 TCP 以同步或异步格式转发。
- 基于 JKI TCP Server，支持多个客户端并发连接。
- 仓库内置标准客户端，可用于连接与指令联调验证。

## 通讯协议

TCP 数据包格式如下：

```
| 数据长度(4B) | 版本(1B) | TYPE(1B) | FLAG1(1B) | FLAG2(1B) |      文本数据      |
╰─────────────────────────── 包头 ───────────────────────────╯╰──── 数据长度范围 ────╯
```

`TYPE` 字段当前支持：

- `info` (`0x00`)：客户端连接（欢迎）和断开（告别）时发送
- `error` (`0x01`)
- `cmd` (`0x02`)
- `cmd-resp` (`0x03`)
- `resp` (`0x04`)
- `async-resp` (`0x05`)
- `status` (`0x06`)
- `interrupt` (`0x07`)

完整协议请见 [协议设计](.doc/Protocol.v0.(zh-cn).md)。

## 指令集

```mermaid
flowchart TD
    A[指令集]:::root --> B[1. CSM 消息 API]:::csm
    A --> C[2. Router 管理 API]:::router
    A --> D[3. Client 内建指令]:::client

    B --> B1[Channels]
    B --> B2[Read]
    B --> B3[read all]

    C --> C1[List]
    C --> C2[List API]
    C --> C3[List State]
    C --> C4[Help]
    C --> C5[Refresh lvcsm]

    D --> D1[Bye]
    D --> D2[Switch]
    D --> D3[TAB 快捷聚焦]

    classDef root fill:#fff7ed,stroke:#c2410c,color:#0f172a,stroke-width:2px;
    classDef csm fill:#eff6ff,stroke:#2563eb,color:#0f172a,stroke-width:1.5px;
    classDef router fill:#f0fdfa,stroke:#0f766e,color:#0f172a,stroke-width:1.5px;
    classDef client fill:#faf5ff,stroke:#9333ea,color:#0f172a,stroke-width:1.5px;
```

### 1）CSM 消息 API

由现有 CSM 业务代码定义，可经 Router 直接转发，无需侵入式改造。

### 2）Router 管理 API

由 CSM-TCP-Router 定义，用于模块管理与运行态查询。

### 3）Client 内建指令（仅客户端）

由内置客户端提供，不属于二次开发时可扩展的指令集 API。

```mermaid
sequenceDiagram
    participant U as 用户
    participant C as 客户端控制台
    participant R as CSM-TCP-Router
    participant S as 服务端日志

    U->>C: 连接（IP + 端口）
    C->>R: 建立 TCP 连接
    R-->>C: welcome (info)
    U->>C: 发送指令
    C->>R: cmd 数据包
    R-->>C: resp / async-resp
    R-->>S: 记录执行历史
    U->>C: Bye
    C->>R: 断开连接
    R-->>C: goodbye (info)
```

## 使用方法

1. 在 VIPM 中安装本工具及依赖。
2. 在 CSM 范例中打开 `CSM-TCP-Router.lvproj`。
3. 启动 `CSM-TCP-Router(Server).vi`。
4. 启动 `Client.vi`，输入服务端 IP 和端口并连接。
5. 发送指令，在客户端控制台查看返回消息。
6. 在服务端日志界面查看历史执行记录。
7. 在 `Client.vi` 中输入 `Bye` 断开连接。
8. 关闭服务端程序。

### 下载

在 VIPM 中搜索 `CSM TCP Router` 并安装。

### 依赖

- Communicable State Machine (CSM) - NEVSTOP
- JKI TCP Server - JKI
- Global Stop - NEVSTOP
- OpenG
