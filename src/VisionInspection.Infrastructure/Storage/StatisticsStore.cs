using System.IO;
using System;
using Newtonsoft.Json;
using VisionInspection.Core.Abstractions;

namespace VisionInspection.Infrastructure.Storage
{
    /// <summary>
    /// 运行统计的 JSON 持久化(stats.json),使产量/良率在重启后延续。
    /// 读写失败均静默(统计非关键路径,不应阻断运行)。
    /// </summary>
    public sealed class StatisticsStore : IStatisticsStore
    {
        /// <summary>可序列化的统计快照。</summary>
        private readonly string _path;
        private readonly object _sync = new object();

        public event Action<string> Warning;

        public StatisticsStore(string path)
        {
            _path = path;
        }

        public StatisticsSnapshot Load()
        {
            try
            {
                if (File.Exists(_path))
                    return JsonConvert.DeserializeObject<StatisticsSnapshot>(File.ReadAllText(_path));
            }
            catch (Exception ex)
            {
                Warning?.Invoke("统计文件损坏，已忽略历史统计：" + ex.Message);
            }
            return null;
        }

        public void Save(long total, long ok, long ng, long error)
        {
            var json = JsonConvert.SerializeObject(
                new StatisticsSnapshot { Total = total, Ok = ok, Ng = ng, Error = error }, Formatting.Indented);
            try
            {
                lock (_sync)
                {
                    AtomicFile.WriteText(_path, json, backup: true);
                }
            }
            catch (Exception ex)
            {
                Warning?.Invoke("统计落盘失败：" + ex.Message);
            }
        }
    }
}
