using System;

namespace VisionInspection.Core.Abstractions
{
    public sealed class StatisticsSnapshot
    {
        public long Total { get; set; }
        public long Ok { get; set; }
        public long Ng { get; set; }
        public long Error { get; set; }
    }

    public interface IStatisticsStore
    {
        event Action<string> Warning;
        StatisticsSnapshot Load();
        void Save(long total, long ok, long ng, long error);
    }
}
