using System;
using System.IO;
using VisionInspection.App.Settings;
using VisionInspection.Camera;
using VisionInspection.Camera.Simulation;
using VisionInspection.Core.Abstractions;
using VisionInspection.Core.Imaging;
using VisionInspection.Infrastructure.Archiving;
using VisionInspection.Infrastructure.Storage;
using VisionInspection.Plc.Mc;
using VisionInspection.Plc.Simulation;
using VisionInspection.Runtime;
using VisionInspection.Vision.Alignment;
using VisionInspection.Vision.Detection;
using VisionInspection.Vision.Inspection;

namespace VisionInspection.App.Hosting
{
    /// <summary>
    /// 应用门面：集中持有配置与运行服务生命周期。改配置后 <see cref="ApplySettings"/> 重建内部
    /// <see cref="RuntimeService"/> 并即时生效；对外事件稳定转发，故运行页无需重新绑定。
    /// </summary>
    public sealed class ApplicationHost : IDisposable
    {
        private readonly string _baseDir;
        private readonly Func<ImageFrame> _demoFrameFactory;
        private readonly Func<int, int, ImageFrame> _demoBoardFactory;
        private readonly AppSettingsStore _settingsStore;

        private RuntimeService _runtime;
        private ICamera _camera;
        private IPlcClient _plc;
        private bool _disposed;

        public AppSettings Settings { get; private set; }
        public IRecipeStore RecipeStore { get; }
        public string StartupWarning { get; private set; }

        // 对外稳定事件（转发当前 runtime）
        public event Action<InspectionSnapshot> SnapshotReady;
        public event Action<RuntimeAlarm> Alarm;
        public event Action<string> Log;
        public event Action<RuntimeLogEvent> StructuredLog;
        public event Action<bool> CameraConnectionChanged;
        public event Action<bool> PlcConnectionChanged;
        public event Action<bool> HeartbeatChanged;
        public event Action<RuntimeState> StateChanged;
        public event Action RuntimeRebuilt;

        public ApplicationHost(string baseDir, Func<ImageFrame> demoFrameFactory,
            Func<int, int, ImageFrame> demoBoardFactory = null)
        {
            _baseDir = baseDir;
            _demoFrameFactory = demoFrameFactory ?? throw new ArgumentNullException(nameof(demoFrameFactory));
            _demoBoardFactory = demoBoardFactory;
            RecipeStore = new JsonRecipeStore(Path.Combine(baseDir, "recipes"));
            _settingsStore = new AppSettingsStore(Path.Combine(baseDir, "settings.json"));
            var load = _settingsStore.Load();
            Settings = load.Settings;
            StartupWarning = load.Warning;
            ValidateSettings(Settings);
            BuildRuntime();
        }

        /// <summary>保存并应用新配置：重建运行链，即时生效。</summary>
        public void ApplySettings(AppSettings settings)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            ValidateSettings(settings);

            var oldSettings = Settings;
            var oldRuntime = _runtime;
            var oldCamera = _camera;
            var oldPlc = _plc;

            RuntimeService newRuntime = null;
            ICamera newCamera = null;
            IPlcClient newPlc = null;
            try
            {
                BuildRuntime(settings, out newRuntime, out newCamera, out newPlc);
                Settings = settings;
                _settingsStore.Save(settings);

                _runtime = newRuntime;
                _camera = newCamera;
                _plc = newPlc;
                DetachRuntime(oldRuntime);
                oldRuntime?.Dispose();
                oldCamera?.Dispose();
                oldPlc?.Dispose();
                RuntimeRebuilt?.Invoke();
            }
            catch
            {
                newRuntime?.Dispose();
                newCamera?.Dispose();
                newPlc?.Dispose();
                Settings = oldSettings;
                _runtime = oldRuntime;
                _camera = oldCamera;
                _plc = oldPlc;
                throw;
            }
        }

        private void BuildRuntime()
        {
            BuildRuntime(Settings, out _runtime, out _camera, out _plc);
        }

