using System;
using System.Collections.Generic;
using System.IO;
using System.Diagnostics;
using System.Threading;
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
            _store.Save(new Recipe { ModelCode = "1", Stations = { new Station { Index = 0, Roi = new RoiRect(0, 0, 10, 10) } } });
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
            public InspectionResult Inspect(ImageFrame frame, Recipe recipe, CancellationToken cancellationToken) => _result;
        }

        private sealed class BlockingInspector : IInspector
        {
            private readonly ManualResetEventSlim _started;
            private readonly ManualResetEventSlim _release;
            private readonly InspectionResult _result;

            public BlockingInspector(ManualResetEventSlim started, ManualResetEventSlim release, InspectionResult result)
            {
                _started = started;
                _release = release;
                _result = result;
            }

            public InspectionResult Inspect(ImageFrame frame, Recipe recipe)
            {
                _started.Set();
                _release.Wait(3000);
                return _result;
            }

            public InspectionResult Inspect(ImageFrame frame, Recipe recipe, CancellationToken cancellationToken)
            {
                _started.Set();
                _release.Wait(3000, cancellationToken);
                return _result;
            }
        }

        private sealed class DisposableCamera : ICamera
        {
            public bool IsDisposed { get; private set; }
            public bool IsConnected { get; private set; } = true;
#pragma warning disable CS0067
            public event EventHandler<CameraFrameEventArgs> FrameReceived;
#pragma warning restore CS0067
            public event EventHandler<CameraConnectionEventArgs> ConnectionChanged;
            public ImageFrame Grab(int timeoutMs = 2000) => Frame();
            public ImageFrame Grab(int timeoutMs, CancellationToken cancellationToken) => Frame();
            public void SetTriggerMode(TriggerMode mode) { }
            public void Open() { IsConnected = true; ConnectionChanged?.Invoke(this, new CameraConnectionEventArgs(true)); }
            public void Close() { IsConnected = false; ConnectionChanged?.Invoke(this, new CameraConnectionEventArgs(false)); }
            public void Dispose() { IsDisposed = true; }
        }

        private sealed class DisposablePlc : IPlcClient
        {
            private readonly SimulatedPlcClient _inner = new SimulatedPlcClient();
            public bool IsDisposed { get; private set; }
            public bool IsConnected => _inner.IsConnected;
            public event EventHandler<PlcConnectionEventArgs> ConnectionChanged
            {
                add { _inner.ConnectionChanged += value; }
                remove { _inner.ConnectionChanged -= value; }
            }
            public void Connect() => _inner.Connect();
            public void Disconnect() => _inner.Disconnect();
            public bool ReadBool(string address) => _inner.ReadBool(address);
            public void WriteBool(string address, bool value) => _inner.WriteBool(address, value);
            public short ReadInt16(string address) => _inner.ReadInt16(address);
            public void WriteInt16(string address, short value) => _inner.WriteInt16(address, value);
            public ushort[] ReadUInt16(string address, ushort length) => _inner.ReadUInt16(address, length);
            public void WriteUInt16(string address, ushort[] values) => _inner.WriteUInt16(address, values);
            public void Dispose() { IsDisposed = true; _inner.Dispose(); }
        }

        private sealed class ThrowingArchiver : IInspectionArchiver
        {
            private readonly Exception _exception;
            public ThrowingArchiver(Exception exception) { _exception = exception; }
            public void Archive(ImageFrame frame, InspectionResult result) { throw _exception; }
            public void Dispose() { }
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
                new InspectionArchiver(_archiveDir), statsStore: new StatisticsStore(statsPath));
            plc.WriteInt16("D190", 1);
            plc.WriteBool("M100", true);
            svc.StepOnce();
            svc.Dispose(); // 强制落盘

            // 新实例应从 stats.json 恢复计数(重启不清零)
            var cam2 = new SimulatedIndustrialCamera(Frame); cam2.Open();
            var plc2 = new SimulatedPlcClient(); plc2.Connect();
            var svc2 = new RuntimeService(cam2, new FakeInspector(ok), plc2, _store,
                new InspectionArchiver(_archiveDir), statsStore: new StatisticsStore(statsPath));

            Assert.Equal(1, svc2.Statistics.Total);
            Assert.Equal(1, svc2.Statistics.Ok);

            svc2.ResetStatistics(); // 清零并落盘
            Assert.Equal(0, svc2.Statistics.Total);
            svc2.Dispose();
        }

        [Fact]
        public void Archive_Failure_Does_Not_Change_Ok_Result()
        {
            var ts = DateTime.UtcNow;
            var ok = new InspectionResult("1", InspectionOutcome.Ok,
                new List<StationResult> { new StationResult(0, PresenceState.Present, 0.9, 0.5) }, ts, 5);

            var cam = new SimulatedIndustrialCamera(Frame); cam.Open();
            var plc = new SimulatedPlcClient(); plc.Connect();
            var local = ts.ToLocalTime();
            var dayDir = Path.Combine(_archiveDir, local.ToString("yyyyMMdd"));
            Directory.CreateDirectory(dayDir);
            using (new FileStream(Path.Combine(dayDir, "results.csv"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
            {
                var svc = new RuntimeService(cam, new FakeInspector(ok), plc, _store, new InspectionArchiver(_archiveDir));
                bool alarmed = false;
                svc.Alarm += a => alarmed = a.Level == "WARN";

                plc.WriteInt16("D190", 1);
                plc.WriteBool("M100", true);

                Assert.True(svc.StepOnce());
                Assert.True(plc.ReadBool("M112"));
                Assert.False(plc.ReadBool("M113"));
                Assert.Equal((short)0, plc.ReadInt16("D210"));
                Assert.True(alarmed);
            }
        }

        [Fact]
        public void Archive_Failure_Emits_Structured_Log_With_Exception()
        {
            var expected = new IOException("disk full");
            var ok = new InspectionResult("1", InspectionOutcome.Ok,
                new List<StationResult> { new StationResult(0, PresenceState.Present, 0.9, 0.5) }, DateTime.UtcNow, 5);
            var cam = new SimulatedIndustrialCamera(Frame); cam.Open();
            var plc = new SimulatedPlcClient(); plc.Connect();
            var svc = new RuntimeService(cam, new FakeInspector(ok), plc, _store, new ThrowingArchiver(expected));

            RuntimeLogEvent captured = null;
            svc.StructuredLog += e =>
            {
                if (e.EventName == "ArchiveFailed") captured = e;
            };

            plc.WriteInt16("D190", 1);
            plc.WriteBool("M100", true);
            svc.StepOnce();

            Assert.NotNull(captured);
            Assert.Equal("ArchiveFailed", captured.EventName);
            Assert.Same(expected, captured.Exception);
            Assert.Equal("RuntimeService", captured.Source);
        }

        [Fact]
        public void Snapshot_Subscriber_Exception_Does_Not_Change_Ok_Result()
        {
            var ok = new InspectionResult("1", InspectionOutcome.Ok,
                new List<StationResult> { new StationResult(0, PresenceState.Present, 0.9, 0.5) }, DateTime.UtcNow, 5);

            var cam = new SimulatedIndustrialCamera(Frame); cam.Open();
            var plc = new SimulatedPlcClient(); plc.Connect();
            var svc = new RuntimeService(cam, new FakeInspector(ok), plc, _store, new InspectionArchiver(_archiveDir));
            svc.SnapshotReady += s => throw new InvalidOperationException("ui failed");

            plc.WriteInt16("D190", 1);
            plc.WriteBool("M100", true);

            Assert.True(svc.StepOnce());
            Assert.True(plc.ReadBool("M112"));
            Assert.False(plc.ReadBool("M113"));
            Assert.Equal((short)0, plc.ReadInt16("D210"));
        }

        [Fact]
        public void Timed_Out_Inspection_Does_Not_Archive_After_Late_Return()
        {
            var ok = new InspectionResult("1", InspectionOutcome.Ok,
                new List<StationResult> { new StationResult(0, PresenceState.Present, 0.9, 0.5) }, DateTime.UtcNow, 5);
            var started = new ManualResetEventSlim(false);
            var release = new ManualResetEventSlim(false);

            var cam = new SimulatedIndustrialCamera(Frame); cam.Open();
            var plc = new SimulatedPlcClient(); plc.Connect();
            var svc = new RuntimeService(cam, new BlockingInspector(started, release, ok), plc, _store,
                new InspectionArchiver(_archiveDir), new RuntimeOptions { InspectTimeoutMs = 100, GrabTimeoutMs = 500 });

            plc.WriteInt16("D190", 1);
            plc.WriteBool("M100", true);
            Assert.True(svc.StepOnce());
            Assert.True(started.IsSet);
            Assert.True(plc.ReadBool("M113"));

            release.Set();
            Thread.Sleep(300);

            Assert.Equal(0, svc.Statistics.Total);
            Assert.False(Directory.Exists(_archiveDir) && Directory.GetFiles(_archiveDir, "results.csv", SearchOption.AllDirectories).Length > 0);
        }

        [Fact]
        public void Stop_Cancels_InFlight_Inspection_Without_Waiting_For_InspectTimeout()
        {
            var ok = new InspectionResult("1", InspectionOutcome.Ok,
                new List<StationResult> { new StationResult(0, PresenceState.Present, 0.9, 0.5) }, DateTime.UtcNow, 5);
            var started = new ManualResetEventSlim(false);
            var release = new ManualResetEventSlim(false);

            var cam = new SimulatedIndustrialCamera(Frame); cam.Open();
            var plc = new SimulatedPlcClient(); plc.Connect();
            var svc = new RuntimeService(cam, new BlockingInspector(started, release, ok), plc, _store,
                new InspectionArchiver(_archiveDir),
                new RuntimeOptions { InspectTimeoutMs = 5000, GrabTimeoutMs = 500, PollIntervalMs = 10 });

            plc.WriteInt16("D190", 1);
            plc.WriteBool("M100", true);
            svc.Start();
            Assert.True(started.Wait(1000));

            var sw = Stopwatch.StartNew();
            svc.Stop();
            sw.Stop();
            release.Set();

            Assert.True(sw.ElapsedMilliseconds < 1500);
            Assert.Equal(RuntimeState.Stopped, svc.State);
        }

        [Fact]
        public void Lifecycle_Methods_Are_Idempotent_For_Repeated_Calls()
        {
            var ok = new InspectionResult("1", InspectionOutcome.Ok,
                new List<StationResult> { new StationResult(0, PresenceState.Present, 0.9, 0.5) }, DateTime.UtcNow, 5);

            var cam = new SimulatedIndustrialCamera(Frame);
            var plc = new SimulatedPlcClient();
            var svc = new RuntimeService(cam, new FakeInspector(ok), plc, _store,
                new InspectionArchiver(_archiveDir),
                new RuntimeOptions { InspectTimeoutMs = 500, GrabTimeoutMs = 500, PollIntervalMs = 10 });

            svc.ConnectDevices();
            svc.ConnectDevices();
            Assert.True(svc.CameraConnected);
            Assert.True(svc.PlcConnected);

            svc.Start();
            svc.Start();
            Assert.True(svc.IsRunning);

            svc.Stop();
            svc.Stop();
            Assert.False(svc.IsRunning);

            svc.DisconnectDevices();
            svc.DisconnectDevices();
            Assert.False(svc.CameraConnected);
            Assert.False(svc.PlcConnected);
        }

        [Fact]
        public void Dispose_Does_Not_Dispose_Devices_When_Runtime_Does_Not_Own_Them()
        {
            var ok = new InspectionResult("1", InspectionOutcome.Ok,
                new List<StationResult>(), DateTime.UtcNow, 5);
            var cam = new DisposableCamera();
            var plc = new DisposablePlc();

            var svc = new RuntimeService(cam, new FakeInspector(ok), plc, _store,
                new InspectionArchiver(_archiveDir), ownsDevices: false);

            svc.Dispose();

            Assert.False(cam.IsDisposed);
            Assert.False(plc.IsDisposed);
        }

        [Fact]
        public void Plc_Disconnect_During_Run_Raises_Fault_State()
        {
            var ok = new InspectionResult("1", InspectionOutcome.Ok,
                new List<StationResult>(), DateTime.UtcNow, 5);
            var cam = new SimulatedIndustrialCamera(Frame); cam.Open();
            var plc = new SimulatedPlcClient(); plc.Connect();
            var svc = new RuntimeService(cam, new FakeInspector(ok), plc, _store,
                new InspectionArchiver(_archiveDir),
                new RuntimeOptions { PollIntervalMs = 10, FaultBackoffMs = 10, InspectTimeoutMs = 500, GrabTimeoutMs = 500 });
            var faulted = new ManualResetEventSlim(false);
            svc.StateChanged += s => { if (s == RuntimeState.Faulted) faulted.Set(); };

            svc.Start();
            plc.Disconnect();

            Assert.True(faulted.Wait(1000));
            Assert.Equal(RuntimeState.Faulted, svc.State);

            svc.Stop();
        }

        [Fact]
        public void Runtime_Fault_Emits_Structured_Log_With_Exception()
        {
            var ok = new InspectionResult("1", InspectionOutcome.Ok,
                new List<StationResult>(), DateTime.UtcNow, 5);
            var cam = new SimulatedIndustrialCamera(Frame); cam.Open();
            var plc = new SimulatedPlcClient(); plc.Connect();
            var svc = new RuntimeService(cam, new FakeInspector(ok), plc, _store,
                new InspectionArchiver(_archiveDir),
                new RuntimeOptions { PollIntervalMs = 10, FaultBackoffMs = 10, InspectTimeoutMs = 500, GrabTimeoutMs = 500 });
            var logged = new ManualResetEventSlim(false);
            RuntimeLogEvent captured = null;
            svc.StructuredLog += e =>
            {
                if (e.EventName == "RuntimeFault")
                {
                    captured = e;
                    logged.Set();
                }
            };

            svc.Start();
            plc.Disconnect();

            Assert.True(logged.Wait(1000));
            Assert.NotNull(captured);
            Assert.NotNull(captured.Exception);
            Assert.Equal("RuntimeService", captured.Source);

            svc.Stop();
        }

    }
}
