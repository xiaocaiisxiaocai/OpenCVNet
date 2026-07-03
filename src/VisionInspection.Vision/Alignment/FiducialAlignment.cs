using System;
using System.Collections.Generic;
using OpenCvSharp;
using VisionInspection.Core.Models;

namespace VisionInspection.Vision.Alignment
{
    /// <summary>
    /// 基于基准点(Mark/角点)的配准：在各搜索区内检出实际基准位置，与标定期望位(搜索区中心)
    /// 建立对应,估计"标定坐标系 → 当前图像"的仿射变换,补偿底板摆放的平移/旋转/缩放偏差。
    /// <para>≥3 点估计全仿射,2 点估计相似变换,1 点仅平移;Type=None 或无搜索区时退化为恒等。</para>
    /// <para>注：搜索区中心即标定期望位——现场标定时应使基准点位于各搜索区中心。</para>
    /// </summary>
    public sealed class FiducialAlignment : IAlignment
    {
        public AlignmentResult Align(Mat image, FiducialConfig fiducial)
        {
            if (image == null || fiducial == null || fiducial.Type == FiducialType.None ||
                fiducial.SearchRegions == null || fiducial.SearchRegions.Count == 0)
                return AlignmentResult.Identity();

            var refPts = new List<Point2f>(); // 标定期望位(搜索区中心)
            var curPts = new List<Point2f>(); // 当前图检出位
            foreach (var region in fiducial.SearchRegions)
            {
                if (TryLocateMark(image, region, out var found))
                {
                    refPts.Add(new Point2f(region.X + region.Width / 2f, region.Y + region.Height / 2f));
                    curPts.Add(found);
                }
            }

            if (curPts.Count == 0)
                return AlignmentResult.Fail("未在搜索区检出任何基准点");

            int requiredMarks = fiducial.MinDetectedMarks > 0
                ? fiducial.MinDetectedMarks
                : fiducial.SearchRegions.Count;
            if (curPts.Count < requiredMarks)
                return AlignmentResult.Fail($"基准点数量不足({curPts.Count}/{requiredMarks})。");

            Mat affine;
            if (curPts.Count >= 3)
                affine = Cv2.EstimateAffine2D(InputArray.Create(refPts.ToArray()), InputArray.Create(curPts.ToArray()));
            else if (curPts.Count == 2)
                affine = Cv2.EstimateAffinePartial2D(InputArray.Create(refPts.ToArray()), InputArray.Create(curPts.ToArray()));
            else
                affine = Translation(curPts[0].X - refPts[0].X, curPts[0].Y - refPts[0].Y);

            if (affine == null || affine.Empty())
                return AlignmentResult.Fail("基准点求解仿射失败(点数不足或退化)");

            var quality = CheckQuality(affine, refPts, curPts, fiducial);
            if (quality != null)
            {
                affine.Dispose();
                return AlignmentResult.Fail(quality);
            }

            var result = new AlignmentResult(true, ToArray(affine));
            affine.Dispose();
            return result;
        }

        /// <summary>在搜索区内检出最显著 blob 的质心(两种极性各试),返回图像绝对坐标。</summary>
        private static bool TryLocateMark(Mat image, RoiRect region, out Point2f center)
        {
            center = default;
            int x = Math.Max(0, region.X);
            int y = Math.Max(0, region.Y);
            int right = Math.Min(image.Width, region.Right);
            int bottom = Math.Min(image.Height, region.Bottom);
            int w = right - x, h = bottom - y;
            if (w < 3 || h < 3) return false;

            using (var roi = new Mat(image, new Rect(x, y, w, h)))
            using (var gray = new Mat())
            {
                if (roi.Channels() == 1) roi.CopyTo(gray);
                else Cv2.CvtColor(roi, gray, ColorConversionCodes.BGR2GRAY);

                double area = (double)w * h;
                if (TryCentroid(gray, false, area, out var c) || TryCentroid(gray, true, area, out c))
                {
                    center = new Point2f(x + c.X, y + c.Y);
                    return true;
                }
            }
            return false;
        }

