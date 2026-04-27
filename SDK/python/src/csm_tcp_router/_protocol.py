"""Internal protocol v0 codec.

Wire format (8-byte header, big-endian)::

    | Data Length (4B) | Version (1B=0x01) | Type (1B) | FLAG1 (1B) | FLAG2 (1B) |
    ╰────────────────────────── Header (8B) ──────────────────────────╯

followed by exactly ``Data Length`` bytes of payload.

This module is internal; nothing is re-exported from the package.
"""

from __future__ import annotations

import struct
from typing import Tuple

from .exceptions import ProtocolError
from .models import Packet, PacketType

__all__: list = []  # internal; nothing re-exported

# Header layout: big-endian uint32 data_len + 4 x uint8 (version, type, flag1, flag2)
_HEADER_FORMAT = "!IBBBB"

#: Number of bytes in the fixed packet header.
HEADER_SIZE: int = struct.calcsize(_HEADER_FORMAT)  # == 8

#: Protocol version byte sent in every outgoing packet.
PROTOCOL_VERSION: int = 0x01


def encode_packet(
    data: bytes,
    packet_type: PacketType,
    flag1: int = 0,
    flag2: int = 0,
) -> bytes:
    """Encode *data* into a complete wire-format packet (header + body).

    :param data: Raw payload bytes.
    :param packet_type: :class:`~csm_tcp_router.models.PacketType` for the header.
    :param flag1: FLAG1 byte (currently unused; defaults to 0).
    :param flag2: FLAG2 byte (currently unused; defaults to 0).
    :returns: Concatenated header + payload bytes ready for ``sendall()``.
    """
    header = struct.pack(
        _HEADER_FORMAT,
        len(data),
        PROTOCOL_VERSION,
        packet_type.value,
        flag1,
        flag2,
    )
    return header + data


def decode_header(header_bytes: bytes) -> Tuple[int, int, int, int, int]:
    """Decode an 8-byte header into its constituent fields.

    :returns: ``(data_len, version, type_byte, flag1, flag2)``
    :raises ProtocolError: if *header_bytes* is not exactly :data:`HEADER_SIZE` bytes.
    """
    if len(header_bytes) != HEADER_SIZE:
        raise ProtocolError(
            f"Expected {HEADER_SIZE}-byte header, got {len(header_bytes)} bytes."
        )
    return struct.unpack(_HEADER_FORMAT, header_bytes)  # type: ignore[return-value]


def parse_packet(header_bytes: bytes, body: bytes) -> Packet:
    """Build a :class:`~csm_tcp_router.models.Packet` from raw header + body.

    Unknown packet type bytes are mapped to :attr:`PacketType.INFO` for
    forward compatibility (the server may introduce new types in future
    protocol revisions).

    :raises ProtocolError: on header size mismatch or body length mismatch.
    """
    data_len, version, type_byte, flag1, flag2 = decode_header(header_bytes)
    if len(body) != data_len:
        raise ProtocolError(
            f"Payload length mismatch: header says {data_len} bytes, "
            f"got {len(body)} bytes."
        )
    try:
        ptype = PacketType(type_byte)
    except ValueError:
        # Forward-compatible: treat unknown type as INFO
        ptype = PacketType.INFO
    return Packet(type=ptype, data=body, version=version, flag1=flag1, flag2=flag2)
