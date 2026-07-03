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

            var presentBright = ScoreAll(recipe, presentSamples, darkForeground: false);
            var absentBright = ScoreAll(recipe, absentSamples, darkForeground: false);
            var presentDark = ScoreAll(recipe, presentSamples, darkForeground: true);
            var absentDark = ScoreAll(recipe, absentSamples, darkForeground: true);

            var thresholds = new Dictionary<int, double>();
            foreach (var st in recipe.Stations)
            {
                var bright = TryBuildThreshold(presentBright, absentBright, st.Index, out var brightThreshold, out var brightMargin);
                var dark = TryBuildThreshold(presentDark, absentDark, st.Index, out var darkThreshold, out var darkMargin);
                if (!bright && !dark) continue;

                bool useDark = dark && (!bright || darkMargin > brightMargin);
                double threshold = useDark ? darkThreshold : brightThreshold;
                if (threshold < 0 || threshold > 1)
                    throw new InvalidOperationException($"工位 {st.Index} 满件/缺件样本不可分，请检查光源、ROI 或极性。");
                st.Threshold = threshold;
                st.DarkIsForeground = useDark;
                thresholds[st.Index] = threshold;
            }
            return thresholds;
        }

        private static bool TryBuildThreshold(
            Dictionary<int, List<double>> present,
            Dictionary<int, List<double>> absent,
            int index,
            out double threshold,
            out double margin)
        {
            threshold = -1;
            margin = -1;
            bool hasP = present.TryGetValue(index, out var ps) && ps.Count > 0;
            bool hasA = absent.TryGetValue(index, out var av) && av.Count > 0;
            if (!hasP || !hasA) return false;

            double minPresent = ps.Min();
            double maxAbsent = av.Max();
            margin = minPresent - maxAbsent;
            if (margin <= 0) return false;
            threshold = (minPresent + maxAbsent) / 2.0;
            return true;
        }

        private Dictionary<int, List<double>> ScoreAll(Recipe recipe, IEnumerable<ImageFrame> samples, bool darkForeground)
        {
            var acc = new Dictionary<int, List<double>>();
            if (samples == null) return acc;

            foreach (var frame in samples)
            {
                using (var image = MatConverter.ToMat(frame))
                {
                    var align = _alignment.Align(image, recipe.Fiducial);
                    if (!align.Success) continue;
                    Mat sampleImage = image;
                    try
                    {
                        sampleImage = WarpToCalibration(image, align);
                        foreach (var st in recipe.Stations)
                        {
                            var mapped = RoiMapper.Clamp(st.Roi, sampleImage.Width, sampleImage.Height);
                            if (mapped.Width <= 0 || mapped.Height <= 0) continue;

                            using (var roi = new Mat(sampleImage, new Rect(mapped.X, mapped.Y, mapped.Width, mapped.Height)))
                            {
                                if (!_detectors.TryGetValue(st.Method, out var det))
                                    continue;
                                var original = st.DarkIsForeground;
                                st.DarkIsForeground = darkForeground;
                                DetectionOutput output;
                                try { output = det.Detect(roi, st); }
                                finally { st.DarkIsForeground = original; }
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
                        if (!ReferenceEquals(sampleImage, image)) sampleImage.Dispose();
                    }
                }
            }
            return acc;
        }

        private static Mat WarpToCalibration(Mat image, AlignmentResult alignment)
        {
            using (var affine = alignment.ToMat())
            using (var inverse = new Mat())
            {
                Cv2.InvertAffineTransform(affine, inverse);
                var warped = new Mat();
                Cv2.WarpAffine(image, warped, inverse, new Size(image.Width, image.Height),
                    InterpolationFlags.Linear, BorderTypes.Constant, Scalar.Black);
                return warped;
            }
        }
    }
}
