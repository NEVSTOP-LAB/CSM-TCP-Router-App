using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CsmTcpRouter;

namespace CsmTcpRouter.Tests
{
    /// <summary>用于测试的最小化 TCP 服务器，模拟 CSM-TCP-Router。</summary>
    /// <remarks>镜像 Python <c>tests/conftest.py</c> MockServer。</remarks>
    internal sealed class MockServer : IDisposable
    {
        private TcpListener _listener;
        private CancellationTokenSource _cts;
        private Task _acceptTask;

        public string Host => "127.0.0.1";
        public int Port { get; private set; }

        public BlockingCollection<string> ReceivedCommands { get; }
            = new BlockingCollection<string>(new ConcurrentQueue<string>());

        private readonly Dictionary<string, (PacketType Type, byte[] Data)> _responses
            = new Dictionary<string, (PacketType, byte[])>();
        private readonly object _stateLock = new object();

        private readonly List<TcpClient> _clients = new List<TcpClient>();

        public void Start()
        {
            _listener = new TcpListener(IPAddress.Loopback, 0);
            _listener.Start();
            Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
            _cts = new CancellationTokenSource();
            _acceptTask = Task.Run(() => AcceptLoopAsync(_cts.Token));
        }

        public void Stop()
        {
            try { _cts?.Cancel(); } catch { }
            try { _listener?.Stop(); } catch { }
            lock (_stateLock)
            {
                foreach (var c in _clients)
                {
                    try { c.Close(); } catch { }
                }
                _clients.Clear();
            }
            try { _acceptTask?.Wait(TimeSpan.FromSeconds(2)); } catch { }
        }

        public void Dispose() => Stop();

        public void SetResponse(string cmd, string respText)
        {
            lock (_stateLock) { _responses[cmd] = (PacketType.Resp, Encoding.UTF8.GetBytes(respText)); }
        }

        public void SetErrorResponse(string cmd, string errorText)
        {
            lock (_stateLock) { _responses[cmd] = (PacketType.Error, Encoding.UTF8.GetBytes(errorText)); }
        }

        public void PushStatus(string payload)
        {
            byte[] wire = ProtocolCodec.EncodePacket(Encoding.UTF8.GetBytes(payload), PacketType.Status);
            List<TcpClient> snapshot;
            lock (_stateLock) { snapshot = new List<TcpClient>(_clients); }
            foreach (var c in snapshot)
            {
                try { c.GetStream().Write(wire, 0, wire.Length); }
                catch { /* 忽略 */ }
            }
        }

        public void PushAsyncResponse(string payload)
        {
            byte[] wire = ProtocolCodec.EncodePacket(Encoding.UTF8.GetBytes(payload), PacketType.AsyncResp);
            List<TcpClient> snapshot;
            lock (_stateLock) { snapshot = new List<TcpClient>(_clients); }
            foreach (var c in snapshot)
            {
                try { c.GetStream().Write(wire, 0, wire.Length); }
                catch { /* 忽略 */ }
            }
        }

        public string GetReceived(TimeSpan? timeout = null)
        {
            if (ReceivedCommands.TryTake(out var cmd, (int)(timeout ?? TimeSpan.FromSeconds(1)).TotalMilliseconds))
                return cmd;
            return null;
        }

        // -----------------------------------------------------------------
        // 内部方法
        // -----------------------------------------------------------------

        private async Task AcceptLoopAsync(CancellationToken ct)
        {
            try
            {
                while (!ct.IsCancellationRequested)
                {
                    TcpClient client;
                    try { client = await _listener.AcceptTcpClientAsync().ConfigureAwait(false); }
                    catch (ObjectDisposedException) { break; }
                    catch (SocketException) { break; }
                    lock (_stateLock) { _clients.Add(client); }
                    _ = Task.Run(() => HandleClientAsync(client, ct));
                }
            }
            catch { /* 忽略 */ }
        }

