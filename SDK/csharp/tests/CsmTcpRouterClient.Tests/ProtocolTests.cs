// Unit tests for the protocol v0 codec (Protocol class).

using System;
using System.Buffers.Binary;
using System.Text;
using Xunit;

namespace CsmTcpRouter.Tests
{
    public class ProtocolTests
    {
        // -----------------------------------------------------------------
        // EncodePacket
        // -----------------------------------------------------------------

        [Fact]
        public void EncodePacket_ReturnsHeaderPlusBody()
        {
            var data = Encoding.UTF8.GetBytes("hello");
            var wire = Protocol.EncodePacket(data, PacketType.Cmd);
            Assert.Equal(Protocol.HeaderSize + data.Length, wire.Length);
        }

        [Fact]
        public void EncodePacket_HeaderFormat()
        {
            var data = Encoding.UTF8.GetBytes("hello");
            var wire = Protocol.EncodePacket(data, PacketType.Cmd);
            uint dataLen = BinaryPrimitives.ReadUInt32BigEndian(wire.AsSpan(0, 4));
            Assert.Equal((uint)data.Length, dataLen);
            Assert.Equal(Protocol.Version, wire[4]);
            Assert.Equal((byte)PacketType.Cmd, wire[5]);
            Assert.Equal(0, wire[6]);
            Assert.Equal(0, wire[7]);
        }

        [Fact]
        public void EncodePacket_BodyAppendedVerbatim()
        {
            var data = Encoding.UTF8.GetBytes("test payload");
            var wire = Protocol.EncodePacket(data, PacketType.Resp);
            var body = new byte[data.Length];
            Buffer.BlockCopy(wire, Protocol.HeaderSize, body, 0, data.Length);
            Assert.Equal(data, body);
        }

        [Fact]
        public void EncodePacket_EmptyBody()
        {
            var wire = Protocol.EncodePacket(Array.Empty<byte>(), PacketType.CmdResp);
            Assert.Equal(Protocol.HeaderSize, wire.Length);
            uint dataLen = BinaryPrimitives.ReadUInt32BigEndian(wire.AsSpan(0, 4));
            Assert.Equal(0u, dataLen);
        }

        [Fact]
        public void EncodePacket_CustomFlags()
        {
            var wire = Protocol.EncodePacket(new byte[] { 0x78 }, PacketType.Info, 0xAB, 0xCD);
            Assert.Equal(0xAB, wire[6]);
            Assert.Equal(0xCD, wire[7]);
        }

        [Fact]
        public void EncodePacket_AllPacketTypesEncode()
        {
            foreach (PacketType ptype in Enum.GetValues(typeof(PacketType)))
            {
                var wire = Protocol.EncodePacket(Encoding.UTF8.GetBytes("data"), ptype);
                Assert.Equal((byte)ptype, wire[5]);
            }
        }

        [Fact]
        public void EncodePacket_Utf8CommandString()
        {
            string cmd = "API: Start Sampling -@ DAQmx";
            var wire = Protocol.EncodePacket(Encoding.UTF8.GetBytes(cmd), PacketType.Cmd);
            string body = Encoding.UTF8.GetString(wire, Protocol.HeaderSize, wire.Length - Protocol.HeaderSize);
            Assert.Equal(cmd, body);
        }

        [Fact]
        public void EncodePacket_LargePayloadLengthField()
        {
            var data = new byte[1024];
            var wire = Protocol.EncodePacket(data, PacketType.Resp);
            uint dataLen = BinaryPrimitives.ReadUInt32BigEndian(wire.AsSpan(0, 4));
            Assert.Equal(1024u, dataLen);
        }

        // -----------------------------------------------------------------
        // DecodeHeader
        // -----------------------------------------------------------------

        [Fact]
        public void DecodeHeader_RoundTrip()
        {
            var wire = Protocol.EncodePacket(Encoding.UTF8.GetBytes("body"),
                PacketType.AsyncResp, 1, 2);
            var header = new byte[Protocol.HeaderSize];
            Buffer.BlockCopy(wire, 0, header, 0, Protocol.HeaderSize);
            var (dataLen, version, type, flag1, flag2) = Protocol.DecodeHeader(header);
            Assert.Equal(4u, dataLen);
            Assert.Equal(Protocol.Version, version);
            Assert.Equal((byte)PacketType.AsyncResp, type);
            Assert.Equal(1, flag1);
            Assert.Equal(2, flag2);
        }

