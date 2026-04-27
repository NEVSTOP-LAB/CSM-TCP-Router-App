"""csm-tcp-router-client – Python client SDK for the CSM-TCP-Router server.

Sync usage::

    from csm_tcp_router import TcpRouterClient

    with TcpRouterClient() as client:
        client.connect("localhost", 30007)
        print(client.list_modules())

Async usage::

    import asyncio
    from csm_tcp_router import AsyncTcpRouterClient

    async def main():
        async with AsyncTcpRouterClient() as client:
            await client.connect("localhost", 30007)
            print(await client.list_modules())

    asyncio.run(main())
"""

from .async_client import AsyncTcpRouterClient
from .client import TcpRouterClient
from .exceptions import (
    ConnectionError,
    ProtocolError,
    ServerError,
    TcpRouterError,
    TimeoutError,
)
from .models import (
    AsyncResponse,
    CommandResponse,
    PacketType,
    StatusNotification,
)

__version__ = "0.2.0"

__all__ = [
    "TcpRouterClient",
    "AsyncTcpRouterClient",
    # Exceptions
    "TcpRouterError",
    "ConnectionError",
    "TimeoutError",
    "ProtocolError",
    "ServerError",
    # Models
    "PacketType",
    "CommandResponse",
    "AsyncResponse",
    "StatusNotification",
    # Version
    "__version__",
]
