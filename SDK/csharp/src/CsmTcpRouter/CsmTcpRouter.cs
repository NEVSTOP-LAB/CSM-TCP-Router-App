// CsmTcpRouter.cs
// ---------------------------------------------------------------------------
// csm-tcp-router-client - C# client SDK for the CSM-TCP-Router LabVIEW server.
//
// Single-file SDK implementing CSM-TCP-Router protocol v0.  Mirrors the
// Python `csm_tcp_router` package layout and feature set:
//
//     * Protocol codec (8-byte header, big-endian, 8 packet types).
//     * Background-receiver TCP transport.
//     * High-level TcpRouterClient with sync and async APIs:
//         - SendAndWait / SendAndWaitAsync     (synchronous CMD/RESP)
//         - Post / PostAsync                   (async CMD with cmd-resp handshake)
//         - PostNoReply / PostNoReplyAsync     (no-reply async CMD)
//         - Ping / PingAsync                   (round-trip latency)
//         - ListModules / ListApi / ListStates / Help
//         - SubscribeStatus / UnsubscribeStatus
//         - RegisterAsyncCallback / UnregisterAsyncCallback
//
// Wire format (8-byte header, big-endian)::
//
//     | Data Length (4B) | Version (1B=0x01) | Type (1B) | FLAG1 (1B) | FLAG2 (1B) |
//     +------------------------ Header (8B) -----------------------+
//
// followed by exactly `Data Length` bytes of payload.
//
// Copyright (c) 2026 NEVSTOP-LAB.  Released under the MIT License.
// ---------------------------------------------------------------------------

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

[assembly: InternalsVisibleTo("CsmTcpRouter.Tests")]

namespace CsmTcpRouter
{
    // -----------------------------------------------------------------------
    // Public enumerations
    // -----------------------------------------------------------------------

    /// <summary>
    /// Packet type constants as defined in the CSM-TCP-Router protocol v0.
    /// </summary>
    public enum PacketType : byte
    {
        /// <summary>Informational message (welcome / goodbye).</summary>
        Info = 0x00,
        /// <summary>Error packet from the server.</summary>
        Error = 0x01,
        /// <summary>Command sent by the client.</summary>
        Cmd = 0x02,
        /// <summary>Server handshake for async / no-reply / subscribe.</summary>
        CmdResp = 0x03,
        /// <summary>Synchronous response payload.</summary>
        Resp = 0x04,
        /// <summary>Asynchronous response payload.</summary>
        AsyncResp = 0x05,
        /// <summary>Status broadcast from a subscribed CSM module.</summary>
        Status = 0x06,
        /// <summary>Interrupt broadcast from a subscribed CSM module.</summary>
        Interrupt = 0x07,
    }

    // -----------------------------------------------------------------------
    // Public data models
    // -----------------------------------------------------------------------

    /// <summary>A decoded packet received from the server.</summary>
    public sealed class Packet
    {
        public PacketType Type { get; }
        public byte[] Data { get; }
        public byte Version { get; }
        public byte Flag1 { get; }
        public byte Flag2 { get; }

        public Packet(PacketType type, byte[] data, byte version = 1, byte flag1 = 0, byte flag2 = 0)
        {
            Type = type;
            Data = data ?? Array.Empty<byte>();
            Version = version;
            Flag1 = flag1;
            Flag2 = flag2;
        }
    }

    /// <summary>The result of a synchronous command (<see cref="TcpRouterClient.SendAndWait"/>).</summary>
    public sealed class CommandResponse
    {
        public byte[] Raw { get; }
        public string Text => Encoding.UTF8.GetString(Raw);

        public CommandResponse(byte[] raw)
        {
            Raw = raw ?? Array.Empty<byte>();
        }

        public override string ToString() => $"CommandResponse(\"{Text}\")";
    }

    /// <summary>An asynchronous response payload delivered via an async-resp packet.</summary>
    public sealed class AsyncResponse
    {
        public byte[] Raw { get; }
        public string OriginalCommand { get; }
        public string Text => Encoding.UTF8.GetString(Raw);

        public AsyncResponse(byte[] raw, string originalCommand = "")
        {
            Raw = raw ?? Array.Empty<byte>();
            OriginalCommand = originalCommand ?? string.Empty;
        }

