using System;
using System.Text;
using CsmTcpRouter;
using Xunit;

namespace CsmTcpRouter.Tests
{
    // Mirrors SDK/python/tests/test_protocol.py.
    public class ProtocolTests
    {
        private const int HeaderSize = 8;
        private const byte ProtocolVersion = 0x01;

        private static (byte DataLenHi3, byte DataLenHi2, byte DataLenHi1, byte DataLenLo,
                         byte Version, byte Type, byte Flag1, byte Flag2)
            ParseHeader(byte[] wire)
        {
            return (wire[0], wire[1], wire[2], wire[3], wire[4], wire[5], wire[6], wire[7]);
        }

        private static uint ReadDataLen(byte[] wire) =>
            ((uint)wire[0] << 24) | ((uint)wire[1] << 16) | ((uint)wire[2] << 8) | wire[3];

        // -------------------------------------------------------------------
        // EncodePacket
        // -------------------------------------------------------------------

        [Fact]
        public void Encode_ReturnsHeaderPlusBody()
        {
            var data = Encoding.UTF8.GetBytes("hello");
            var wire = ProtocolCodec.EncodePacket(data, PacketType.Cmd);
            Assert.Equal(HeaderSize + data.Length, wire.Length);
        }

        [Fact]
        public void Encode_HeaderFormat()
        {
            var data = Encoding.UTF8.GetBytes("hello");
            var wire = ProtocolCodec.EncodePacket(data, PacketType.Cmd);
            Assert.Equal((uint)data.Length, ReadDataLen(wire));
            Assert.Equal(ProtocolVersion, wire[4]);
            Assert.Equal((byte)PacketType.Cmd, wire[5]);
            Assert.Equal(0, wire[6]);
            Assert.Equal(0, wire[7]);
        }

        [Fact]
        public void Encode_BodyAppendedVerbatim()
        {
            var data = Encoding.UTF8.GetBytes("test payload");
            var wire = ProtocolCodec.EncodePacket(data, PacketType.Resp);
            var slice = new byte[data.Length];
            Buffer.BlockCopy(wire, HeaderSize, slice, 0, data.Length);
            Assert.Equal(data, slice);
        }

        [Fact]
        public void Encode_EmptyBody()
        {
            var wire = ProtocolCodec.EncodePacket(Array.Empty<byte>(), PacketType.CmdResp);
            Assert.Equal(HeaderSize, wire.Length);
            Assert.Equal(0u, ReadDataLen(wire));
        }

        [Fact]
        public void Encode_CustomFlags()
        {
            var wire = ProtocolCodec.EncodePacket(new byte[] { 0x78 }, PacketType.Info, flag1: 0xAB, flag2: 0xCD);
            Assert.Equal(0xAB, wire[6]);
            Assert.Equal(0xCD, wire[7]);
        }

        [Fact]
        public void Encode_AllPacketTypes()
        {
            foreach (PacketType ptype in Enum.GetValues(typeof(PacketType)))
            {
                var wire = ProtocolCodec.EncodePacket(Encoding.UTF8.GetBytes("data"), ptype);
                Assert.Equal((byte)ptype, wire[5]);
            }
        }

        [Fact]
        public void Encode_Utf8CommandString()
        {
            const string cmd = "API: Start Sampling -@ DAQmx";
            var wire = ProtocolCodec.EncodePacket(Encoding.UTF8.GetBytes(cmd), PacketType.Cmd);
            Assert.Equal(cmd, Encoding.UTF8.GetString(wire, HeaderSize, wire.Length - HeaderSize));
        }

        [Fact]
        public void Encode_LargePayloadLengthField()
        {
            var data = new byte[1024];
            var wire = ProtocolCodec.EncodePacket(data, PacketType.Resp);
            Assert.Equal(1024u, ReadDataLen(wire));
        }

        [Fact]
        public void Encode_NullDataTreatedAsEmpty()
        {
            var wire = ProtocolCodec.EncodePacket(null, PacketType.Cmd);
            Assert.Equal(HeaderSize, wire.Length);
            Assert.Equal(0u, ReadDataLen(wire));
        }

