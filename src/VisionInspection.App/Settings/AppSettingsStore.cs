using System.IO;
using Newtonsoft.Json;

namespace VisionInspection.App.Settings
{
    /// <summary>应用设置的 JSON 持久化（settings.json）。</summary>
    public sealed class AppSettingsStore
    {
        private readonly string _path;

        public AppSettingsStore(string path)
        {
            _path = path;
        }

        public AppSettings LoadOrDefault()
        {
            try
            {
                if (File.Exists(_path))
                    return JsonConvert.DeserializeObject<AppSettings>(File.ReadAllText(_path)) ?? new AppSettings();
            }
            catch
            {
                // 配置损坏时回退默认，不阻断启动
            }
            return new AppSettings();
        }

        public void Save(AppSettings settings)
        {
            File.WriteAllText(_path, JsonConvert.SerializeObject(settings, Formatting.Indented));
        }
    }
}