        /// <summary>
        /// Parse an ASYNC_RESP packet.  Server format:
        /// <c>"&lt;response-data&gt; &lt;- &lt;original-command&gt;"</c>.
        /// </summary>
        public static AsyncResponse FromPacket(Packet packet)
        {
            if (packet == null) throw new ArgumentNullException(nameof(packet));
            string text = Encoding.UTF8.GetString(packet.Data);
            int sep = text.IndexOf(" <- ", StringComparison.Ordinal);
            if (sep >= 0)
            {
                string left = text.Substring(0, sep);
                string right = text.Substring(sep + 4);
                return new AsyncResponse(Encoding.UTF8.GetBytes(left), right);
            }
            return new AsyncResponse(packet.Data);
        }

        public override string ToString() => $"AsyncResponse(\"{Text}\", cmd=\"{OriginalCommand}\")";
    }

    /// <summary>A status broadcast delivered via a STATUS or INTERRUPT packet.</summary>
    public sealed class StatusNotification
    {
        public byte[] Raw { get; }
        public PacketType PacketType { get; }
        public string StatusName { get; }
        public string Data { get; }
        public string ModuleName { get; }

        public StatusNotification(
            byte[] raw,
            PacketType packetType = PacketType.Status,
            string statusName = "",
            string data = "",
            string moduleName = "")
        {
            Raw = raw ?? Array.Empty<byte>();
            PacketType = packetType;
            StatusName = statusName ?? string.Empty;
            Data = data ?? string.Empty;
            ModuleName = moduleName ?? string.Empty;
        }

        /// <summary>
        /// Parse a STATUS or INTERRUPT packet.  Server format:
        /// <c>"&lt;status-name&gt; &gt;&gt; &lt;data&gt; &lt;- &lt;module&gt;"</c>.
        /// </summary>
        public static StatusNotification FromPacket(Packet packet)
        {
            if (packet == null) throw new ArgumentNullException(nameof(packet));
            string text = Encoding.UTF8.GetString(packet.Data);
            string module = string.Empty;
            string left = text;
            int sepArrow = text.LastIndexOf(" <- ", StringComparison.Ordinal);
            if (sepArrow >= 0)
            {
                left = text.Substring(0, sepArrow);
                module = text.Substring(sepArrow + 4).Trim();
            }
            string statusName = string.Empty;
            string data = left.Trim();
            int sepGtGt = left.IndexOf(" >> ", StringComparison.Ordinal);
            if (sepGtGt >= 0)
            {
                statusName = left.Substring(0, sepGtGt).Trim();
                data = left.Substring(sepGtGt + 4).Trim();
            }
            return new StatusNotification(packet.Data, packet.Type, statusName, data, module);
        }

        public override string ToString() =>
            $"StatusNotification(status=\"{StatusName}\", data=\"{Data}\", module=\"{ModuleName}\")";
    }

    // -----------------------------------------------------------------------
    // Exception hierarchy
    // -----------------------------------------------------------------------

    /// <summary>Base exception for all CSM-TCP-Router client errors.</summary>
    public class CsmTcpRouterException : Exception
    {
        public CsmTcpRouterException() { }
        public CsmTcpRouterException(string message) : base(message) { }
        public CsmTcpRouterException(string message, Exception innerException) : base(message, innerException) { }
    }

    /// <summary>Raised when a connection cannot be established or is lost.</summary>
    public class RouterConnectionException : CsmTcpRouterException
    {
        public RouterConnectionException(string message) : base(message) { }
        public RouterConnectionException(string message, Exception innerException) : base(message, innerException) { }
    }

    /// <summary>Raised when a synchronous operation exceeds its timeout.</summary>
    public class RouterTimeoutException : CsmTcpRouterException
    {
        public RouterTimeoutException(string message) : base(message) { }
    }

    /// <summary>Raised when an invalid or unexpected protocol frame is received.</summary>
    public class ProtocolException : CsmTcpRouterException
    {
        public ProtocolException(string message) : base(message) { }
    }

    /// <summary>
    /// Raised when the server returns an error packet.  CSM Error format:
    /// <c>[Error: &lt;code&gt;] &lt;message&gt;</c>.
    /// </summary>
    public class ServerException : CsmTcpRouterException
    {
        public string Code { get; }
        public string ServerMessage { get; }

        public ServerException(string message, string code = "")
            : base(message)
        {
            ServerMessage = message ?? string.Empty;
            Code = code ?? string.Empty;
        }