        private void BuildRuntime(AppSettings settings, out RuntimeService runtime, out ICamera camera, out IPlcClient plc)
        {
            camera = CreateCamera(settings.Camera);
            plc = CreatePlc(settings.Plc);

            var detector = new ForegroundRatioDetector(settings.Detection.DarkIsForeground, settings.Detection.GrayThreshold);
            // 配准:配方 Fiducial.Type=None 时 FiducialAlignment 退化为恒等,故演示不受影响;
            // 配方配置基准点后即自动补偿底板摆放偏差。
            var inspector = new OpenCvInspector(new FiducialAlignment(), new IPresenceDetector[] { detector });

            var archiveRoot = Path.Combine(_baseDir, settings.Archive.Directory);
            var archiver = new SpoolingInspectionArchiver(new InspectionArchiver(
                archiveRoot, settings.Archive.SaveNgImageOnly, settings.Archive.RetentionDays),
                Path.Combine(archiveRoot, ".spool"));

            var options = new RuntimeOptions
            {
                PollIntervalMs = settings.Runtime.PollIntervalMs,
                GrabTimeoutMs = settings.Runtime.GrabTimeoutMs,
                HeartbeatIntervalMs = settings.Runtime.HeartbeatIntervalMs,
                InspectTimeoutMs = settings.Runtime.InspectTimeoutMs,
                FaultBackoffMs = settings.Runtime.FaultBackoffMs
            };

            var statsStore = new StatisticsStore(Path.Combine(_baseDir, "stats.json"));
            runtime = new RuntimeService(camera, inspector, plc, RecipeStore, archiver, options,
                settings.Handshake, statsStore, ownsDevices: false);
            AttachRuntime(runtime);
        }

        private void AttachRuntime(RuntimeService runtime)
        {
            runtime.SnapshotReady += ForwardSnapshotReady;
            runtime.Alarm += ForwardAlarm;
            runtime.Log += ForwardLog;
            runtime.StructuredLog += ForwardStructuredLog;
            runtime.CameraConnectionChanged += ForwardCameraConnectionChanged;
            runtime.PlcConnectionChanged += ForwardPlcConnectionChanged;
            runtime.HeartbeatChanged += ForwardHeartbeatChanged;
            runtime.StateChanged += ForwardStateChanged;
        }

        private void DetachRuntime(RuntimeService runtime)
        {
            if (runtime == null) return;
            runtime.SnapshotReady -= ForwardSnapshotReady;
            runtime.Alarm -= ForwardAlarm;
            runtime.Log -= ForwardLog;
            runtime.StructuredLog -= ForwardStructuredLog;
            runtime.CameraConnectionChanged -= ForwardCameraConnectionChanged;
            runtime.PlcConnectionChanged -= ForwardPlcConnectionChanged;
            runtime.HeartbeatChanged -= ForwardHeartbeatChanged;
            runtime.StateChanged -= ForwardStateChanged;
        }

        private void ForwardSnapshotReady(InspectionSnapshot s) => SnapshotReady?.Invoke(s);
        private void ForwardAlarm(RuntimeAlarm a) => Alarm?.Invoke(a);
        private void ForwardLog(string m) => Log?.Invoke(m);
        private void ForwardStructuredLog(RuntimeLogEvent e) => StructuredLog?.Invoke(e);
        private void ForwardCameraConnectionChanged(bool c) => CameraConnectionChanged?.Invoke(c);
        private void ForwardPlcConnectionChanged(bool c) => PlcConnectionChanged?.Invoke(c);
        private void ForwardHeartbeatChanged(bool h) => HeartbeatChanged?.Invoke(h);
        private void ForwardStateChanged(RuntimeState s) => StateChanged?.Invoke(s);

        public RuntimeState State => _runtime.State;

        private ICamera CreateCamera(CameraSettings cs)
        {
            var options = new CameraOptions
            {
                Kind = ParseCameraKind(cs.Mode),
                OfflineFolder = cs.OfflineFolder,
                Loop = true,
                DeviceIdentifier = cs.DeviceIdentifier
            };
            return CameraFactory.Create(options, _demoFrameFactory);
        }

        private static CameraKind ParseCameraKind(string mode)
        {
            if (string.Equals(mode, "Offline", StringComparison.OrdinalIgnoreCase)) return CameraKind.Offline;
            if (string.Equals(mode, "Hikvision", StringComparison.OrdinalIgnoreCase)) return CameraKind.Hikvision;
            return CameraKind.Simulated;
        }

