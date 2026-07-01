namespace VisionInspection.Core.Models
{
    /// <summary>单工位有无判定状态。</summary>
    public enum PresenceState
    {
        /// <summary>未判定 / 不确定（异常，通常按安全侧当作缺件处理）。</summary>
        Unknown = 0,

        /// <summary>有件。</summary>
        Present = 1,

        /// <summary>缺件。</summary>
        Absent = 2
    }
}
