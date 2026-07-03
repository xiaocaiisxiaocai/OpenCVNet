using System;

namespace VisionInspection.Plc.Mc
{
    /// <summary>MC 协议软元件地址（二进制代码 + 号 + 位/字）。</summary>
    public struct DeviceAddress
    {
        public byte Code;    // 二进制软元件代码
        public int Number;   // 软元件号
        public bool IsBit;   // 位软元件 = true

        public DeviceAddress(byte code, int number, bool isBit)
        {
            Code = code;
            Number = number;
            IsBit = isBit;
        }
    }

    /// <summary>
    /// 软元件地址解析（字符串 → <see cref="DeviceAddress"/>）。
    /// 号的进制随软元件类型：M/D/L/R 十进制；B/W/ZR 十六进制；X/Y 默认按 FX 八进制。
    /// </summary>
    public static class DeviceCode
    {
        public static DeviceAddress Parse(string address)
        {
            if (string.IsNullOrWhiteSpace(address))
                throw new ArgumentException("软元件地址为空。", nameof(address));

            var a = address.Trim().ToUpperInvariant();
            string prefix = MatchPrefix(a);
            if (prefix == null || a.Length == prefix.Length)
                throw new FormatException("非法软元件地址：" + address);
            string numStr = a.Substring(prefix.Length);

            switch (prefix)
            {
                case "M": return new DeviceAddress(0x90, ParseNum(numStr, 10), true);
                case "X": return new DeviceAddress(0x9C, ParseNum(numStr, 8), true);
                case "Y": return new DeviceAddress(0x9D, ParseNum(numStr, 8), true);
                case "L": return new DeviceAddress(0x92, ParseNum(numStr, 10), true);
                case "B": return new DeviceAddress(0xA0, ParseNum(numStr, 16), true);
                case "D": return new DeviceAddress(0xA8, ParseNum(numStr, 10), false);
                case "W": return new DeviceAddress(0xB4, ParseNum(numStr, 16), false);
                case "R": return new DeviceAddress(0xAF, ParseNum(numStr, 10), false);
                case "ZR": return new DeviceAddress(0xB0, ParseNum(numStr, 16), false);
                default: throw new NotSupportedException("不支持的软元件类型：" + prefix);
            }
        }

        private static string MatchPrefix(string address)
        {
            string[] prefixes = { "ZR", "M", "X", "Y", "L", "B", "D", "W", "R" };
            foreach (var p in prefixes)
                if (address.StartsWith(p, StringComparison.Ordinal)) return p;
            return null;
        }

        private static int ParseNum(string s, int radix)
            => radix == 16 || radix == 8 ? Convert.ToInt32(s, radix) : int.Parse(s);
    }
}