        private static IPlcClient CreatePlc(PlcSettings ps)
            => ps.Mode == "Melsec"
                ? (IPlcClient)new MelsecMcClient(ps.Host, ps.Port, ps.TimeoutMs)
                : new SimulatedPlcClient();

        // —— 运行控制代理 ——
        public RuntimeStatistics Statistics => _runtime.Statistics;
        public bool IsRunning => _runtime.IsRunning;
        public bool CameraConnected => _runtime.CameraConnected;
        public bool PlcConnected => _runtime.PlcConnected;

        public void Start() => _runtime.Start();
        public void Stop() => _runtime.Stop();
        public void ConnectDevices() => _runtime.ConnectDevices();
        public void DisconnectDevices() => _runtime.DisconnectDevices();
        public bool StepOnce() => _runtime.StepOnce();
        public void ResetStatistics() => _runtime.ResetStatistics();

        /// <summary>从相机取一帧（配方标定“相机取图”用）。演示相机按当前配方行×列生成满件底图,真机走实际取图。</summary>
        public ImageFrame CaptureFrame(int rows, int cols)
        {
            if (!_camera.IsConnected) _camera.Open();
            if (_camera is SimulatedIndustrialCamera && _demoBoardFactory != null)
                return _demoBoardFactory(rows, cols);
            return _camera.Grab(Settings.Runtime.GrabTimeoutMs);
        }

        /// <summary>演示：模拟一次 PLC 触发（仅模拟 PLC 有效；真机由 PLC 自动触发）。</summary>
        public void SimulateTrigger(short modelCode = 1)
        {
            if (!(_plc is SimulatedPlcClient sim)) return;
            if (!_camera.IsConnected) ConnectDevices();
            sim.WriteInt16(Settings.Handshake.ModelCodeWord, modelCode);
            sim.WriteBool(Settings.Handshake.TriggerBit, true);
            if (_runtime.IsRunning)
                return;

            _runtime.StepOnce();
            sim.WriteBool(Settings.Handshake.TriggerBit, false);
            _runtime.StepOnce();
        }

        public bool IsSimulatedPlc => _plc is SimulatedPlcClient;

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            DetachRuntime(_runtime);
            _runtime?.Dispose();
            _camera?.Dispose();
            _plc?.Dispose();
        }

        private static void ValidateSettings(AppSettings settings)
        {
            if (settings.Runtime.PollIntervalMs <= 0) throw new ArgumentException("轮询周期必须大于 0。");
            if (settings.Runtime.GrabTimeoutMs <= 0) throw new ArgumentException("取像超时必须大于 0。");
            if (settings.Runtime.HeartbeatIntervalMs <= 0) throw new ArgumentException("心跳周期必须大于 0。");
            if (settings.Runtime.InspectTimeoutMs < 0) throw new ArgumentException("检测超时不能为负数。");
            if (settings.Runtime.FaultBackoffMs < 0) throw new ArgumentException("故障退避不能为负数。");
            if (settings.Plc.Port <= 0 || settings.Plc.Port > 65535) throw new ArgumentException("PLC 端口必须在 1~65535。");
            if (settings.Plc.TimeoutMs <= 0) throw new ArgumentException("PLC 超时必须大于 0。");
            if (settings.Detection.GrayThreshold < 0 || settings.Detection.GrayThreshold > 255) throw new ArgumentException("灰度阈值必须在 0~255。");
            if (settings.Handshake.DefectBitmapWordCount <= 0) throw new ArgumentException("位图字数必须大于 0。");
            if (settings.Handshake.UseSequence)
            {
                if (string.IsNullOrWhiteSpace(settings.Handshake.RequestSequenceWord))
                    throw new ArgumentException("启用序号确认时，请求序号字不能为空。");
                if (string.IsNullOrWhiteSpace(settings.Handshake.AckSequenceWord))
                    throw new ArgumentException("启用序号确认时，确认序号字不能为空。");
            }
            if (string.IsNullOrWhiteSpace(settings.Archive.Directory)) throw new ArgumentException("归档目录不能为空。");
            if (settings.Archive.RetentionDays < 0) throw new ArgumentException("保留天数不能为负数。");
        }
    }
}
