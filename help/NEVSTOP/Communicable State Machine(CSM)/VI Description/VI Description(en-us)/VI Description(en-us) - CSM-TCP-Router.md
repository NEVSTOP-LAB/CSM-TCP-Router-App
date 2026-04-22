# CSM-TCP-Router

## API

CSM-TCP-Router APIs are divided into two parts:

- Server VIs: build and run the TCP router service side.
- Client VIs: connect to router and send/receive commands from LabVIEW applications.

> [!NOTE]
> The list below is inferred from current project structure, public VI naming, and existing examples.

## Server VIs

### CSM-TCP-Router.vi
Core router VI in `src/_addons/TCP-Router/`.

It starts the TCP router communication layer on top of CSM, handles TCP packet routing, and provides Router built-in command handling.

### CSM-TCP-Router(Server).vi
Example server startup VI in `src/Server/`.

It is the standard runnable entry VI used in examples to host CSM modules through CSM-TCP-Router.

### Router built-in commands (server side)
The server also exposes built-in management commands via TCP messages:

- `List`: list available CSM modules.
- `List API`: list exposed APIs for a target module.
- `List State`: list available CSM states for a target module.
- `Help`: read and return module VI Description help text.
- `Refresh lvcsm`: refresh cached lvcsm data.

## Client VIs

All client APIs are under `src/_addons/TCP-Router/ClientAPI/`.

### Obtain.vi
Create/connect a client session to TCP Router server.

### Release.vi
Release client session and related resources.

### Send Message and Wait for Reply.vi
Send a synchronous command and wait for final response.

### Post Message.vi
Post an asynchronous command (server executes without waiting for response in this call).

### Post No-Rep Message.vi
Post a no-reply asynchronous command.

### Ping.vi
Check server reachability and get communication latency.

### Wait for Server.vi
Wait until router server is reachable or timeout.

### Register Status Change.vi / Unregister Status Change.vi
Status subscription API pair.

> [!NOTE]
> These are retained for compatibility; project also contains newer “for Client” variants.

### Register Status for Client.vi / Unregister Status for Client.vi
Register/unregister status subscription for a specific client context.

### Status Queue.vi
Get status updates via queue-based consumption.

### ASync-Response Queue.vi
Get async command responses via queue.

### ASync-Response User Event.vi
Get async command responses via LabVIEW User Event.

### Register Broadcast.vi / Unregister Broadcast.vi
Subscribe/unsubscribe broadcast messages.

### Register Broadcast for Client.vi / Unregister Broadcast for Client.vi
Client-scoped broadcast subscription API pair.
