using System.IO;
using System;
using Newtonsoft.Json;
using VisionInspection.Infrastructure.Storage;

namespace VisionInspection.App.Settings
{
    public sealed class AppSettingsLoadResult
    {
        public AppSettings Settings { get; }
        public bool UsedDefault { get; }
        public bool RecoveredFromBackup { get; }
        public string Warning { get; }

        public AppSettingsLoadResult(AppSettings settings, bool usedDefault, bool recoveredFromBackup, string warning)
        {
            Settings = settings;
            UsedDefault = usedDefault;
            RecoveredFromBackup = recoveredFromBackup;
            Warning = warning;
        }
    }

    /// <summary>应用设置的 JSON 持久化（settings.json）。</summary>
    public sealed class AppSettingsStore
    {
        private readonly string _path;

        public AppSettingsStore(string path)
        {
            _path = path;
        }

        public AppSettingsLoadResult Load()
        {
            if (!File.Exists(_path))
                return new AppSettingsLoadResult(new AppSettings(), true, false, null);

            try
            {
                var settings = Read(_path);
                return new AppSettingsLoadResult(settings, false, false, null);
            }
            catch (Exception ex)
            {
                var warning = "配置损坏，已隔离原文件：" + ex.Message;
                QuarantineBrokenFile(_path);

                var bak = _path + ".bak";
                if (File.Exists(bak))
                {
                    try
                    {
                        var backup = Read(bak);
                        return new AppSettingsLoadResult(backup, false, true, warning + "；已加载备份配置。");
                    }
                    catch (Exception bakEx)
                    {
                        warning += "；备份配置也无法加载：" + bakEx.Message;
                    }
                }

                return new AppSettingsLoadResult(new AppSettings(), true, false, warning + "；已回退默认配置。");
            }
        }

        public AppSettings LoadOrDefault() => Load().Settings;

        public void Save(AppSettings settings)
        {
            var json = JsonConvert.SerializeObject(settings, Formatting.Indented);
            AtomicFile.WriteText(_path, json, backup: true);
        }

        private static AppSettings Read(string path)
        {
            return JsonConvert.DeserializeObject<AppSettings>(File.ReadAllText(path)) ?? new AppSettings();
        }

        private static void QuarantineBrokenFile(string path)
        {
            if (!File.Exists(path)) return;
            var bad = path + ".bad-" + DateTime.Now.ToString("yyyyMMddHHmmssfff");
            try { File.Move(path, bad); }
            catch { }
        }
    }
}
