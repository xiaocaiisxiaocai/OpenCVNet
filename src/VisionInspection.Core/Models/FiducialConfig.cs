using System.Collections.Generic;

namespace VisionInspection.Core.Models
{
    /// <summary>定位基准类型（用于底板配准，消除摆放平移 / 旋转偏差）。</summary>
    public enum FiducialType
    {
        /// <summary>不配准（仅适用于底板严格固定的场景，不推荐）。</summary>
        None = 0,

        /// <summary>基于底板角点 / 轮廓配准。</summary>
        BoardCorners = 1,

        /// <summary>基于 Mark 点配准。</summary>
        MarkPoints = 2
    }

    /// <summary>
    /// 定位基准配置。里程碑 2 将据此计算仿射变换，把标定坐标系 ROI 映射到当前图像。
    /// </summary>
    public sealed class FiducialConfig
    {
        public FiducialType Type { get; set; } = FiducialType.None;

        /// <summary>基准搜索区域（Mark / 角点大致位置），加速并稳定定位。</summary>
        public List<RoiRect> SearchRegions { get; set; } = new List<RoiRect>();

        /// <summary>要求至少检出的基准点数量；0 表示必须检出全部配置搜索区。</summary>
        public int MinDetectedMarks { get; set; } = 0;

        public double MaxResidualPixels { get; set; } = 8.0;
        public double MaxRmsResidualPixels { get; set; } = 5.0;
        public double MinScale { get; set; } = 0.9;
        public double MaxScale { get; set; } = 1.1;
        public double MaxRotationDegrees { get; set; } = 15.0;
    }
}
