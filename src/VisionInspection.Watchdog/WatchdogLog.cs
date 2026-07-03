using System;
using System.IO;

namespace VisionInspection.Watchdog
{
    public static class WatchdogLog
    {
        public static void Write(string file, string message, long maxBytes = 1024 * 1024)
        {
            try
            {
                RotateIfNeeded(file, maxBytes);
                File.AppendAllText(file, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {message}{Environment.NewLine}");
            }
            catch
            {
                // 日志失败不影响监控
            }
        }

        private static void RotateIfNeeded(string file, long maxBytes)
        {
            if (maxBytes <= 0 || !File.Exists(file)) return;
            var info = new FileInfo(file);
            if (info.Length < maxBytes) return;

            var backup = file + ".1";
            if (File.Exists(backup)) File.Delete(backup);
            File.Move(file, backup);
        }
    }
}