        // -------------------------------------------------------------------
        // DecodeHeader
        // -------------------------------------------------------------------

        [Fact]
        public void DecodeHeader_RoundTrip()
        {
            var wire = ProtocolCodec.EncodePacket(Encoding.UTF8.GetBytes("body"), PacketType.AsyncResp, flag1: 1, flag2: 2);
            var header = new byte[HeaderSize];
            Buffer.BlockCopy(wire, 0, header, 0, HeaderSize);
            var (dataLen, version, typeByte, flag1, flag2) = ProtocolCodec.DecodeHeader(header);
            Assert.Equal(4u, dataLen);
            Assert.Equal(ProtocolVersion, version);
            Assert.Equal((byte)PacketType.AsyncResp, typeByte);
            Assert.Equal(1, flag1);
            Assert.Equal(2, flag2);
        }

        [Fact]
        public void DecodeHeader_WrongLengthThrows()
        {
            Assert.Throws<ProtocolException>(() => ProtocolCodec.DecodeHeader(new byte[7]));
        }

        [Fact]
        public void DecodeHeader_ZeroLengthThrows()
        {
            Assert.Throws<ProtocolException>(() => ProtocolCodec.DecodeHeader(Array.Empty<byte>()));
        }

        // -------------------------------------------------------------------
        // ParsePacket
        // -------------------------------------------------------------------

        private static (byte[] Header, byte[] Body) MakeWire(byte[] data, PacketType ptype)
        {
            var wire = ProtocolCodec.EncodePacket(data, ptype);
            var header = new byte[HeaderSize];
            var body = new byte[wire.Length - HeaderSize];
            Buffer.BlockCopy(wire, 0, header, 0, HeaderSize);
            Buffer.BlockCopy(wire, HeaderSize, body, 0, body.Length);
            return (header, body);
        }

        [Fact]
        public void Parse_BasicRoundTrip()
        {
            var (header, body) = MakeWire(Encoding.UTF8.GetBytes("hello"), PacketType.Resp);
            var pkt = ProtocolCodec.ParsePacket(header, body);
            Assert.Equal(PacketType.Resp, pkt.Type);
            Assert.Equal("hello", Encoding.UTF8.GetString(pkt.Data));
            Assert.Equal(ProtocolVersion, pkt.Version);
        }

        [Fact]
        public void Parse_AllKnownTypes()
        {
            foreach (PacketType ptype in Enum.GetValues(typeof(PacketType)))
            {
                var (header, body) = MakeWire(Encoding.UTF8.GetBytes("data"), ptype);
                var pkt = ProtocolCodec.ParsePacket(header, body);
                Assert.Equal(ptype, pkt.Type);
            }
        }

        [Fact]
        public void Parse_UnknownTypeMappedToInfo()
        {
            // Manually craft a packet with an unknown type byte (0xFF).
            var header = new byte[] { 0, 0, 0, 4, ProtocolVersion, 0xFF, 0, 0 };
            var body = Encoding.UTF8.GetBytes("data");
            var pkt = ProtocolCodec.ParsePacket(header, body);
            Assert.Equal(PacketType.Info, pkt.Type);
        }

        [Fact]
        public void Parse_BodyLengthMismatchThrows()
        {
            var (header, _) = MakeWire(Encoding.UTF8.GetBytes("hello"), PacketType.Cmd);
            Assert.Throws<ProtocolException>(() => ProtocolCodec.ParsePacket(header, Encoding.UTF8.GetBytes("hi")));
        }

        [Fact]
        public void Parse_EmptyBody()
        {
            var (header, body) = MakeWire(Array.Empty<byte>(), PacketType.CmdResp);
            var pkt = ProtocolCodec.ParsePacket(header, body);
            Assert.Empty(pkt.Data);
        }

        [Fact]
        public void Parse_FlagsPreserved()
        {
            var wire = ProtocolCodec.EncodePacket(new byte[] { 0x78 }, PacketType.Status, flag1: 3, flag2: 7);
            var header = new byte[HeaderSize];
            var body = new byte[wire.Length - HeaderSize];
            Buffer.BlockCopy(wire, 0, header, 0, HeaderSize);
            Buffer.BlockCopy(wire, HeaderSize, body, 0, body.Length);
            var pkt = ProtocolCodec.ParsePacket(header, body);
            Assert.Equal(3, pkt.Flag1);
            Assert.Equal(7, pkt.Flag2);
        }

