using System;
using System.Collections.Generic;
using System.Globalization;

namespace VisionInspection.Core.Models
{
    /// <summary>配方一致性校验，防止坏配方进入检测关键路径。</summary>
    public static class RecipeValidator
    {
        public static void Validate(Recipe recipe, int bitmapWordCount)
        {
            if (recipe == null) throw new ArgumentNullException(nameof(recipe));
            if (bitmapWordCount <= 0) throw new ArgumentOutOfRangeException(nameof(bitmapWordCount));
            if (string.IsNullOrWhiteSpace(recipe.ModelCode))
                throw new ArgumentException("配方型号码不能为空。", nameof(recipe));
            if (!ushort.TryParse(recipe.ModelCode, NumberStyles.None, CultureInfo.InvariantCulture, out _))
                throw new ArgumentException("配方型号码必须是 0~65535 的数字。", nameof(recipe));
            if (recipe.Stations == null || recipe.Stations.Count == 0)
                throw new ArgumentException("配方至少需要 1 个工位。", nameof(recipe));

            ValidateFiducial(recipe.Fiducial);

            int capacity = bitmapWordCount * 16;
            var seen = new HashSet<int>();
            foreach (var station in recipe.Stations)
            {
                if (station.Index < 0)
                    throw new ArgumentException("工位序号不能为负数。", nameof(recipe));
                if (station.Index >= capacity)
                    throw new ArgumentException($"工位序号 {station.Index} 超出位图容量 {capacity}。", nameof(recipe));
                if (!seen.Add(station.Index))
                    throw new ArgumentException($"工位序号重复：{station.Index}。", nameof(recipe));
                if (station.Threshold < 0 || station.Threshold > 1)
                    throw new ArgumentException($"工位 {station.Index} 阈值必须在 0~1。", nameof(recipe));
                if (station.Roi.Width <= 0 || station.Roi.Height <= 0)
                    throw new ArgumentException($"工位 {station.Index} ROI 不能为空。", nameof(recipe));
            }
        }

        private static void ValidateFiducial(FiducialConfig fiducial)
        {
            if (fiducial == null) return;
            if (fiducial.MinDetectedMarks < 0)
                throw new ArgumentException("基准点最小检出数量不能为负数。");
            if (fiducial.MaxResidualPixels <= 0 || fiducial.MaxRmsResidualPixels <= 0)
                throw new ArgumentException("配准残差阈值必须大于 0。");
            if (fiducial.MinScale <= 0 || fiducial.MaxScale <= 0 || fiducial.MinScale > fiducial.MaxScale)
                throw new ArgumentException("配准尺度范围非法。");
            if (fiducial.MaxRotationDegrees < 0 || fiducial.MaxRotationDegrees > 180)
                throw new ArgumentException("配准旋转角阈值必须在 0~180。");
        }
    }
}