        public override string ToString()
        {
            return string.IsNullOrEmpty(Code)
                ? ServerMessage
                : $"[Error: {Code}] {ServerMessage}";
        }
    }

    // -----------------------------------------------------------------------
    // Internal protocol codec
    // -----------------------------------------------------------------------

    internal static class ProtocolCodec
    {
        public const int HeaderSize = 8;
        public const byte ProtocolVersion = 0x01;

        /// <summary>Encode <paramref name="data"/> into a complete wire-format packet (header + body).</summary>
        public static byte[] EncodePacket(byte[] data, PacketType packetType, byte flag1 = 0, byte flag2 = 0)
        {
            data = data ?? Array.Empty<byte>();
            var wire = new byte[HeaderSize + data.Length];
            uint len = (uint)data.Length;
            wire[0] = (byte)((len >> 24) & 0xFF);
            wire[1] = (byte)((len >> 16) & 0xFF);
            wire[2] = (byte)((len >> 8) & 0xFF);
            wire[3] = (byte)(len & 0xFF);
            wire[4] = ProtocolVersion;
            wire[5] = (byte)packetType;
            wire[6] = flag1;
            wire[7] = flag2;
            Buffer.BlockCopy(data, 0, wire, HeaderSize, data.Length);
            return wire;
        }

        /// <summary>Decode an 8-byte header into its constituent fields.</summary>
        public static (uint DataLen, byte Version, byte TypeByte, byte Flag1, byte Flag2) DecodeHeader(byte[] header)
        {
            if (header == null || header.Length != HeaderSize)
                throw new ProtocolException(
                    $"Expected {HeaderSize}-byte header, got {(header == null ? 0 : header.Length)} bytes.");
            uint dataLen = ((uint)header[0] << 24) | ((uint)header[1] << 16) | ((uint)header[2] << 8) | header[3];
            return (dataLen, header[4], header[5], header[6], header[7]);
        }

        /// <summary>Build a <see cref="Packet"/> from raw header + body.</summary>
        public static Packet ParsePacket(byte[] header, byte[] body)
        {
            var (dataLen, version, typeByte, flag1, flag2) = DecodeHeader(header);
            body = body ?? Array.Empty<byte>();
            if ((uint)body.Length != dataLen)
                throw new ProtocolException(
                    $"Payload length mismatch: header says {dataLen} bytes, got {body.Length} bytes.");
            // Forward-compatible: unknown type bytes are mapped to Info.
            PacketType ptype = Enum.IsDefined(typeof(PacketType), typeByte)
                ? (PacketType)typeByte
                : PacketType.Info;
            return new Packet(ptype, body, version, flag1, flag2);
        }

        /// <summary>Extract code and message from a CSM Error format <c>[Error: code] msg</c>.</summary>
        public static ServerException ParseServerError(Packet packet)
        {
            string text = Encoding.UTF8.GetString(packet.Data).Trim();
            string code = string.Empty;
            string msg = text;
            if (text.StartsWith("[Error:", StringComparison.Ordinal))
            {
                int end = text.IndexOf(']');
                if (end > 0)
                {
                    code = text.Substring(7, end - 7).Trim();
                    msg = text.Substring(end + 1).Trim();
                }
            }
            return new ServerException(msg, code);
        }
    }

    // -----------------------------------------------------------------------
    // Internal TCP transport (background receive task)
    // -----------------------------------------------------------------------

    internal sealed class Transport : IDisposable
    {
        private readonly object _sendLock = new object();
        private readonly Action<Packet> _onPacket;
        private readonly Action _onDisconnect;

        private TcpClient _client;
        private NetworkStream _stream;
        private CancellationTokenSource _cts;
        private Task _recvTask;
        private volatile bool _stopped;

        public Transport(Action<Packet> onPacket, Action onDisconnect)
        {
            _onPacket = onPacket ?? throw new ArgumentNullException(nameof(onPacket));
            _onDisconnect = onDisconnect ?? throw new ArgumentNullException(nameof(onDisconnect));
        }

        public bool Connected
        {
            get
            {
                var c = _client;
                return c != null && c.Connected && !_stopped;
            }
        }

        public void Connect(string host, int port, TimeSpan? timeout = null)
        {
            ConnectAsync(host, port, timeout).GetAwaiter().GetResult();
        }

