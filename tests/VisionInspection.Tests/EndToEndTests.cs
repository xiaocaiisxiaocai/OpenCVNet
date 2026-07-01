using System;
using System.IO;
using VisionInspection.Camera.Simulation;
using VisionInspection.Core.Imaging;
using VisionInspection.Core.Models;
using VisionInspection.Infrastructure.Archiving;
using VisionInspection.Infrastructure.Storage;
using VisionInspection.Plc.Simulation;
using VisionInspection.Runtime;
using VisionInspection.Vision.Inspection;
using Xunit;

namespace VisionInspection.Tests
{
    /// <summary>
    /// 端到端：真实 OpenCvInspector + 模拟相机/PLC + 归档 + 握手，复现并固化「演示触发」修复
    /// （相机未 Open 时先确保连接，再触发，检测链路完整成功并留档）。
    /// </summary>
    public class EndToEndTests : IDisposable
    {
        private readonly string _recipeDir = Path.Combine(Path.GetTempPath(), "vi_e2e_r_" + Path.GetRandomFileName());
        private readonly string _archiveDir = Path.Combine(Path.GetTempPath(), "vi_e2e_a_" + Path.GetRandomFileName());

        public void Dispose()
        {
            if (Directory.Exists(_recipeDir)) Directory.Delete(_recipeDir, true);
            if (Directory.Exists(_archiveDir)) Directory.Delete(_archiveDir, true);
        }

        private static ImageFrame DemoFrame()
        {
            const int w = 200, h = 200, stride = w * 3;
            var data = new byte[stride * h];
            for (int i = 0; i < data.Length; i++) data[i] = 128; // 灰底，同 App 演示帧
            return new ImageFrame(w, h, stride, PixelFormat.Bgr24, data, DateTime.UtcNow);
        }

        [Fact]
        public void Demo_Trigger_Ensures_Connection_Then_Completes_And_Archives()
        {
            Directory.CreateDirectory(_recipeDir);
            var store = new JsonRecipeStore(_recipeDir);
            store.Save(new Recipe
            {
                ModelCode = "1",
                Stations = { new Station { Index = 0, Roi = new RoiRect(10, 10, 180, 180), Threshold = 0.05 } }
            });

            var camera = new SimulatedIndustrialCamera(DemoFrame); // 未 Open（复现启动后未点“启动运行”）
            var plc = new SimulatedPlcClient();                    // 未 Connect
            var archiver = new InspectionArchiver(_archiveDir);
            var runtime = new RuntimeService(camera, new OpenCvInspector(), plc, store, archiver);

            // App 的 simulate 委托逻辑（含修复：先确保连接）
            if (!camera.IsConnected) camera.Open();
            if (!plc.IsConnected) plc.Connect();
            plc.WriteInt16("D190", 1);
            plc.WriteBool("M100", true);
            bool ran = runtime.StepOnce();

            Assert.True(ran);
            Assert.Equal(1, runtime.Statistics.Total);
            Assert.Equal(0, runtime.Statistics.Error);          // 关键：不再“相机未连接”
            Assert.NotEmpty(Directory.GetFiles(_archiveDir, "results.csv", SearchOption.AllDirectories)); // 已留档
        }
    }
}
