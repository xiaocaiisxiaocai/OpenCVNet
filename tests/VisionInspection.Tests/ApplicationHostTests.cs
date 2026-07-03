using System;
using System.IO;
using System.Diagnostics;
using System.Threading;
using VisionInspection.App.Hosting;
using VisionInspection.App.Settings;
using VisionInspection.Core.Imaging;
using VisionInspection.Core.Models;
using VisionInspection.Plc.Simulation;
using Xunit;

namespace VisionInspection.Tests
{
    public class ApplicationHostTests : IDisposable
    {
        private readonly string _baseDir;

        public ApplicationHostTests()
        {
            _baseDir = Path.Combine(Path.GetTempPath(), "vi_host_" + Path.GetRandomFileName());
            Directory.CreateDirectory(_baseDir);
        }

        public void Dispose()
        {
            if (Directory.Exists(_baseDir)) Directory.Delete(_baseDir, true);
        }

        [Fact]
        public void SimulateTrigger_Does_Not_Clear_Trigger_When_Runtime_Is_Busy()
        {
            var frameRequested = new ManualResetEventSlim(false);
            var releaseFrame = new ManualResetEventSlim(false);
            using (var host = new ApplicationHost(_baseDir, () =>
            {
                frameRequested.Set();
                releaseFrame.Wait(3000);
                return Frame();
            }))
            {
                host.RecipeStore.Save(new Recipe
                {
                    ModelCode = "1",
                    Stations = { new Station { Index = 0, Roi = new RoiRect(0, 0, 4, 4) } }
                });
                host.Settings.Runtime.PollIntervalMs = 10;
                host.Settings.Runtime.GrabTimeoutMs = 5000;
                host.Settings.Runtime.InspectTimeoutMs = 5000;

                host.Start();
                var plc = GetSimulatedPlc(host);
                plc.WriteInt16(host.Settings.Handshake.ModelCodeWord, 1);
                plc.WriteBool(host.Settings.Handshake.TriggerBit, true);
                Assert.True(frameRequested.Wait(1000));

                var sw = Stopwatch.StartNew();
                host.SimulateTrigger();
                sw.Stop();
                bool triggerStillHigh = plc.ReadBool(host.Settings.Handshake.TriggerBit);
                releaseFrame.Set();
                host.Stop();

                Assert.True(sw.ElapsedMilliseconds < 500);
                Assert.True(triggerStillHigh);
            }
        }

        [Fact]
        public void SimulateTrigger_Runs_Synchronously_When_Runtime_Is_Stopped()
        {
            using (var host = new ApplicationHost(_baseDir, Frame))
            {
                host.RecipeStore.Save(new Recipe
                {
                    ModelCode = "1",
                    Stations = { new Station { Index = 0, Roi = new RoiRect(0, 0, 4, 4) } }
                });

                host.SimulateTrigger();

                var plc = GetSimulatedPlc(host);
                Assert.False(plc.ReadBool(host.Settings.Handshake.TriggerBit));
                Assert.False(plc.ReadBool(host.Settings.Handshake.DoneBit));
                Assert.Equal(1, host.Statistics.Total);
            }
        }

        [Fact]
        public void ApplySettings_Failure_Keeps_Previous_Runtime_Usable()
        {
            using (var host = new ApplicationHost(_baseDir, Frame))
            {
                host.RecipeStore.Save(new Recipe
                {
                    ModelCode = "1",
                    Stations = { new Station { Index = 0, Roi = new RoiRect(0, 0, 4, 4) } }
                });
                var bad = new AppSettings();
                bad.Camera.Mode = "Hikvision";
                bad.Plc.Mode = "Simulated";

                Assert.ThrowsAny<Exception>(() => host.ApplySettings(bad));

                host.SimulateTrigger();
                Assert.Equal(1, host.Statistics.Total);
                Assert.True(host.IsSimulatedPlc);
            }
        }

        private static ImageFrame Frame()
            => new ImageFrame(4, 4, 12, PixelFormat.Bgr24, new byte[12 * 4], DateTime.UtcNow);

        private static SimulatedPlcClient GetSimulatedPlc(ApplicationHost host)
        {
            var field = typeof(ApplicationHost).GetField("_plc", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            return (SimulatedPlcClient)field.GetValue(host);
        }
    }
}
