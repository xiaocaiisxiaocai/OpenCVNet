using System;
using System.Collections.Generic;

namespace VisionInspection.Core.Models
{
    /// <summary>
    /// 配方 = 一种产品。检测引擎完全由当前配方驱动（工位数量 / 大小 / 阈值均来自配方，不硬编码 N×M）。
    /// 运行时按 <see cref="ModelCode"/> 与 PLC 下发的型号码匹配切换。
    /// </summary>
    public sealed class Recipe
    {
        /// <summary>型号码（唯一键，与 PLC 下发的型号码匹配）。</summary>
        public string ModelCode { get; set; }

        public string Name { get; set; }

        /// <summary>逻辑行列数（仅用于显示 / 生成网格，物理尺寸以各工位 ROI 为准）。</summary>
        public int Rows { get; set; }
        public int Columns { get; set; }

        public FiducialConfig Fiducial { get; set; } = new FiducialConfig();

        public List<Station> Stations { get; set; } = new List<Station>();

        public int SchemaVersion { get; set; } = 1;
        public DateTime CreatedAtUtc { get; set; }
        public DateTime UpdatedAtUtc { get; set; }

        public int StationCount => Stations?.Count ?? 0;
    }
}
