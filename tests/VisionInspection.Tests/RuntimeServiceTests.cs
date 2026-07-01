using System;
using System.Collections.Generic;
using System.IO;
using VisionInspection.Core.Abstractions;
using VisionInspection.Core.Imaging;
using VisionInspection.Core.Models;
using VisionInspection.Camera.Simulation;
using VisionInspection.Infrastructure.Archiving;
using VisionInspection.Infrastructure.Storage;
using VisionInspection.Plc.Simulation;
using VisionInspection.Runtime;
using Xunit;

namespace VisionInspection.Tests
{
    public class RuntimeServiceTests : IDisposable
    {
        private readonly string _recipeDir;
        private readonly string _archiveDir;
        private readonly JsonRecipeStore _store;

        public RuntimeServiceTests()
        {
            _recipeDir = Path.Combine(Path.GetTempPath(), "vi_rt_r_" + Path.GetRandomFileName());
            _archiveDir = Path.Combine(Path.GetTempPath(), "vi_rt_a_" + Path.GetRandomFileName());
            Directory.CreateDirectory(_recipeDir);
            _store = new JsonRecipeStore(_recipeDir);
            _store.Save(new Recipe { ModelCode = "1", Stations = { new Station { Index = 0 } } });
        }

        public void Dispose()
        {
            if (Directory.Exists(_recipeDir)) Directory.Delete(_recipeDir, true);
            if (Directory.Exists(_archiveDir)) Directory.Delete(_archiveDir, true);
        }

        private sealed class FakeInspector : IInspector
        {
            private readonly InspectionResult _result;
            public FakeInspector(InspectionResult result) { _result = result; }
            public InspectionResult Inspect(ImageFrame frame, Recipe recipe) => _result;
        }

        private static ImageFrame Frame() =>
            new ImageFrame(4, 4, 12, PixelFormat.Bgr24, new byte[12 * 4], DateTime.UtcNow);

        [Fact]
        public void Trigger_Runs_Ok_And_Counts()
        {
            var ok = new InspectionResult("1", InspectionOutcome.Ok,
                new List<StationResult> { new StationResult(0, PresenceState.Present, 0.9, 0.5) }, DateTime.UtcNow, 5);

            var cam = new SimulatedIndustrialCamera(Frame); cam.Open();
            var plc = new SimulatedPlcClient(); plc.Connect();
            var archiver = new InspectionArchiver(_archiveDir);
            var svc = new RuntimeService(cam, new FakeInspector(ok), plc, _store, archiver);

            plc.WriteInt16("D190", 1);
            plc.WriteBool("M100", true);
            bool ran = svc.StepOnce();

            Assert.True(ran);
            Assert.Equal(1, svc.Statistics.Total);
            Assert.Equal(1, svc.Statistics.Ok);
            Assert.Equal(1.0, svc.Statistics.YieldRate);
            Assert.True(plc.ReadBool("M112")); // OK 位
        }

        [Fact]
        public void Ng_Raises_Alarm_And_Counts()
        {
            var ng = new InspectionResult("1", InspectionOutcome.Ng,
                new List<StationResult> { new StationResult(0, PresenceState.Absent, 0.05, 0.5) }, DateTime.UtcNow, 5);

            var cam = new SimulatedIndustrialCamera(Frame); cam.Open();
            var plc = new SimulatedPlcClient(); plc.Connect();
            var archiver = new InspectionArchiver(_archiveDir);
            var svc = new RuntimeService(cam, new FakeInspector(ng), plc, _store, archiver);

            var alarms = new List<RuntimeAlarm>();
            svc.Alarm += a => alarms.Add(a);

            plc.WriteInt16("D190", 1);
            plc.WriteBool("M100", true);
            svc.StepOnce();

            Assert.Equal(1, svc.Statistics.Ng);
            Assert.Single(alarms);
            Assert.Equal("NG", alarms[0].Level);
            Assert.True(plc.ReadBool("M113")); // NG 位
        }

        [Fact]
        public void Statistics_Persist_Across_Restart()
        {
            Directory.CreateDirectory(_archiveDir);
            var statsPath = Path.Combine(_archiveDir, "stats.json");
            var ok = new InspectionResult("1", InspectionOutcome.Ok,
                new List<StationResult> { new StationResult(0, PresenceState.Present, 0.9, 0.5) }, DateTime.UtcNow, 5);

            var cam = new SimulatedIndustrialCamera(Frame); cam.Open();
            var plc = new SimulatedPlcClient(); plc.Connect();
            var svc = new RuntimeService(cam, new FakeInspector(ok), plc, _store,
                new InspectionArchiver(_archiveDir), statsPath: statsPath);
            plc.WriteInt16("D190", 1);
            plc.WriteBool("M100", true);
            svc.StepOnce();
            svc.Dispose(); // 强制落盘

            // 新实例应从 stats.json 恢复计数(重启不清零)
            var cam2 = new SimulatedIndustrialCamera(Frame); cam2.Open();
            var plc2 = new SimulatedPlcClient(); plc2.Connect();
            var svc2 = new RuntimeService(cam2, new FakeInspector(ok), plc2, _store,
                new InspectionArchiver(_archiveDir), statsPath: statsPath);

            Assert.Equal(1, svc2.Statistics.Total);
            Assert.Equal(1, svc2.Statistics.Ok);

            svc2.ResetStatistics(); // 清零并落盘
            Assert.Equal(0, svc2.Statistics.Total);
            svc2.Dispose();
        }
    }
}
