using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace TcpDebugAssistant
{
    /// <summary>十六进制 / 文本编解码工具。</summary>
    public static class Codec
    {
        /// <summary>字节数组 → "AA BB CC" 形式。</summary>
        public static string ToHex(byte[] data, int len)
        {
            var sb = new StringBuilder(len * 3);
            for (int i = 0; i < len; i++)
            {
                if (i > 0) sb.Append(' ');
                sb.Append(data[i].ToString("X2"));
            }
            return sb.ToString();
        }

        /// <summary>解析 "AA BB CC" / "AABBCC" / "AA,BB" 等（紧凑写法按两字符一组），非法返回 null。</summary>
        public static byte[] FromHex(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return Array.Empty<byte>();
            var sb = new StringBuilder(text.Length);
            foreach (var ch in text)
            {
                if (char.IsWhiteSpace(ch) || ch == ',' || ch == ';' || ch == '-' || ch == '\r' || ch == '\n' || ch == '\t') continue;
                sb.Append(ch);
            }
            var hex = sb.ToString();
            if (hex.Length == 0 || hex.Length % 2 != 0) return null;
            var bytes = new byte[hex.Length / 2];
            for (int i = 0; i < bytes.Length; i++)
            {
                if (!byte.TryParse(hex.Substring(i * 2, 2),
                        System.Globalization.NumberStyles.HexNumber, null, out bytes[i]))
                    return null;
            }
            return bytes;
        }

        public static byte[] Encode(string text, Encoding enc) => enc.GetBytes(text);
        public static string Decode(byte[] data, int len, Encoding enc) => enc.GetString(data, 0, len);
    }

    /// <summary>一行收发日志：方向 / 内容 / 时间。</summary>
    public class TrafficEventArgs : EventArgs
    {
        public string Tag;          // 对端标识（客户端模式为 remote，服务端模式为 ip:port）
        public bool Sent;           // true=发送 false=接收
        public byte[] Data;
        public DateTime Time;
    }

    public class StateEventArgs : EventArgs
    {
        public string Message;      // 状态/错误描述
        public bool IsError;
        public DateTime Time;
    }

    /// <summary>
    /// TCP 客户端控制器：连接单台远端，收发数据，全部事件已切到 UI 线程之外由调用方处理。
    /// </summary>
    public class TcpClientController : IDisposable
    {
        private TcpClient _client;
        private NetworkStream _stream;
        private CancellationTokenSource _cts;
        private readonly ConcurrentQueue<TrafficEventArgs> _traffic = new();
        private readonly ConcurrentQueue<StateEventArgs> _states = new();

        public event Action<TrafficEventArgs> Traffic;
        public event Action<StateEventArgs> State;
        public bool IsConnected => _client?.Connected ?? false;
        public string Remote => _client?.Client?.RemoteEndPoint?.ToString() ?? "-";

        public async Task ConnectAsync(string host, int port)
        {
            await CloseAsync();
            _cts = new CancellationTokenSource();
            try
            {
                RaiseState($"正在连接 {host}:{port} ...", false);
                _client = new TcpClient();
                await _client.ConnectAsync(host, port).WaitAsync(TimeSpan.FromSeconds(8));
                _stream = _client.GetStream();
                RaiseState($"已连接 {Remote}", false);
                _ = ReceiveLoop(_cts.Token);
            }
            catch (Exception ex)
            {
                RaiseState("连接失败：" + ex.Message, true);
                await CloseAsync();
            }
        }

        public async Task SendAsync(byte[] data)
        {
            if (!IsConnected || _stream == null)
            {
                RaiseState("未连接，无法发送", true);
                return;
            }
            try
            {
                await _stream.WriteAsync(data, 0, data.Length);
                await _stream.FlushAsync();
                RaiseTraffic(Remote, true, data);
            }
            catch (Exception ex)
            {
                RaiseState("发送失败：" + ex.Message, true);
            }
        }

        private async Task ReceiveLoop(CancellationToken ct)
        {
            var buf = new byte[64 * 1024];
            try
            {
                while (!ct.IsCancellationRequested && _client != null && _client.Connected)
                {
                    int n = await _stream.ReadAsync(buf, 0, buf.Length, ct);
                    if (n <= 0) break;
                    RaiseTraffic(Remote, false, buf, n);
                }
                RaiseState("连接已断开（对端关闭）", false);
            }
            catch (OperationCanceledException) { }
            catch (ObjectDisposedException) { }
            catch (Exception ex)
            {
                if (!ct.IsCancellationRequested) RaiseState("接收错误：" + ex.Message, true);
            }
            finally
            {
                await CloseAsync();
                RaiseState("已断开", false);
            }
        }

        public async Task CloseAsync()
        {
            try { _cts?.Cancel(); } catch { }
            try { _stream?.Close(); } catch { }
            try { _client?.Close(); } catch { }
            _stream = null;
            _client = null;
            await Task.Yield();
        }

        public void Dispose() { CloseAsync().Wait(200); _cts?.Dispose(); }

        private void RaiseTraffic(string tag, bool sent, byte[] data, int len = -1)
        {
            var e = new TrafficEventArgs { Tag = tag, Sent = sent, Data = data, Time = DateTime.Now };
            if (len >= 0) e.Data = data.AsSpan(0, len).ToArray();
            Traffic?.Invoke(e);
        }
        private void RaiseState(string msg, bool err)
        {
            State?.Invoke(new StateEventArgs { Message = msg, IsError = err, Time = DateTime.Now });
        }
    }

    /// <summary>服务端接入的一个客户端会话。</summary>
    public class ClientSession
    {
        public TcpClient Client;
        public NetworkStream Stream;
        public string Tag;                 // ip:port
        public DateTime ConnectTime;
        public long RecvBytes, SentBytes;
        internal CancellationTokenSource Cts;
        public override string ToString() => Tag;
    }

    /// <summary>
    /// TCP 服务端控制器：监听端口，管理多个客户端会话。
    /// </summary>
    public class TcpServerController : IDisposable
    {
        private TcpListener _listener;
        private CancellationTokenSource _cts;
        private readonly object _lock = new();
        public List<ClientSession> Sessions { get; } = new();

        public event Action<TrafficEventArgs> Traffic;
        public event Action<StateEventArgs> State;
        public event Action<ClientSession> ClientConnected;
        public event Action<ClientSession> ClientDisconnected;
        public bool IsListening => _listener != null;

        public async Task StartAsync(int port)
        {
            await StopAsync();
            _cts = new CancellationTokenSource();
            try
            {
                _listener = new TcpListener(IPAddress.Any, port);
                _listener.Start();
                RaiseState($"监听中：0.0.0.0:{port}", false);
                _ = AcceptLoop(_cts.Token);
            }
            catch (Exception ex)
            {
                RaiseState("监听失败：" + ex.Message, true);
                _listener = null;
            }
        }

        private async Task AcceptLoop(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                TcpClient client;
                try { client = await _listener.AcceptTcpClientAsync(ct); }
                catch (OperationCanceledException) { break; }
                catch (ObjectDisposedException) { break; }
                catch (Exception ex) { RaiseState("接受连接失败：" + ex.Message, true); break; }

                var s = new ClientSession
                {
                    Client = client,
                    Stream = client.GetStream(),
                    Tag = client.Client.RemoteEndPoint?.ToString() ?? "?",
                    ConnectTime = DateTime.Now,
                    Cts = CancellationTokenSource.CreateLinkedTokenSource(ct)
                };
                lock (_lock) Sessions.Add(s);
                RaiseState($"客户端接入：{s.Tag}", false);
                ClientConnected?.Invoke(s);
                _ = ReceiveLoop(s);
            }
        }

        private async Task ReceiveLoop(ClientSession s)
        {
            var buf = new byte[64 * 1024];
            try
            {
                while (true)
                {
                    int n = await s.Stream.ReadAsync(buf, 0, buf.Length, s.Cts.Token);
                    if (n <= 0) break;
                    Interlocked.Add(ref s.RecvBytes, n);
                    RaiseTraffic(s.Tag, false, buf, n);
                }
            }
            catch (OperationCanceledException) { }
            catch (ObjectDisposedException) { }
            catch (Exception ex) { RaiseState($"{s.Tag} 接收错误：{ex.Message}", true); }
            finally { RemoveSession(s, "断开"); }
        }

        public async Task SendAsync(ClientSession s, byte[] data)
        {
            if (s == null) { RaiseState("请先选择要发送到的客户端", true); return; }
            try
            {
                await s.Stream.WriteAsync(data, 0, data.Length);
                Interlocked.Add(ref s.SentBytes, data.Length);
                RaiseTraffic(s.Tag, true, data);
            }
            catch (Exception ex) { RaiseState($"发送到 {s.Tag} 失败：{ex.Message}", true); }
        }

        public async Task SendToAllAsync(byte[] data)
        {
            ClientSession[] snapshot;
            lock (_lock) snapshot = Sessions.ToArray();
            foreach (var s in snapshot) await SendAsync(s, data);
        }

        public void Kick(ClientSession s) => RemoveSession(s, "被手动断开");

        private void RemoveSession(ClientSession s, string reason)
        {
            bool removed = false;
            lock (_lock) removed = Sessions.Remove(s);
            if (!removed) return;
            try { s.Cts?.Cancel(); } catch { }
            try { s.Stream?.Close(); } catch { }
            try { s.Client?.Close(); } catch { }
            RaiseState($"客户端 {s.Tag} {reason}", false);
            ClientDisconnected?.Invoke(s);
        }

        public async Task StopAsync()
        {
            try { _cts?.Cancel(); } catch { }
            ClientSession[] snapshot;
            lock (_lock) snapshot = Sessions.ToArray();
            foreach (var s in snapshot) RemoveSession(s, "服务端已停止");
            try { _listener?.Stop(); } catch { }
            _listener = null;
            await Task.Yield();
        }

        public void Dispose() { StopAsync().Wait(300); _cts?.Dispose(); }

        private void RaiseTraffic(string tag, bool sent, byte[] data, int len = -1)
        {
            var e = new TrafficEventArgs { Tag = tag, Sent = sent, Data = data, Time = DateTime.Now };
            if (len >= 0) e.Data = data.AsSpan(0, len).ToArray();
            Traffic?.Invoke(e);
        }
        private void RaiseState(string msg, bool err)
        {
            State?.Invoke(new StateEventArgs { Message = msg, IsError = err, Time = DateTime.Now });
        }
    }
}
