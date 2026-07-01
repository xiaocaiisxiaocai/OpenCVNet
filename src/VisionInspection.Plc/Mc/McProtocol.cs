using System;
using System.IO;

namespace VisionInspection.Plc.Mc
{
    /// <summary>
    /// MELSEC 通讯协议 3E 帧（二进制）的字批量读 / 写编解码。
    /// 位软元件（如 M）的单点读写在客户端层以「16 位对齐字读改写」实现，故此处仅需字命令。
    /// </summary>
    public static class McProtocol
    {
        private const ushort CmdBatchRead = 0x0401;
        private const ushort CmdBatchWrite = 0x1401;
        private const ushort SubCommandWord = 0x0000;
        private const ushort SubCommandBit = 0x0001;

        public static byte[] BuildReadWordsRequest(DeviceAddress dev, ushort count, ushort monitorTimer = 16)
        {
            using (var ms = new MemoryStream())
            {
                WriteHeader(ms);
                long lenPos = ms.Position;
                WriteUInt16(ms, 0); // 数据长度占位
                WriteUInt16(ms, monitorTimer);
                WriteUInt16(ms, CmdBatchRead);
                WriteUInt16(ms, SubCommandWord);
                WriteDevice(ms, dev);
                WriteUInt16(ms, count);
                FillLength(ms, lenPos);
                return ms.ToArray();
            }
        }

        public static byte[] BuildWriteWordsRequest(DeviceAddress dev, ushort[] values, ushort monitorTimer = 16)
        {
            if (values == null || values.Length == 0)
                throw new ArgumentException("写入值不能为空。", nameof(values));

            using (var ms = new MemoryStream())
            {
                WriteHeader(ms);
                long lenPos = ms.Position;
                WriteUInt16(ms, 0);
                WriteUInt16(ms, monitorTimer);
                WriteUInt16(ms, CmdBatchWrite);
                WriteUInt16(ms, SubCommandWord);
                WriteDevice(ms, dev);
                WriteUInt16(ms, (ushort)values.Length);
                foreach (var v in values) WriteUInt16(ms, v);
                FillLength(ms, lenPos);
                return ms.ToArray();
            }
        }

        /// <summary>解析读字响应为字数组（校验副头部与结束码）。</summary>
        public static ushort[] ParseReadWordsResponse(byte[] response)
        {
            var payload = ParsePayload(response);
            var result = new ushort[payload.Length / 2];
            for (int i = 0; i < result.Length; i++)
                result[i] = (ushort)(payload[i * 2] | (payload[i * 2 + 1] << 8));
            return result;
        }

        // —— 位单位命令(子指令 0x0001)——
        // 位软元件单点读写直接用位命令,避免"字读改写"与 PLC 并发写同字时互相覆盖。

        /// <summary>构造位单位批量读请求(count 为位点数)。</summary>
        public static byte[] BuildReadBitsRequest(DeviceAddress dev, ushort count, ushort monitorTimer = 16)
        {
            using (var ms = new MemoryStream())
            {
                WriteHeader(ms);
                long lenPos = ms.Position;
                WriteUInt16(ms, 0);
                WriteUInt16(ms, monitorTimer);
                WriteUInt16(ms, CmdBatchRead);
                WriteUInt16(ms, SubCommandBit);
                WriteDevice(ms, dev);
                WriteUInt16(ms, count);
                FillLength(ms, lenPos);
                return ms.ToArray();
            }
        }

