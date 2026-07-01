using System.IO;
using Newtonsoft.Json;

namespace VisionInspection.Infrastructure.Storage
{
    /// <summary>
    /// 运行统计的 JSON 持久化(stats.json),使产量/良率在重启后延续。
    /// 读写失败均静默(统计非关键路径,不应阻断运行)。
    /// </summary>
    public sealed class StatisticsStore
    {
        /// <summary>可序列化的统计快照。</summary>
        public sealed class Snapshot
        {
            public long Total { get; set; }
            public long Ok { get; set; }
            public long Ng { get; set; }
            public long Error { get; set; }
        }

        private readonly string _path;
        private readonly object _sync = new object();

        public StatisticsStore(string path)
        {
            _path = path;
        }

        public Snapshot Load()
        {
            try
            {
                if (File.Exists(_path))
                    return JsonConvert.DeserializeObject<Snapshot>(File.ReadAllText(_path));
            }
            catch { /* 损坏则视为无历史 */ }
            return null;
        }

        public void Save(long total, long ok, long ng, long error)
        {
            var json = JsonConvert.SerializeObject(
                new Snapshot { Total = total, Ok = ok, Ng = ng, Error = error }, Formatting.Indented);
            try
            {
                lock (_sync) File.WriteAllText(_path, json);
            }
            catch { /* 落盘失败不影响运行 */ }
        }
    }
}
