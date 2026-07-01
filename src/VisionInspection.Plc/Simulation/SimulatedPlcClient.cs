using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using VisionInspection.Core.Abstractions;

namespace VisionInspection.Plc.Simulation
{
    /// <summary>
    /// 内存模拟 PLC：软元件以「前缀 + 号」为键存于字典，用于握手状态机的单元测试与无硬件联调。
    /// </summary>
    public sealed class SimulatedPlcClient : IPlcClient
    {
        private static readonly Regex AddressPattern = new Regex(@"^([A-Z]+)(\d+)$", RegexOptions.Compiled);

        private readonly Dictionary<string, bool> _bits = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, ushort> _words = new Dictionary<string, ushort>(StringComparer.OrdinalIgnoreCase);
        private bool _connected;

        public bool IsConnected => _connected;
        public event EventHandler<PlcConnectionEventArgs> ConnectionChanged;

        public void Connect()
        {
            _connected = true;
            ConnectionChanged?.Invoke(this, new PlcConnectionEventArgs(true));
        }

        public void Disconnect()
        {
            _connected = false;
            ConnectionChanged?.Invoke(this, new PlcConnectionEventArgs(false));
        }

        public bool ReadBool(string address) => _bits.TryGetValue(Norm(address), out var v) && v;
        public void WriteBool(string address, bool value) => _bits[Norm(address)] = value;

        public short ReadInt16(string address) => (short)(_words.TryGetValue(Norm(address), out var v) ? v : (ushort)0);
        public void WriteInt16(string address, short value) => _words[Norm(address)] = (ushort)value;

        public ushort[] ReadUInt16(string address, ushort length)
        {
            var (prefix, start) = Split(address);
            var result = new ushort[length];
            for (int i = 0; i < length; i++)
                result[i] = _words.TryGetValue(prefix + (start + i), out var v) ? v : (ushort)0;
            return result;
        }

        public void WriteUInt16(string address, ushort[] values)
        {
            if (values == null) throw new ArgumentNullException(nameof(values));
            var (prefix, start) = Split(address);
            for (int i = 0; i < values.Length; i++)
                _words[prefix + (start + i)] = values[i];
        }

        public void Dispose() { }

        private static string Norm(string a) => a.Trim().ToUpperInvariant();

        private static (string prefix, int number) Split(string address)
        {
            var m = AddressPattern.Match(Norm(address));
            if (!m.Success) throw new FormatException("非法软元件地址：" + address);
            return (m.Groups[1].Value, int.Parse(m.Groups[2].Value));
        }
    }
}
