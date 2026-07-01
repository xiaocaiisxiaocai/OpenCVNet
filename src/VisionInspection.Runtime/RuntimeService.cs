using System;
using System.Threading;
using VisionInspection.Core.Abstractions;
using VisionInspection.Core.Imaging;
using VisionInspection.Core.Models;
using VisionInspection.Infrastructure.Archiving;
using VisionInspection.Plc.Handshake;

namespace VisionInspection.Runtime
{
    /// <summary>一次检测的快照：图像 + 结果 + 所用配方（供界面实时显示与叠加）。</summary>
    public sealed class InspectionSnapshot
    {
        public ImageFrame Frame { get; }
        public InspectionResult Result { get; }
        public Recipe Recipe { get; }

        public InspectionSnapshot(ImageFrame frame, InspectionResult result, Recipe recipe)
        {
            Frame = frame;
            Result = result;
            Recipe = recipe;
        }
    }

    /// <summary>
    /// 运行编排服务：后台轮询握手，触发时取像 → 检测 → 归档 → 回写 PLC，并维护统计、心跳、报警、自恢复，
    /// 通过事件向界面推送实时图像快照与设备连接状态。
    /// </summary>
    public sealed class RuntimeService : IDisposable
    {
        private readonly ICamera _camera;
        private readonly IInspector _inspector;
        private readonly IPlcClient _plc;
        private readonly InspectionArchiver _archiver;
        private readonly HandshakeController _handshake;
        private readonly RuntimeOptions _options;
        private readonly RuntimeStatistics _stats = new RuntimeStatistics();
        private readonly VisionInspection.Infrastructure.Storage.StatisticsStore _statsStore;
        private int _lastStatsSaveTick;

        private Thread _worker;
        private volatile bool _running;
        private int _msSinceHeartbeat;
        private bool _hb;
        private bool _inFault;          // 是否处于持续故障态（用于告警去重）
        private string _lastFaultMsg;

        public RuntimeStatistics Statistics => _stats;
        public bool IsRunning => _running;
        public bool CameraConnected => _camera.IsConnected;
        public bool PlcConnected => _plc.IsConnected;

        public event Action<InspectionResult> Inspected;
        public event Action<InspectionSnapshot> SnapshotReady;
        public event Action<RuntimeAlarm> Alarm;
        public event Action<string> Log;
        public event Action<bool> CameraConnectionChanged;
        public event Action<bool> PlcConnectionChanged;
        public event Action<bool> HeartbeatChanged;

        public RuntimeService(
            ICamera camera,
            IInspector inspector,
            IPlcClient plc,
            IRecipeStore store,
            InspectionArchiver archiver,
            RuntimeOptions options = null,
            HandshakeAddressMap map = null,
            string statsPath = null)
        {
            _camera = camera ?? throw new ArgumentNullException(nameof(camera));
            _inspector = inspector ?? throw new ArgumentNullException(nameof(inspector));
            _plc = plc ?? throw new ArgumentNullException(nameof(plc));
            if (store == null) throw new ArgumentNullException(nameof(store));
            _archiver = archiver ?? throw new ArgumentNullException(nameof(archiver));
            _options = options ?? new RuntimeOptions();

            if (!string.IsNullOrEmpty(statsPath))
            {
                _statsStore = new VisionInspection.Infrastructure.Storage.StatisticsStore(statsPath);
                var snap = _statsStore.Load();
                if (snap != null) _stats.Restore(snap.Total, snap.Ok, snap.Ng, snap.Error); // 重启延续产量
            }

            _camera.ConnectionChanged += (s, e) => CameraConnectionChanged?.Invoke(e.IsConnected);
            _plc.ConnectionChanged += (s, e) => PlcConnectionChanged?.Invoke(e.IsConnected);

            _handshake = new HandshakeController(plc, store, InspectAndArchive, map, _options.InspectTimeoutMs);
            _handshake.Log += m => Log?.Invoke(m);
        }

        public HandshakeAddressMap Map => _handshake.Map;