        [Fact]
        public void Parse_HeaderTooShortThrows()
        {
            Assert.Throws<ProtocolException>(() => ProtocolCodec.ParsePacket(new byte[4], Array.Empty<byte>()));
        }

        [Fact]
        public void HeaderSize_IsEight()
        {
            Assert.Equal(8, ProtocolCodec.HeaderSize);
        }

        // -------------------------------------------------------------------
        // ParseServerError
        // -------------------------------------------------------------------

        [Fact]
        public void ParseServerError_PlainMessage()
        {
            var pkt = new Packet(PacketType.Error, Encoding.UTF8.GetBytes("something went wrong"));
            var err = ProtocolCodec.ParseServerError(pkt);
            Assert.Equal("something went wrong", err.ServerMessage);
            Assert.Equal(string.Empty, err.Code);
        }

        [Fact]
        public void ParseServerError_CsmFormat()
        {
            var pkt = new Packet(PacketType.Error, Encoding.UTF8.GetBytes("[Error: 42] module not found"));
            var err = ProtocolCodec.ParseServerError(pkt);
            Assert.Equal("42", err.Code);
            Assert.Equal("module not found", err.ServerMessage);
            Assert.Equal("[Error: 42] module not found", err.ToString());
        }

        [Fact]
        public void ParseServerError_CsmFormatNoMessage()
        {
            var pkt = new Packet(PacketType.Error, Encoding.UTF8.GetBytes("[Error: 0]"));
            var err = ProtocolCodec.ParseServerError(pkt);
            Assert.Equal("0", err.Code);
            Assert.Equal(string.Empty, err.ServerMessage);
        }

        [Fact]
        public void ParseServerError_MalformedBracketNoCrash()
        {
            var pkt = new Packet(PacketType.Error, Encoding.UTF8.GetBytes("[Error: no closing bracket"));
            var err = ProtocolCodec.ParseServerError(pkt);
            Assert.Equal(string.Empty, err.Code);
            Assert.Contains("no closing bracket", err.ServerMessage);
        }

        // -------------------------------------------------------------------
        // Model parsing helpers
        // -------------------------------------------------------------------

        [Fact]
        public void AsyncResponse_FromPacket_SplitsOnSeparator()
        {
            var pkt = new Packet(PacketType.AsyncResp, Encoding.UTF8.GetBytes("result <- API: Start -> DIO"));
            var resp = AsyncResponse.FromPacket(pkt);
            Assert.Equal("result", resp.Text);
            Assert.Equal("API: Start -> DIO", resp.OriginalCommand);
        }

        [Fact]
        public void AsyncResponse_FromPacket_NoSeparator()
        {
            var pkt = new Packet(PacketType.AsyncResp, Encoding.UTF8.GetBytes("just text"));
            var resp = AsyncResponse.FromPacket(pkt);
            Assert.Equal("just text", resp.Text);
            Assert.Equal(string.Empty, resp.OriginalCommand);
        }

        [Fact]
        public void StatusNotification_FromPacket_FullForm()
        {
            var pkt = new Packet(PacketType.Status, Encoding.UTF8.GetBytes("Status >> value42 <- AI"));
            var notif = StatusNotification.FromPacket(pkt);
            Assert.Equal("Status", notif.StatusName);
            Assert.Equal("value42", notif.Data);
            Assert.Equal("AI", notif.ModuleName);
            Assert.Equal(PacketType.Status, notif.PacketType);
        }

        [Fact]
        public void StatusNotification_FromPacket_InterruptType()
        {
            var pkt = new Packet(PacketType.Interrupt, Encoding.UTF8.GetBytes("Alarm >> fire <- Safety"));
            var notif = StatusNotification.FromPacket(pkt);
            Assert.Equal(PacketType.Interrupt, notif.PacketType);
            Assert.Equal("Alarm", notif.StatusName);
        }
    }
}