        public async Task ConnectAsync(string host, int port, TimeSpan? timeout = null)
        {
            if (Connected)
                throw new RouterConnectionException("Already connected; call Disconnect() first.");
            var to = timeout ?? TimeSpan.FromSeconds(5);
            var client = new TcpClient();
            try
            {
                var connectTask = client.ConnectAsync(host, port);
                var winner = await Task.WhenAny(connectTask, Task.Delay(to)).ConfigureAwait(false);
                if (winner != connectTask)
                {
                    try { client.Close(); } catch { /* ignore */ }
                    throw new RouterConnectionException(
                        $"Cannot connect to {host}:{port}: timed out after {to.TotalSeconds:F1}s.");
                }
                await connectTask.ConfigureAwait(false); // surface any connect exception
            }
            catch (RouterConnectionException)
            {
                throw;
            }
            catch (Exception exc)
            {
                try { client.Close(); } catch { /* ignore */ }
                throw new RouterConnectionException($"Cannot connect to {host}:{port}: {exc.Message}", exc);
            }

            _client = client;
            _stream = client.GetStream();
            _stopped = false;
            _cts = new CancellationTokenSource();
            _recvTask = Task.Run(() => RecvLoopAsync(_cts.Token));
        }

        public void Disconnect(TimeSpan? joinTimeout = null)
        {
            _stopped = true;
            try { _cts?.Cancel(); } catch { /* ignore */ }
            try { _stream?.Close(); } catch { /* ignore */ }
            try { _client?.Close(); } catch { /* ignore */ }
            _stream = null;
            _client = null;
            var jt = joinTimeout ?? TimeSpan.FromSeconds(2);
            try { _recvTask?.Wait(jt); } catch { /* ignore */ }
            _recvTask = null;
            try { _cts?.Dispose(); } catch { /* ignore */ }
            _cts = null;
        }

        public void SendRaw(byte[] data)
        {
            if (!Connected) throw new RouterConnectionException("Not connected.");
            lock (_sendLock)
            {
                try
                {
                    var s = _stream;
                    if (s == null) throw new RouterConnectionException("Not connected.");
                    s.Write(data, 0, data.Length);
                }
                catch (Exception exc) when (!(exc is RouterConnectionException))
                {
                    _stopped = true;
                    throw new RouterConnectionException($"Send failed: {exc.Message}", exc);
                }
            }
        }

        public void Dispose() => Disconnect();

        // ---------------------------------------------------------------
        // Internal: background receive loop
        // ---------------------------------------------------------------

        private async Task RecvLoopAsync(CancellationToken ct)
        {
            var stream = _stream;
            try
            {
                var headerBuf = new byte[ProtocolCodec.HeaderSize];
                while (!ct.IsCancellationRequested)
                {
                    if (!await ReadExactlyAsync(stream, headerBuf, 0, headerBuf.Length, ct).ConfigureAwait(false))
                        break;

                    uint dataLen = ((uint)headerBuf[0] << 24) | ((uint)headerBuf[1] << 16) | ((uint)headerBuf[2] << 8) | headerBuf[3];
                    byte[] body = dataLen == 0 ? Array.Empty<byte>() : new byte[dataLen];
                    if (dataLen > 0 && !await ReadExactlyAsync(stream, body, 0, body.Length, ct).ConfigureAwait(false))
                        break;

                    Packet packet;
                    try
                    {
                        packet = ProtocolCodec.ParsePacket(headerBuf, body);
                    }
                    catch (ProtocolException)
                    {
                        // Corrupted frame -- skip it and keep the loop alive.
                        continue;
                    }

                    try { _onPacket(packet); } catch { /* swallow callback errors */ }
                }
            }
            catch (IOException) { /* connection dropped */ }
            catch (ObjectDisposedException) { /* socket closed during read */ }
            catch (OperationCanceledException) { /* shutdown */ }
            finally
            {
                if (!_stopped)
                {
                    _stopped = true;
                    try { _onDisconnect(); } catch { /* ignore */ }
                }
            }
        }

        private static async Task<bool> ReadExactlyAsync(Stream stream, byte[] buf, int offset, int count, CancellationToken ct)
        {
            int read = 0;
            while (read < count)
            {
                int n;
                try
                {
                    n = await stream.ReadAsync(buf, offset + read, count - read, ct).ConfigureAwait(false);
                }
                catch (IOException) { return false; }
                catch (ObjectDisposedException) { return false; }
                if (n == 0) return false;
                read += n;
            }
            return true;
        }
    }

