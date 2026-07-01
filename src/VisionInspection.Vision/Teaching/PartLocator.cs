using System;
using System.Collections.Generic;
using System.Linq;
using OpenCvSharp;
using VisionInspection.Core.Imaging;
using VisionInspection.Core.Models;
using VisionInspection.Vision.Imaging;

namespace VisionInspection.Vision.Teaching
{
    /// <summary>件相对底图的明暗极性。</summary>
    public enum PartPolarity
    {
        /// <summary>自动：两种极性各试一遍，取识别结果更合理者。</summary>
        Auto,
        /// <summary>亮件：件比底板亮（常规正照）。</summary>
        BrightParts,
        /// <summary>暗件：件比底板暗（背光挡光变暗）。</summary>
        DarkParts
    }

    /// <summary>自动定位参数。</summary>
    public sealed class PartLocatorOptions
    {
        /// <summary>件明暗极性。</summary>
        public PartPolarity Polarity { get; set; } = PartPolarity.Auto;

        /// <summary>单件最小面积占全图比例（滤除噪点/碎块）。</summary>
        public double MinAreaRatio { get; set; } = 0.003;

        /// <summary>单件最大面积占全图比例（滤除近乎整图的连通域）。</summary>
        public double MaxAreaRatio { get; set; } = 0.6;

        /// <summary>形态学核尺寸（奇数，去噪与填洞）。</summary>
        public int MorphKernel { get; set; } = 5;

        /// <summary>在贴合外框基础上向外扩张的像素（0=紧贴）。</summary>
        public int InflatePixels { get; set; }
    }

    /// <summary>定位到的单件：贴合外框 + 行主序推得的行列号。</summary>
    public struct LocatedPart
    {
        public RoiRect Roi { get; }
        public int Row { get; }
        public int Column { get; }

        public LocatedPart(RoiRect roi, int row, int column)
        {
            Roi = roi;
            Row = row;
            Column = column;
        }
    }

    /// <summary>
    /// 从标定底图自动定位板件位置，生成贴合各件的 ROI。
    /// 相比等距网格「打底」，本类按件的真实轮廓出框，免去大量手动微调。
    /// 流程：灰度 → Otsu 二值（按极性） → 形态学去噪 → 外轮廓 → 面积过滤 → 行主序排序。
    /// </summary>
    public sealed class PartLocator
    {
        /// <summary>在底图上定位板件，返回按行主序（上→下、左→右）排列的贴合外框。</summary>
        public IReadOnlyList<LocatedPart> Locate(ImageFrame frame, PartLocatorOptions options = null)
        {
            if (frame == null) throw new ArgumentNullException(nameof(frame));
            var opt = options ?? new PartLocatorOptions();

            using (var image = MatConverter.ToMat(frame))
            using (var gray = new Mat())
            {
                if (image.Channels() == 1) image.CopyTo(gray);
                else Cv2.CvtColor(image, gray, ColorConversionCodes.BGR2GRAY);
                Cv2.GaussianBlur(gray, gray, new Size(3, 3), 0);

                double imgArea = (double)gray.Width * gray.Height;

                List<Rect> boxes;
                if (opt.Polarity == PartPolarity.BrightParts)
                    boxes = DetectBoxes(gray, invert: false, imgArea, opt);
                else if (opt.Polarity == PartPolarity.DarkParts)
                    boxes = DetectBoxes(gray, invert: true, imgArea, opt);
                else
                {
                    // 自动：两种极性各跑一遍，取“更合理”者（有效框更多；相等则前景面积更小者，
                    // 因为件通常不占满整图）。
                    var bright = DetectBoxes(gray, invert: false, imgArea, opt);
                    var dark = DetectBoxes(gray, invert: true, imgArea, opt);
                    boxes = ChooseBetter(bright, dark);
                }

                var ordered = OrderRowMajor(boxes);
                var result = new List<LocatedPart>(ordered.Count);
                foreach (var b in ordered)
                {
                    var inflated = Inflate(b.Box, opt.InflatePixels, gray.Width, gray.Height);
                    result.Add(new LocatedPart(
                        new RoiRect(inflated.X, inflated.Y, inflated.Width, inflated.Height),
                        b.Row, b.Column));
                }
                return result;
            }
        }

