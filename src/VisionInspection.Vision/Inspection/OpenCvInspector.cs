using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using OpenCvSharp;
using VisionInspection.Core.Abstractions;
using VisionInspection.Core.Imaging;
using VisionInspection.Core.Models;
using VisionInspection.Vision.Alignment;
using VisionInspection.Vision.Detection;
using VisionInspection.Vision.Imaging;

namespace VisionInspection.Vision.Inspection
{
    /// <summary>
    /// 基于 OpenCvSharp 的检测引擎：转 Mat → 定位配准 → 按配方逐工位映射 ROI → 判有无 → 汇总。
    /// 工位相互独立，采用并行处理。任一工位非“有件”则整体判 NG。
    /// </summary>
    public sealed class OpenCvInspector : IInspector
    {
        private readonly IAlignment _alignment;
        private readonly IReadOnlyDictionary<DetectionMethod, IPresenceDetector> _detectors;

        public OpenCvInspector(IAlignment alignment = null, IEnumerable<IPresenceDetector> detectors = null)
        {
            _alignment = alignment ?? new IdentityAlignment();
            var map = new Dictionary<DetectionMethod, IPresenceDetector>();
            foreach (var d in detectors ?? DefaultDetectors())
                map[d.Method] = d;
            _detectors = map;
        }

        private static IEnumerable<IPresenceDetector> DefaultDetectors()
        {
            yield return new ForegroundRatioDetector();
        }

        public InspectionResult Inspect(ImageFrame frame, Recipe recipe)
            => Inspect(frame, recipe, CancellationToken.None);

        public InspectionResult Inspect(ImageFrame frame, Recipe recipe, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (frame == null) throw new ArgumentNullException(nameof(frame));
            if (recipe == null)
                return InspectionResult.CreateError(null, "NO_RECIPE", "配方为空。", DateTime.UtcNow);

            var sw = Stopwatch.StartNew();
            using (var image = MatConverter.ToMat(frame))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var align = _alignment.Align(image, recipe.Fiducial);
                    if (!align.Success)
                    {
                        return InspectionResult.CreateError(recipe.ModelCode, "ALIGN_FAIL",
                            align.Message ?? "定位配准失败。", DateTime.UtcNow);
                    }

                    var stations = recipe.Stations ?? new List<Station>();
                    if (stations.Count == 0 || stations.All(s => !s.Enabled))
                    {
                        return InspectionResult.CreateError(recipe.ModelCode, "NO_STATION",
                            "配方没有启用工位。", DateTime.UtcNow);
                    }

                    var results = new StationResult[stations.Count];
                    Mat inspectImage = image;
                    try
                    {
                        inspectImage = WarpToCalibration(image, align);
                        Parallel.For(0, stations.Count, i =>
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            try
                            {
                                results[i] = InspectStation(inspectImage, stations[i]);
                            }
                            catch
                            {
                                results[i] = new StationResult(stations[i].Index, PresenceState.Unknown, 0.0, stations[i].Threshold);
                            }
                        });
                    }
                    finally
                    {
                        if (!ReferenceEquals(inspectImage, image)) inspectImage.Dispose();
                    }

                    var list = new List<StationResult>(results);
                    var outcome = list.Any(r => r.State != PresenceState.Present)
                        ? InspectionOutcome.Ng
                        : InspectionOutcome.Ok;

                    sw.Stop();
                    return new InspectionResult(recipe.ModelCode, outcome, list, DateTime.UtcNow, (int)sw.ElapsedMilliseconds);
            }
        }

        private StationResult InspectStation(Mat image, Station st)
        {
            if (!st.Enabled)
                return new StationResult(st.Index, PresenceState.Present, 1.0, st.Threshold);

            var mapped = RoiMapper.Clamp(st.Roi, image.Width, image.Height);
            if (mapped.Width <= 0 || mapped.Height <= 0)
                return new StationResult(st.Index, PresenceState.Unknown, 0.0, st.Threshold);

            using (var roi = new Mat(image, new Rect(mapped.X, mapped.Y, mapped.Width, mapped.Height)))
            {
                if (!_detectors.TryGetValue(st.Method, out var detector))
                    return new StationResult(st.Index, PresenceState.Unknown, 0.0, st.Threshold);
                var output = detector.Detect(roi, st);
                return new StationResult(st.Index, output.State, output.Score, st.Threshold);
            }
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
