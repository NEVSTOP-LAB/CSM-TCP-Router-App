# CSM-TCP-Router

[English](./README.md) | [中文](./README(zh-cn).md)

CSM-TCP-Router is a reusable TCP communication layer for CSM applications.  
It turns a local CSM program into a remotely controllable TCP server through the CSM invisible bus mechanism.

## Features

```mermaid
flowchart LR
    A[Remote TCP Clients]:::client --> B[CSM-TCP-Router]:::router
    B --> C[CSM Invisible Bus]:::bus
    C --> D[Local CSM Modules]:::module
    D --> C
    C --> B
    B --> A

    classDef client fill:#e8f4ff,stroke:#1d4ed8,color:#0f172a,stroke-width:1.5px;
    classDef router fill:#ecfeff,stroke:#0e7490,color:#0f172a,stroke-width:2px;
    classDef bus fill:#f5f3ff,stroke:#7c3aed,color:#0f172a,stroke-width:1.5px;
    classDef module fill:#f0fdf4,stroke:#15803d,color:#0f172a,stroke-width:1.5px;
```

- Any CSM message available locally can be forwarded through TCP in synchronous or asynchronous format.
- Based on JKI TCP Server, it supports multiple concurrent client connections.
- The repository includes a standard client for connection and command verification.

## Protocol

TCP packet format:

```
| Data Length (4B) | Version (1B) | TYPE (1B) | FLAG1 (1B) | FLAG2 (1B) |      Text Data      |
╰──────────────────────────── Header ────────────────────────────╯╰─ Data Length Range ─╯
```

Supported packet `TYPE` values:

- `info` (`0x00`): sent on connect (welcome) and disconnect (goodbye)
- `error` (`0x01`)
- `cmd` (`0x02`)
- `cmd-resp` (`0x03`)
- `resp` (`0x04`)
- `async-resp` (`0x05`)
- `status` (`0x06`)
- `interrupt` (`0x07`)

See [Protocol Design](.doc/Protocol.v0.(en).md) for full details.

## Command Sets

```mermaid
flowchart TD
    A[Command Sets]:::root --> B[1. CSM Message APIs]:::csm
    A --> C[2. Router Management APIs]:::router
    A --> D[3. Client Built-ins]:::client

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
    D --> D3[TAB key focus]

    classDef root fill:#fff7ed,stroke:#c2410c,color:#0f172a,stroke-width:2px;
    classDef csm fill:#eff6ff,stroke:#2563eb,color:#0f172a,stroke-width:1.5px;
    classDef router fill:#f0fdfa,stroke:#0f766e,color:#0f172a,stroke-width:1.5px;
    classDef client fill:#faf5ff,stroke:#9333ea,color:#0f172a,stroke-width:1.5px;
```

### 1) CSM Message APIs

Defined by existing CSM-based application code and forwarded through the router without intrusive changes.

### 2) Router Management APIs

Defined by CSM-TCP-Router for module management and runtime inspection.

### 3) Client Built-ins (Client only)

Built into the bundled client and not available through secondary API development.

```mermaid
sequenceDiagram
    participant U as User
    participant C as Client Console
    participant R as CSM-TCP-Router
    participant S as Server Log

    U->>C: Connect (IP + Port)
    C->>R: TCP connect
    R-->>C: welcome (info)
    U->>C: Send command
    C->>R: cmd packet
    R-->>C: resp / async-resp
    R-->>S: Execution history
    U->>C: Bye
    C->>R: disconnect
    R-->>C: goodbye (info)
```

## Usage

1. Install this package and dependencies in VIPM.
2. Open `CSM-TCP-Router.lvproj` from CSM examples.
3. Run `CSM-TCP-Router(Server).vi`.
4. Run `Client.vi`, enter server IP/port, and connect.
5. Send commands and check returned messages in the client console.
6. Check execution history in the server log panel.
7. Enter `Bye` in `Client.vi` to disconnect.
8. Stop the server.

### Download

Search `CSM TCP Router` in VIPM and install.

### Dependencies

- Communicable State Machine (CSM) - NEVSTOP
- JKI TCP Server - JKI
- Global Stop - NEVSTOP
- OpenG
