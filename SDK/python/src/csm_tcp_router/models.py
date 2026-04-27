"""Public data models and enumerations for the CSM-TCP-Router protocol."""

from __future__ import annotations

from dataclasses import dataclass
from enum import IntEnum

__all__ = [
    "AsyncResponse",
    "CommandResponse",
    "Packet",
    "PacketType",
    "StatusNotification",
]


class PacketType(IntEnum):
    """Packet type constants as defined in the CSM-TCP-Router protocol v0.

    Wire values
    -----------
    ``INFO``       0x00 – informational messages (welcome / goodbye)
    ``ERROR``      0x01 – error messages from the server
    ``CMD``        0x02 – command sent by the client
    ``CMD_RESP``   0x03 – server handshake for async / no-reply / subscribe
    ``RESP``       0x04 – synchronous response payload
    ``ASYNC_RESP`` 0x05 – asynchronous response payload
    ``STATUS``     0x06 – status broadcast from a subscribed CSM module
    ``INTERRUPT``  0x07 – interrupt broadcast from a subscribed CSM module
    """

    INFO = 0x00
    ERROR = 0x01
    CMD = 0x02
    CMD_RESP = 0x03
    RESP = 0x04
    ASYNC_RESP = 0x05
    STATUS = 0x06
    INTERRUPT = 0x07


@dataclass(frozen=True)
class Packet:
    """A decoded packet received from the server (internal representation)."""

    type: PacketType
    data: bytes
    version: int = 1
    flag1: int = 0
    flag2: int = 0


@dataclass(frozen=True)
class CommandResponse:
    """The result of a synchronous command (:meth:`~csm_tcp_router.TcpRouterClient.send_and_wait`)."""

    raw: bytes

    @property
    def text(self) -> str:
        """Decoded UTF-8 text of the response payload."""
        return self.raw.decode("utf-8", errors="replace")

    def __repr__(self) -> str:
        return f"CommandResponse({self.text!r})"


@dataclass(frozen=True)
class AsyncResponse:
    """An asynchronous response payload delivered via an ``async-resp`` packet.

    Attributes:
        raw: Raw response bytes (the part *before* the `` <- `` separator).
        original_command: The original command text echoed back by the server
                          (the part *after* the `` <- `` separator).
    """

    raw: bytes
    original_command: str = ""

    @property
    def text(self) -> str:
        """Decoded UTF-8 text of the response payload."""
        return self.raw.decode("utf-8", errors="replace")

    @classmethod
    def from_packet(cls, packet: Packet) -> AsyncResponse:
        """Parse an ``ASYNC_RESP`` packet.

        Server format: ``"<response-data> <- <original-command>"``.
        """
        text = packet.data.decode("utf-8", errors="replace")
        parts = text.split(" <- ", 1)
        if len(parts) == 2:
            return cls(raw=parts[0].encode("utf-8"), original_command=parts[1])
        return cls(raw=packet.data)

    def __repr__(self) -> str:
        return f"AsyncResponse({self.text!r}, cmd={self.original_command!r})"


@dataclass(frozen=True)
class StatusNotification:
    """A status broadcast delivered via a ``status`` or ``interrupt`` packet.

    Attributes:
        raw: Full raw payload bytes.
        packet_type: Either :attr:`PacketType.STATUS` or
                     :attr:`PacketType.INTERRUPT`.
        status_name: The name of the broadcasted status (left of ``>>``).
        data: The status payload (between ``>>`` and ``<-``).
        module_name: The sending CSM module name (right of ``<-``).
    """

    raw: bytes
    packet_type: PacketType = PacketType.STATUS
    status_name: str = ""
    data: str = ""
    module_name: str = ""

    @classmethod
    def from_packet(cls, packet: Packet) -> StatusNotification:
        """Parse a ``STATUS`` or ``INTERRUPT`` packet.

        Server format: ``"<status-name> >> <data> <- <module>"``.
        """
        text = packet.data.decode("utf-8", errors="replace")
        module = ""
        left = text
        if " <- " in text:
            left, module = text.rsplit(" <- ", 1)
            module = module.strip()
        status_name = ""
        data = left.strip()
        if " >> " in left:
            status_name, data = left.split(" >> ", 1)
            status_name = status_name.strip()
            data = data.strip()
        return cls(
            raw=packet.data,
            packet_type=packet.type,
            status_name=status_name,
            data=data,
            module_name=module,
        )

    def __repr__(self) -> str:
        return (
            f"StatusNotification(status={self.status_name!r}, "
            f"data={self.data!r}, module={self.module_name!r})"
        )