    // -----------------------------------------------------------------------
    // High-level client
    // -----------------------------------------------------------------------

    /// <summary>Callback delegate for status / interrupt broadcasts.</summary>
    public delegate void StatusCallback(StatusNotification notification);

    /// <summary>Callback delegate for asynchronous-response packets.</summary>
    public delegate void AsyncResponseCallback(AsyncResponse response);

    /// <summary>
    /// C# client for a CSM-TCP-Router server.  Mirrors the LabVIEW ClientAPI
    /// VIs and the Python <c>TcpRouterClient</c>; speaks protocol v0.
    ///
    /// The class is thread-safe.  At most one in-flight synchronous command
    /// and one in-flight async / subscription command may be outstanding at a
    /// time; concurrent callers are serialised by internal semaphores.
    /// </summary>
    public sealed class TcpRouterClient : IDisposable
    {
        // One-item-deep "queues" for synchronised waits, implemented via TCS.
        // Reset to a fresh TCS by each waiter inside the corresponding lock.
        private TaskCompletionSource<object> _respTcs;
        private TaskCompletionSource<object> _cmdRespTcs;

        private readonly SemaphoreSlim _respLock = new SemaphoreSlim(1, 1);
        private readonly SemaphoreSlim _cmdRespLock = new SemaphoreSlim(1, 1);

        private readonly object _stateLock = new object();
        private readonly Dictionary<(string Status, string Module), StatusCallback> _statusCallbacks
            = new Dictionary<(string, string), StatusCallback>();
        private readonly Dictionary<string, AsyncResponseCallback> _asyncCallbacks
            = new Dictionary<string, AsyncResponseCallback>();

        private readonly Transport _transport;

        /// <summary>Polling queue for async-resp packets received from the server.</summary>
        public ConcurrentQueue<AsyncResponse> AsyncResponseQueue { get; } = new ConcurrentQueue<AsyncResponse>();

        /// <summary>Polling queue for status / interrupt notifications.</summary>
        public ConcurrentQueue<StatusNotification> StatusQueue { get; } = new ConcurrentQueue<StatusNotification>();

        public TcpRouterClient()
        {
            _transport = new Transport(OnPacket, OnDisconnect);
        }

        // ---------------------------------------------------------------
        // Connection management
        // ---------------------------------------------------------------

        /// <summary>Connect to a CSM-TCP-Router server.</summary>
        public void Connect(string host, int port, TimeSpan? timeout = null)
            => _transport.Connect(host, port, timeout);

        /// <summary>Connect to a CSM-TCP-Router server (async).</summary>
        public Task ConnectAsync(string host, int port, TimeSpan? timeout = null)
            => _transport.ConnectAsync(host, port, timeout);

        /// <summary>
        /// Disconnect from the server and release all resources.  Any threads
        /// currently blocked in <see cref="SendAndWait"/> / <see cref="Post"/>
        /// will receive a <see cref="RouterConnectionException"/> immediately
        /// rather than waiting for their timeout to expire.
        /// </summary>
        public void Disconnect()
        {
            // Unblock any pending waiters before tearing down the transport.
            var sentinel = new RouterConnectionException("Disconnected from server.");
            UnblockWaiters(sentinel);
            _transport.Disconnect();
        }

        /// <summary><c>true</c> when the underlying transport is connected.</summary>
        public bool Connected => _transport.Connected;

        /// <summary>
        /// Poll until <paramref name="host"/>:<paramref name="port"/> accepts
        /// a connection or <paramref name="timeout"/> elapses.
        /// </summary>
        public bool WaitForServer(string host, int port, TimeSpan? timeout = null, TimeSpan? retryInterval = null)
            => WaitForServerAsync(host, port, timeout, retryInterval).GetAwaiter().GetResult();

