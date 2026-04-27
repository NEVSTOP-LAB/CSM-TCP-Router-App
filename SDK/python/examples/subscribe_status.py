"""Status subscription example for csm-tcp-router-client.

Prerequisites
-------------
A running CSM-TCP-Router server that has a CSM module publishing a status.
The reference app (``CSM-TCP-Router(Server).vi``) exposes an ``AI`` module
that continuously broadcasts a ``Status`` status.

Install the SDK::

    pip install csm-tcp-router-client

Run this example::

    python subscribe_status.py
"""

import signal
import threading
import time

from csm_tcp_router_client import ConnectionError, ServerError, StatusNotification, TcpRouterClient

HOST = "localhost"
PORT = 30007

# Module and status name to subscribe to (adjust to match your server)
MODULE_NAME = "AI"
STATUS_NAME = "Status"

# Global stop flag
_stop = threading.Event()


def on_status(notification: StatusNotification) -> None:
    """Callback invoked on every status broadcast from the server."""
    print(
        f"[{time.strftime('%H:%M:%S')}] "
        f"{notification.status_name} @ {notification.module_name} "
        f"→ {notification.data}"
    )


def main() -> None:
    # Allow Ctrl-C to exit cleanly
    signal.signal(signal.SIGINT, lambda *_: _stop.set())

    with TcpRouterClient() as client:
        try:
            client.connect(HOST, PORT)
        except ConnectionError as exc:
            print(f"Connection failed: {exc}")
            return

        print(f"Connected to {HOST}:{PORT}")

        # Subscribe to status broadcasts
        try:
            client.subscribe_status(STATUS_NAME, MODULE_NAME, callback=on_status)
            print(
                f"Subscribed to '{STATUS_NAME}' from module '{MODULE_NAME}'. "
                "Press Ctrl-C to exit.\n"
            )
        except ServerError as exc:
            print(f"Subscription failed: {exc}")
            return

        # Keep running until Ctrl-C
        while not _stop.is_set():
            # You can also poll client.status_queue here if you prefer
            # notification = client.status_queue.get(timeout=1.0)
            time.sleep(0.1)

        # Unsubscribe cleanly before disconnecting
        try:
            client.unsubscribe_status(STATUS_NAME, MODULE_NAME)
            print("\nUnsubscribed.")
        except Exception:
            pass

    print("Disconnected.")


if __name__ == "__main__":
    main()
