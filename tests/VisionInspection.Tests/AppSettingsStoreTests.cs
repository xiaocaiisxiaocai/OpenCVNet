using System.IO;
using Newtonsoft.Json;
using VisionInspection.App.Settings;
using Xunit;

namespace VisionInspection.Tests
{
    public class AppSettingsStoreTests
    {
        [Fact]
        public void LoadResult_Uses_Backup_And_Quarantines_Broken_Settings()
        {
            var dir = Path.Combine(Path.GetTempPath(), "vi_settings_" + Path.GetRandomFileName());
            Directory.CreateDirectory(dir);
            try
            {
                var path = Path.Combine(dir, "settings.json");
                File.WriteAllText(path, "{ broken");
                File.WriteAllText(path + ".bak", JsonConvert.SerializeObject(new AppSettings
                {
                    Plc = { Mode = "Melsec", Host = "192.168.0.10" }
                }));
                var store = new AppSettingsStore(path);

                var result = store.Load();

                Assert.Equal("Melsec", result.Settings.Plc.Mode);
                Assert.True(result.RecoveredFromBackup);
                Assert.True(Directory.GetFiles(dir, "settings.json.bad-*").Length == 1);
                Assert.Contains("配置损坏", result.Warning);
            }
            finally
            {
                if (Directory.Exists(dir)) Directory.Delete(dir, true);
            }
        }
    }
}
