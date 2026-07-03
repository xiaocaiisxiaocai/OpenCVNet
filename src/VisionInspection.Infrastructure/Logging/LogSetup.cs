using System.IO;
using System.Text;
using Serilog;
using Serilog.Core;

namespace VisionInspection.Infrastructure.Logging
{
    /// <summary>Serilog 日志配置：按天滚动文件，保留 30 天。</summary>
    public static class LogSetup
    {
        public static Logger CreateLogger(string logDir)
        {
            Directory.CreateDirectory(logDir);
            return new LoggerConfiguration()
                .MinimumLevel.Information()
                .Enrich.FromLogContext()
                .WriteTo.File(
                    Path.Combine(logDir, "vi-.log"),
                    rollingInterval: RollingInterval.Day,
                    fileSizeLimitBytes: 50 * 1024 * 1024,
                    rollOnFileSizeLimit: true,
                    retainedFileCountLimit: 30,
                    encoding: Encoding.UTF8,
                    outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] {Message:lj} {Properties:j}{NewLine}{Exception}")
                .CreateLogger();
        }
    }
}
