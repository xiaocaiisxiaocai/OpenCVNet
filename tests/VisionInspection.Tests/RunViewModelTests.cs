using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using VisionInspection.App.Hosting;
using VisionInspection.App.ViewModels;
using VisionInspection.Core.Imaging;
using VisionInspection.Core.Models;
using VisionInspection.Runtime;
using Xunit;

namespace VisionInspection.Tests
{
    public class RunViewModelTests : IDisposable
    {
        private readonly string _baseDir;

        public RunViewModelTests()
        {
            _baseDir = Path.Combine(Path.GetTempPath(), "vi_vm_" + Path.GetRandomFileName());
            Directory.CreateDirectory(_baseDir);
        }

        public void Dispose()
        {
            if (Directory.Exists(_baseDir)) Directory.Delete(_baseDir, true);
        }

        [Fact]
        public void RenderSnapshot_Reuses_Existing_Station_Rows_And_Overlays()
        {
            using (var host = new ApplicationHost(_baseDir, Frame))
            {
                var vm = new RunViewModel(host);
                var recipe = Recipe2();

                Render(vm, Snapshot(recipe, PresenceState.Present, PresenceState.Absent));
                var firstOverlay = vm.Overlays[0];
                var firstRow = vm.StationResults[0];

                Render(vm, Snapshot(recipe, PresenceState.Absent, PresenceState.Present));

                Assert.Same(firstOverlay, vm.Overlays[0]);
                Assert.Same(firstRow, vm.StationResults[0]);
                Assert.False(vm.StationResults[0].Ok);
                Assert.Equal("缺件", vm.StationResults[0].State);
                Assert.True(vm.StationResults[1].Ok);
            }
        }

        [Fact]
        public void RenderSnapshot_Shows_Unknown_Separately_From_Missing()
        {
            using (var host = new ApplicationHost(_baseDir, Frame))
            {
                var vm = new RunViewModel(host);

                Render(vm, new InspectionSnapshot(Frame(), new InspectionResult("1", InspectionOutcome.Ng,
                    new List<StationResult>
                    {
                        new StationResult(0, PresenceState.Unknown, 0, 0.5),
                        new StationResult(1, PresenceState.Absent, 0, 0.5)
                    }, DateTime.UtcNow, 1), Recipe2()));

                Assert.Equal("未知", vm.StationResults[0].State);
                Assert.Equal("缺件", vm.StationResults[1].State);
            }
        }

        [Fact]
        public void Alarm_Updates_LastAlarmSummary()
        {
            using (var host = new ApplicationHost(_baseDir, Frame))
            {
                var vm = new RunViewModel(host);
                var method = typeof(RunViewModel).GetMethod("OnAlarm", BindingFlags.Instance | BindingFlags.NonPublic);

                method.Invoke(vm, new object[] { new RuntimeAlarm("FAULT", "PLC 断线") });

                Assert.Contains("PLC 断线", vm.LastAlarmSummary);
            }
        }

        [Fact]
        public void Exposes_Display_Version_For_Field_Diagnostics()
        {
            using (var host = new ApplicationHost(_baseDir, Frame))
            {
                var vm = new RunViewModel(host);

                Assert.StartsWith("v", vm.DisplayVersion);
                Assert.True(vm.DisplayVersion.Length > 1);
            }
        }

        private static void Render(RunViewModel vm, InspectionSnapshot snapshot)
        {
            var method = typeof(RunViewModel).GetMethod("RenderSnapshot", BindingFlags.Instance | BindingFlags.NonPublic);
            method.Invoke(vm, new object[] { snapshot, null });
        }

        private static InspectionSnapshot Snapshot(Recipe recipe, PresenceState first, PresenceState second)
            => new InspectionSnapshot(Frame(), new InspectionResult("1", InspectionOutcome.Ng,
                new List<StationResult>
                {
                    new StationResult(0, first, first == PresenceState.Present ? 0.9 : 0.1, 0.5),
                    new StationResult(1, second, second == PresenceState.Present ? 0.9 : 0.1, 0.5)
                }, DateTime.UtcNow, 7), recipe);

        private static Recipe Recipe2()
        {
            return new Recipe
            {
                ModelCode = "1",
                Stations =
                {
                    new Station { Index = 0, Roi = new RoiRect(0, 0, 4, 4) },
                    new Station { Index = 1, Roi = new RoiRect(5, 0, 4, 4) }
                }
            };
        }

        private static ImageFrame Frame()
            => new ImageFrame(10, 4, 30, PixelFormat.Bgr24, new byte[120], DateTime.UtcNow);
    }
}
