# CSM-TCP-Router

[English](./README.md) | [中文](./README(zh-cn).md)

This repository demonstrates how to build a reusable TCP communication layer—CSM-TCP-Router—that turns a local program into a TCP server for remote control, showcasing the power of the CSM framework's invisible bus.

## Features

![framework](.doc/CSM-TCP-Router%201.svg)

- Any CSM message that can be sent locally can also be transmitted to the local program over TCP, using CSM synchronous and asynchronous message formats.
- Based on the JKI-TCP-Server library, it supports multiple TCP clients connecting simultaneously.
- [client] Provides a standard TCP client that can connect to the server to verify remote connections and message sending.

## Protocol

The TCP packet format used in the CSM-TCP-Router is defined as follows:

```
| Data Length (4B) | Version (1B) | TYPE (1B) | FLAG1 (1B) | FLAG2 (1B) |      Text Data          |
╰───────────────────────────────── Header ──────────────────────────────╯╰─── Data Length Range ──╯
```

This field specifies the packet type as an enumerated value. Supported types are:

- Information Packet (`info`) - `0x00`: Sent by the server when a client connects (welcome message) and when the connection is closed (goodbye message)
- Error Packet (`error`) - `0x01`
- Command Packet (`cmd`) - `0x02`
- Command Response Packet (`cmd-resp`) - `0x03`
- Synchronous Response Packet (`resp`) - `0x04`
- Asynchronous Response Packet (`async-resp`) - `0x05`
- Status Broadcast Packet (`status`) - `0x06`
- Interrupt Broadcast Packet (`interrupt`) - `0x07`

For detailed communication protocol definitions, see [Protocol Design](.doc/Protocol.v0.(en).md).

## Supported Command Sets

![image](.doc/CSM-TCP-Router.drawio.png)

### 1. CSM Message Command Set

Defined by the existing CSM-based application code. Because the CSM framework uses an invisible bus for message passing, remote communication requires no intrusive changes to the existing code.

For example, the AI CSM module in this program provides:

- `Channels`: List all channels
- `Read`: Read the value of a specified channel
- `read all`: Read the values of all channels

These messages can be sent to the local program via TCP connection for remote control.

### 2. CSM-TCP-Router Command Set

Defined by the CSM-TCP-Router layer. These commands expose the management functions of CSM modules for remote control.

- `List`: List all CSM modules
- `List API`: List all APIs of a specified module
- `List State`: List all CSM states of a specified module
- `Help`: Display the help file of the module, stored in the Documentation field of the CSM VI
- `Refresh lvcsm`: Refresh the cache file

### [Client Only] 3. CSM-TCP-Router Client Command Set

The bundled standard CSM-TCP-Router Client includes additional built-in commands that are not available when building on the command set API.

- `Bye`: Disconnect
- `Switch`: Switch the active module to omit the module name when entering commands; omit the parameter to switch back to the default mode
- TAB key: Automatically focus on the input dialog box

![CSM-TCP-Router Client Console](.doc/Client.png)

## Usage

1. Install this tool and dependencies via VIPM
2. Open the example project CSM-TCP-Router.lvproj in the CSM examples
3. Start the CSM-TCP-Router(Server).vi in the code project
4. Start Client.vi, enter the server's IP address and port number, and click connect
5. Enter commands and click send to see the returned messages in the console
6. View the history of executed messages in the log interface of the Server program
7. Enter `Bye` in Client.vi to disconnect
8. Close the Server program

### Download

Search for CSM TCP Router in VIPM to download and install.

### Dependencies

- Communicable State Machine (CSM) - NEVSTOP
- JKI TCP Server - JKI
- Global Stop - NEVSTOP
- OpenG
