namespace VisionInspection.Core.Models
{
    /// <summary>整体检测结论。</summary>
    public enum InspectionOutcome
    {
        /// <summary>全部工位合格。</summary>
        Ok = 0,

        /// <summary>存在缺件。</summary>
        Ng = 1,

        /// <summary>检测异常（无匹配配方 / 相机故障 / 配准失败等），不可判定。</summary>
        Error = 2
    }
}
