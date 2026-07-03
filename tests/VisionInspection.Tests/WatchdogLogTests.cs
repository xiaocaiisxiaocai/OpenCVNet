using System.IO;
using VisionInspection.Watchdog;
using Xunit;

namespace VisionInspection.Tests
{
    public class WatchdogLogTests
    {
        [Fact]
        public void Write_Rotates_Log_When_Size_Limit_Is_Exceeded()
        {
            var dir = Path.Combine(Path.GetTempPath(), "vi_wdlog_" + Path.GetRandomFileName());
            Directory.CreateDirectory(dir);
            try
            {
                var file = Path.Combine(dir, "watchdog.log");
                File.WriteAllText(file, new string('x', 128));

                WatchdogLog.Write(file, "hello", maxBytes: 16);

                Assert.True(File.Exists(file + ".1"));
                Assert.Contains("hello", File.ReadAllText(file));
            }
            finally
            {
                if (Directory.Exists(dir)) Directory.Delete(dir, true);
            }
        }
    }
}