        private static bool TryCentroid(Mat gray, bool invert, double regionArea, out Point2f c)
        {
            c = default;
            using (var bin = new Mat())
            {
                var type = ThresholdTypes.Otsu | (invert ? ThresholdTypes.BinaryInv : ThresholdTypes.Binary);
                Cv2.Threshold(gray, bin, 0, 255, type);
                Cv2.FindContours(bin, out Point[][] contours, out _,
                    RetrievalModes.External, ContourApproximationModes.ApproxSimple);

                Point[] best = null;
                double bestArea = 0;
                foreach (var ct in contours)
                {
                    double a = Cv2.ContourArea(ct);
                    if (a > bestArea) { bestArea = a; best = ct; }
                }
                // 排除过小噪点与近乎占满(背景被当前景)的情况
                if (best == null || bestArea < regionArea * 0.01 || bestArea > regionArea * 0.85)
                    return false;

                var m = Cv2.Moments(best);
                if (m.M00 == 0) return false;
                c = new Point2f((float)(m.M10 / m.M00), (float)(m.M01 / m.M00));
                return true;
            }
        }

        private static Mat Translation(double dx, double dy)
        {
            var m = new Mat(2, 3, MatType.CV_64FC1);
            m.Set(0, 0, 1.0); m.Set(0, 1, 0.0); m.Set(0, 2, dx);
            m.Set(1, 0, 0.0); m.Set(1, 1, 1.0); m.Set(1, 2, dy);
            return m;
        }

        private static double[] ToArray(Mat affine)
        {
            return new[]
            {
                affine.At<double>(0, 0),
                affine.At<double>(0, 1),
                affine.At<double>(0, 2),
                affine.At<double>(1, 0),
                affine.At<double>(1, 1),
                affine.At<double>(1, 2)
            };
        }

        private static string CheckQuality(Mat affine, IReadOnlyList<Point2f> refPts, IReadOnlyList<Point2f> curPts, FiducialConfig fiducial)
        {
            double maxResidual = 0;
            double sumResidual2 = 0;
            for (int i = 0; i < refPts.Count; i++)
            {
                double x = affine.At<double>(0, 0) * refPts[i].X +
                           affine.At<double>(0, 1) * refPts[i].Y +
                           affine.At<double>(0, 2);
                double y = affine.At<double>(1, 0) * refPts[i].X +
                           affine.At<double>(1, 1) * refPts[i].Y +
                           affine.At<double>(1, 2);
                double dx = x - curPts[i].X;
                double dy = y - curPts[i].Y;
                double residual = Math.Sqrt(dx * dx + dy * dy);
                maxResidual = Math.Max(maxResidual, residual);
                sumResidual2 += residual * residual;
            }

            double rmsResidual = Math.Sqrt(sumResidual2 / refPts.Count);
            if (maxResidual > fiducial.MaxResidualPixels || rmsResidual > fiducial.MaxRmsResidualPixels)
                return $"配准残差超限(max={maxResidual:F1}px,rms={rmsResidual:F1}px)。";

            double a = affine.At<double>(0, 0);
            double b = affine.At<double>(0, 1);
            double c = affine.At<double>(1, 0);
            double d = affine.At<double>(1, 1);
            double det = a * d - b * c;
            if (det <= 0) return "配准矩阵方向异常。";

            double sx = Math.Sqrt(a * a + c * c);
            double sy = Math.Sqrt(b * b + d * d);
            if (sx < fiducial.MinScale || sx > fiducial.MaxScale || sy < fiducial.MinScale || sy > fiducial.MaxScale)
                return "配准尺度超限。";

            double angle = Math.Atan2(c, a) * 180.0 / Math.PI;
            if (Math.Abs(angle) > fiducial.MaxRotationDegrees)
                return "配准旋转角超限。";

            return null;
        }
    }
}
