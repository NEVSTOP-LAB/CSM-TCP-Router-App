"""Unit tests for the protocol v0 codec (_protocol.py)."""

from __future__ import annotations

import struct

import pytest

from csm_tcp_router._protocol import (
    HEADER_SIZE,
    PROTOCOL_VERSION,
    decode_header,
    encode_packet,
    parse_packet,
)
from csm_tcp_router.exceptions import ProtocolError
from csm_tcp_router.models import PacketType

# ---------------------------------------------------------------------------
# encode_packet
# ---------------------------------------------------------------------------

class TestEncodePacket:
    def test_returns_header_plus_body(self):
        data = b"hello"
        wire = encode_packet(data, PacketType.CMD)
        assert len(wire) == HEADER_SIZE + len(data)

    def test_header_format(self):
        data = b"hello"
        wire = encode_packet(data, PacketType.CMD)
        data_len, version, type_byte, flag1, flag2 = struct.unpack(
            "!IBBBB", wire[:HEADER_SIZE]
        )
        assert data_len == len(data)
        assert version == PROTOCOL_VERSION
        assert type_byte == PacketType.CMD.value
        assert flag1 == 0
        assert flag2 == 0

    def test_body_appended_verbatim(self):
        data = b"test payload"
        wire = encode_packet(data, PacketType.RESP)
        assert wire[HEADER_SIZE:] == data

    def test_empty_body(self):
        wire = encode_packet(b"", PacketType.CMD_RESP)
        assert len(wire) == HEADER_SIZE
        (data_len,) = struct.unpack("!I", wire[:4])
        assert data_len == 0

    def test_custom_flags(self):
        wire = encode_packet(b"x", PacketType.INFO, flag1=0xAB, flag2=0xCD)
        _, _, _, flag1, flag2 = struct.unpack("!IBBBB", wire[:HEADER_SIZE])
        assert flag1 == 0xAB
        assert flag2 == 0xCD

    def test_all_packet_types_encode(self):
        for ptype in PacketType:
            wire = encode_packet(b"data", ptype)
            assert wire[5] == ptype.value  # TYPE byte at offset 5

    def test_utf8_command_string(self):
        cmd = "API: Start Sampling -@ DAQmx"
        wire = encode_packet(cmd.encode("utf-8"), PacketType.CMD)
        assert wire[HEADER_SIZE:] == cmd.encode("utf-8")

    def test_large_payload_length_field(self):
        data = b"x" * 1024
        wire = encode_packet(data, PacketType.RESP)
        (data_len,) = struct.unpack("!I", wire[:4])
        assert data_len == 1024


# ---------------------------------------------------------------------------
# decode_header
# ---------------------------------------------------------------------------

class TestDecodeHeader:
    def test_round_trip(self):
        wire = encode_packet(b"body", PacketType.ASYNC_RESP, flag1=1, flag2=2)
        data_len, version, type_byte, flag1, flag2 = decode_header(
            wire[:HEADER_SIZE]
        )
        assert data_len == 4
        assert version == PROTOCOL_VERSION
        assert type_byte == PacketType.ASYNC_RESP.value
        assert flag1 == 1
        assert flag2 == 2

    def test_wrong_length_raises(self):
        with pytest.raises(ProtocolError, match="header"):
            decode_header(b"\x00" * 7)

    def test_zero_length_raises(self):
        with pytest.raises(ProtocolError):
            decode_header(b"")


# ---------------------------------------------------------------------------
# parse_packet
# ---------------------------------------------------------------------------

class TestParsePacket:
    def _make_wire(self, data: bytes, ptype: PacketType) -> tuple:
        wire = encode_packet(data, ptype)
        return wire[:HEADER_SIZE], wire[HEADER_SIZE:]

    def test_basic_round_trip(self):
        header, body = self._make_wire(b"hello", PacketType.RESP)
        pkt = parse_packet(header, body)
        assert pkt.type == PacketType.RESP
        assert pkt.data == b"hello"
        assert pkt.version == PROTOCOL_VERSION

    def test_all_known_types(self):
        for ptype in PacketType:
            header, body = self._make_wire(b"data", ptype)
            pkt = parse_packet(header, body)
            assert pkt.type == ptype

    def test_unknown_type_mapped_to_info(self):
        # Manually craft a packet with an unknown type byte (0xFF)
        raw_header = struct.pack("!IBBBB", 4, PROTOCOL_VERSION, 0xFF, 0, 0)
        pkt = parse_packet(raw_header, b"data")
        assert pkt.type == PacketType.INFO

    def test_body_length_mismatch_raises(self):
        header, _ = self._make_wire(b"hello", PacketType.CMD)
        with pytest.raises(ProtocolError, match="mismatch"):
            parse_packet(header, b"hi")  # shorter body

    def test_empty_body(self):
        header, body = self._make_wire(b"", PacketType.CMD_RESP)
        pkt = parse_packet(header, body)
        assert pkt.data == b""

    def test_flags_preserved(self):
        wire = encode_packet(b"x", PacketType.STATUS, flag1=3, flag2=7)
        pkt = parse_packet(wire[:HEADER_SIZE], wire[HEADER_SIZE:])
        assert pkt.flag1 == 3
        assert pkt.flag2 == 7

    def test_header_too_short_raises(self):
        with pytest.raises(ProtocolError):
            parse_packet(b"\x00" * 4, b"")


# ---------------------------------------------------------------------------
# HEADER_SIZE constant
# ---------------------------------------------------------------------------

def test_header_size_is_eight():
    assert HEADER_SIZE == 8
