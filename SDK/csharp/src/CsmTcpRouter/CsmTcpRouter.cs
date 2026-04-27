// CsmTcpRouter.cs
// ---------------------------------------------------------------------------
// csm-tcp-router-client - CSM-TCP-Router LabVIEW 服务器的 C# 客户端 SDK。
//
// 单文件 SDK，实现 CSM-TCP-Router 协议 v0。镜像了
// Python `csm_tcp_router` 包的布局和功能：
//
//     * 协议编解码器（8 字节头，大端序，8 种数据包类型）。
//     * 后台接收 TCP 传输层。
//     * 高层 TcpRouterClient，提供同步和异步 API：
//         - SendAndWait / SendAndWaitAsync     （同步 CMD/RESP）
//         - Post / PostAsync                   （带 cmd-resp 握手的异步 CMD）
//         - PostNoReply / PostNoReplyAsync     （无回复异步 CMD）
//         - Ping / PingAsync                   （往返延迟）
//         - ListModules / ListApi / ListStates / Help
//         - SubscribeStatus / UnsubscribeStatus
//         - RegisterAsyncCallback / UnregisterAsyncCallback
//
// 线路格式（8 字节头，大端序）：
//
//     | Data Length (4B) | Version (1B=0x01) | Type (1B) | FLAG1 (1B) | FLAG2 (1B) |
//     +------------------------ Header (8B) -----------------------+
//
// 后跟恰好 `Data Length` 字节的有效载荷。
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
    // 公共枚举
    // -----------------------------------------------------------------------

    /// <summary>
    /// CSM-TCP-Router 协议 v0 中定义的数据包类型常量。
    /// </summary>
    public enum PacketType : byte
    {
        /// <summary>信息消息（欢迎/再见）。</summary>
        Info = 0x00,
        /// <summary>来自服务器的错误数据包。</summary>
        Error = 0x01,
        /// <summary>客户端发送的命令。</summary>
        Cmd = 0x02,
        /// <summary>服务器对异步/无回复/订阅命令的握手确认。</summary>
        CmdResp = 0x03,
        /// <summary>同步响应有效载荷。</summary>
        Resp = 0x04,
        /// <summary>异步响应有效载荷。</summary>
        AsyncResp = 0x05,
        /// <summary>来自已订阅 CSM 模块的状态广播。</summary>
        Status = 0x06,
        /// <summary>来自已订阅 CSM 模块的中断广播。</summary>
        Interrupt = 0x07,
    }

    // -----------------------------------------------------------------------
    // 公共数据模型
    // -----------------------------------------------------------------------

    /// <summary>从服务器接收到的已解码数据包。</summary>
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

    /// <summary>同步命令（<see cref="TcpRouterClient.SendAndWait"/>）的结果。</summary>
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

    /// <summary>通过异步响应数据包传递的异步响应有效载荷。</summary>
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
        /// 解析 ASYNC_RESP 数据包。服务器格式：
        /// <c>"&lt;response-data&gt; &lt;- &lt;original-command&gt;"</c>。
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

    /// <summary>通过 STATUS 或 INTERRUPT 数据包传递的状态广播。</summary>
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
        /// 解析 STATUS 或 INTERRUPT 数据包。服务器格式：
        /// <c>"&lt;status-name&gt; &gt;&gt; &lt;data&gt; &lt;- &lt;module&gt;"</c>。
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
    // 异常层次结构
    // -----------------------------------------------------------------------

    /// <summary>所有 CSM-TCP-Router 客户端错误的基础异常。</summary>
    public class CsmTcpRouterException : Exception
    {
        public CsmTcpRouterException() { }
        public CsmTcpRouterException(string message) : base(message) { }
        public CsmTcpRouterException(string message, Exception innerException) : base(message, innerException) { }
    }

    /// <summary>当连接无法建立或连接丢失时引发。</summary>
    public class RouterConnectionException : CsmTcpRouterException
    {
        public RouterConnectionException(string message) : base(message) { }
        public RouterConnectionException(string message, Exception innerException) : base(message, innerException) { }
    }

    /// <summary>当同步操作超过其超时时间时引发。</summary>
    public class RouterTimeoutException : CsmTcpRouterException
    {
        public RouterTimeoutException(string message) : base(message) { }
    }

    /// <summary>当收到无效或意外的协议帧时引发。</summary>
    public class ProtocolException : CsmTcpRouterException
    {
        public ProtocolException(string message) : base(message) { }
    }

    /// <summary>
    /// 当服务器返回错误数据包时引发。CSM 错误格式：
    /// <c>[Error: &lt;code&gt;] &lt;message&gt;</c>。
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
    // 内部协议编解码器
    // -----------------------------------------------------------------------

    internal static class ProtocolCodec
    {
        public const int HeaderSize = 8;
        public const byte ProtocolVersion = 0x01;

        /// <summary>将 <paramref name="data"/> 编码为完整的线路格式数据包（头 + 体）。</summary>
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

        /// <summary>将 8 字节头解码为其组成字段。</summary>
        public static (uint DataLen, byte Version, byte TypeByte, byte Flag1, byte Flag2) DecodeHeader(byte[] header)
        {
            if (header == null || header.Length != HeaderSize)
                throw new ProtocolException(
                    $"Expected {HeaderSize}-byte header, got {(header == null ? 0 : header.Length)} bytes.");
            uint dataLen = ((uint)header[0] << 24) | ((uint)header[1] << 16) | ((uint)header[2] << 8) | header[3];
            return (dataLen, header[4], header[5], header[6], header[7]);
        }

        /// <summary>从原始头 + 体构建 <see cref="Packet"/>。</summary>
        public static Packet ParsePacket(byte[] header, byte[] body)
        {
            var (dataLen, version, typeByte, flag1, flag2) = DecodeHeader(header);
            body = body ?? Array.Empty<byte>();
            if ((uint)body.Length != dataLen)
                throw new ProtocolException(
                    $"Payload length mismatch: header says {dataLen} bytes, got {body.Length} bytes.");
            // 向前兼容：未知类型字节映射为 Info。
            PacketType ptype = Enum.IsDefined(typeof(PacketType), typeByte)
                ? (PacketType)typeByte
                : PacketType.Info;
            return new Packet(ptype, body, version, flag1, flag2);
        }

        /// <summary>从 CSM 错误格式 <c>[Error: code] msg</c> 中提取代码和消息。</summary>
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
    // 内部 TCP 传输层（后台接收任务）
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
                    try { client.Close(); } catch { /* 忽略 */ }
                    throw new RouterConnectionException(
                        $"Cannot connect to {host}:{port}: timed out after {to.TotalSeconds:F1}s.");
                }
                await connectTask.ConfigureAwait(false); // 让任何连接异常浮现
            }
            catch (RouterConnectionException)
            {
                throw;
            }
            catch (Exception exc)
            {
                try { client.Close(); } catch { /* 忽略 */ }
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
            try { _cts?.Cancel(); } catch { /* 忽略 */ }
            try { _stream?.Close(); } catch { /* 忽略 */ }
            try { _client?.Close(); } catch { /* 忽略 */ }
            _stream = null;
            _client = null;
            var jt = joinTimeout ?? TimeSpan.FromSeconds(2);
            try { _recvTask?.Wait(jt); } catch { /* 忽略 */ }
            _recvTask = null;
            try { _cts?.Dispose(); } catch { /* 忽略 */ }
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
        // 内部：后台接收循环
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
                        // 损坏的帧——跳过并保持循环运行。
                        continue;
                    }

                    try { _onPacket(packet); } catch { /* swallow callback errors */ }
                }
            }
            catch (IOException) { /* 连接已断开 */ }
            catch (ObjectDisposedException) { /* 读取期间套接字已关闭 */ }
            catch (OperationCanceledException) { /* 正在关闭 */ }
            finally
            {
                if (!_stopped)
                {
                    _stopped = true;
                    try { _onDisconnect(); } catch { /* 忽略 */ }
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
    // 高层客户端
    // -----------------------------------------------------------------------

    /// <summary>用于状态/中断广播的回调委托。</summary>
    public delegate void StatusCallback(StatusNotification notification);

    /// <summary>用于异步响应数据包的回调委托。</summary>
    public delegate void AsyncResponseCallback(AsyncResponse response);

    /// <summary>
    /// CSM-TCP-Router 服务器的 C# 客户端。镜像了 LabVIEW ClientAPI VI
    /// 和 Python <c>TcpRouterClient</c>；使用协议 v0。
    ///
    /// 该类是线程安全的。任意时刻最多只能有一个正在执行的同步命令
    /// 和一个正在执行的异步/订阅命令；并发调用者由内部信号量序列化。
    /// </summary>
    public sealed class TcpRouterClient : IDisposable
    {
        // 通过 TCS 实现的单元素"队列"，用于同步等待。
        // 每个等待者在相应的锁内将其重置为新的 TCS。
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

        /// <summary>用于轮询从服务器接收到的异步响应数据包的队列。</summary>
        public ConcurrentQueue<AsyncResponse> AsyncResponseQueue { get; } = new ConcurrentQueue<AsyncResponse>();

        /// <summary>用于轮询状态/中断通知的队列。</summary>
        public ConcurrentQueue<StatusNotification> StatusQueue { get; } = new ConcurrentQueue<StatusNotification>();

        public TcpRouterClient()
        {
            _transport = new Transport(OnPacket, OnDisconnect);
        }

        // ---------------------------------------------------------------
        // 连接管理
        // ---------------------------------------------------------------

        /// <summary>连接到 CSM-TCP-Router 服务器。</summary>
        public void Connect(string host, int port, TimeSpan? timeout = null)
            => _transport.Connect(host, port, timeout);

        /// <summary>连接到 CSM-TCP-Router 服务器（异步）。</summary>
        public Task ConnectAsync(string host, int port, TimeSpan? timeout = null)
            => _transport.ConnectAsync(host, port, timeout);

        /// <summary>
        /// 从服务器断开连接并释放所有资源。当前阻塞在
        /// <see cref="SendAndWait"/> / <see cref="Post"/> 中的线程将立即
        /// 收到 <see cref="RouterConnectionException"/>，而不是等待超时。
        /// </summary>
        public void Disconnect()
        {
            // 在拆除传输层之前解除所有挂起等待者的阻塞。
            var sentinel = new RouterConnectionException("Disconnected from server.");
            UnblockWaiters(sentinel);
            _transport.Disconnect();
        }

        /// <summary>当底层传输层已连接时为 <c>true</c>。</summary>
        public bool Connected => _transport.Connected;

        /// <summary>
        /// 轮询直到 <paramref name="host"/>:<paramref name="port"/> 接受连接
        /// 或 <paramref name="timeout"/> 超时。
        /// </summary>
        public bool WaitForServer(string host, int port, TimeSpan? timeout = null, TimeSpan? retryInterval = null)
            => WaitForServerAsync(host, port, timeout, retryInterval).GetAwaiter().GetResult();

        /// <summary><see cref="WaitForServer"/> 的异步版本。</summary>
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
                            // 观察任何连接异常（已故障任务）；
                            // 成功表示服务器可访问。
                            await connectTask.ConfigureAwait(false);
                            try { probe.Close(); } catch { /* 忽略 */ }
                            return true;
                        }
                        catch (SocketException) { /* 尚未就绪 */ }
                        catch (IOException) { /* 尚未就绪 */ }
                    }
                    else
                    {
                        // 延迟获胜；通过关闭探测套接字终止正在进行的连接尝试，
                        // 然后观察任何挂起的异常，以免其未被观察到。
                        try { probe.Close(); } catch { /* 忽略 */ }
                        try
                        {
                            await connectTask.ConfigureAwait(false);
                        }
                        catch (SocketException) { /* 尚未就绪 */ }
                        catch (IOException) { /* 尚未就绪 */ }
                        catch (ObjectDisposedException) { /* 关闭探测套接字导致连接中止 */ }
                    }
                }
                await Task.Delay(interval).ConfigureAwait(false);
            }
            return false;
        }

        // ---------------------------------------------------------------
        // 核心命令方法（同步包装）
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
        // 核心命令方法（异步）
        // ---------------------------------------------------------------

        /// <summary>
        /// 发送同步命令（后缀 <c>-@</c>）并等待响应。
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
        /// 发送异步命令（后缀 <c>-&gt;</c>）并等待 cmd-resp 握手。
        /// </summary>
        public Task PostAsync(string command, TimeSpan? timeout = null)
            => SendAndAwaitCmdRespAsync(command, timeout);

        /// <summary>
        /// 发送异步无回复命令（后缀 <c>-&gt;|</c>）并等待 cmd-resp 握手。
        /// </summary>
        public Task PostNoReplyAsync(string command, TimeSpan? timeout = null)
            => SendAndAwaitCmdRespAsync(command, timeout);

        /// <summary>发送 Ping 并测量往返延迟。</summary>
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

        /// <summary>订阅 CSM 模块的状态广播。</summary>
        public async Task SubscribeStatusAsync(
            string statusName, string moduleName, StatusCallback callback = null, TimeSpan? timeout = null)
        {
            if (statusName == null) throw new ArgumentNullException(nameof(statusName));
            if (moduleName == null) throw new ArgumentNullException(nameof(moduleName));

            var key = (statusName, moduleName);
            // 在发送之前注册回调，以消除状态数据包在回调
            // 存储之前到达的竞争条件。
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

        /// <summary>取消状态订阅。</summary>
        public async Task UnsubscribeStatusAsync(string statusName, string moduleName, TimeSpan? timeout = null)
        {
            if (statusName == null) throw new ArgumentNullException(nameof(statusName));
            if (moduleName == null) throw new ArgumentNullException(nameof(moduleName));

            string cmd = $"{statusName}@{moduleName} -><unregister>";
            await SendAndAwaitCmdRespAsync(cmd, timeout).ConfigureAwait(false);
            lock (_stateLock) { _statusCallbacks.Remove((statusName, moduleName)); }
        }

        /// <summary>
        /// 为异步响应数据包注册回调，通过异步响应有效载荷中回显的原始命令
        /// （在 <c> &lt;- </c> 分隔符之后）进行匹配。
        /// </summary>
        public void RegisterAsyncCallback(string originalCommand, AsyncResponseCallback callback)
        {
            if (originalCommand == null) throw new ArgumentNullException(nameof(originalCommand));
            if (callback == null) throw new ArgumentNullException(nameof(callback));
            lock (_stateLock) { _asyncCallbacks[originalCommand] = callback; }
        }

        /// <summary>移除之前注册的异步回调。</summary>
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
        // 内部辅助方法
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
                // 协议 v0 没有关联 ID，因此超时命令的延迟 RESP 可能被
                // 错误地归属于*下一个* SendAndWait 调用。强制断开连接，
                // 使连接在调用者重新连接之前不可用。
                try { _transport.Disconnect(); } catch { /* 忽略 */ }
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
                // 与 WaitForRespAsync 中相同的去同步风险：延迟的 CMD_RESP 可能
                // 完成下一个正在等待的调用。强制断开连接，使握手超时后
                // 连接无法被复用。
                try { _transport.Disconnect(); } catch { /* 忽略 */ }
                throw new RouterTimeoutException($"No cmd-resp received within {timeout.TotalSeconds:F1}s.");
            }
            object item = await tcs.Task.ConfigureAwait(false);
            if (item is Exception exc) throw exc;
            // CMD_RESP 有效载荷是握手确认；丢弃它。
        }

        private void UnblockWaiters(Exception sentinel)
        {
            _respTcs?.TrySetResult(sentinel);
            _cmdRespTcs?.TrySetResult(sentinel);
        }

        // ---------------------------------------------------------------
        // 内部：数据包分发（在接收任务线程上运行）
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
                        try { cb(resp); } catch { /* 吞掉回调错误 */ }
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
                        try { cb(notif); } catch { /* 吞掉回调错误 */ }
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
                    // 静默丢弃（欢迎/再见消息）。
                    break;

                case PacketType.Cmd:
                    // 服务器永远不应发送 CMD；为向前兼容性忽略。
                    break;
            }
        }

        internal void OnDisconnect()
        {
            UnblockWaiters(new RouterConnectionException("Connection lost unexpectedly."));
        }
    }

    // -----------------------------------------------------------------------
    // 小型便利扩展
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
