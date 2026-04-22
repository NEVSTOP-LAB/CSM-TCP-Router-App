"""Basic usage example for csm-tcp-router-client.

Prerequisites
-------------
A running CSM-TCP-Router server (LabVIEW app).  The reference app defaults
to port 30007.  Start it from ``CSM-TCP-Router(Server).vi``.

Install the SDK::

    pip install csm-tcp-router-client

Run this example::

    python basic_usage.py
"""


from csm_tcp_router import TcpRouterClient
from csm_tcp_router.exceptions import ConnectionError

HOST = "localhost"
PORT = 30007


def main() -> None:
    # -----------------------------------------------------------------------
    # 1. Wait until the server is ready (optional – useful during app startup)
    # -----------------------------------------------------------------------
    print("Waiting for server …", end=" ", flush=True)
    client = TcpRouterClient()
    ok = client.wait_for_server(HOST, PORT, timeout=30, retry_interval=0.5)
    if not ok:
        print("TIMEOUT – server did not start within 30 s.")
        return
    print("ready.")

    # -----------------------------------------------------------------------
    # 2. Connect (use as a context manager so disconnect is always called)
    # -----------------------------------------------------------------------
    with TcpRouterClient() as client:
        try:
            client.connect(HOST, PORT)
        except ConnectionError as exc:
            print(f"Connection failed: {exc}")
            return

        print(f"Connected to {HOST}:{PORT}")

        # -------------------------------------------------------------------
        # 3. Ping – verify round-trip latency
        # -------------------------------------------------------------------
        ok, ms = client.ping()
        if ok:
            print(f"Ping OK  latency={ms * 1000:.1f} ms")
        else:
            print("Ping failed.")

        # -------------------------------------------------------------------
        # 4. List CSM modules loaded on the server
        # -------------------------------------------------------------------
        modules = client.list_modules()
        print(f"\nLoaded modules:\n{modules}")

        # -------------------------------------------------------------------
        # 5. List the API for the first module (if any)
        # -------------------------------------------------------------------
        first_module = modules.strip().splitlines()[0] if modules.strip() else None
        if first_module:
            api_text = client.list_api(first_module)
            print(f"\nAPI for '{first_module}':\n{api_text}")

        # -------------------------------------------------------------------
        # 6. Send a synchronous command  (replace with a real API of yours)
        # -------------------------------------------------------------------
        # resp = client.send_and_wait("API: Read -@ DAQmx")
        # print(f"\nSync response: {resp.text}")

        # -------------------------------------------------------------------
        # 7. Send an asynchronous command  (server returns cmd-resp handshake)
        # -------------------------------------------------------------------
        # client.post("API: Start Sampling -> DAQmx")
        # print("Async command sent – waiting for async-resp …")
        # time.sleep(1)
        # if not client.async_response_queue.empty():
        #     ar = client.async_response_queue.get_nowait()
        #     print(f"Async-resp: {ar.text}")

        # -------------------------------------------------------------------
        # 8. Send a no-reply command
        # -------------------------------------------------------------------
        # client.post_no_reply("API: Reset ->| DAQmx")
        # print("No-reply command sent.")

        print("\nDone.")

    print("Disconnected.")


if __name__ == "__main__":
    main()
