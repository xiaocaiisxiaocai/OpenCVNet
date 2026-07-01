using System.Collections.Generic;
using OpenCvSharp;
using VisionInspection.Core.Models;
using VisionInspection.Vision.Alignment;
using Xunit;

namespace VisionInspection.Tests
{
    /// <summary>
    /// 用 OpenCvSharp 合成"带基准点、整体平移"的底板,验证 <see cref="FiducialAlignment"/>
    /// 能求出正确的仿射变换(把标定坐标映射回当前图像),无需真实相机。
    /// </summary>
    public class FiducialAlignmentTests
    {
        private const int W = 640, H = 480;

        // 标定期望位(= 搜索区中心)
        private static readonly Point[] CalibMarks = { new Point(100, 100), new Point(500, 100), new Point(300, 400) };

        /// <summary>亮底 + 暗方块基准点;marks 按 (dx,dy) 整体平移。</summary>
        private static Mat BuildBoard(int dx, int dy)
        {
            var img = new Mat(H, W, MatType.CV_8UC3, new Scalar(220, 220, 220));
            foreach (var m in CalibMarks)
                Cv2.Rectangle(img, new Rect(m.X - 15 + dx, m.Y - 15 + dy, 30, 30), new Scalar(30, 30, 30), -1);
            return img;
        }

        private static FiducialConfig ThreeMarkConfig()
        {
            var cfg = new FiducialConfig { Type = FiducialType.MarkPoints };
            foreach (var m in CalibMarks)                       // 搜索区 80×80,居中于标定位
                cfg.SearchRegions.Add(new RoiRect(m.X - 40, m.Y - 40, 80, 80));
            return cfg;
        }

        private static (double x, double y) Apply(Mat a, double x, double y) =>
            (a.At<double>(0, 0) * x + a.At<double>(0, 1) * y + a.At<double>(0, 2),
             a.At<double>(1, 0) * x + a.At<double>(1, 1) * y + a.At<double>(1, 2));

        [Fact]
        public void Recovers_Translation_From_Three_Marks()
        {
            const int dx = 20, dy = 15;
            using (var board = BuildBoard(dx, dy))
            {
                var result = new FiducialAlignment().Align(board, ThreeMarkConfig());

                Assert.True(result.Success);
                // 标定点 (300,300) 应映射到当前图像 (300+dx, 300+dy) 附近
                var (mx, my) = Apply(result.Affine, 300, 300);
                Assert.InRange(mx, 300 + dx - 2, 300 + dx + 2);
                Assert.InRange(my, 300 + dy - 2, 300 + dy + 2);
            }
        }

        [Fact]
        public void No_Fiducial_Falls_Back_To_Identity()
        {
            using (var board = BuildBoard(0, 0))
            {
                var result = new FiducialAlignment().Align(board, new FiducialConfig()); // Type=None

                Assert.True(result.Success);
                var (mx, my) = Apply(result.Affine, 123, 456);
                Assert.Equal(123, mx, 3);   // 恒等
                Assert.Equal(456, my, 3);
            }
        }

        [Fact]
        public void No_Marks_Detected_Fails()
        {
            // 纯净亮底、无基准点 → 搜索区检不出 → 配准失败
            using (var blank = new Mat(H, W, MatType.CV_8UC3, new Scalar(220, 220, 220)))
            {
                var result = new FiducialAlignment().Align(blank, ThreeMarkConfig());
                Assert.False(result.Success);
            }
        }
    }
}
