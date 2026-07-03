using System;
using System.IO;
using System.Net.Sockets;
using VisionInspection.Core.Abstractions;

namespace VisionInspection.Plc.Mc
{
    /// <summary>
    /// 三菱 MC 3E 二进制 TCP 客户端（IPlcClient 实现）。
    /// 位软元件单点读写采用 MC「位单位命令」(子指令 0x0001)，不做字读改写，避免与 PLC 并发写同字互相覆盖。
    /// 线程安全（同一连接串行事务）。注：真机行为需现场用实际 PLC 验证。
    /// </summary>
    public sealed class MelsecMcClient : IPlcClient
    {
        private readonly string _host;
        private readonly int _port;
        private readonly int _timeoutMs;
        private readonly object _sync = new object();
        private TcpClient _tcp;
        private NetworkStream _stream;

        public MelsecMcClient(string host, int port = 5000, int timeoutMs = 2000)
        {
            _host = host;
            _port = port;
            _timeoutMs = timeoutMs;
        }

        public bool IsConnected => _tcp != null && _tcp.Connected;
        public event EventHandler<PlcConnectionEventArgs> ConnectionChanged;

        public void Connect()
        {
            lock (_sync)
            {
                DisposeSocket();
                var tcp = new TcpClient { ReceiveTimeout = _timeoutMs, SendTimeout = _timeoutMs };
                var ar = tcp.BeginConnect(_host, _port, null, null);
                if (!ar.AsyncWaitHandle.WaitOne(_timeoutMs))
                {
                    tcp.Close();
                    throw new TimeoutException($"PLC 连接超时：{_host}:{_port}");
                }
                tcp.EndConnect(ar);
                _tcp = tcp;
                _stream = tcp.GetStream();
            }
            ConnectionChanged?.Invoke(this, new PlcConnectionEventArgs(true));
        }

        public void Disconnect()
        {
            lock (_sync)
            {
                DisposeSocket();
            }
            ConnectionChanged?.Invoke(this, new PlcConnectionEventArgs(false));
        }

        private void DisposeSocket()
        {
            _stream?.Dispose();
            _stream = null;
            _tcp?.Close();
            _tcp = null;
        }

        public ushort[] ReadUInt16(string address, ushort length)
        {
            var dev = DeviceCode.Parse(address);
            var resp = Transact(McProtocol.BuildReadWordsRequest(dev, length));
            return McProtocol.ParseReadWordsResponse(resp);
        }

        public void WriteUInt16(string address, ushort[] values)
        {
            var dev = DeviceCode.Parse(address);
            McProtocol.EnsureWriteAck(Transact(McProtocol.BuildWriteWordsRequest(dev, values)));
        }

        public short ReadInt16(string address) => (short)ReadUInt16(address, 1)[0];
        public void WriteInt16(string address, short value) => WriteUInt16(address, new[] { (ushort)value });

        public bool ReadBool(string address)
        {
            var dev = DeviceCode.Parse(address);
            if (!dev.IsBit)
                return ReadUInt16(address, 1)[0] != 0;

            var resp = Transact(McProtocol.BuildReadBitsRequest(dev, 1));
            return McProtocol.ParseReadBitsResponse(resp, 1)[0];
        }

        public void WriteBool(string address, bool value)
        {
            var dev = DeviceCode.Parse(address);
            if (!dev.IsBit)
            {
                WriteUInt16(address, new[] { (ushort)(value ? 1 : 0) });
                return;
            }

            // 位单位写:直接写单点,不做字读改写,避免与 PLC 并发写同字时互相覆盖。
            McProtocol.EnsureWriteAck(Transact(McProtocol.BuildWriteBitsRequest(dev, new[] { value })));
        }

        private byte[] Transact(byte[] request)
        {
            bool dropped = false;
            try
            {
                lock (_sync)
                {
                    if (_stream == null) throw new InvalidOperationException("PLC 未连接。");
                    try
                    {
                        _stream.Write(request, 0, request.Length);
                        return ReadResponse(_stream);
                    }
                    catch (Exception ex) when (ex is IOException || ex is SocketException || ex is ObjectDisposedException)
                    {
                        // 传输层故障:立即断开,使 IsConnected=false 并触发外层重连。
                        // 注意:McException(结束码非 0)是协议级错误、连接仍好,不在此捕获、不误判断线。
                        DisposeSocket();
                        dropped = true;
                        throw;
                    }
                }
            }
            finally
            {
                if (dropped) ConnectionChanged?.Invoke(this, new PlcConnectionEventArgs(false));
            }
        }

        private static byte[] ReadResponse(NetworkStream stream)
        {
            // 先读固定头 9 字节（含数据长度域），再按长度读剩余。
            var head = ReadExact(stream, 9);
            if (head[0] != 0xD0 || head[1] != 0x00)
                throw new IOException($"PLC 响应帧副头部错误：0x{head[0]:X2} 0x{head[1]:X2}");
            int len = head[7] | (head[8] << 8);
            var body = ReadExact(stream, len);

            var full = new byte[9 + len];
            Array.Copy(head, full, 9);
            Array.Copy(body, 0, full, 9, len);
            return full;
        }

        private static byte[] ReadExact(NetworkStream stream, int count)
        {
            var buf = new byte[count];
            int off = 0;
            while (off < count)
            {
                int n = stream.Read(buf, off, count - off);
                if (n <= 0) throw new IOException("PLC 连接已关闭。");
                off += n;
            }
            return buf;
        }

        public void Dispose() => Disconnect();
    }
}
