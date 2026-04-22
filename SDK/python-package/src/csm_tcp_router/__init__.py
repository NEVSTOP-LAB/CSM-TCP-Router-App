"""csm-tcp-router-client – Python client SDK for the CSM-TCP-Router server.

Usage::

    from csm_tcp_router import TcpRouterClient

    with TcpRouterClient() as client:
        client.connect("localhost", 30007)
        print(client.list_modules())
"""

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

__version__ = "0.1.0"

__all__ = [
    "TcpRouterClient",
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
