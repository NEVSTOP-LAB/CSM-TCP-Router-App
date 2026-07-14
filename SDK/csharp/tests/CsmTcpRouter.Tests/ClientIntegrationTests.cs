using System;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace CsmTcpRouter.Tests
{
    /// <summary>
    /// 针对真实回环 <see cref="MockServer"/> 的端到端客户端测试。
    /// 镜像 SDK/python/tests/test_integration.py 以及 test_client.py 的部分内容。
    /// </summary>
    public class ClientIntegrationTests
    {
        private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(2);

        /// <summary>
        /// 将 TcpListener 绑定到端口 0（由操作系统分配），获取端口号，然后停止
        /// 监听器。在测试期间该端口几乎可以确定是关闭的，因此我们可以依赖连接
        /// 尝试失败，而无需依赖系统状态（例如，端口 1 可能是开放的）。
        /// </summary>
        private static int GetClosedPort()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            return port;
        }

        // ---------------------------------------------------------------
        // 连接 / 断开连接
        // ---------------------------------------------------------------

        [Fact]
        public void Connect_Disconnect_RoundTrip()
        {
            using var server = new MockServer();
            server.Start();
            using var client = new TcpRouterClient();
            client.Connect(server.Host, server.Port, DefaultTimeout);
            Assert.True(client.Connected);
            client.Disconnect();
            Assert.False(client.Connected);
        }

        [Fact]
        public async Task ConnectAsync_RoundTrip()
        {
            using var server = new MockServer();
            server.Start();
            using var client = new TcpRouterClient();
            await client.ConnectAsync(server.Host, server.Port, DefaultTimeout);
            Assert.True(client.Connected);
            client.Disconnect();
        }

        [Fact]
        public void Connect_Twice_Throws()
        {
            using var server = new MockServer();
            server.Start();
            using var client = new TcpRouterClient();
            client.Connect(server.Host, server.Port, DefaultTimeout);
            Assert.Throws<RouterConnectionException>(
                () => client.Connect(server.Host, server.Port, DefaultTimeout));
        }

        [Fact]
        public void Connect_BadPort_Throws()
        {
            using var client = new TcpRouterClient();
            int closedPort = GetClosedPort();
            Assert.Throws<RouterConnectionException>(
                () => client.Connect("127.0.0.1", closedPort, TimeSpan.FromMilliseconds(500)));
        }

        // ---------------------------------------------------------------
        // SendAndWait / 内置方法
        // ---------------------------------------------------------------

        [Fact]
        public void Ping_ReturnsTrueAndElapsed()
        {
            using var server = new MockServer();
            server.Start();
            using var client = new TcpRouterClient();
            client.Connect(server.Host, server.Port, DefaultTimeout);
            var (ok, elapsed) = client.Ping(DefaultTimeout);
            Assert.True(ok);
            Assert.True(elapsed >= TimeSpan.Zero);
        }

        [Fact]
        public void ListModules_ReturnsServerText()
        {
            using var server = new MockServer();
            server.Start();
            using var client = new TcpRouterClient();
            client.Connect(server.Host, server.Port, DefaultTimeout);
            var modules = client.ListModules(DefaultTimeout);
            Assert.Contains("AI", modules);
            Assert.Contains("DIO", modules);
        }

        [Fact]
        public void ListApi_FormatsCommand()
        {
            using var server = new MockServer();
            server.Start();
            using var client = new TcpRouterClient();
            client.Connect(server.Host, server.Port, DefaultTimeout);
            var api = client.ListApi("DAQmx", DefaultTimeout);
            Assert.Contains("DAQmx", api);
        }

        [Fact]
        public void SendAndWait_CustomResponse()
        {
            using var server = new MockServer();
            server.Start();
            server.SetResponse("Custom Cmd", "Custom Reply");
            using var client = new TcpRouterClient();
            client.Connect(server.Host, server.Port, DefaultTimeout);
            var resp = client.SendAndWait("Custom Cmd", DefaultTimeout);
            Assert.Equal("Custom Reply", resp.Text);
        }

        [Fact]
        public void SendAndWait_ServerError_Throws()
        {
            using var server = new MockServer();
            server.Start();
            server.SetErrorResponse("Bad Cmd", "[Error: 42] bad");
            using var client = new TcpRouterClient();
            client.Connect(server.Host, server.Port, DefaultTimeout);
            var ex = Assert.Throws<ServerException>(() => client.SendAndWait("Bad Cmd", DefaultTimeout));
            Assert.Equal("42", ex.Code);
            Assert.Equal("bad", ex.ServerMessage);
        }

        [Fact]
        public void SendAndWait_Timeout_Throws()
        {
            using var server = new MockServer();
            server.Start();
            using var client = new TcpRouterClient();
            client.Connect(server.Host, server.Port, DefaultTimeout);
            // 服务器默认对未知命令回复 CmdResp，而不是 Resp，
            // 因此 SendAndWait 在等待 Resp 时会超时。
            Assert.Throws<RouterTimeoutException>(
                () => client.SendAndWait("Unknown XYZ", TimeSpan.FromMilliseconds(200)));
        }

        [Fact]
        public void SendAndWait_NotConnected_Throws()
        {
            using var client = new TcpRouterClient();
            Assert.Throws<RouterConnectionException>(
                () => client.SendAndWait("Ping", DefaultTimeout));
        }

        // ---------------------------------------------------------------
        // Post（异步 cmd-resp 握手）
        // ---------------------------------------------------------------

        [Fact]
        public void Post_CompletesOnHandshake()
        {
            using var server = new MockServer();
            server.Start();
            using var client = new TcpRouterClient();
            client.Connect(server.Host, server.Port, DefaultTimeout);
            client.Post("API: Start -> DAQmx", DefaultTimeout);
            // 确保服务器实际收到了命令。
            string cmd = server.GetReceived(DefaultTimeout);
            Assert.Equal("API: Start -> DAQmx", cmd);
        }

        [Fact]
        public void PostNoReply_CompletesOnHandshake()
        {
            using var server = new MockServer();
            server.Start();
            using var client = new TcpRouterClient();
            client.Connect(server.Host, server.Port, DefaultTimeout);
            client.PostNoReply("API: Reset ->| DAQmx", DefaultTimeout);
            string cmd = server.GetReceived(DefaultTimeout);
            Assert.Equal("API: Reset ->| DAQmx", cmd);
        }

        // ---------------------------------------------------------------
        // 订阅 / 状态广播
        // ---------------------------------------------------------------

        [Fact]
        public void SubscribeStatus_DeliversToCallbackAndQueue()
        {
            using var server = new MockServer();
            server.Start();
            using var client = new TcpRouterClient();
            client.Connect(server.Host, server.Port, DefaultTimeout);

            StatusNotification received = null;
            using var ev = new ManualResetEventSlim();
            client.SubscribeStatus("Status", "AI", n => { received = n; ev.Set(); }, DefaultTimeout);

            server.PushStatus("Status >> v1 <- AI");

            Assert.True(ev.Wait(DefaultTimeout), "callback was not invoked in time");
            Assert.Equal("Status", received.StatusName);
            Assert.Equal("v1", received.Data);
            Assert.Equal("AI", received.ModuleName);

            Assert.True(client.StatusQueue.TryDequeue(out var queued));
            Assert.Equal("Status", queued.StatusName);
        }

        [Fact]
        public void UnsubscribeStatus_RemovesCallback()
        {
            using var server = new MockServer();
            server.Start();
            using var client = new TcpRouterClient();
            client.Connect(server.Host, server.Port, DefaultTimeout);

            int hits = 0;
            using var ev = new ManualResetEventSlim();
            client.SubscribeStatus(
                "Status", "AI",
                _ => { Interlocked.Increment(ref hits); ev.Set(); },
                DefaultTimeout);
            client.UnsubscribeStatus("Status", "AI", DefaultTimeout);

            server.PushStatus("Status >> v1 <- AI");
            Assert.False(ev.Wait(TimeSpan.FromMilliseconds(150)), "callback was invoked after unsubscribe");
            Assert.Equal(0, hits);
        }

        [Fact]
        public void RegisterAsyncCallback_DeliversAsyncResponse()
        {
            using var server = new MockServer();
            server.Start();
            using var client = new TcpRouterClient();
            client.Connect(server.Host, server.Port, DefaultTimeout);

            AsyncResponse received = null;
            using var ev = new ManualResetEventSlim();
            client.RegisterAsyncCallback("API: Start -> DIO", ar => { received = ar; ev.Set(); });

            server.PushAsyncResponse("done <- API: Start -> DIO");

            Assert.True(ev.Wait(DefaultTimeout), "async callback not invoked");
            Assert.Equal("done", received.Text);
            Assert.Equal("API: Start -> DIO", received.OriginalCommand);
        }

        // ---------------------------------------------------------------
        // 等待期间断开连接会解除等待者的阻塞
        // ---------------------------------------------------------------

        [Fact]
        public void Disconnect_WhileWaiting_RaisesConnectionError()
        {
            using var server = new MockServer();
            server.Start();
            using var client = new TcpRouterClient();
            client.Connect(server.Host, server.Port, DefaultTimeout);

            // 发起一个永远不会收到响应的 SendAndWait；在等待期间断开连接。
            var task = Task.Run(() => client.SendAndWait("Unknown XYZ", TimeSpan.FromSeconds(10)));
            Thread.Sleep(100);
            client.Disconnect();
            var agg = Assert.Throws<AggregateException>(() => task.Wait(TimeSpan.FromSeconds(2)));
            Assert.IsType<RouterConnectionException>(agg.InnerException);
        }

        // ---------------------------------------------------------------
        // WaitForServer
        // ---------------------------------------------------------------

        [Fact]
        public void WaitForServer_ReturnsTrueWhenReady()
        {
            using var server = new MockServer();
            server.Start();
            using var client = new TcpRouterClient();
            bool ready = client.WaitForServer(server.Host, server.Port, TimeSpan.FromSeconds(2), TimeSpan.FromMilliseconds(50));
            Assert.True(ready);
        }

        [Fact]
        public void WaitForServer_ReturnsFalseOnTimeout()
        {
            using var client = new TcpRouterClient();
            int closedPort = GetClosedPort();
            bool ready = client.WaitForServer("127.0.0.1", closedPort, TimeSpan.FromMilliseconds(300), TimeSpan.FromMilliseconds(50));
            Assert.False(ready);
        }

        // ---------------------------------------------------------------
        // 异步 API
        // ---------------------------------------------------------------

        [Fact]
        public async Task SendAndWaitAsync_RoundTrip()
        {
            using var server = new MockServer();
            server.Start();
            server.SetResponse("Hello", "World");
            using var client = new TcpRouterClient();
            await client.ConnectAsync(server.Host, server.Port, DefaultTimeout);
            var resp = await client.SendAndWaitAsync("Hello", DefaultTimeout);
            Assert.Equal("World", resp.Text);
        }

        [Fact]
        public async Task PingAsync_ReturnsOkAndElapsed()
        {
            using var server = new MockServer();
            server.Start();
            using var client = new TcpRouterClient();
            await client.ConnectAsync(server.Host, server.Port, DefaultTimeout);
            var (ok, elapsed) = await client.PingAsync(DefaultTimeout);
            Assert.True(ok);
            Assert.True(elapsed >= TimeSpan.Zero);
        }

        [Fact]
        public async Task ListModulesAsync_ReturnsText()
        {
            using var server = new MockServer();
            server.Start();
            using var client = new TcpRouterClient();
            await client.ConnectAsync(server.Host, server.Port, DefaultTimeout);
            string modules = await client.ListModulesAsync(DefaultTimeout);
            Assert.Contains("AI", modules);
        }

        [Fact]
        public async Task SendAndWaitAsync_SerialisesConcurrentCallers()
        {
            using var server = new MockServer();
            server.Start();
            server.SetResponse("A", "alpha");
            server.SetResponse("B", "bravo");
            using var client = new TcpRouterClient();
            await client.ConnectAsync(server.Host, server.Port, DefaultTimeout);

            var tasks = Enumerable.Range(0, 10).Select(i =>
                client.SendAndWaitAsync(i % 2 == 0 ? "A" : "B", DefaultTimeout)).ToArray();
            var results = await Task.WhenAll(tasks);
            for (int i = 0; i < tasks.Length; i++)
            {
                Assert.Equal(i % 2 == 0 ? "alpha" : "bravo", results[i].Text);
            }
        }
    }
}