        private async Task HandleClientAsync(TcpClient client, CancellationToken ct)
        {
            try
            {
                var stream = client.GetStream();
                // 欢迎信息数据包
                var welcome = ProtocolCodec.EncodePacket(Encoding.UTF8.GetBytes("Welcome to mock server"), PacketType.Info);
                await stream.WriteAsync(welcome, 0, welcome.Length, ct).ConfigureAwait(false);

                var headerBuf = new byte[8];
                while (!ct.IsCancellationRequested)
                {
                    if (!await ReadExactlyAsync(stream, headerBuf, 0, headerBuf.Length, ct).ConfigureAwait(false))
                        break;
                    uint dataLen = ((uint)headerBuf[0] << 24) | ((uint)headerBuf[1] << 16) | ((uint)headerBuf[2] << 8) | headerBuf[3];
                    var body = dataLen == 0 ? Array.Empty<byte>() : new byte[dataLen];
                    if (dataLen > 0 && !await ReadExactlyAsync(stream, body, 0, body.Length, ct).ConfigureAwait(false))
                        break;
                    byte typeByte = headerBuf[5];
                    if (typeByte == (byte)PacketType.Cmd)
                    {
                        string cmdText = Encoding.UTF8.GetString(body).Trim();
                        ReceivedCommands.Add(cmdText, ct);
                        HandleCommand(stream, cmdText);
                    }
                }
            }
            catch (IOException) { }
            catch (ObjectDisposedException) { }
            catch (OperationCanceledException) { }
            finally
            {
                lock (_stateLock) { _clients.Remove(client); }
                try { client.Close(); } catch { }
            }
        }

        private void HandleCommand(NetworkStream stream, string cmd)
        {
            (PacketType Type, byte[] Data) custom;
            bool hasCustom;
            lock (_stateLock) { hasCustom = _responses.TryGetValue(cmd, out custom); }
            if (hasCustom)
            {
                var wire = ProtocolCodec.EncodePacket(custom.Data, custom.Type);
                stream.Write(wire, 0, wire.Length);
                return;
            }

            byte[] reply;
            if (cmd == "Ping")
                reply = ProtocolCodec.EncodePacket(Encoding.UTF8.GetBytes("Pong"), PacketType.Resp);
            else if (cmd == "List")
                reply = ProtocolCodec.EncodePacket(Encoding.UTF8.GetBytes("AI\nDIO\nSystem"), PacketType.Resp);
            else if (cmd.StartsWith("List API ", StringComparison.Ordinal))
            {
                string module = cmd.Substring("List API ".Length).Trim();
                reply = ProtocolCodec.EncodePacket(
                    Encoding.UTF8.GetBytes($"API: Start -> {module}\nAPI: Stop -> {module}"),
                    PacketType.Resp);
            }
            else if (cmd.StartsWith("List State ", StringComparison.Ordinal))
            {
                string module = cmd.Substring("List State ".Length).Trim();
                reply = ProtocolCodec.EncodePacket(
                    Encoding.UTF8.GetBytes($"Idle <- {module}\nRunning <- {module}"),
                    PacketType.Resp);
            }
            else if (cmd.Contains("-><register>") || cmd.Contains("-><unregister>"))
            {
                reply = ProtocolCodec.EncodePacket(Array.Empty<byte>(), PacketType.CmdResp);
            }
            else
            {
                // 对其他任何命令进行通用异步握手。
                reply = ProtocolCodec.EncodePacket(Array.Empty<byte>(), PacketType.CmdResp);
            }

            stream.Write(reply, 0, reply.Length);
        }

        private static async Task<bool> ReadExactlyAsync(Stream s, byte[] buf, int off, int count, CancellationToken ct)
        {
            int read = 0;
            while (read < count)
            {
                int n;
                try { n = await s.ReadAsync(buf, off + read, count - read, ct).ConfigureAwait(false); }
                catch (IOException) { return false; }
                catch (ObjectDisposedException) { return false; }
                if (n == 0) return false;
                read += n;
            }
            return true;
        }
    }
}
