using System.Threading;
using VisionInspection.Core.Models;

namespace VisionInspection.Runtime
{
    /// <summary>运行统计（线程安全）。</summary>
    public sealed class RuntimeStatistics
    {
        private long _total, _ok, _ng, _error;

        public long Total => Interlocked.Read(ref _total);
        public long Ok => Interlocked.Read(ref _ok);
        public long Ng => Interlocked.Read(ref _ng);
        public long Error => Interlocked.Read(ref _error);

        /// <summary>良率 = OK / 总数。</summary>
        public double YieldRate
        {
            get { var t = Total; return t == 0 ? 0.0 : (double)Ok / t; }
        }

        public void Record(InspectionResult r)
        {
            Interlocked.Increment(ref _total);
            switch (r.Outcome)
            {
                case InspectionOutcome.Ok: Interlocked.Increment(ref _ok); break;
                case InspectionOutcome.Ng: Interlocked.Increment(ref _ng); break;
                default: Interlocked.Increment(ref _error); break;
            }
        }

        public void Reset()
        {
            Interlocked.Exchange(ref _total, 0);
            Interlocked.Exchange(ref _ok, 0);
            Interlocked.Exchange(ref _ng, 0);
            Interlocked.Exchange(ref _error, 0);
        }

        /// <summary>抓取当前计数快照（用于持久化）。</summary>
        public (long Total, long Ok, long Ng, long Error) Capture()
            => (Total, Ok, Ng, Error);

        /// <summary>从持久化快照恢复计数（重启不清零）。</summary>
        public void Restore(long total, long ok, long ng, long error)
        {
            Interlocked.Exchange(ref _total, total);
            Interlocked.Exchange(ref _ok, ok);
            Interlocked.Exchange(ref _ng, ng);
            Interlocked.Exchange(ref _error, error);
        }
    }
}
