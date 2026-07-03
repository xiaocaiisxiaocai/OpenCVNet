using System;
using VisionInspection.Core.Abstractions;
using VisionInspection.Plc.Mc;

namespace VisionInspection.PlcProbe
{
    internal static class Program
    {
        private static int Main(string[] args)
        {
            if (args.Length < 4)
            {
                Usage();
                return 2;
            }

            string host = args[0];
            if (!int.TryParse(args[1], out var port))
            {
                Console.Error.WriteLine("端口无效。");
                return 2;
            }

            string op = args[2].ToLowerInvariant();
            string address = args[3];
            int timeoutMs = args.Length >= 6 && int.TryParse(args[5], out var t) ? t : 2000;

            try
            {
                using (IPlcClient plc = new MelsecMcClient(host, port, timeoutMs))
                {
                    plc.Connect();
                    switch (op)
                    {
                        case "read-bool":
                            Console.WriteLine(plc.ReadBool(address) ? "1" : "0");
                            break;
                        case "write-bool":
                            RequireValue(args);
                            plc.WriteBool(address, ParseBool(args[4]));
                            Console.WriteLine("OK");
                            break;
                        case "read-int16":
                            Console.WriteLine(plc.ReadInt16(address));
                            break;
                        case "write-int16":
                            RequireValue(args);
                            plc.WriteInt16(address, short.Parse(args[4]));
                            Console.WriteLine("OK");
                            break;
                        default:
                            Usage();
                            return 2;
                    }
                }
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex.GetType().Name + ": " + ex.Message);
                return 1;
            }
        }

        private static void RequireValue(string[] args)
        {
            if (args.Length < 5) throw new ArgumentException("写操作需要 value 参数。");
        }

        private static bool ParseBool(string value)
        {
            if (value == "1" || value.Equals("true", StringComparison.OrdinalIgnoreCase)) return true;
            if (value == "0" || value.Equals("false", StringComparison.OrdinalIgnoreCase)) return false;
            throw new FormatException("bool value 必须是 1/0/true/false。");
        }

        private static void Usage()
        {
            Console.WriteLine("用法:");
            Console.WriteLine("  VisionInspection.PlcProbe.exe <host> <port> read-bool <addr> [timeoutMs]");
            Console.WriteLine("  VisionInspection.PlcProbe.exe <host> <port> write-bool <addr> <value> [timeoutMs]");
            Console.WriteLine("  VisionInspection.PlcProbe.exe <host> <port> read-int16 <addr> [timeoutMs]");
            Console.WriteLine("  VisionInspection.PlcProbe.exe <host> <port> write-int16 <addr> <value> [timeoutMs]");
        }
    }
}
