"""Async quickstart example for csm-tcp-router-client.

Run against a live CSM-TCP-Router server::

    pip install csm-tcp-router-client
    python examples/async_usage.py
"""

import asyncio

from csm_tcp_router_client import AsyncTcpRouterClient, StatusNotification


async def on_status(notif: StatusNotification) -> None:
    """Async callback – invoked each time the subscribed status changes."""
    print(f"[async callback] {notif.module_name}/{notif.status_name} = {notif.data!r}")


async def main() -> None:
    # ---------------------------------------------------------------------------
    # Basic connection
    # ---------------------------------------------------------------------------
    async with AsyncTcpRouterClient() as client:
        # Optional: wait until the server is available (e.g. during app startup)
        print("Waiting for server …", end=" ", flush=True)
        ok = await client.wait_for_server("localhost", 30007, timeout=15.0)
        if not ok:
            print("timed out")
            return
        print("ready")

        await client.connect("localhost", 30007)
        print(f"Connected: {client.connected}")

        # ---------------------------------------------------------------------------
        # Router management helpers
        # ---------------------------------------------------------------------------
        modules = await client.list_modules()
        print(f"\nLoaded modules:\n{modules}")

        # Ping / latency check
        ok, elapsed_s = await client.ping()
        print(f"\nPing: {ok}, latency = {elapsed_s * 1000:.1f} ms")

        # ---------------------------------------------------------------------------
        # Synchronous command  (client blocks until RESP arrives)
        # ---------------------------------------------------------------------------
        resp = await client.send_and_wait("API: Read -@ DAQmx", timeout=5.0)
        print(f"\nsend_and_wait → {resp.text!r}")

        # ---------------------------------------------------------------------------
        # Asynchronous command  (await the cmd-resp handshake only)
        # ---------------------------------------------------------------------------
        await client.post("API: Start Sampling -> DAQmx", timeout=5.0)
        print("post → handshake received (async result delivered via queue)")

        # Collect the eventual async response from the queue
        if client.async_response_queue is not None:
            try:
                ar = await asyncio.wait_for(client.async_response_queue.get(), timeout=5.0)
                print(f"async_response_queue → {ar.text!r}")
            except asyncio.TimeoutError:
                print("async_response_queue → no result yet (server may not have replied)")

        # ---------------------------------------------------------------------------
        # No-reply command
        # ---------------------------------------------------------------------------
        await client.post_no_reply("API: Reset ->| DAQmx", timeout=5.0)
        print("post_no_reply → handshake received")

        # ---------------------------------------------------------------------------
        # Status subscription with an async callback
        # ---------------------------------------------------------------------------
        await client.subscribe_status("Status", "DAQmx", callback=on_status, timeout=5.0)
        print("\nSubscribed to Status@DAQmx — waiting 3 s for notifications …")
        await asyncio.sleep(3.0)

        # Also drain any notifications that arrived via the polling queue
        if client.status_queue is not None:
            count = 0
            while not client.status_queue.empty():
                notif = client.status_queue.get_nowait()
                print(
                    f"  [queue poll] {notif.module_name}/{notif.status_name} = {notif.data!r}"
                )
                count += 1
            print(f"  {count} notification(s) retrieved from queue")

        await client.unsubscribe_status("Status", "DAQmx", timeout=5.0)
        print("Unsubscribed")

    print("\nDisconnected.")


if __name__ == "__main__":
    asyncio.run(main())
