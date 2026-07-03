namespace VisionInspection.Core.Models
{
    /// <summary>
    /// 单个工位定义。板件大小不一 → 每工位拥有各自大小的 ROI 与独立阈值。
    /// </summary>
    public sealed class Station
    {
        /// <summary>全局工位序号（0 基），决定其在缺件位图中的 bit 位置，须在配方内唯一。</summary>
        public int Index { get; set; }

        /// <summary>逻辑行号（0 基，仅用于显示 / 排序）。</summary>
        public int Row { get; set; }

        /// <summary>逻辑列号（0 基，仅用于显示 / 排序）。</summary>
        public int Column { get; set; }

        public string Name { get; set; }

        /// <summary>该工位 ROI（标定坐标系，大小可各异）。</summary>
        public RoiRect Roi { get; set; }

        public DetectionMethod Method { get; set; } = DetectionMethod.ForegroundRatio;

        /// <summary>判定阈值，含义随 <see cref="Method"/>（如前景占比阈值 0~1）。</summary>
        public double Threshold { get; set; } = 0.5;

        /// <summary>可选工位级前景极性；null 表示继承全局检测器配置。</summary>
        public bool? DarkIsForeground { get; set; }

        /// <summary>是否启用该工位检测（false 则跳过，不计入判定）。</summary>
        public bool Enabled { get; set; } = true;

        /// <summary>“无件”基准图相对路径（BaselineDiff / TemplateMatch 使用），里程碑 2 填充。</summary>
        public string BaselineImagePath { get; set; }
    }
}
