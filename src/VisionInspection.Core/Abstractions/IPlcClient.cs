using System;

namespace VisionInspection.Core.Abstractions
{
    /// <summary>
    /// PLC 通讯抽象（三菱 MC / SLMP）。地址以软元件字符串表示（如 "M100"、"D200"），由实现解析。
    /// 握手层依赖此接口，便于用模拟实现做单元测试。
    /// </summary>
    public interface IPlcClient : IDisposable
    {
        bool IsConnected { get; }

        void Connect();
        void Disconnect();

        bool ReadBool(string address);
        void WriteBool(string address, bool value);

        short ReadInt16(string address);
        void WriteInt16(string address, short value);

        /// <summary>批量读字软元件（如 D 区），<paramref name="length"/> 为字个数。</summary>
        ushort[] ReadUInt16(string address, ushort length);

        /// <summary>批量写字软元件。</summary>
        void WriteUInt16(string address, ushort[] values);

        event EventHandler<PlcConnectionEventArgs> ConnectionChanged;
    }

    public sealed class PlcConnectionEventArgs : EventArgs
    {
        public bool IsConnected { get; }
        public PlcConnectionEventArgs(bool isConnected) { IsConnected = isConnected; }
    }
}
