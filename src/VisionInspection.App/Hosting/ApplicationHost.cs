using System;
using System.IO;
using VisionInspection.App.Settings;
using VisionInspection.Camera.Offline;
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

        public AppSettings Settings { get; private set; }
        public IRecipeStore RecipeStore { get; }

        // 对外稳定事件（转发当前 runtime）
        public event Action<InspectionSnapshot> SnapshotReady;
        public event Action<RuntimeAlarm> Alarm;
        public event Action<string> Log;
        public event Action<bool> CameraConnectionChanged;
        public event Action<bool> PlcConnectionChanged;
        public event Action<bool> HeartbeatChanged;

        public ApplicationHost(string baseDir, Func<ImageFrame> demoFrameFactory,
            Func<int, int, ImageFrame> demoBoardFactory = null)
        {
            _baseDir = baseDir;
            _demoFrameFactory = demoFrameFactory ?? throw new ArgumentNullException(nameof(demoFrameFactory));
            _demoBoardFactory = demoBoardFactory;
            RecipeStore = new JsonRecipeStore(Path.Combine(baseDir, "recipes"));
            _settingsStore = new AppSettingsStore(Path.Combine(baseDir, "settings.json"));
            Settings = _settingsStore.LoadOrDefault();
            BuildRuntime();
        }

        /// <summary>保存并应用新配置：重建运行链，即时生效。</summary>
        public void ApplySettings(AppSettings settings)
        {
            Settings = settings;
            _settingsStore.Save(settings);
            BuildRuntime();
        }

        private void BuildRuntime()
        {
            _runtime?.Dispose();

            _camera = CreateCamera(Settings.Camera);
            _plc = CreatePlc(Settings.Plc);

            var detector = new ForegroundRatioDetector(Settings.Detection.DarkIsForeground, Settings.Detection.GrayThreshold);
            // 配准:配方 Fiducial.Type=None 时 FiducialAlignment 退化为恒等,故演示不受影响;
            // 配方配置基准点后即自动补偿底板摆放偏差。
            var inspector = new OpenCvInspector(new FiducialAlignment(), new IPresenceDetector[] { detector });

            var archiver = new InspectionArchiver(
                Path.Combine(_baseDir, Settings.Archive.Directory),
                Settings.Archive.SaveNgImageOnly, Settings.Archive.RetentionDays);

            var options = new RuntimeOptions
            {
                PollIntervalMs = Settings.Runtime.PollIntervalMs,
                GrabTimeoutMs = Settings.Runtime.GrabTimeoutMs,
                HeartbeatIntervalMs = Settings.Runtime.HeartbeatIntervalMs,
                InspectTimeoutMs = Settings.Runtime.InspectTimeoutMs,
                FaultBackoffMs = Settings.Runtime.FaultBackoffMs
            };

            _runtime = new RuntimeService(_camera, inspector, _plc, RecipeStore, archiver, options,
                Settings.Handshake, Path.Combine(_baseDir, "stats.json"));
            _runtime.SnapshotReady += s => SnapshotReady?.Invoke(s);
            _runtime.Alarm += a => Alarm?.Invoke(a);
            _runtime.Log += m => Log?.Invoke(m);
            _runtime.CameraConnectionChanged += c => CameraConnectionChanged?.Invoke(c);
            _runtime.PlcConnectionChanged += c => PlcConnectionChanged?.Invoke(c);
            _runtime.HeartbeatChanged += h => HeartbeatChanged?.Invoke(h);
        }

        private ICamera CreateCamera(CameraSettings cs)
        {
            switch (cs.Mode)
            {
                case "Offline":
                    return new OfflineImageCamera(cs.OfflineFolder, true);
                case "Hikvision":
                    throw new NotSupportedException("海康相机需现场安装 MV SDK 并启用 HIKVISION 编译符号，参见 docs/camera-integration.md。");
                default:
                    return new SimulatedIndustrialCamera(_demoFrameFactory);
            }
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
            _runtime.StepOnce();
            sim.WriteBool(Settings.Handshake.TriggerBit, false);
            _runtime.StepOnce();
        }

        public bool IsSimulatedPlc => _plc is SimulatedPlcClient;

        public void Dispose()
        {
            _runtime?.Dispose();
        }
    }
}