        /// <summary>Async version of <see cref="WaitForServer"/>.</summary>
        public async Task<bool> WaitForServerAsync(
            string host, int port, TimeSpan? timeout = null, TimeSpan? retryInterval = null)
        {
            var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(30));
            var interval = retryInterval ?? TimeSpan.FromMilliseconds(500);
            while (DateTime.UtcNow < deadline)
            {
                using (var probe = new TcpClient())
                {
                    var connectTask = probe.ConnectAsync(host, port);
                    var winner = await Task.WhenAny(connectTask, Task.Delay(TimeSpan.FromSeconds(1))).ConfigureAwait(false);
                    if (winner == connectTask)
                    {
                        try
                        {
                            // Observe any connect exception (faulted task);
                            // success means the server is reachable.
                            await connectTask.ConfigureAwait(false);
                            try { probe.Close(); } catch { /* ignore */ }
                            return true;
                        }
                        catch (SocketException) { /* not ready yet */ }
                        catch (IOException) { /* not ready yet */ }
                    }
                    else
                    {
                        // Delay won; abort the in-flight connect attempt by
                        // closing the probe socket, then observe any pending
                        // exception so it is not unobserved.
                        try { probe.Close(); } catch { /* ignore */ }
                        try
                        {
                            await connectTask.ConfigureAwait(false);
                        }
                        catch (SocketException) { /* not ready yet */ }
                        catch (IOException) { /* not ready yet */ }
                        catch (ObjectDisposedException) { /* connect aborted by closing probe */ }
                    }
                }
                await Task.Delay(interval).ConfigureAwait(false);
            }
            return false;
        }

        // ---------------------------------------------------------------
        // Core command methods (sync wrappers)
        // ---------------------------------------------------------------

        public CommandResponse SendAndWait(string command, TimeSpan? timeout = null)
            => SendAndWaitAsync(command, timeout).GetAwaiter().GetResult();

        public void Post(string command, TimeSpan? timeout = null)
            => PostAsync(command, timeout).GetAwaiter().GetResult();

        public void PostNoReply(string command, TimeSpan? timeout = null)
            => PostNoReplyAsync(command, timeout).GetAwaiter().GetResult();

        public (bool Ok, TimeSpan Elapsed) Ping(TimeSpan? timeout = null)
            => PingAsync(timeout).GetAwaiter().GetResult();

        public string ListModules(TimeSpan? timeout = null) => SendAndWait("List", timeout).Text;
        public string ListApi(string module, TimeSpan? timeout = null) => SendAndWait($"List API {module}", timeout).Text;
        public string ListStates(string module, TimeSpan? timeout = null) => SendAndWait($"List State {module}", timeout).Text;
        public string Help(string module, TimeSpan? timeout = null) => SendAndWait($"Help {module}", timeout).Text;

        public void SubscribeStatus(string statusName, string moduleName, StatusCallback callback = null, TimeSpan? timeout = null)
            => SubscribeStatusAsync(statusName, moduleName, callback, timeout).GetAwaiter().GetResult();

        public void UnsubscribeStatus(string statusName, string moduleName, TimeSpan? timeout = null)
            => UnsubscribeStatusAsync(statusName, moduleName, timeout).GetAwaiter().GetResult();

        // ---------------------------------------------------------------
        // Core command methods (async)
        // ---------------------------------------------------------------