        [Fact]
        public void DecodeHeader_WrongLengthRaises()
        {
            Assert.Throws<ProtocolError>(() => Protocol.DecodeHeader(new byte[7]));
        }

        [Fact]
        public void DecodeHeader_ZeroLengthRaises()
        {
            Assert.Throws<ProtocolError>(() => Protocol.DecodeHeader(Array.Empty<byte>()));
        }

        // -----------------------------------------------------------------
        // ParsePacket
        // -----------------------------------------------------------------

        private static (byte[] header, byte[] body) MakeWire(byte[] data, PacketType ptype)
        {
            var wire = Protocol.EncodePacket(data, ptype);
            var header = new byte[Protocol.HeaderSize];
            var body = new byte[wire.Length - Protocol.HeaderSize];
            Buffer.BlockCopy(wire, 0, header, 0, Protocol.HeaderSize);
            Buffer.BlockCopy(wire, Protocol.HeaderSize, body, 0, body.Length);
            return (header, body);
        }

        [Fact]
        public void ParsePacket_BasicRoundTrip()
        {
            var (h, b) = MakeWire(Encoding.UTF8.GetBytes("hello"), PacketType.Resp);
            var pkt = Protocol.ParsePacket(h, b);
            Assert.Equal(PacketType.Resp, pkt.Type);
            Assert.Equal(Encoding.UTF8.GetBytes("hello"), pkt.Data);
            Assert.Equal(Protocol.Version, pkt.Version);
        }

        [Fact]
        public void ParsePacket_AllKnownTypes()
        {
            foreach (PacketType ptype in Enum.GetValues(typeof(PacketType)))
            {
                var (h, b) = MakeWire(Encoding.UTF8.GetBytes("data"), ptype);
                var pkt = Protocol.ParsePacket(h, b);
                Assert.Equal(ptype, pkt.Type);
            }
        }

        [Fact]
        public void ParsePacket_UnknownTypeMappedToInfo()
        {
            // Manually craft a header with unknown type byte 0xFF
            var header = new byte[Protocol.HeaderSize];
            BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(0, 4), 4u);
            header[4] = Protocol.Version;
            header[5] = 0xFF;
            var pkt = Protocol.ParsePacket(header, Encoding.UTF8.GetBytes("data"));
            Assert.Equal(PacketType.Info, pkt.Type);
        }

        [Fact]
        public void ParsePacket_BodyLengthMismatchRaises()
        {
            var (h, _) = MakeWire(Encoding.UTF8.GetBytes("hello"), PacketType.Cmd);
            Assert.Throws<ProtocolError>(() => Protocol.ParsePacket(h, Encoding.UTF8.GetBytes("hi")));
        }

        [Fact]
        public void ParsePacket_EmptyBody()
        {
            var (h, b) = MakeWire(Array.Empty<byte>(), PacketType.CmdResp);
            var pkt = Protocol.ParsePacket(h, b);
            Assert.Empty(pkt.Data);
        }

        [Fact]
        public void ParsePacket_FlagsPreserved()
        {
            var wire = Protocol.EncodePacket(new byte[] { 0x78 }, PacketType.Status, 3, 7);
            var header = new byte[Protocol.HeaderSize];
            var body = new byte[wire.Length - Protocol.HeaderSize];
            Buffer.BlockCopy(wire, 0, header, 0, Protocol.HeaderSize);
            Buffer.BlockCopy(wire, Protocol.HeaderSize, body, 0, body.Length);
            var pkt = Protocol.ParsePacket(header, body);
            Assert.Equal(3, pkt.Flag1);
            Assert.Equal(7, pkt.Flag2);
        }

        [Fact]
        public void ParsePacket_HeaderTooShortRaises()
        {
            Assert.Throws<ProtocolError>(() => Protocol.ParsePacket(new byte[4], Array.Empty<byte>()));
        }

        // -----------------------------------------------------------------
        // HEADER_SIZE constant
        // -----------------------------------------------------------------

        [Fact]
        public void HeaderSize_IsEight()
        {
            Assert.Equal(8, Protocol.HeaderSize);
        }
    }
}
