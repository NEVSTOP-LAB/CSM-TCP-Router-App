"""Interactive client console for csm-tcp-router-client.

A small REPL that connects to a running CSM-TCP-Router server, accepts
user-typed commands from stdin, and forwards them through the SDK.
The same command set, prompt and output format are implemented in the C
and C# SDK examples (``client_console.c`` and ``examples/ClientConsole``)
so behavior is identical across all three languages.

Prerequisites
-------------
A running CSM-TCP-Router server (LabVIEW app) – the reference server
defaults to port 30007.  Start it from ``CSM-TCP-Router(Server).vi``.

Install the SDK::

    pip install csm-tcp-router-client

Run this example::

    python client_console.py [host] [port]

Available commands at the ``csm>`` prompt
-----------------------------------------
    help                 Show this help text
    quit / exit          Disconnect and exit
    ping                 Measure round-trip latency
    list                 List CSM modules loaded on the server
    api <module>         List the API of a module
    state <module>       List the states of a module
    mhelp <module>       Server-side Help for a module
    send <command>       Send a synchronous command and print the response
    post <command>       Send an asynchronous command (``->`` suffix)
    nopost <command>     Send a no-reply asynchronous command (``->|``)
    sub <status>@<mod>   Subscribe to a status broadcast
    unsub <status>@<mod> Unsubscribe from a status broadcast
"""

from __future__ import annotations

import sys

from csm_tcp_router_client import (
    AsyncResponse,
    ConnectionError,
    ServerError,
    StatusNotification,
    TcpRouterClient,
    TcpRouterError,
)

DEFAULT_HOST = "localhost"
DEFAULT_PORT = 30007

HELP_TEXT = """\
Available commands:
  help                 Show this help text
  quit / exit          Disconnect and exit
  ping                 Measure round-trip latency
  list                 List CSM modules loaded on the server
  api <module>         List the API of a module
  state <module>       List the states of a module
  mhelp <module>       Server-side Help for a module
  send <command>       Send a synchronous command and print the response
  post <command>       Send an asynchronous command (-> suffix)
  nopost <command>     Send a no-reply asynchronous command (->|)
  sub <status>@<mod>   Subscribe to a status broadcast
  unsub <status>@<mod> Unsubscribe from a status broadcast"""


def _on_status(notification: StatusNotification) -> None:
    """Print every status broadcast received on a subscription."""
    print(
        f"\n[STATUS] {notification.status_name}@{notification.module_name}"
        f": {notification.data}"
    )


def _on_async(response: AsyncResponse) -> None:
    """Print every async-resp packet that matches a registered command."""
    print(f"\n[ASYNC] {response.text}  (cmd={response.original_command})")


def _split_status_module(arg: str) -> tuple[str, str]:
    """Parse ``<status>@<module>`` into a ``(status, module)`` tuple."""
    if "@" not in arg:
        raise ValueError("expected '<status>@<module>'")
    status, module = arg.split("@", 1)
    status, module = status.strip(), module.strip()
    if not status or not module:
        raise ValueError("expected '<status>@<module>'")
    return status, module


def _dispatch(client: TcpRouterClient, line: str) -> bool:
    """Execute one user line.  Return False to exit the REPL."""
    line = line.strip()
    if not line:
        return True

    parts = line.split(None, 1)
    cmd = parts[0].lower()
    arg = parts[1].strip() if len(parts) == 2 else ""

    if cmd in ("quit", "exit"):
        return False
    if cmd == "help":
        print(HELP_TEXT)
        return True
    if cmd == "ping":
        ok, elapsed = client.ping()
        if ok:
            print(f"Ping OK  latency={elapsed * 1000:.1f} ms")
        else:
            print("Ping failed.")
        return True
    if cmd == "list":
        print(client.list_modules())
        return True
    if cmd == "api":
        if not arg:
            print("Error: usage: api <module>")
        else:
            print(client.list_api(arg))
        return True
    if cmd == "state":
        if not arg:
            print("Error: usage: state <module>")
        else:
            print(client.list_states(arg))
        return True
    if cmd == "mhelp":
        if not arg:
            print("Error: usage: mhelp <module>")
        else:
            print(client.help(arg))
        return True
    if cmd == "send":
        if not arg:
            print("Error: usage: send <command>")
        else:
            resp = client.send_and_wait(arg)
            print(f"Response: {resp.text}")
        return True
    if cmd == "post":
        if not arg:
            print("Error: usage: post <command>")
        else:
            client.register_async_callback(arg, _on_async)
            client.post(arg)
            print("Async command sent.")
        return True
    if cmd == "nopost":
        if not arg:
            print("Error: usage: nopost <command>")
        else:
            client.post_no_reply(arg)
            print("No-reply command sent.")
        return True
    if cmd == "sub":
        status, module = _split_status_module(arg)
        client.subscribe_status(status, module, callback=_on_status)
        print(f"Subscribed to {status}@{module}")
        return True
    if cmd == "unsub":
        status, module = _split_status_module(arg)
        client.unsubscribe_status(status, module)
        print(f"Unsubscribed from {status}@{module}")
        return True

    print(f"Error: unknown command '{cmd}'.  Type 'help' for the command list.")
    return True


def main(argv: list[str]) -> int:
    host = argv[1] if len(argv) > 1 else DEFAULT_HOST
    if len(argv) > 2:
        try:
            port = int(argv[2])
        except ValueError:
            print(f"Error: invalid port '{argv[2]}'")
            return 1
        if port < 1 or port > 65535:
            print(f"Error: invalid port '{argv[2]}'")
            return 1
    else:
        port = DEFAULT_PORT

    print("CSM-TCP-Router Client Console")
    print(f"Connecting to {host}:{port} ...")

    client = TcpRouterClient()
    try:
        client.connect(host, port)
    except ConnectionError as exc:
        print(f"Error: {exc}")
        return 1

    print(f"Connected to {host}:{port}.  Type 'help' for commands, 'quit' to exit.")
    try:
        while True:
            try:
                line = input("csm> ")
            except (EOFError, KeyboardInterrupt):
                print()
                break
            try:
                if not _dispatch(client, line):
                    break
            except ServerError as exc:
                print(f"Error: {exc}")
            except TcpRouterError as exc:
                print(f"Error: {exc}")
            except ValueError as exc:
                print(f"Error: {exc}")
    finally:
        client.disconnect()
        print("Disconnected.")
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
