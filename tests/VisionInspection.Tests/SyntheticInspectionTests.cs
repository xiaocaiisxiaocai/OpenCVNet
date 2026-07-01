using System.Collections.Generic;
using System.Linq;
using OpenCvSharp;
using VisionInspection.Core.Imaging;
using VisionInspection.Core.Models;
using VisionInspection.Vision.Imaging;
using VisionInspection.Vision.Inspection;
using VisionInspection.Vision.Teaching;
using Xunit;

namespace VisionInspection.Tests
{
    /// <summary>
    /// 用 OpenCvSharp 合成 2×2 底板（200×200，黑底 + 白块=有件）做端到端检测验证，
    /// 无需真实相机与样张。
    /// </summary>
    public class SyntheticInspectionTests
    {
        /// <summary>构造底板：present[i]=true 时第 i 工位画一个白块（有件）。</summary>
        private static ImageFrame BuildBoard(bool[] present)
        {
            // 深灰底（贴近真实场景、非纯黑）——纯黑会掩盖自适应阈值对均匀空 ROI 的误判。
            using (var img = new Mat(200, 200, MatType.CV_8UC3, new Scalar(60, 60, 60)))
            {
                var centers = new[]
                {
                    new Point(50, 50), new Point(150, 50),
                    new Point(50, 150), new Point(150, 150)
                };
                for (int i = 0; i < 4; i++)
                    if (present[i])
                        Cv2.Rectangle(img, new Rect(centers[i].X - 30, centers[i].Y - 30, 60, 60), Scalar.White, -1);

                return MatConverter.ToFrame(img);
            }
        }

        private static Recipe BuildRecipe()
        {
            var recipe = new Recipe { ModelCode = "TEST", Name = "合成底板", Rows = 2, Columns = 2 };
            var rois = new[]
            {
                new RoiRect(10, 10, 80, 80),
                new RoiRect(110, 10, 80, 80),
                new RoiRect(10, 110, 80, 80),
                new RoiRect(110, 110, 80, 80),
            };
            for (int i = 0; i < 4; i++)
                recipe.Stations.Add(new Station
                {
                    Index = i,
                    Row = i / 2,
                    Column = i % 2,
                    Roi = rois[i],
                    Threshold = 0.1
                });
            return recipe;
        }

        [Fact]
        public void All_Present_Yields_Ok()
        {
            var frame = BuildBoard(new[] { true, true, true, true });
            var result = new OpenCvInspector().Inspect(frame, BuildRecipe());

            Assert.Equal(InspectionOutcome.Ok, result.Outcome);
            Assert.Equal(0, result.MissingCount);
            Assert.All(result.Stations, s => Assert.True(s.IsPresent));
        }

        [Fact]
        public void One_Missing_Yields_Ng_And_Correct_Index()
        {
            var frame = BuildBoard(new[] { true, true, false, true }); // 工位 2 缺件
            var result = new OpenCvInspector().Inspect(frame, BuildRecipe());

            Assert.Equal(InspectionOutcome.Ng, result.Outcome);
            Assert.Equal(1, result.MissingCount);
            Assert.Equal(2, result.Stations.Single(s => s.IsMissing).StationIndex);
        }

        [Fact]
        public void Null_Recipe_Yields_Error()
        {
            var frame = BuildBoard(new[] { true, true, true, true });
            var result = new OpenCvInspector().Inspect(frame, null);

            Assert.Equal(InspectionOutcome.Error, result.Outcome);
            Assert.Equal("NO_RECIPE", result.ErrorCode);
        }

        [Fact]
        public void Teacher_Sets_Threshold_Between_Present_And_Absent()
        {
            var recipe = BuildRecipe();
            var present = new List<ImageFrame> { BuildBoard(new[] { true, true, true, true }) };
            var absent = new List<ImageFrame> { BuildBoard(new[] { false, false, false, false }) };

            var thresholds = new ThresholdTeacher().Teach(recipe, present, absent);

            Assert.Equal(4, thresholds.Count);
            // 满件占比约 0.56、缺件约 0，标定阈值应落在两者中间。
            Assert.All(thresholds.Values, t => Assert.InRange(t, 0.2, 0.4));
        }

        [Fact]
        public void MatConverter_Roundtrip_Preserves_Pixels()
        {
            using (var mat = new Mat(10, 12, MatType.CV_8UC3, new Scalar(10, 20, 30)))
            {
                var frame = MatConverter.ToFrame(mat);
                Assert.Equal(12, frame.Width);
                Assert.Equal(10, frame.Height);

                using (var back = MatConverter.ToMat(frame))
                {
                    var px = back.At<Vec3b>(5, 6);
                    Assert.Equal(10, px.Item0);
                    Assert.Equal(20, px.Item1);
                    Assert.Equal(30, px.Item2);
                }
            }
        }
    }
}