        /// <summary>构造位单位批量写请求(每字节高/低半字节各 1 点,1=ON)。</summary>
        public static byte[] BuildWriteBitsRequest(DeviceAddress dev, bool[] values, ushort monitorTimer = 16)
        {
            if (values == null || values.Length == 0)
                throw new ArgumentException("写入位不能为空。", nameof(values));

            using (var ms = new MemoryStream())
            {
                WriteHeader(ms);
                long lenPos = ms.Position;
                WriteUInt16(ms, 0);
                WriteUInt16(ms, monitorTimer);
                WriteUInt16(ms, CmdBatchWrite);
                WriteUInt16(ms, SubCommandBit);
                WriteDevice(ms, dev);
                WriteUInt16(ms, (ushort)values.Length);
                for (int i = 0; i < values.Length; i += 2)
                {
                    byte b = (byte)((values[i] ? 1 : 0) << 4);
                    if (i + 1 < values.Length && values[i + 1]) b |= 0x01;
                    ms.WriteByte(b);
                }
                FillLength(ms, lenPos);
                return ms.ToArray();
            }
        }

        /// <summary>解析位单位读响应为布尔数组(每字节 2 点:高半字节在前)。</summary>
        public static bool[] ParseReadBitsResponse(byte[] response, int count)
        {
            var payload = ParsePayload(response);
            var result = new bool[count];
            for (int i = 0; i < count; i++)
            {
                if (i / 2 >= payload.Length)
                    throw new InvalidDataException("MC 位读响应数据不足。");
                byte b = payload[i / 2];
                int nibble = (i % 2 == 0) ? (b >> 4) & 0x0F : b & 0x0F;
                result[i] = nibble != 0;
            }
            return result;
        }

        /// <summary>校验写响应结束码（正常则无异常）。</summary>
        public static void EnsureWriteAck(byte[] response) => ParsePayload(response);

        /// <summary>校验响应副头部(0xD0)与结束码，返回数据部分。</summary>
        public static byte[] ParsePayload(byte[] response)
        {
            if (response == null || response.Length < 11)
                throw new InvalidDataException("MC 响应帧过短。");
            if (response[0] != 0xD0 || response[1] != 0x00)
                throw new InvalidDataException("MC 响应副头部非法。");

            int len = response[7] | (response[8] << 8);       // 数据长度（含结束码）
            int endCode = response[9] | (response[10] << 8);
            if (endCode != 0)
                throw new McException(endCode);

            int dataLen = Math.Max(0, len - 2);
            var payload = new byte[dataLen];
            Array.Copy(response, 11, payload, 0, dataLen);
            return payload;
        }

        private static void WriteHeader(Stream s)
        {
            // 副头部 0x0050 + 网络号 00 + PC 号 FF + 请求目标模块 IO 0x03FF + 站号 00
            s.WriteByte(0x50); s.WriteByte(0x00);
            s.WriteByte(0x00);
            s.WriteByte(0xFF);
            s.WriteByte(0xFF); s.WriteByte(0x03);
            s.WriteByte(0x00);
        }

        private static void WriteDevice(Stream s, DeviceAddress dev)
        {
            // 软元件号 3 字节小端 + 软元件代码 1 字节
            s.WriteByte((byte)(dev.Number & 0xFF));
            s.WriteByte((byte)((dev.Number >> 8) & 0xFF));
            s.WriteByte((byte)((dev.Number >> 16) & 0xFF));
            s.WriteByte(dev.Code);
        }

        private static void WriteUInt16(Stream s, ushort v)
        {
            s.WriteByte((byte)(v & 0xFF));
            s.WriteByte((byte)((v >> 8) & 0xFF));
        }

        private static void FillLength(MemoryStream ms, long lenPos)
        {
            // 数据长度 = 监视定时器起至末尾 = 总长 - (头 7 + 长度域 2)
            int dataLen = (int)(ms.Length - 9);
            long cur = ms.Position;
            ms.Position = lenPos;
            WriteUInt16(ms, (ushort)dataLen);
            ms.Position = cur;
        }
    }

    /// <summary>MC 协议返回非 0 结束码时抛出。</summary>
    public sealed class McException : Exception
    {
        public int EndCode { get; }
        public McException(int endCode)
            : base($"MC 协议错误，结束代码 0x{endCode:X4}")
        {
            EndCode = endCode;
        }
    }
}
