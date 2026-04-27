// Unit tests for TcpRouterClient internal dispatch and helpers.

using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace CsmTcpRouter.Tests
{
    public class ServerErrorParserTests
    {
        private static Packet MakeError(string text)
            => new Packet(PacketType.Error, Encoding.UTF8.GetBytes(text));

        [Fact]
        public void PlainMessage()
        {
            var err = ServerErrorParser.Parse(MakeError("something went wrong"));
            Assert.Equal("something went wrong", err.Message);
            Assert.Equal(string.Empty, err.Code);
        }

        [Fact]
        public void CsmFormat()
        {
            var err = ServerErrorParser.Parse(MakeError("[Error: 42] module not found"));
            Assert.Equal("42", err.Code);
            Assert.Equal("module not found", err.Message);
            Assert.Equal("[Error: 42] module not found", err.ToString());
        }

        [Fact]
        public void CsmFormatNoMessage()
        {
            var err = ServerErrorParser.Parse(MakeError("[Error: 0]"));
            Assert.Equal("0", err.Code);
            Assert.Equal(string.Empty, err.Message);
        }

        [Fact]
        public void MalformedBracketNoCrash()
        {
            var err = ServerErrorParser.Parse(MakeError("[Error: no closing bracket"));
            Assert.Equal(string.Empty, err.Code);
            Assert.Contains("no closing bracket", err.Message);
        }
    }

    public class PacketDispatchTests
    {
        private static Packet MakePacket(PacketType ptype, string text = "")
            => new Packet(ptype, Encoding.UTF8.GetBytes(text));

        [Fact]
        public void RespUnblocksWaitForResp()
        {
            using var client = new TcpRouterClient();
            var pkt = MakePacket(PacketType.Resp, "ok");
            using var timer = new Timer(_ => client.DispatchPacket(pkt), null, 50, Timeout.Infinite);
            var resp = client.WaitForResp(1.0);
            Assert.Equal("ok", resp.Text);
        }

        [Fact]
        public void CmdRespUnblocksWaitForCmdResp()
        {
            using var client = new TcpRouterClient();
            var pkt = MakePacket(PacketType.CmdResp);
            using var timer = new Timer(_ => client.DispatchPacket(pkt), null, 50, Timeout.Infinite);
            client.WaitForCmdResp(1.0);
        }

        [Fact]
        public void ErrorUnblocksRespWaiterWithException()
        {
            using var client = new TcpRouterClient();
            var pkt = MakePacket(PacketType.Error, "[Error: 1] bad");
            using var timer = new Timer(_ => client.DispatchPacket(pkt), null, 50, Timeout.Infinite);
            Assert.Throws<ServerError>(() => client.WaitForResp(1.0));
        }

        [Fact]
        public void ErrorUnblocksCmdRespWaiterWithException()
        {
            using var client = new TcpRouterClient();
            var pkt = MakePacket(PacketType.Error, "no module");
            using var timer = new Timer(_ => client.DispatchPacket(pkt), null, 50, Timeout.Infinite);
            Assert.Throws<ServerError>(() => client.WaitForCmdResp(1.0));
        }

        [Fact]
        public void AsyncRespAddedToQueue()
        {
            using var client = new TcpRouterClient();
            var pkt = MakePacket(PacketType.AsyncResp, "result <- API: Start -> DIO");
            client.DispatchPacket(pkt);
            Assert.True(client.AsyncResponseQueue.TryTake(out var ar, TimeSpan.FromMilliseconds(500)));
            Assert.Equal("result", ar!.Text);
            Assert.Equal("API: Start -> DIO", ar.OriginalCommand);
        }

        [Fact]
        public void AsyncRespCallsCallback()
        {
            using var client = new TcpRouterClient();
            var received = new List<AsyncResponse>();
            client.RegisterAsyncCallback("API: Start -> DIO", received.Add);
            var pkt = MakePacket(PacketType.AsyncResp, "result <- API: Start -> DIO");
            client.DispatchPacket(pkt);
            Thread.Sleep(50);
            Assert.Single(received);
            Assert.Equal("result", received[0].Text);
        }

        [Fact]
        public void StatusAddedToQueue()
        {
            using var client = new TcpRouterClient();
            var pkt = MakePacket(PacketType.Status, "Status >> value42 <- AI");
            client.DispatchPacket(pkt);
            Assert.True(client.StatusQueue.TryTake(out var notif, TimeSpan.FromMilliseconds(500)));
            Assert.Equal("Status", notif!.StatusName);
            Assert.Equal("value42", notif.Data);
            Assert.Equal("AI", notif.ModuleName);
        }

        [Fact]
        public void InterruptAddedToStatusQueue()
        {
            using var client = new TcpRouterClient();
            var pkt = MakePacket(PacketType.Interrupt, "Alarm >> fire <- Safety");
            client.DispatchPacket(pkt);
            Assert.True(client.StatusQueue.TryTake(out var notif, TimeSpan.FromMilliseconds(500)));
            Assert.Equal(PacketType.Interrupt, notif!.PacketType);
            Assert.Equal("Alarm", notif.StatusName);
        }

        [Fact]
        public void InfoPacketSilentlyDiscarded()
        {
            using var client = new TcpRouterClient();
            client.DispatchPacket(MakePacket(PacketType.Info, "Welcome to the server"));
            Assert.Empty(client.AsyncResponseQueue);
            Assert.Empty(client.StatusQueue);
        }

        [Fact]
        public void OnDisconnectUnblocksWaiters()
        {
            using var client = new TcpRouterClient();
            using var timer = new Timer(_ => client.NotifyDisconnect(), null, 50, Timeout.Infinite);
            Assert.Throws<RouterConnectionError>(() => client.WaitForResp(1.0));
        }
    }

    public class TimeoutTests
    {
        [Fact]
        public void WaitForRespTimeout()
        {
            using var client = new TcpRouterClient();
            var ex = Assert.Throws<RouterTimeoutError>(() => client.WaitForResp(0.1));
            Assert.Contains("0.1s", ex.Message);
        }

        [Fact]
        public void WaitForCmdRespTimeout()
        {
            using var client = new TcpRouterClient();
            var ex = Assert.Throws<RouterTimeoutError>(() => client.WaitForCmdResp(0.1));
            Assert.Contains("0.1s", ex.Message);
        }
    }

    public class AsyncCallbackTests
    {
        [Fact]
        public void RegisterAndUnregister()
        {
            using var client = new TcpRouterClient();
            Action<AsyncResponse> cb = _ => { };
            client.RegisterAsyncCallback("cmd", cb);
            // dispatching a matching async-resp should invoke the callback
            int calls = 0;
            client.UnregisterAsyncCallback("cmd");
            client.RegisterAsyncCallback("cmd", _ => { calls++; });
            client.DispatchPacket(new Packet(PacketType.AsyncResp,
                Encoding.UTF8.GetBytes("data <- cmd")));
            Thread.Sleep(20);
            Assert.Equal(1, calls);

            client.UnregisterAsyncCallback("cmd");
            client.DispatchPacket(new Packet(PacketType.AsyncResp,
                Encoding.UTF8.GetBytes("data2 <- cmd")));
            Thread.Sleep(20);
            Assert.Equal(1, calls); // not invoked again
        }
    }

    public class ContextManagerTests
    {
        [Fact]
        public void DisposeWhenNeverConnectedDoesNotThrow()
        {
            var client = new TcpRouterClient();
            client.Dispose();   // should not throw
            Assert.False(client.Connected);
        }
    }
}
