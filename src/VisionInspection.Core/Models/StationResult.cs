namespace VisionInspection.Core.Models
{
    /// <summary>单工位检测结果。</summary>
    public sealed class StationResult
    {
        public int StationIndex { get; }
        public PresenceState State { get; }

        /// <summary>判定得分（如前景像素占比 0~1 或匹配相关度），用于调试与追溯。</summary>
        public double Score { get; }

        public double Threshold { get; }

        public StationResult(int stationIndex, PresenceState state, double score, double threshold)
        {
            StationIndex = stationIndex;
            State = state;
            Score = score;
            Threshold = threshold;
        }

        public bool IsPresent => State == PresenceState.Present;
        public bool IsMissing => State != PresenceState.Present;
    }
}