        /// <summary>
        /// Send a synchronous command (suffix <c>-@</c>) and wait for the response.
        /// </summary>
        public async Task<CommandResponse> SendAndWaitAsync(string command, TimeSpan? timeout = null)
        {
            if (command == null) throw new ArgumentNullException(nameof(command));
            var to = timeout ?? TimeSpan.FromSeconds(5);
            byte[] wire = ProtocolCodec.EncodePacket(Encoding.UTF8.GetBytes(command), PacketType.Cmd);

            await _respLock.WaitAsync().ConfigureAwait(false);
            try
            {
                _respTcs = new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously);
                _transport.SendRaw(wire);
                return await WaitForRespAsync(to).ConfigureAwait(false);
            }
            finally
            {
                _respLock.Release();
            }
        }

        /// <summary>
        /// Send an async command (suffix <c>-&gt;</c>) and wait for the cmd-resp handshake.
        /// </summary>
        public Task PostAsync(string command, TimeSpan? timeout = null)
            => SendAndAwaitCmdRespAsync(command, timeout);

        /// <summary>
        /// Send an async no-reply command (suffix <c>-&gt;|</c>) and wait for the cmd-resp handshake.
        /// </summary>
        public Task PostNoReplyAsync(string command, TimeSpan? timeout = null)
            => SendAndAwaitCmdRespAsync(command, timeout);

        /// <summary>Send a Ping and measure round-trip latency.</summary>
        public async Task<(bool Ok, TimeSpan Elapsed)> PingAsync(TimeSpan? timeout = null)
        {
            var to = timeout ?? TimeSpan.FromSeconds(2);
            try
            {
                var sw = Stopwatch.StartNew();
                await SendAndWaitAsync("Ping", to).ConfigureAwait(false);
                sw.Stop();
                return (true, sw.Elapsed);
            }
            catch (RouterConnectionException) { return (false, TimeSpan.Zero); }
            catch (RouterTimeoutException) { return (false, TimeSpan.Zero); }
            catch (ServerException) { return (false, TimeSpan.Zero); }
        }

        public Task<string> ListModulesAsync(TimeSpan? timeout = null)
            => SendAndWaitAsync("List", timeout).ContinueWithText();

        public Task<string> ListApiAsync(string module, TimeSpan? timeout = null)
            => SendAndWaitAsync($"List API {module}", timeout).ContinueWithText();

        public Task<string> ListStatesAsync(string module, TimeSpan? timeout = null)
            => SendAndWaitAsync($"List State {module}", timeout).ContinueWithText();

        public Task<string> HelpAsync(string module, TimeSpan? timeout = null)
            => SendAndWaitAsync($"Help {module}", timeout).ContinueWithText();

        /// <summary>Subscribe to a CSM module's status broadcast.</summary>
        public async Task SubscribeStatusAsync(
            string statusName, string moduleName, StatusCallback callback = null, TimeSpan? timeout = null)
        {
            if (statusName == null) throw new ArgumentNullException(nameof(statusName));
            if (moduleName == null) throw new ArgumentNullException(nameof(moduleName));

            var key = (statusName, moduleName);
            // Register the callback *before* sending to eliminate the race
            // where a STATUS packet could arrive before the callback is stored.
            lock (_stateLock) { _statusCallbacks[key] = callback; }

            string cmd = $"{statusName}@{moduleName} -><register>";
            try
            {
                await SendAndAwaitCmdRespAsync(cmd, timeout).ConfigureAwait(false);
            }
            catch
            {
                lock (_stateLock) { _statusCallbacks.Remove(key); }
                throw;
            }
        }

        /// <summary>Cancel a status subscription.</summary>
        public async Task UnsubscribeStatusAsync(string statusName, string moduleName, TimeSpan? timeout = null)
        {
            if (statusName == null) throw new ArgumentNullException(nameof(statusName));
            if (moduleName == null) throw new ArgumentNullException(nameof(moduleName));

            string cmd = $"{statusName}@{moduleName} -><unregister>";
            await SendAndAwaitCmdRespAsync(cmd, timeout).ConfigureAwait(false);
            lock (_stateLock) { _statusCallbacks.Remove((statusName, moduleName)); }
        }

        /// <summary>
        /// Register a callback for async-resp packets, matched by the original
        /// command echoed in the async-resp payload (after the <c> &lt;- </c> separator).
        /// </summary>
        public void RegisterAsyncCallback(string originalCommand, AsyncResponseCallback callback)
        {
            if (originalCommand == null) throw new ArgumentNullException(nameof(originalCommand));
            if (callback == null) throw new ArgumentNullException(nameof(callback));
            lock (_stateLock) { _asyncCallbacks[originalCommand] = callback; }
        }

        /// <summary>Remove a previously registered async callback.</summary>
        public void UnregisterAsyncCallback(string originalCommand)
        {
            if (originalCommand == null) return;
            lock (_stateLock) { _asyncCallbacks.Remove(originalCommand); }
        }

        // ---------------------------------------------------------------
        // IDisposable
        // ---------------------------------------------------------------

        public void Dispose()
        {
            Disconnect();
            _respLock.Dispose();
            _cmdRespLock.Dispose();
        }

        // ---------------------------------------------------------------
        // Internal helpers
        // ---------------------------------------------------------------

        private async Task SendAndAwaitCmdRespAsync(string command, TimeSpan? timeout)
        {
            if (command == null) throw new ArgumentNullException(nameof(command));
            var to = timeout ?? TimeSpan.FromSeconds(5);
            byte[] wire = ProtocolCodec.EncodePacket(Encoding.UTF8.GetBytes(command), PacketType.Cmd);

            await _cmdRespLock.WaitAsync().ConfigureAwait(false);
            try
            {
                _cmdRespTcs = new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously);
                _transport.SendRaw(wire);
                await WaitForCmdRespAsync(to).ConfigureAwait(false);
            }
            finally
            {
                _cmdRespLock.Release();
            }
        }

        private async Task<CommandResponse> WaitForRespAsync(TimeSpan timeout)
        {
            var tcs = _respTcs;
            var winner = await Task.WhenAny(tcs.Task, Task.Delay(timeout)).ConfigureAwait(false);
            if (winner != tcs.Task)
            {
                // Protocol v0 has no correlation id, so a late RESP for the
                // timed-out command could be misattributed to the *next*
                // SendAndWait call.  Force a disconnect so the connection
                // is unusable until the caller reconnects.
                try { _transport.Disconnect(); } catch { /* ignore */ }
                throw new RouterTimeoutException($"No response received within {timeout.TotalSeconds:F1}s.");
            }
            object item = await tcs.Task.ConfigureAwait(false);
            if (item is Exception exc) throw exc;
            var packet = (Packet)item;
            return new CommandResponse(packet.Data);
        }

        private async Task WaitForCmdRespAsync(TimeSpan timeout)
        {
            var tcs = _cmdRespTcs;
            var winner = await Task.WhenAny(tcs.Task, Task.Delay(timeout)).ConfigureAwait(false);
            if (winner != tcs.Task)
            {
                // Same desync risk as WaitForRespAsync: a late CMD_RESP could
                // complete the next in-flight waiter.  Force a disconnect so
                // the connection cannot be reused after a handshake timeout.
                try { _transport.Disconnect(); } catch { /* ignore */ }
                throw new RouterTimeoutException($"No cmd-resp received within {timeout.TotalSeconds:F1}s.");
            }
            object item = await tcs.Task.ConfigureAwait(false);
            if (item is Exception exc) throw exc;
            // CMD_RESP payload is a handshake acknowledgment; discard it.
        }

        private void UnblockWaiters(Exception sentinel)
        {
            _respTcs?.TrySetResult(sentinel);
            _cmdRespTcs?.TrySetResult(sentinel);
        }

        // ---------------------------------------------------------------
        // Internal: packet dispatch (runs on the receive task thread)
        // ---------------------------------------------------------------

        internal void OnPacket(Packet packet)
        {
            switch (packet.Type)
            {
                case PacketType.Resp:
                    _respTcs?.TrySetResult(packet);
                    break;

                case PacketType.CmdResp:
                    _cmdRespTcs?.TrySetResult(packet);
                    break;

                case PacketType.AsyncResp:
                {
                    var resp = AsyncResponse.FromPacket(packet);
                    AsyncResponseQueue.Enqueue(resp);
                    AsyncResponseCallback cb;
                    lock (_stateLock) { _asyncCallbacks.TryGetValue(resp.OriginalCommand, out cb); }
                    if (cb != null)
                    {
                        try { cb(resp); } catch { /* swallow callback errors */ }
                    }
                    break;
                }

                case PacketType.Status:
                case PacketType.Interrupt:
                {
                    var notif = StatusNotification.FromPacket(packet);
                    StatusQueue.Enqueue(notif);
                    StatusCallback cb;
                    lock (_stateLock) { _statusCallbacks.TryGetValue((notif.StatusName, notif.ModuleName), out cb); }
                    if (cb != null)
                    {
                        try { cb(notif); } catch { /* swallow callback errors */ }
                    }
                    break;
                }

                case PacketType.Error:
                {
                    var err = ProtocolCodec.ParseServerError(packet);
                    _respTcs?.TrySetResult(err);
                    _cmdRespTcs?.TrySetResult(err);
                    break;
                }

                case PacketType.Info:
                    // Silently discarded (welcome / goodbye messages).
                    break;

                case PacketType.Cmd:
                    // Server should never send CMD; ignore for forward compatibility.
                    break;
            }
        }

        internal void OnDisconnect()
        {
            UnblockWaiters(new RouterConnectionException("Connection lost unexpectedly."));
        }
    }

    // -----------------------------------------------------------------------
    // Small convenience extensions
    // -----------------------------------------------------------------------

    internal static class TaskExtensions
    {
        public static async Task<string> ContinueWithText(this Task<CommandResponse> task)
        {
            var resp = await task.ConfigureAwait(false);
            return resp.Text;
        }
    }
}
