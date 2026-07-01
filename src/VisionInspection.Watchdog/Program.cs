using System;
using System.Diagnostics;
using System.IO;
using System.Threading;

namespace VisionInspection.Watchdog
{
    /// <summary>
    /// 看门狗：监控主程序存活与"假死"。进程不存在则拉起；进程存在但心跳文件超时(UI 卡死)则结束并重启。
    /// 含重启退避与风暴防护。自身单实例。
    /// 用法：VisionInspection.Watchdog.exe [主程序路径]；缺省监控同目录的 VisionInspection.App.exe。
    /// </summary>
    internal static class Program
    {
        private const string MutexName = @"Global\VisionInspection.Watchdog.SingleInstance";
        private const int CheckIntervalMs = 5000;
        private const int HeartbeatStaleMs = 30000;   // 心跳超过 30s 未更新 → 判假死
        private const int GraceAfterStartMs = 20000;  // 拉起后宽限期,不做假死判定
        private const int MaxBackoffMs = 60000;       // 退避封顶

        [STAThread]
        private static void Main(string[] args)
        {
            using (var mutex = new Mutex(true, MutexName, out bool createdNew))
            {
                if (!createdNew) return; // 已有看门狗在运行

                string targetExe = ResolveTarget(args);
                string processName = Path.GetFileNameWithoutExtension(targetExe);
                string workDir = Path.GetDirectoryName(targetExe) ?? AppDomain.CurrentDomain.BaseDirectory;
                string logFile = Path.Combine(EnsureLogDir(workDir), "watchdog.log");
                string heartbeatFile = Path.Combine(workDir, "heartbeat");

                Log(logFile, $"看门狗启动,监控目标：{targetExe}");

                int consecutiveRestarts = 0;
                DateTime lastRestart = DateTime.MinValue;

                while (true)
                {
                    try
                    {
                        var procs = Process.GetProcessesByName(processName);
                        bool running = procs.Length > 0;
                        bool inGrace = (DateTime.Now - lastRestart).TotalMilliseconds < GraceAfterStartMs;
                        bool hung = running && !inGrace && HeartbeatStale(heartbeatFile);

                        if (hung)
                        {
                            Log(logFile, "心跳超时,判定假死,结束进程…");
                            foreach (var p in procs)
                            {
                                try { p.Kill(); p.WaitForExit(5000); } catch { }
                            }
                            running = false;
                        }

                        if (!running)
                        {
                            // 重启退避:60s 内连续重启则递增退避,防崩溃风暴。
                            if ((DateTime.Now - lastRestart).TotalSeconds < 60) consecutiveRestarts++;
                            else consecutiveRestarts = 1;

                            if (consecutiveRestarts > 1)
                            {
                                int backoff = Math.Min(MaxBackoffMs, 2000 * (consecutiveRestarts - 1));
                                Log(logFile, $"退避 {backoff}ms 后重启(连续第 {consecutiveRestarts} 次)…");
                                Thread.Sleep(backoff);
                            }

                            Log(logFile, "拉起目标…");
                            Process.Start(new ProcessStartInfo(targetExe)
                            {
                                WorkingDirectory = workDir,
                                UseShellExecute = true
                            });
                            lastRestart = DateTime.Now;
                        }
                    }
                    catch (Exception ex)
                    {
                        Log(logFile, "监控异常：" + ex.Message);
                    }

                    Thread.Sleep(CheckIntervalMs);
                }
            }
        }

        /// <summary>心跳文件超期(或异常)判为陈旧;文件不存在(尚未生成)不判假死。</summary>
        private static bool HeartbeatStale(string file)
        {
            try
            {
                if (!File.Exists(file)) return false;
                return (DateTime.Now - File.GetLastWriteTime(file)).TotalMilliseconds > HeartbeatStaleMs;
            }
            catch { return false; }
        }

        private static string ResolveTarget(string[] args)
        {
            if (args.Length > 0 && !string.IsNullOrWhiteSpace(args[0]))
                return Path.GetFullPath(args[0]);
            return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "VisionInspection.App.exe");
        }

        private static string EnsureLogDir(string workDir)
        {
            var dir = Path.Combine(workDir, "logs");
            Directory.CreateDirectory(dir);
            return dir;
        }

        private static void Log(string file, string message)
        {
            try
            {
                File.AppendAllText(file, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {message}{Environment.NewLine}");
            }
            catch
            {
                // 日志失败不影响监控
            }
        }
    }
}
