using System.IO;
using VisionInspection.Infrastructure.Storage;
using Xunit;

namespace VisionInspection.Tests
{
    public class StatisticsStoreTests
    {
        [Fact]
        public void Load_Broken_File_Raises_Warning()
        {
            var dir = Path.Combine(Path.GetTempPath(), "vi_stats_" + Path.GetRandomFileName());
            Directory.CreateDirectory(dir);
            try
            {
                var path = Path.Combine(dir, "stats.json");
                File.WriteAllText(path, "{ broken");
                var store = new StatisticsStore(path);
                string warning = null;
                store.Warning += m => warning = m;

                var snapshot = store.Load();

                Assert.Null(snapshot);
                Assert.Contains("统计文件损坏", warning);
            }
            finally
            {
                if (Directory.Exists(dir)) Directory.Delete(dir, true);
            }
        }
    }
}