        private static List<Rect> DetectBoxes(Mat gray, bool invert, double imgArea, PartLocatorOptions opt)
        {
            var type = ThresholdTypes.Otsu | (invert ? ThresholdTypes.BinaryInv : ThresholdTypes.Binary);
            using (var bin = new Mat())
            {
                Cv2.Threshold(gray, bin, 0, 255, type);

                int k = Math.Max(1, opt.MorphKernel | 1); // 强制奇数
                using (var kernel = Cv2.GetStructuringElement(MorphShapes.Rect, new Size(k, k)))
                {
                    Cv2.MorphologyEx(bin, bin, MorphTypes.Open, kernel);
                    Cv2.MorphologyEx(bin, bin, MorphTypes.Close, kernel);
                }

                Cv2.FindContours(bin, out Point[][] contours, out _,
                    RetrievalModes.External, ContourApproximationModes.ApproxSimple);

                double min = opt.MinAreaRatio * imgArea;
                double max = opt.MaxAreaRatio * imgArea;
                var boxes = new List<Rect>();
                foreach (var c in contours)
                {
                    var r = Cv2.BoundingRect(c);
                    double area = (double)r.Width * r.Height;
                    if (area >= min && area <= max) boxes.Add(r);
                }
                return boxes;
            }
        }

        /// <summary>取“更合理”的极性结果。</summary>
        private static List<Rect> ChooseBetter(List<Rect> a, List<Rect> b)
        {
            if (a.Count != b.Count) return a.Count > b.Count ? a : b;
            if (a.Count == 0) return a;
            long areaA = a.Sum(r => (long)r.Width * r.Height);
            long areaB = b.Sum(r => (long)r.Width * r.Height);
            return areaA <= areaB ? a : b;
        }

        /// <summary>行主序排序：按 Y 分行（容差=中位高度*0.6），行内按 X 升序，并赋行列号。</summary>
        private static List<(Rect Box, int Row, int Column)> OrderRowMajor(List<Rect> boxes)
        {
            var result = new List<(Rect, int, int)>();
            if (boxes.Count == 0) return result;

            var byY = boxes.OrderBy(r => r.Y + r.Height / 2.0).ToList();
            var heights = boxes.Select(r => r.Height).OrderBy(h => h).ToList();
            double medianH = heights[heights.Count / 2];
            double tol = Math.Max(1.0, medianH * 0.6);

            var rows = new List<List<Rect>>();
            var current = new List<Rect> { byY[0] };
            double bandCenter = byY[0].Y + byY[0].Height / 2.0;
            for (int i = 1; i < byY.Count; i++)
            {
                double cy = byY[i].Y + byY[i].Height / 2.0;
                if (cy - bandCenter <= tol)
                {
                    current.Add(byY[i]);
                }
                else
                {
                    rows.Add(current);
                    current = new List<Rect> { byY[i] };
                }
                bandCenter = cy;
            }
            rows.Add(current);

            for (int r = 0; r < rows.Count; r++)
            {
                var line = rows[r].OrderBy(b => b.X).ToList();
                for (int c = 0; c < line.Count; c++)
                    result.Add((line[c], r, c));
            }
            return result;
        }

        private static Rect Inflate(Rect r, int px, int w, int h)
        {
            if (px <= 0) return r;
            int x = Math.Max(0, r.X - px);
            int y = Math.Max(0, r.Y - px);
            int right = Math.Min(w, r.X + r.Width + px);
            int bottom = Math.Min(h, r.Y + r.Height + px);
            return new Rect(x, y, right - x, bottom - y);
        }
    }
}
