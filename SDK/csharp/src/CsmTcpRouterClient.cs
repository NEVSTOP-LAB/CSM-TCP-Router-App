// -----------------------------------------------------------------------------
//  CsmTcpRouterClient – single-file C# client SDK for the CSM-TCP-Router server.
//
//  This file bundles the entire client implementation (sync + async API) along
//  with the wire-protocol codec, exception hierarchy and public data models
//  into a single compilable .cs file.
//
//  Sync usage:
//
//      using CsmTcpRouter;
//
//      using var client = new TcpRouterClient();
//      client.Connect("localhost", 30007);
//      Console.WriteLine(client.ListModules());
//
//  Async usage:
//
//      using CsmTcpRouter;
//
//      await using var client = new TcpRouterClient();
//      await client.ConnectAsync("localhost", 30007);
//      Console.WriteLine(await client.ListModulesAsync());
//
//  Wire format (8-byte header, big-endian):
//
//      | Data Length (4B) | Version (1B = 0x01) | Type (1B) | FLAG1 (1B) | FLAG2 (1B) |
//      ╰────────────────────────────── Header (8B) ─────────────────────────────────╯
//
//  followed by exactly Data Length bytes of payload.
//
//  Project: https://github.com/NEVSTOP-LAB/CSM-TCP-Router-App
//  License: MIT
// -----------------------------------------------------------------------------

