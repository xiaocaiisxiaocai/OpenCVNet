using System.Linq;
using OpenCvSharp;
using VisionInspection.Core.Imaging;
using VisionInspection.Vision.Imaging;
using VisionInspection.Vision.Teaching;
using Xunit;

namespace VisionInspection.Tests
{
    /// <summary>
    /// 用 OpenCvSharp 合成底图验证 <see cref="PartLocator"/> 自动定位：
    /// 2×3 排布的方块，缺件、亮件/暗件极性均覆盖，无需真实相机。
    /// </summary>
    public class PartLocatorTests
    {
        private const int W = 640, H = 480, Cols = 3, Rows = 2, Block = 120;

        // 各工位中心（与演示底板一致的等距排布）。
        private static Point Center(int r, int c) => new Point(c * (W / Cols) + (W / Cols) / 2, r * (H / Rows) + (H / Rows) / 2);

        /// <summary>合成底图：present[r*Cols+c]=true 时画一块。bright=false 时反相（亮底暗件）。</summary>
        private static ImageFrame BuildBoard(bool[] present, bool bright = true)
        {
            var bg = bright ? new Scalar(40, 40, 40) : new Scalar(210, 210, 210);
            var fg = bright ? new Scalar(220, 220, 220) : new Scalar(35, 35, 35);
            using (var img = new Mat(H, W, MatType.CV_8UC3, bg))
            {
                for (int r = 0; r < Rows; r++)
                    for (int c = 0; c < Cols; c++)
                        if (present[r * Cols + c])
                        {
                            var ctr = Center(r, c);
                            Cv2.Rectangle(img, new Rect(ctr.X - Block / 2, ctr.Y - Block / 2, Block, Block), fg, -1);
                        }
                return MatConverter.ToFrame(img);
            }
        }

        [Fact]
        public void Locates_All_Parts_RowMajor_With_TightBoxes()
        {
            var frame = BuildBoard(Enumerable.Repeat(true, Rows * Cols).ToArray());

            var parts = new PartLocator().Locate(frame);

            Assert.Equal(Rows * Cols, parts.Count);
            // 行列号应还原成 2 行 × 3 列
            Assert.Equal(Rows - 1, parts.Max(p => p.Row));
            Assert.Equal(Cols - 1, parts.Max(p => p.Column));

            // 行主序：第 0 个应是左上块，且外框贴合（≈Block 尺寸、中心对齐）。
            var first = parts[0];
            Assert.Equal(0, first.Row);
            Assert.Equal(0, first.Column);
            var c00 = Center(0, 0);
            Assert.InRange(first.Roi.X + first.Roi.Width / 2, c00.X - 12, c00.X + 12);
            Assert.InRange(first.Roi.Y + first.Roi.Height / 2, c00.Y - 12, c00.Y + 12);
            Assert.InRange(first.Roi.Width, Block - 20, Block + 20);
            Assert.InRange(first.Roi.Height, Block - 20, Block + 20);
        }

        [Fact]
        public void Skips_Missing_Part()
        {
            var present = Enumerable.Repeat(true, Rows * Cols).ToArray();
            present[4] = false; // 第 2 行中间缺件

            var parts = new PartLocator().Locate(frame: BuildBoard(present));

            Assert.Equal(Rows * Cols - 1, parts.Count);
        }

        [Fact]
        public void Auto_Polarity_Handles_Dark_Parts_On_Bright_Background()
        {
            var frame = BuildBoard(Enumerable.Repeat(true, Rows * Cols).ToArray(), bright: false);

            // 极性设为“自动”，应能识别亮底上的暗件。
            var parts = new PartLocator().Locate(frame, new PartLocatorOptions { Polarity = PartPolarity.Auto });

            Assert.Equal(Rows * Cols, parts.Count);
        }

        [Fact]
        public void No_Parts_On_Blank_Board_Returns_Empty()
        {
            var frame = BuildBoard(Enumerable.Repeat(false, Rows * Cols).ToArray());

            var parts = new PartLocator().Locate(frame);

            Assert.Empty(parts);
        }
    }
}
