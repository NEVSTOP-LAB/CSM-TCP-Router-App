"""Exception hierarchy for csm-tcp-router-client."""

__all__ = [
    "ConnectionError",
    "ProtocolError",
    "ServerError",
    "TcpRouterError",
    "TimeoutError",
]


class TcpRouterError(Exception):
    """Base exception for all CSM-TCP-Router client errors."""


class ConnectionError(TcpRouterError):
    """Raised when a connection cannot be established or is lost."""


class TimeoutError(TcpRouterError):
    """Raised when a synchronous operation exceeds its timeout."""


class ProtocolError(TcpRouterError):
    """Raised when an invalid or unexpected protocol frame is received."""


class ServerError(TcpRouterError):
    """Raised when the server returns an error packet.

    Attributes:
        message: Human-readable error text from the server.
        code: Optional error code extracted from the CSM Error format
              ``[Error: <code>] <message>``.
    """

    def __init__(self, message: str, code: str = "") -> None:
        super().__init__(message)
        self.message = message
        self.code = code

    def __str__(self) -> str:
        if self.code:
            return f"[Error: {self.code}] {self.message}"
        return self.message
