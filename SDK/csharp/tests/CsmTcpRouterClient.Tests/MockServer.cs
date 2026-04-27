// MockServer – minimal TCP server that emulates a CSM-TCP-Router for testing.
//
// Mirrors SDK/python/tests/conftest.py:MockServer.

using System;
using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace CsmTcpRouter.Tests
{
    /// <summary>
    /// Minimal TCP server that emulates a CSM-TCP-Router for testing.
    /// </summary>
    public sealed class MockServer : IDisposable
    {
        private TcpListener? _listener;
        private CancellationTokenSource? _cts;
        private Task? _acceptTask;
        private readonly object _clientsLock = new object();
        private readonly List<TcpClient> _clients = new List<TcpClient>();
        private readonly ConcurrentDictionary<string, (PacketType, byte[])> _responses =
            new ConcurrentDictionary<string, (PacketType, byte[])>();

        public string Host => "127.0.0.1";
        public int Port { get; private set; }

        /// <summary>All raw command strings received from the client, in order.</summary>
        public BlockingCollection<string> ReceivedCommands { get; } = new BlockingCollection<string>();

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
            lock (_clientsLock)
            {
                foreach (var c in _clients)
                {
                    try { c.Close(); } catch { }
                }
                _clients.Clear();
            }
            try { _acceptTask?.Wait(2000); } catch { }
            _cts?.Dispose();
        }

        public void Dispose() => Stop();

        public void SetResponse(string cmd, string respText)
            => _responses[cmd] = (PacketType.Resp, Encoding.UTF8.GetBytes(respText));

        public void SetErrorResponse(string cmd, string errorText)
            => _responses[cmd] = (PacketType.Error, Encoding.UTF8.GetBytes(errorText));

        public void PushStatus(string payload)
        {
            var wire = Protocol.EncodePacket(Encoding.UTF8.GetBytes(payload), PacketType.Status);
            List<TcpClient> snapshot;
            lock (_clientsLock) { snapshot = new List<TcpClient>(_clients); }
            foreach (var c in snapshot)
            {
                try { c.GetStream().Write(wire, 0, wire.Length); } catch { }
            }
        }

        public string? GetReceived(double timeoutSeconds = 1.0)
        {
            return ReceivedCommands.TryTake(out var item, TimeSpan.FromSeconds(timeoutSeconds)) ? item : null;
        }

        private async Task AcceptLoopAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                TcpClient client;
                try
                {
                    client = await _listener!.AcceptTcpClientAsync().ConfigureAwait(false);
                }
                catch (ObjectDisposedException) { break; }
                catch (SocketException) { break; }

                lock (_clientsLock) { _clients.Add(client); }
                _ = Task.Run(() => HandleClientAsync(client, token));
            }
        }

        private async Task HandleClientAsync(TcpClient client, CancellationToken token)
        {
            try
            {
                var stream = client.GetStream();
                // Welcome packet
                var welcome = Protocol.EncodePacket(
                    Encoding.UTF8.GetBytes("Welcome to mock server"), PacketType.Info);
                await stream.WriteAsync(welcome, 0, welcome.Length, token).ConfigureAwait(false);

                byte[] header = new byte[Protocol.HeaderSize];
                while (!token.IsCancellationRequested)
                {
                    if (!await ReadExactly(stream, header, 0, Protocol.HeaderSize, token).ConfigureAwait(false))
                        break;

                    uint dataLen = BinaryPrimitives.ReadUInt32BigEndian(header.AsSpan(0, 4));
                    byte[] body = dataLen == 0 ? Array.Empty<byte>() : new byte[(int)dataLen];
                    if (dataLen > 0 && !await ReadExactly(stream, body, 0, body.Length, token).ConfigureAwait(false))
                        break;

                    byte typeByte = header[5];
                    if (typeByte == (byte)PacketType.Cmd)
                    {
                        string cmdText = Encoding.UTF8.GetString(body).Trim();
                        ReceivedCommands.TryAdd(cmdText);
                        await HandleCommandAsync(stream, cmdText, token).ConfigureAwait(false);
                    }
                }
            }
            catch (OperationCanceledException) { }
            catch (IOException) { }
            catch (ObjectDisposedException) { }
            finally
            {
                lock (_clientsLock) { _clients.Remove(client); }
                try { client.Close(); } catch { }
            }
        }

        private static async Task<bool> ReadExactly(NetworkStream stream, byte[] buffer, int offset,
            int count, CancellationToken token)
        {
            int read = 0;
            while (read < count)
            {
                int n;
                try
                {
                    n = await stream.ReadAsync(buffer, offset + read, count - read, token).ConfigureAwait(false);
                }
                catch { return false; }
                if (n == 0) return false;
                read += n;
            }
            return true;
        }

        private async Task HandleCommandAsync(NetworkStream stream, string cmd, CancellationToken token)
        {
            if (_responses.TryGetValue(cmd, out var canned))
            {
                var (type, data) = canned;
                var wire = Protocol.EncodePacket(data, type);
                await stream.WriteAsync(wire, 0, wire.Length, token).ConfigureAwait(false);
                return;
            }

            byte[] reply;
            if (cmd == "Ping")
                reply = Protocol.EncodePacket(Encoding.UTF8.GetBytes("Pong"), PacketType.Resp);
            else if (cmd == "List")
                reply = Protocol.EncodePacket(Encoding.UTF8.GetBytes("AI\nDIO\nSystem"), PacketType.Resp);
            else if (cmd.StartsWith("List API ", StringComparison.Ordinal))
            {
                string module = cmd.Substring("List API ".Length).Trim();
                string payload = $"API: Start -> {module}\nAPI: Stop -> {module}";
                reply = Protocol.EncodePacket(Encoding.UTF8.GetBytes(payload), PacketType.Resp);
            }
            else if (cmd.StartsWith("List State ", StringComparison.Ordinal))
            {
                string module = cmd.Substring("List State ".Length).Trim();
                string payload = $"Idle <- {module}\nRunning <- {module}";
                reply = Protocol.EncodePacket(Encoding.UTF8.GetBytes(payload), PacketType.Resp);
            }
            else if (cmd.Contains("-><register>") || cmd.Contains("-><unregister>"))
            {
                reply = Protocol.EncodePacket(Array.Empty<byte>(), PacketType.CmdResp);
            }
            else
            {
                // Generic async handshake for any other command
                reply = Protocol.EncodePacket(Array.Empty<byte>(), PacketType.CmdResp);
            }

            await stream.WriteAsync(reply, 0, reply.Length, token).ConfigureAwait(false);
        }
    }
}
