using System;
using System.Collections.Generic;
using System.Linq;
using OpenCvSharp;
using VisionInspection.Core.Imaging;
using VisionInspection.Core.Models;
using VisionInspection.Vision.Alignment;
using VisionInspection.Vision.Detection;
using VisionInspection.Vision.Imaging;

namespace VisionInspection.Vision.Teaching
{
    /// <summary>
    /// 阈值示教：对“有件”“无件”样本集分别打分，为每工位自动标定阈值（取两类得分中点）。
    /// 阈值方向由样本自动决定，无需人工判断亮 / 暗。
    /// </summary>
    public sealed class ThresholdTeacher
    {
        private readonly IAlignment _alignment;
        private readonly IReadOnlyDictionary<DetectionMethod, IPresenceDetector> _detectors;

        public ThresholdTeacher(IAlignment alignment = null, IEnumerable<IPresenceDetector> detectors = null)
        {
            _alignment = alignment ?? new IdentityAlignment();
            var map = new Dictionary<DetectionMethod, IPresenceDetector>();
            foreach (var d in detectors ?? new IPresenceDetector[] { new ForegroundRatioDetector() })
                map[d.Method] = d;
            _detectors = map;
        }

        /// <summary>
        /// 根据样本标定阈值并写回配方各工位。
        /// <paramref name="presentSamples"/> 为满件图，<paramref name="absentSamples"/> 为缺件图。
        /// 返回每工位建议阈值（工位 Index → threshold）；样本不足的工位保持原阈值。
        /// </summary>
        public IReadOnlyDictionary<int, double> Teach(
            Recipe recipe,
            IEnumerable<ImageFrame> presentSamples,
            IEnumerable<ImageFrame> absentSamples)
        {
            if (recipe == null) throw new ArgumentNullException(nameof(recipe));

            var present = ScoreAll(recipe, presentSamples);
            var absent = ScoreAll(recipe, absentSamples);

            var thresholds = new Dictionary<int, double>();
            foreach (var st in recipe.Stations)
            {
                bool hasP = present.TryGetValue(st.Index, out var ps) && ps.Count > 0;
                bool hasA = absent.TryGetValue(st.Index, out var av) && av.Count > 0;
                if (!hasP || !hasA) continue;

                double minPresent = ps.Min();
                double maxAbsent = av.Max();
                double threshold = (minPresent + maxAbsent) / 2.0;
                st.Threshold = threshold;
                thresholds[st.Index] = threshold;
            }
            return thresholds;
        }

        private Dictionary<int, List<double>> ScoreAll(Recipe recipe, IEnumerable<ImageFrame> samples)
        {
            var acc = new Dictionary<int, List<double>>();
            if (samples == null) return acc;

            foreach (var frame in samples)
            {
                using (var image = MatConverter.ToMat(frame))
                {
                    var align = _alignment.Align(image, recipe.Fiducial);
                    try
                    {
                        if (!align.Success) continue;
                        foreach (var st in recipe.Stations)
                        {
                            var mapped = RoiMapper.Clamp(RoiMapper.Map(st.Roi, align.Affine), image.Width, image.Height);
                            if (mapped.Width <= 0 || mapped.Height <= 0) continue;

                            using (var roi = new Mat(image, new Rect(mapped.X, mapped.Y, mapped.Width, mapped.Height)))
                            {
                                var det = _detectors.TryGetValue(st.Method, out var d)
                                    ? d
                                    : _detectors[DetectionMethod.ForegroundRatio];
                                var output = det.Detect(roi, st);
                                if (!acc.TryGetValue(st.Index, out var list))
                                {
                                    list = new List<double>();
                                    acc[st.Index] = list;
                                }
                                list.Add(output.Score);
                            }
                        }
                    }
                    finally
                    {
                        align.Affine?.Dispose();
                    }
                }
            }
            return acc;
        }
    }
}
