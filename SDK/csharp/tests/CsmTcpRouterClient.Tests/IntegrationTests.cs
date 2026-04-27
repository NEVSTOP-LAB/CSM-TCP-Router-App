// Integration tests: real TcpRouterClient talking to MockServer over localhost TCP.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace CsmTcpRouter.Tests
{
    [Collection("Integration")]
    public class ConnectionTests
    {
        [Fact]
        public void ConnectAndDisconnect()
        {
            using var server = new MockServer();
            server.Start();
            using var client = new TcpRouterClient();
            client.Connect(server.Host, server.Port);
            Assert.True(client.Connected);
            client.Disconnect();
            Assert.False(client.Connected);
        }

        [Fact]
        public void UsingBlockDisconnects()
        {
            using var server = new MockServer();
            server.Start();
            TcpRouterClient capturedClient;
            using (var client = new TcpRouterClient())
            {
                client.Connect(server.Host, server.Port);
                Assert.True(client.Connected);
                capturedClient = client;
            }
            Assert.False(capturedClient.Connected);
        }

        [Fact]
        public void ConnectBadPortRaises()
        {
            using var client = new TcpRouterClient();
            Assert.Throws<RouterConnectionError>(() =>
                client.Connect("127.0.0.1", 1, timeoutSeconds: 0.5));
        }

        [Fact]
        public void WaitForServerSuccess()
        {
            using var server = new MockServer();
            server.Start();
            using var client = new TcpRouterClient();
            bool ok = client.WaitForServer(server.Host, server.Port, 5.0, 0.1);
            Assert.True(ok);
        }

        [Fact]
        public void WaitForServerTimeout()
        {
            using var client = new TcpRouterClient();
            bool ok = client.WaitForServer("127.0.0.1", 1, 0.3, 0.1);
            Assert.False(ok);
        }
    }

    [Collection("Integration")]
    public class PingIntegrationTests
    {
        [Fact]
        public void PingReturnsTrueAndElapsed()
        {
            using var server = new MockServer();
            server.Start();
            using var client = new TcpRouterClient();
            client.Connect(server.Host, server.Port);
            var (ok, elapsed) = client.Ping(2.0);
            Assert.True(ok);
            Assert.True(elapsed > 0);
        }
    }

    [Collection("Integration")]
    public class SendAndWaitIntegrationTests
    {
        [Fact]
        public void ListModules()
        {
            using var server = new MockServer();
            server.Start();
            using var client = new TcpRouterClient();
            client.Connect(server.Host, server.Port);
            var resp = client.SendAndWait("List", 2.0);
            Assert.Contains("AI", resp.Text);
            Assert.Contains("DIO", resp.Text);
        }

        [Fact]
        public void ListApi()
        {
            using var server = new MockServer();
            server.Start();
            using var client = new TcpRouterClient();
            client.Connect(server.Host, server.Port);
            string text = client.ListApi("DAQmx", 2.0);
            Assert.Contains("DAQmx", text);
        }

        [Fact]
        public void ListStates()
        {
            using var server = new MockServer();
            server.Start();
            using var client = new TcpRouterClient();
            client.Connect(server.Host, server.Port);
            string text = client.ListStates("DAQmx", 2.0);
            Assert.True(text.Contains("Idle") || text.Contains("Running"));
        }

        [Fact]
        public void CustomCommandResponse()
        {
            using var server = new MockServer();
            server.Start();
            server.SetResponse("My Command", "My Reply");
            using var client = new TcpRouterClient();
            client.Connect(server.Host, server.Port);
            var resp = client.SendAndWait("My Command", 2.0);
            Assert.Equal("My Reply", resp.Text);
        }

        [Fact]
        public void ServerErrorRaises()
        {
            using var server = new MockServer();
            server.Start();
            server.SetErrorResponse("Bad Command", "[Error: 7] not found");
            using var client = new TcpRouterClient();
            client.Connect(server.Host, server.Port);
            var ex = Assert.Throws<ServerError>(() => client.SendAndWait("Bad Command", 2.0));
            Assert.Equal("7", ex.Code);
        }

        [Fact]
        public void CommandReceivedByServer()
        {
            using var server = new MockServer();
            server.Start();
            server.SetResponse("API: Probe -@ Sensor", "ok");
            using var client = new TcpRouterClient();
            client.Connect(server.Host, server.Port);
            client.SendAndWait("API: Probe -@ Sensor", 2.0);
            string? received = server.GetReceived(0.5);
            Assert.Equal("API: Probe -@ Sensor", received);
        }
    }

    [Collection("Integration")]
    public class PostIntegrationTests
    {
        [Fact]
        public void PostSendsAndReceivesHandshake()
        {
            using var server = new MockServer();
            server.Start();
            using var client = new TcpRouterClient();
            client.Connect(server.Host, server.Port);
            client.Post("API: Start -> DIO", 2.0);
        }

        [Fact]
        public void PostNoReplySendsAndReceivesHandshake()
        {
            using var server = new MockServer();
            server.Start();
            using var client = new TcpRouterClient();
            client.Connect(server.Host, server.Port);
            client.PostNoReply("API: Reset ->| DIO", 2.0);
        }

        [Fact]
        public void PostErrorRaises()
        {
            using var server = new MockServer();
            server.Start();
            server.SetErrorResponse("API: Start -> DIO", "module missing");
            using var client = new TcpRouterClient();
            client.Connect(server.Host, server.Port);
            Assert.Throws<ServerError>(() => client.Post("API: Start -> DIO", 2.0));
        }
    }

    [Collection("Integration")]
    public class StatusSubscriptionIntegrationTests
    {
        [Fact]
        public void SubscribeReceivesStatusViaQueue()
        {
            using var server = new MockServer();
            server.Start();
            using var client = new TcpRouterClient();
            client.Connect(server.Host, server.Port);
            client.SubscribeStatus("Status", "AI", timeoutSeconds: 2.0);
            server.PushStatus("Status >> 42.5 <- AI");

            Assert.True(client.StatusQueue.TryTake(out var notif, TimeSpan.FromSeconds(2)));
            Assert.Equal("Status", notif!.StatusName);
            Assert.Equal("42.5", notif.Data);
            Assert.Equal("AI", notif.ModuleName);
        }

        [Fact]
        public void SubscribeCallbackIsInvoked()
        {
            var received = new List<StatusNotification>();
            using (var server = new MockServer())
            {
                server.Start();
                using var client = new TcpRouterClient();
                client.Connect(server.Host, server.Port);
                client.SubscribeStatus("Status", "AI",
                    callback: n => { lock (received) received.Add(n); },
                    timeoutSeconds: 2.0);
                server.PushStatus("Status >> hello <- AI");
                Thread.Sleep(300);
            }
            Assert.Single(received);
            Assert.Equal("hello", received[0].Data);
        }

        [Fact]
        public void UnsubscribeRemovesCallback()
        {
            var received = new List<StatusNotification>();
            using (var server = new MockServer())
            {
                server.Start();
                using var client = new TcpRouterClient();
                client.Connect(server.Host, server.Port);
                client.SubscribeStatus("Status", "AI",
                    callback: n => { lock (received) received.Add(n); },
                    timeoutSeconds: 2.0);
                client.UnsubscribeStatus("Status", "AI", 2.0);
                server.PushStatus("Status >> ignored <- AI");
                Thread.Sleep(300);
            }
            Assert.Empty(received);
        }

        [Fact]
        public void MultipleStatusNotifications()
        {
            var received = new List<StatusNotification>();
            using (var server = new MockServer())
            {
                server.Start();
                using var client = new TcpRouterClient();
                client.Connect(server.Host, server.Port);
                client.SubscribeStatus("Temp", "Sensor",
                    callback: n => { lock (received) received.Add(n); },
                    timeoutSeconds: 2.0);
                for (int i = 0; i < 5; i++)
                    server.PushStatus($"Temp >> {i} <- Sensor");
                Thread.Sleep(500);
            }
            Assert.Equal(5, received.Count);
            for (int i = 0; i < 5; i++)
                Assert.Equal(i.ToString(), received[i].Data);
        }
    }

    [Collection("Integration")]
    public class DisconnectBehaviourTests
    {
        /// <summary>
        /// SendAndWait raises TimeoutError when the server sends CMD_RESP instead of RESP.
        /// MockServer's default handler sends a CMD_RESP, which goes to the cmd_resp queue.
        /// SendAndWait waits on the resp queue, so it must time out.
        /// </summary>
        [Fact]
        public void WaitRaisesTimeoutWhenNoResp()
        {
            using var server = new MockServer();
            server.Start();
            using var client = new TcpRouterClient();
            client.Connect(server.Host, server.Port);
            Assert.Throws<RouterTimeoutError>(() =>
                client.SendAndWait("Unknown Sync Command", 0.3));
        }
    }

    [Collection("Integration")]
    public class AsyncApiIntegrationTests
    {
        [Fact]
        public async Task SendAndWaitAsyncWorks()
        {
            using var server = new MockServer();
            server.Start();
            await using var client = new TcpRouterClient();
            await client.ConnectAsync(server.Host, server.Port);
            var resp = await client.SendAndWaitAsync("List", 2.0);
            Assert.Contains("AI", resp.Text);
        }

        [Fact]
        public async Task PostAsyncWorks()
        {
            using var server = new MockServer();
            server.Start();
            await using var client = new TcpRouterClient();
            await client.ConnectAsync(server.Host, server.Port);
            await client.PostAsync("API: Start -> DIO", 2.0);
        }

        [Fact]
        public async Task PingAsyncReturnsTrue()
        {
            using var server = new MockServer();
            server.Start();
            await using var client = new TcpRouterClient();
            await client.ConnectAsync(server.Host, server.Port);
            var (ok, elapsed) = await client.PingAsync(2.0);
            Assert.True(ok);
            Assert.True(elapsed > 0);
        }
    }
}