        private InspectionResult InspectAndArchive(Recipe recipe)
        {
            var frame = _camera.Grab(_options.GrabTimeoutMs);
            var result = _inspector.Inspect(frame, recipe);
            _archiver.Archive(frame, result);

            _stats.Record(result);
            PersistStats(false);
            Inspected?.Invoke(result);
            SnapshotReady?.Invoke(new InspectionSnapshot(frame, result, recipe));
            if (result.Outcome != InspectionOutcome.Ok)
                Alarm?.Invoke(new RuntimeAlarm(
                    result.Outcome == InspectionOutcome.Ng ? "NG" : "ERROR",
                    $"型号 {result.ModelCode}：{result.Outcome}，缺件 {result.MissingCount}"));

            return result;
        }

        /// <summary>连接相机与 PLC（不启动运行循环），供界面「连接设备」使用。</summary>
        public void ConnectDevices()
        {
            if (!_camera.IsConnected) _camera.Open();
            if (!_plc.IsConnected) _plc.Connect();
            Log?.Invoke("设备已连接");
        }

        /// <summary>断开相机与 PLC（会先停止运行循环）。</summary>
        public void DisconnectDevices()
        {
            Stop();
            try { _camera.Close(); } catch { }
            try { _plc.Disconnect(); } catch { }
            Log?.Invoke("设备已断开");
        }

        /// <summary>执行一次轮询步（生产线程循环调用，亦供测试同步调用）。</summary>
        public bool StepOnce() => _handshake.ProcessOnce();

        /// <summary>清零并持久化统计。</summary>
        public void ResetStatistics()
        {
            _stats.Reset();
            PersistStats(true);
        }

        private void PersistStats(bool force)
        {
            if (_statsStore == null) return;
            int now = Environment.TickCount;
            if (!force && unchecked(now - _lastStatsSaveTick) < 5000) return; // 节流:最多每 5s 落盘
            _lastStatsSaveTick = now;
            var s = _stats.Capture();
            _statsStore.Save(s.Total, s.Ok, s.Ng, s.Error);
        }

        public void Start()
        {
            if (_running) return;
            ConnectDevices();
            _running = true;
            _worker = new Thread(Loop) { IsBackground = true, Name = "RuntimeLoop" };
            _worker.Start();
            Log?.Invoke("运行已启动");
        }

        public void Stop()
        {
            if (!_running) return;
            _running = false;
            _worker?.Join(2000);
            PersistStats(true);
            Log?.Invoke("运行已停止");
        }

        private void Loop()
        {
            while (_running)
            {
                bool stepOk = false;
                try
                {
                    StepOnce();
                    stepOk = true;
                    _msSinceHeartbeat += _options.PollIntervalMs;
                    if (_msSinceHeartbeat >= _options.HeartbeatIntervalMs)
                    {
                        _handshake.ToggleHeartbeat();
                        _hb = !_hb;
                        HeartbeatChanged?.Invoke(_hb);
                        _msSinceHeartbeat = 0;
                    }
                }
                catch (Exception ex)
                {
                    // 持续故障只在“首次/信息变化”时告警一次,避免刷屏与日志暴涨。
                    if (!_inFault || ex.Message != _lastFaultMsg)
                    {
                        Alarm?.Invoke(new RuntimeAlarm("FAULT", "运行异常：" + ex.Message));
                        _lastFaultMsg = ex.Message;
                    }
                    _inFault = true;
                    TryRecover();
                }

                if (stepOk && _inFault) // 从故障恢复,提示一次
                {
                    _inFault = false;
                    _lastFaultMsg = null;
                    Log?.Invoke("运行已从故障恢复");
                }

                // 故障态用更长退避,减轻紧循环下的重连/日志压力。
                Thread.Sleep(_inFault ? Math.Max(_options.PollIntervalMs, _options.FaultBackoffMs)
                                      : _options.PollIntervalMs);
            }
        }

        private void TryRecover()
        {
            try { if (!_plc.IsConnected) _plc.Connect(); } catch { }
            try { if (!_camera.IsConnected) _camera.Open(); } catch { }
        }

        public void Dispose()
        {
            Stop();
            PersistStats(true);
            _camera?.Dispose();
            _plc?.Dispose();
        }
    }
}
