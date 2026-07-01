namespace VisionInspection.Core.Models
{
    /// <summary>单工位“有无”判定方法（由简到繁，可逐工位配置）。</summary>
    public enum DetectionMethod
    {
        /// <summary>ROI 内前景像素占比阈值法（默认，最简单可靠）。</summary>
        ForegroundRatio = 0,

        /// <summary>与“无件基准图”差分法。</summary>
        BaselineDiff = 1,

        /// <summary>模板匹配相关度法。</summary>
        TemplateMatch = 2
    }
}
