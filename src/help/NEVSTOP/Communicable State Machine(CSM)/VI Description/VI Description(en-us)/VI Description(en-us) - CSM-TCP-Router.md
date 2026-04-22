# CSM-TCP-Router

## API

> [!NOTE]
> <b>CSM-TCP-Router API Scope</b>
>
> CSM-TCP-Router APIs are split into two parts:
> - <b>Server VIs</b>: Start and host the TCP router service for CSM modules.
> - <b>Client VIs</b>: Connect to the router and send/receive commands.
>
> The API list below is inferred from current project structure, VI naming, and shipped examples.

## Server VIs

### CSM-TCP-Router.vi
Core router VI in `src/_addons/TCP-Router/`.

Starts the CSM TCP router communication layer, handles packet routing, and serves Router built-in management commands.

### CSM-TCP-Router(Server).vi
Server startup VI in `src/Server/`.

Standard runnable entry VI used by the example project to host CSM modules through CSM-TCP-Router.

### Router Built-in Commands
Commands provided by the router service side:

- `List`: List all available CSM modules.
- `List API`: List exposed APIs of a specified module.
- `List State`: List CSM states of a specified module.
- `Help`: Return module help text from VI Description.
- `Refresh lvcsm`: Refresh cached lvcsm data.

## Client VIs

Client API VIs are under `src/_addons/TCP-Router/ClientAPI/`.

### Obtain.vi
Create and connect a client session to the TCP router server.

### Release.vi
Release a client session and related resources.

### Send Message and Wait for Reply.vi
Send a synchronous command and wait for the final response.

### Post Message.vi
Post an asynchronous command (non-blocking for final response).

### Post No-Rep Message.vi
Post an asynchronous command that does not require final response.

### Ping.vi
Check server reachability and return communication elapsed time.

### Wait for Server.vi
Wait until the server is reachable or timeout is reached.

### Register Status Change.vi
Register a status-change subscription callback.

### Unregister Status Change.vi
Unregister a status-change subscription callback.

### Register Status for Client.vi
Register status subscription for a specified client context.

### Unregister Status for Client.vi
Unregister status subscription for a specified client context.

### Status Queue.vi
Receive status updates through queue-based API.

### ASync-Response Queue.vi
Receive asynchronous command responses through queue-based API.

### ASync-Response User Event.vi
Receive asynchronous command responses through User Event API.

### Register Broadcast.vi
Register broadcast-message subscription.

### Unregister Broadcast.vi
Unregister broadcast-message subscription.

### Register Broadcast for Client.vi
Register broadcast-message subscription for a specified client context.

### Unregister Broadcast for Client.vi
Unregister broadcast-message subscription for a specified client context.
