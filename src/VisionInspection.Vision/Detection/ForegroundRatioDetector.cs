using OpenCvSharp;
using VisionInspection.Core.Models;

namespace VisionInspection.Vision.Detection
{
    /// <summary>
    /// 前景像素占比法：ROI 灰度化 → Otsu 二值 → 统计前景占比，与工位阈值比较。
    /// <para>默认“亮像素为前景”（正常照明下有件较亮）；背光场景有件挡光变暗，可将
    /// <see cref="DarkIsForeground"/> 置 true。阈值方向可交由示教依样本自动标定。</para>
    /// </summary>
    public sealed class ForegroundRatioDetector : IPresenceDetector
    {
        public DetectionMethod Method => DetectionMethod.ForegroundRatio;

        /// <summary>是否以“暗像素”为前景（背光场景选 true）。</summary>
        public bool DarkIsForeground { get; }

        /// <summary>
        /// 固定灰度阈值：灰度 &gt; 此值的像素视为“亮”。
        /// 用固定阈值而非 Otsu —— Otsu 对均匀空 ROI 无明暗双峰会退化，把整块误判为前景，导致缺件被判成有件。
        /// 现场按光照标定此值（或经示教）。
        /// </summary>
        public int GrayThreshold { get; }

        public ForegroundRatioDetector(bool darkIsForeground = false, int grayThreshold = 128)
        {
            DarkIsForeground = darkIsForeground;
            GrayThreshold = grayThreshold;
        }

        public DetectionOutput Detect(Mat roiImage, Station station)
        {
            using (var gray = ToGray(roiImage))
            using (var bin = new Mat())
            {
                // 固定阈值二值化：亮像素（> GrayThreshold）为前景。
                // 切勿用 Otsu：空 ROI（均匀背景）无双峰会被误判为全前景 → 缺件误判成有件。
                Cv2.Threshold(gray, bin, GrayThreshold, 255, ThresholdTypes.Binary);
                int white = Cv2.CountNonZero(bin);
                int total = bin.Rows * bin.Cols;
                if (total == 0) return new DetectionOutput(PresenceState.Unknown, 0.0);

                int fg = DarkIsForeground ? (total - white) : white;
                double ratio = (double)fg / total;
                var state = ratio >= station.Threshold ? PresenceState.Present : PresenceState.Absent;
                return new DetectionOutput(state, ratio);
            }
        }

        private static Mat ToGray(Mat src)
        {
            if (src.Channels() == 1) return src.Clone();
            var gray = new Mat();
            var code = src.Channels() == 4 ? ColorConversionCodes.BGRA2GRAY : ColorConversionCodes.BGR2GRAY;
            Cv2.CvtColor(src, gray, code);
            return gray;
        }
    }
}