using System;
using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace CsmTcpRouter
{
    // =====================================================================
    // Protocol constants & packet types
    // =====================================================================

    /// <summary>
    /// Packet type constants as defined in the CSM-TCP-Router protocol v0.
    /// </summary>
    public enum PacketType : byte
    {
        /// <summary>Informational messages (welcome / goodbye).</summary>
        Info = 0x00,
        /// <summary>Error messages from the server.</summary>
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

    /// <summary>Protocol-level constants.</summary>
    public static class Protocol
    {
        /// <summary>Number of bytes in the fixed packet header.</summary>
        public const int HeaderSize = 8;

        /// <summary>Protocol version byte sent in every outgoing packet.</summary>
        public const byte Version = 0x01;

        /// <summary>SDK semantic version.</summary>
        public const string SdkVersion = "0.1.0";

        /// <summary>
        /// Encode <paramref name="data"/> into a complete wire-format packet (header + body).
        /// </summary>
        public static byte[] EncodePacket(byte[] data, PacketType type, byte flag1 = 0, byte flag2 = 0)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            var wire = new byte[HeaderSize + data.Length];
            BinaryPrimitives.WriteUInt32BigEndian(wire.AsSpan(0, 4), (uint)data.Length);
            wire[4] = Version;
            wire[5] = (byte)type;
            wire[6] = flag1;
            wire[7] = flag2;
            Buffer.BlockCopy(data, 0, wire, HeaderSize, data.Length);
            return wire;
        }

        /// <summary>
        /// Decode an 8-byte header into its constituent fields.
        /// </summary>
        /// <exception cref="ProtocolError">If <paramref name="header"/> is not exactly 8 bytes.</exception>
        public static (uint dataLen, byte version, byte type, byte flag1, byte flag2) DecodeHeader(byte[] header)
        {
            if (header == null || header.Length != HeaderSize)
            {
                int len = header?.Length ?? 0;
                throw new ProtocolError($"Expected {HeaderSize}-byte header, got {len} bytes.");
            }
            uint dataLen = BinaryPrimitives.ReadUInt32BigEndian(header.AsSpan(0, 4));
            return (dataLen, header[4], header[5], header[6], header[7]);
        }

        /// <summary>Build a <see cref="Packet"/> from raw header + body.</summary>
        /// <exception cref="ProtocolError">on header size mismatch or body length mismatch.</exception>
        public static Packet ParsePacket(byte[] header, byte[] body)
        {
            var (dataLen, version, typeByte, flag1, flag2) = DecodeHeader(header);
            int bodyLen = body?.Length ?? 0;
            if (bodyLen != dataLen)
            {
                throw new ProtocolError(
                    $"Payload length mismatch: header says {dataLen} bytes, got {bodyLen} bytes.");
            }
            // Forward-compatible: unknown type byte → Info
            PacketType ptype = Enum.IsDefined(typeof(PacketType), typeByte)
                ? (PacketType)typeByte
                : PacketType.Info;
            return new Packet(ptype, body ?? Array.Empty<byte>(), version, flag1, flag2);
        }
    }

    // =====================================================================
    // Exceptions
    // =====================================================================

    /// <summary>Base exception for all CSM-TCP-Router client errors.</summary>
    public class TcpRouterError : Exception
    {
        public TcpRouterError() { }
        public TcpRouterError(string message) : base(message) { }
        public TcpRouterError(string message, Exception inner) : base(message, inner) { }
    }

    /// <summary>Raised when a connection cannot be established or is lost.</summary>
    public class RouterConnectionError : TcpRouterError
    {
        public RouterConnectionError(string message) : base(message) { }
        public RouterConnectionError(string message, Exception inner) : base(message, inner) { }
    }

    /// <summary>Raised when an operation exceeds its timeout.</summary>
    public class RouterTimeoutError : TcpRouterError
    {
        public RouterTimeoutError(string message) : base(message) { }
    }

    /// <summary>Raised when an invalid or unexpected protocol frame is received.</summary>
    public class ProtocolError : TcpRouterError
    {
        public ProtocolError(string message) : base(message) { }
    }

    /// <summary>Raised when the server returns an error packet.</summary>
    public class ServerError : TcpRouterError
    {
        /// <summary>Optional error code extracted from the CSM Error format <c>[Error: code] message</c>.</summary>
        public string Code { get; }

        public new string Message { get; }

        public ServerError(string message, string code = "") : base(message)
        {
            Message = message;
            Code = code ?? string.Empty;
        }

        public override string ToString()
            => string.IsNullOrEmpty(Code) ? Message : $"[Error: {Code}] {Message}";
    }

    // =====================================================================
    // Public data models
    // =====================================================================

    /// <summary>A decoded packet received from the server.</summary>
    public sealed class Packet
    {
        public PacketType Type { get; }
        public byte[] Data { get; }
        public byte Version { get; }
        public byte Flag1 { get; }
        public byte Flag2 { get; }

        public Packet(PacketType type, byte[] data, byte version = Protocol.Version, byte flag1 = 0, byte flag2 = 0)
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

        /// <summary>Decoded UTF-8 text of the response payload.</summary>
        public string Text => Encoding.UTF8.GetString(Raw);

        public CommandResponse(byte[] raw) { Raw = raw ?? Array.Empty<byte>(); }

        public override string ToString() => $"CommandResponse({Text})";
    }

    /// <summary>An asynchronous response payload delivered via an <c>async-resp</c> packet.</summary>
    public sealed class AsyncResponse
    {
        /// <summary>Raw response bytes (the part before the <c> &lt;- </c> separator).</summary>
        public byte[] Raw { get; }

        /// <summary>Original command text echoed back by the server (the part after the <c> &lt;- </c> separator).</summary>
        public string OriginalCommand { get; }

        /// <summary>Decoded UTF-8 text of the response payload.</summary>
        public string Text => Encoding.UTF8.GetString(Raw);

        public AsyncResponse(byte[] raw, string originalCommand = "")
        {
            Raw = raw ?? Array.Empty<byte>();
            OriginalCommand = originalCommand ?? string.Empty;
        }

        /// <summary>Parse an <see cref="PacketType.AsyncResp"/> packet.</summary>
        public static AsyncResponse FromPacket(Packet packet)
        {
            string text = Encoding.UTF8.GetString(packet.Data);
            int idx = text.IndexOf(" <- ", StringComparison.Ordinal);
            if (idx >= 0)
            {
                string left = text.Substring(0, idx);
                string right = text.Substring(idx + 4);
                return new AsyncResponse(Encoding.UTF8.GetBytes(left), right);
            }
            return new AsyncResponse(packet.Data);
        }

        public override string ToString() => $"AsyncResponse({Text}, cmd={OriginalCommand})";
    }

    /// <summary>A status broadcast delivered via a <c>STATUS</c> or <c>INTERRUPT</c> packet.</summary>
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

        /// <summary>Parse a <see cref="PacketType.Status"/> or <see cref="PacketType.Interrupt"/> packet.</summary>
        public static StatusNotification FromPacket(Packet packet)
        {
            string text = Encoding.UTF8.GetString(packet.Data);
            string module = string.Empty;
            string left = text;
            int sepIdx = text.LastIndexOf(" <- ", StringComparison.Ordinal);
            if (sepIdx >= 0)
            {
                left = text.Substring(0, sepIdx);
                module = text.Substring(sepIdx + 4).Trim();
            }
            string statusName = string.Empty;
            string data = left.Trim();
            int gtIdx = left.IndexOf(" >> ", StringComparison.Ordinal);
            if (gtIdx >= 0)
            {
                statusName = left.Substring(0, gtIdx).Trim();
                data = left.Substring(gtIdx + 4).Trim();
            }
            return new StatusNotification(packet.Data, packet.Type, statusName, data, module);
        }

        public override string ToString()
            => $"StatusNotification(status={StatusName}, data={Data}, module={ModuleName})";
    }

    // =====================================================================
    // Internal helper: parse a server ERROR packet into a ServerError
    // =====================================================================

    internal static class ServerErrorParser
    {
        /// <summary>Extract code and message from a CSM Error format <c>[Error: code] msg</c>.</summary>
        public static ServerError Parse(Packet packet)
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
            return new ServerError(msg, code);
        }
    }

    // =====================================================================
    // Subscription key helper (status_name + module_name)
    // =====================================================================

    internal readonly struct SubKey : IEquatable<SubKey>
    {
        public string StatusName { get; }
        public string ModuleName { get; }

        public SubKey(string statusName, string moduleName)
        {
            StatusName = statusName ?? string.Empty;
            ModuleName = moduleName ?? string.Empty;
        }

        public bool Equals(SubKey other)
            => StatusName == other.StatusName && ModuleName == other.ModuleName;

        public override bool Equals(object? obj) => obj is SubKey o && Equals(o);

        public override int GetHashCode()
            => unchecked(StringComparer.Ordinal.GetHashCode(StatusName) * 397
                ^ StringComparer.Ordinal.GetHashCode(ModuleName));
    }

    // =====================================================================
    // TcpRouterClient – combined sync + async client
    // =====================================================================

    /// <summary>
    /// .NET client for a CSM-TCP-Router server.  Provides both synchronous
    /// (blocking) and asynchronous (Task-based) APIs over the same underlying
    /// TCP connection.  The class is thread-safe; internal serialisation
    /// locks ensure at most one in-flight <c>RESP</c> waiter and one
    /// in-flight <c>CMD_RESP</c> waiter at a time.
    /// </summary>
    public sealed class TcpRouterClient : IDisposable, IAsyncDisposable
    {
        // Receive-thread / Task synchronization
        private TcpClient? _tcpClient;
        private NetworkStream? _stream;
        private CancellationTokenSource? _recvCts;
        private Task? _recvTask;
        private readonly object _stateLock = new object();
        private volatile bool _isConnected;

        // One-item-deep async queues for synchronised waits.
        // Items are either Packet or Exception.
        private readonly BlockingCollection<object> _respQueue = new BlockingCollection<object>();
        private readonly BlockingCollection<object> _cmdRespQueue = new BlockingCollection<object>();

        /// <summary>Polling queue for <see cref="AsyncResponse"/> objects received from the server.</summary>
        public BlockingCollection<AsyncResponse> AsyncResponseQueue { get; } = new BlockingCollection<AsyncResponse>();

        /// <summary>Polling queue for <see cref="StatusNotification"/> objects received from the server.</summary>
        public BlockingCollection<StatusNotification> StatusQueue { get; } = new BlockingCollection<StatusNotification>();

        // Callback registries (protected by _cbLock)
        private readonly Dictionary<SubKey, Action<StatusNotification>?> _statusCallbacks =
            new Dictionary<SubKey, Action<StatusNotification>?>();
        private readonly Dictionary<string, Action<AsyncResponse>> _asyncCallbacks =
            new Dictionary<string, Action<AsyncResponse>>();
        private readonly object _cbLock = new object();

        // Serialisation locks for in-flight RESP / CMD_RESP waiters.
        private readonly SemaphoreSlim _respLock = new SemaphoreSlim(1, 1);
        private readonly SemaphoreSlim _cmdRespLock = new SemaphoreSlim(1, 1);
        // Send lock
        private readonly SemaphoreSlim _sendLock = new SemaphoreSlim(1, 1);

        /// <summary><c>true</c> if the underlying transport is currently connected.</summary>
        public bool Connected => _isConnected;

        // ---------------------------------------------------------------
        // Connection management
        // ---------------------------------------------------------------

        /// <summary>Connect to a CSM-TCP-Router server (synchronous).</summary>
        public void Connect(string host, int port, double timeoutSeconds = 5.0)
        {
            ConnectAsync(host, port, timeoutSeconds).GetAwaiter().GetResult();
        }

        /// <summary>Connect to a CSM-TCP-Router server (async).</summary>
        public async Task ConnectAsync(string host, int port, double timeoutSeconds = 5.0,
            CancellationToken cancellationToken = default)
        {
            if (_isConnected)
                throw new RouterConnectionError("Already connected; call Disconnect() first.");

            var client = new TcpClient();
            try
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                cts.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));
                try
                {
#if NET6_0_OR_GREATER
                    await client.ConnectAsync(host, port, cts.Token).ConfigureAwait(false);
#else
                    var connectTask = client.ConnectAsync(host, port);
                    using (cts.Token.Register(() => { try { client.Close(); } catch { } }))
                    {
                        await connectTask.ConfigureAwait(false);
                    }
#endif
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    throw new RouterConnectionError(
                        $"Connection to {host}:{port} timed out after {timeoutSeconds:F1}s.");
                }
            }
            catch (RouterConnectionError)
            {
                client.Dispose();
                throw;
            }
            catch (Exception ex)
            {
                client.Dispose();
                throw new RouterConnectionError($"Cannot connect to {host}:{port}: {ex.Message}", ex);
            }

            lock (_stateLock)
            {
                _tcpClient = client;
                _stream = client.GetStream();
                _recvCts = new CancellationTokenSource();
                _isConnected = true;
                _recvTask = Task.Run(() => RecvLoopAsync(_stream, _recvCts.Token));
            }
        }

        /// <summary>Disconnect from the server and release all resources (synchronous).</summary>
        public void Disconnect()
        {
            DisconnectAsync().GetAwaiter().GetResult();
        }

        /// <summary>Disconnect from the server and release all resources (async).</summary>
        public async Task DisconnectAsync()
        {
            // Wake any blocked waiters first.
            var sentinel = new RouterConnectionError("Disconnected from server.");
            try { _respQueue.TryAdd(sentinel); } catch { }
            try { _cmdRespQueue.TryAdd(sentinel); } catch { }

            CancellationTokenSource? cts;
            Task? recvTask;
            TcpClient? client;
            NetworkStream? stream;
            lock (_stateLock)
            {
                cts = _recvCts;
                recvTask = _recvTask;
                client = _tcpClient;
                stream = _stream;
                _isConnected = false;
                _recvCts = null;
                _recvTask = null;
                _tcpClient = null;
                _stream = null;
            }

            try { cts?.Cancel(); } catch { }
            try { stream?.Close(); } catch { }
            try { client?.Close(); } catch { }

            if (recvTask != null)
            {
                try
                {
                    await Task.WhenAny(recvTask, Task.Delay(2000)).ConfigureAwait(false);
                }
                catch { }
            }

            cts?.Dispose();
            stream?.Dispose();
            client?.Dispose();
        }

        /// <summary>
        /// Poll until <paramref name="host"/>:<paramref name="port"/> accepts a connection
        /// or <paramref name="timeoutSeconds"/> elapses. Synchronous.
        /// </summary>
        public bool WaitForServer(string host, int port,
            double timeoutSeconds = 30.0, double retryIntervalSeconds = 0.5)
        {
            return WaitForServerAsync(host, port, timeoutSeconds, retryIntervalSeconds)
                .GetAwaiter().GetResult();
        }

        /// <summary>
        /// Poll until <paramref name="host"/>:<paramref name="port"/> accepts a connection
        /// or <paramref name="timeoutSeconds"/> elapses. Async.
        /// </summary>
        public async Task<bool> WaitForServerAsync(string host, int port,
            double timeoutSeconds = 30.0, double retryIntervalSeconds = 0.5,
            CancellationToken cancellationToken = default)
        {
            var deadline = DateTime.UtcNow.AddSeconds(timeoutSeconds);
            while (DateTime.UtcNow < deadline)
            {
                cancellationToken.ThrowIfCancellationRequested();
                using var probe = new TcpClient();
                try
                {
                    using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    cts.CancelAfter(TimeSpan.FromSeconds(1.0));
#if NET6_0_OR_GREATER
                    await probe.ConnectAsync(host, port, cts.Token).ConfigureAwait(false);
#else
                    var t = probe.ConnectAsync(host, port);
                    using (cts.Token.Register(() => { try { probe.Close(); } catch { } }))
                    {
                        await t.ConfigureAwait(false);
                    }
#endif
                    return true;
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
                catch { /* fall through to retry */ }

                await Task.Delay(TimeSpan.FromSeconds(retryIntervalSeconds), cancellationToken)
                    .ConfigureAwait(false);
            }
            return false;
        }

        // ---------------------------------------------------------------
        // Core command methods (sync)
        // ---------------------------------------------------------------

        /// <summary>Send a synchronous command and block until the response arrives.</summary>
        public CommandResponse SendAndWait(string command, double timeoutSeconds = 5.0)
            => SendAndWaitAsync(command, timeoutSeconds).GetAwaiter().GetResult();

        /// <summary>Send an asynchronous command and wait for the <c>cmd-resp</c> handshake.</summary>
        public void Post(string command, double timeoutSeconds = 5.0)
            => PostAsync(command, timeoutSeconds).GetAwaiter().GetResult();

        /// <summary>Send an async no-reply command and wait for the <c>cmd-resp</c> handshake.</summary>
        public void PostNoReply(string command, double timeoutSeconds = 5.0)
            => PostNoReplyAsync(command, timeoutSeconds).GetAwaiter().GetResult();

        /// <summary>Send a <c>Ping</c> command and measure round-trip latency.</summary>
        /// <returns>(true, elapsedSeconds) on success, (false, 0.0) on failure or error.</returns>
        public (bool Ok, double Elapsed) Ping(double timeoutSeconds = 2.0)
        {
            var t = PingAsync(timeoutSeconds).GetAwaiter().GetResult();
            return t;
        }

        // ---------------------------------------------------------------
        // Core command methods (async)
        // ---------------------------------------------------------------

        /// <summary>Send a synchronous command and await the response.</summary>
        public async Task<CommandResponse> SendAndWaitAsync(string command, double timeoutSeconds = 5.0,
            CancellationToken cancellationToken = default)
        {
            var wire = Protocol.EncodePacket(Encoding.UTF8.GetBytes(command), PacketType.Cmd);
            await _respLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await SendRawAsync(wire, cancellationToken).ConfigureAwait(false);
                return await WaitForRespAsync(timeoutSeconds, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                _respLock.Release();
            }
        }

        /// <summary>Send an asynchronous command and await the <c>cmd-resp</c> handshake.</summary>
        public async Task PostAsync(string command, double timeoutSeconds = 5.0,
            CancellationToken cancellationToken = default)
        {
            var wire = Protocol.EncodePacket(Encoding.UTF8.GetBytes(command), PacketType.Cmd);
            await _cmdRespLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await SendRawAsync(wire, cancellationToken).ConfigureAwait(false);
                await WaitForCmdRespAsync(timeoutSeconds, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                _cmdRespLock.Release();
            }
        }

        /// <summary>Send an async no-reply command and await the <c>cmd-resp</c> handshake.</summary>
        public Task PostNoReplyAsync(string command, double timeoutSeconds = 5.0,
            CancellationToken cancellationToken = default)
            => PostAsync(command, timeoutSeconds, cancellationToken);

        /// <summary>Send a <c>Ping</c> command and measure round-trip latency.</summary>
        public async Task<(bool Ok, double Elapsed)> PingAsync(double timeoutSeconds = 2.0,
            CancellationToken cancellationToken = default)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                await SendAndWaitAsync("Ping", timeoutSeconds, cancellationToken).ConfigureAwait(false);
                sw.Stop();
                return (true, sw.Elapsed.TotalSeconds);
            }
            catch (RouterConnectionError) { return (false, 0.0); }
            catch (RouterTimeoutError) { return (false, 0.0); }
            catch (ServerError) { return (false, 0.0); }
        }

        // ---------------------------------------------------------------
        // Router management helpers (sync + async)
        // ---------------------------------------------------------------

        public string ListModules(double timeoutSeconds = 5.0)
            => SendAndWait("List", timeoutSeconds).Text;

        public async Task<string> ListModulesAsync(double timeoutSeconds = 5.0,
            CancellationToken cancellationToken = default)
            => (await SendAndWaitAsync("List", timeoutSeconds, cancellationToken).ConfigureAwait(false)).Text;

        public string ListApi(string module, double timeoutSeconds = 5.0)
            => SendAndWait($"List API {module}", timeoutSeconds).Text;

        public async Task<string> ListApiAsync(string module, double timeoutSeconds = 5.0,
            CancellationToken cancellationToken = default)
            => (await SendAndWaitAsync($"List API {module}", timeoutSeconds, cancellationToken)
                .ConfigureAwait(false)).Text;

        public string ListStates(string module, double timeoutSeconds = 5.0)
            => SendAndWait($"List State {module}", timeoutSeconds).Text;

        public async Task<string> ListStatesAsync(string module, double timeoutSeconds = 5.0,
            CancellationToken cancellationToken = default)
            => (await SendAndWaitAsync($"List State {module}", timeoutSeconds, cancellationToken)
                .ConfigureAwait(false)).Text;

        public string Help(string module, double timeoutSeconds = 5.0)
            => SendAndWait($"Help {module}", timeoutSeconds).Text;

        public async Task<string> HelpAsync(string module, double timeoutSeconds = 5.0,
            CancellationToken cancellationToken = default)
            => (await SendAndWaitAsync($"Help {module}", timeoutSeconds, cancellationToken)
                .ConfigureAwait(false)).Text;

        // ---------------------------------------------------------------
        // Status / interrupt subscriptions
        // ---------------------------------------------------------------

        /// <summary>Subscribe to a CSM module's status broadcast (synchronous).</summary>
        public void SubscribeStatus(string statusName, string moduleName,
            Action<StatusNotification>? callback = null, double timeoutSeconds = 5.0)
            => SubscribeStatusAsync(statusName, moduleName, callback, timeoutSeconds)
                .GetAwaiter().GetResult();

        /// <summary>Subscribe to a CSM module's status broadcast (async).</summary>
        public async Task SubscribeStatusAsync(string statusName, string moduleName,
            Action<StatusNotification>? callback = null, double timeoutSeconds = 5.0,
            CancellationToken cancellationToken = default)
        {
            var key = new SubKey(statusName, moduleName);
            // Register *before* sending to eliminate the race where a STATUS
            // packet could arrive before the callback is stored.
            lock (_cbLock) { _statusCallbacks[key] = callback; }
            string cmd = $"{statusName}@{moduleName} -><register>";
            var wire = Protocol.EncodePacket(Encoding.UTF8.GetBytes(cmd), PacketType.Cmd);
            try
            {
                await _cmdRespLock.WaitAsync(cancellationToken).ConfigureAwait(false);
                try
                {
                    await SendRawAsync(wire, cancellationToken).ConfigureAwait(false);
                    await WaitForCmdRespAsync(timeoutSeconds, cancellationToken).ConfigureAwait(false);
                }
                finally
                {
                    _cmdRespLock.Release();
                }
            }
            catch
            {
                lock (_cbLock) { _statusCallbacks.Remove(key); }
                throw;
            }
        }

        /// <summary>Cancel a status subscription (sync).</summary>
        public void UnsubscribeStatus(string statusName, string moduleName, double timeoutSeconds = 5.0)
            => UnsubscribeStatusAsync(statusName, moduleName, timeoutSeconds).GetAwaiter().GetResult();

        /// <summary>Cancel a status subscription (async).</summary>
        public async Task UnsubscribeStatusAsync(string statusName, string moduleName,
            double timeoutSeconds = 5.0, CancellationToken cancellationToken = default)
        {
            string cmd = $"{statusName}@{moduleName} -><unregister>";
            var wire = Protocol.EncodePacket(Encoding.UTF8.GetBytes(cmd), PacketType.Cmd);
            await _cmdRespLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await SendRawAsync(wire, cancellationToken).ConfigureAwait(false);
                await WaitForCmdRespAsync(timeoutSeconds, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                _cmdRespLock.Release();
            }
            lock (_cbLock) { _statusCallbacks.Remove(new SubKey(statusName, moduleName)); }
        }

        /// <summary>Register a callback for <c>async-resp</c> packets.</summary>
        public void RegisterAsyncCallback(string originalCommand, Action<AsyncResponse> callback)
        {
            if (callback == null) throw new ArgumentNullException(nameof(callback));
            lock (_cbLock) { _asyncCallbacks[originalCommand] = callback; }
        }

        /// <summary>Remove a previously registered async callback.</summary>
        public void UnregisterAsyncCallback(string originalCommand)
        {
            lock (_cbLock) { _asyncCallbacks.Remove(originalCommand); }
        }

        // ---------------------------------------------------------------
        // Internal: send raw bytes
        // ---------------------------------------------------------------

        private async Task SendRawAsync(byte[] data, CancellationToken cancellationToken)
        {
            NetworkStream? stream;
            lock (_stateLock) { stream = _stream; }
            if (!_isConnected || stream == null)
                throw new RouterConnectionError("Not connected.");

            await _sendLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await stream.WriteAsync(data, 0, data.Length, cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (!(ex is OperationCanceledException))
            {
                _isConnected = false;
                throw new RouterConnectionError($"Send failed: {ex.Message}", ex);
            }
            finally
            {
                _sendLock.Release();
            }
        }

        // ---------------------------------------------------------------
        // Internal: receive loop
        // ---------------------------------------------------------------

        private async Task RecvLoopAsync(NetworkStream stream, CancellationToken token)
        {
            byte[] header = new byte[Protocol.HeaderSize];
            try
            {
                while (!token.IsCancellationRequested)
                {
                    if (!await ReadExactlyAsync(stream, header, 0, Protocol.HeaderSize, token).ConfigureAwait(false))
                        break;

                    uint dataLen = BinaryPrimitives.ReadUInt32BigEndian(header.AsSpan(0, 4));
                    byte[] body;
                    if (dataLen == 0)
                    {
                        body = Array.Empty<byte>();
                    }
                    else
                    {
                        body = new byte[(int)dataLen];
                        if (!await ReadExactlyAsync(stream, body, 0, body.Length, token).ConfigureAwait(false))
                            break;
                    }

                    Packet packet;
                    try
                    {
                        packet = Protocol.ParsePacket(header, body);
                    }
                    catch (ProtocolError)
                    {
                        // skip corrupted frame – keep loop alive
                        continue;
                    }

                    DispatchPacket(packet);
                }
            }
            catch (OperationCanceledException) { /* normal shutdown */ }
            catch (IOException) { /* connection dropped */ }
            catch (ObjectDisposedException) { /* stream closed */ }
            finally
            {
                if (_isConnected)
                {
                    _isConnected = false;
                    NotifyDisconnect();
                }
            }
        }

        private static async Task<bool> ReadExactlyAsync(NetworkStream stream,
            byte[] buffer, int offset, int count, CancellationToken token)
        {
            int read = 0;
            while (read < count)
            {
                int n;
                try
                {
                    n = await stream.ReadAsync(buffer, offset + read, count - read, token)
                        .ConfigureAwait(false);
                }
                catch (IOException) { return false; }
                catch (ObjectDisposedException) { return false; }
                if (n == 0) return false;
                read += n;
            }
            return true;
        }

        // ---------------------------------------------------------------
        // Internal: packet dispatch (runs on receive task)
        // ---------------------------------------------------------------

        internal void DispatchPacket(Packet packet)
        {
            switch (packet.Type)
            {
                case PacketType.Resp:
                    try { _respQueue.TryAdd(packet); } catch { }
                    break;

                case PacketType.CmdResp:
                    try { _cmdRespQueue.TryAdd(packet); } catch { }
                    break;

                case PacketType.AsyncResp:
                {
                    var resp = AsyncResponse.FromPacket(packet);
                    try { AsyncResponseQueue.TryAdd(resp); } catch { }
                    Action<AsyncResponse>? cb;
                    lock (_cbLock)
                    {
                        _asyncCallbacks.TryGetValue(resp.OriginalCommand, out cb);
                    }
                    if (cb != null)
                    {
                        try { cb(resp); } catch { /* user callback exceptions swallowed */ }
                    }
                    break;
                }

                case PacketType.Status:
                case PacketType.Interrupt:
                {
                    var notif = StatusNotification.FromPacket(packet);
                    try { StatusQueue.TryAdd(notif); } catch { }
                    Action<StatusNotification>? cb;
                    lock (_cbLock)
                    {
                        _statusCallbacks.TryGetValue(new SubKey(notif.StatusName, notif.ModuleName), out cb);
                    }
                    if (cb != null)
                    {
                        try { cb(notif); } catch { /* user callback exceptions swallowed */ }
                    }
                    break;
                }

                case PacketType.Error:
                {
                    var err = ServerErrorParser.Parse(packet);
                    try { _respQueue.TryAdd(err); } catch { }
                    try { _cmdRespQueue.TryAdd(err); } catch { }
                    break;
                }

                case PacketType.Info:
                default:
                    // Silently discarded (welcome / goodbye messages)
                    break;
            }
        }

        internal void NotifyDisconnect()
        {
            var sentinel = new RouterConnectionError("Connection lost unexpectedly.");
            try { _respQueue.TryAdd(sentinel); } catch { }
            try { _cmdRespQueue.TryAdd(sentinel); } catch { }
        }

        // ---------------------------------------------------------------
        // Internal: synchronised waiters
        // ---------------------------------------------------------------

        internal async Task<CommandResponse> WaitForRespAsync(double timeoutSeconds, CancellationToken cancellationToken)
        {
            object item;
            try
            {
                item = await TakeAsync(_respQueue, timeoutSeconds, cancellationToken).ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
                throw new RouterTimeoutError($"No response received within {timeoutSeconds:F1}s.");
            }
            if (item is Exception ex) throw ex;
            var pkt = (Packet)item;
            return new CommandResponse(pkt.Data);
        }

        internal async Task WaitForCmdRespAsync(double timeoutSeconds, CancellationToken cancellationToken)
        {
            object item;
            try
            {
                item = await TakeAsync(_cmdRespQueue, timeoutSeconds, cancellationToken).ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
                throw new RouterTimeoutError($"No cmd-resp received within {timeoutSeconds:F1}s.");
            }
            if (item is Exception ex) throw ex;
            // CMD_RESP payload is a handshake acknowledgement; discard it.
        }

        // Internal sync waiters (used by tests that mirror the Python suite)
        internal CommandResponse WaitForResp(double timeoutSeconds)
        {
            if (!_respQueue.TryTake(out var item, TimeSpan.FromSeconds(timeoutSeconds)))
                throw new RouterTimeoutError($"No response received within {timeoutSeconds:F1}s.");
            if (item is Exception ex) throw ex;
            var pkt = (Packet)item;
            return new CommandResponse(pkt.Data);
        }

        internal void WaitForCmdResp(double timeoutSeconds)
        {
            if (!_cmdRespQueue.TryTake(out var item, TimeSpan.FromSeconds(timeoutSeconds)))
                throw new RouterTimeoutError($"No cmd-resp received within {timeoutSeconds:F1}s.");
            if (item is Exception ex) throw ex;
        }

        // Polls a BlockingCollection until an item is available, the timeout
        // elapses, or cancellation is requested. We use Task.Run so that the
        // blocking Take does not block the calling async context.
        private static Task<object> TakeAsync(BlockingCollection<object> queue,
            double timeoutSeconds, CancellationToken cancellationToken)
        {
            return Task.Run(() =>
            {
                int millis = (int)Math.Min(int.MaxValue, Math.Max(0, timeoutSeconds * 1000.0));
                if (!queue.TryTake(out var item, millis, cancellationToken))
                    throw new TimeoutException();
                return item!;
            }, cancellationToken);
        }

        // ---------------------------------------------------------------
        // IDisposable / IAsyncDisposable
        // ---------------------------------------------------------------

        public void Dispose()
        {
            try { Disconnect(); } catch { }
            _respLock.Dispose();
            _cmdRespLock.Dispose();
            _sendLock.Dispose();
        }

        public async ValueTask DisposeAsync()
        {
            try { await DisconnectAsync().ConfigureAwait(false); } catch { }
            _respLock.Dispose();
            _cmdRespLock.Dispose();
            _sendLock.Dispose();
        }
    }
}
