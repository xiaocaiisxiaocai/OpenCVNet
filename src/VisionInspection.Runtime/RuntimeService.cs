using System;
using System.Threading;
using VisionInspection.Core.Abstractions;
using VisionInspection.Core.Imaging;
using VisionInspection.Core.Models;
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
        private sealed class CycleContext
        {
            public ImageFrame Frame { get; }
            public Recipe Recipe { get; }
            public InspectionResult Result { get; }

            public CycleContext(ImageFrame frame, Recipe recipe, InspectionResult result)
            {
                Frame = frame;
                Recipe = recipe;
                Result = result;
            }
        }

        private readonly ICamera _camera;
        private readonly IInspector _inspector;
        private readonly IPlcClient _plc;
        private readonly IInspectionArchiver _archiver;
        private readonly HandshakeController _handshake;
        private readonly RuntimeOptions _options;
        private readonly bool _ownsDevices;
        private readonly RuntimeStatistics _stats = new RuntimeStatistics();
        private readonly IStatisticsStore _statsStore;
        private readonly object _lifecycleLock = new object();
        private int _lastStatsSaveTick;

        private Thread _worker;
        private volatile bool _running;
        private int _lastHeartbeatTick;
        private bool _hb;
        private bool _inFault;          // 是否处于持续故障态（用于告警去重）
        private string _lastFaultMsg;
        private int _stepInProgress;
        private readonly System.Collections.Concurrent.ConcurrentQueue<CycleContext> _completedContexts =
            new System.Collections.Concurrent.ConcurrentQueue<CycleContext>();
        private RuntimeState _state = RuntimeState.Stopped;
        private CancellationTokenSource _runCts;

        public RuntimeStatistics Statistics => _stats;
        public bool IsRunning => _running;
        public RuntimeState State => _state;
        public bool CameraConnected => _camera.IsConnected;
        public bool PlcConnected => _plc.IsConnected;

        public event Action<InspectionResult> Inspected;
        public event Action<InspectionSnapshot> SnapshotReady;
        public event Action<RuntimeAlarm> Alarm;
        public event Action<string> Log;
        public event Action<RuntimeLogEvent> StructuredLog;
        public event Action<bool> CameraConnectionChanged;
        public event Action<bool> PlcConnectionChanged;
        public event Action<bool> HeartbeatChanged;
        public event Action<RuntimeState> StateChanged;

        public RuntimeService(
            ICamera camera,
            IInspector inspector,
            IPlcClient plc,
            IRecipeStore store,
            IInspectionArchiver archiver,
            RuntimeOptions options = null,
            HandshakeAddressMap map = null,
            IStatisticsStore statsStore = null,
            bool ownsDevices = true)
        {
            _camera = camera ?? throw new ArgumentNullException(nameof(camera));
            _inspector = inspector ?? throw new ArgumentNullException(nameof(inspector));
            _plc = plc ?? throw new ArgumentNullException(nameof(plc));
            if (store == null) throw new ArgumentNullException(nameof(store));
            _archiver = archiver ?? throw new ArgumentNullException(nameof(archiver));
            _options = options ?? new RuntimeOptions();
            _ownsDevices = ownsDevices;

            _statsStore = statsStore;
            if (_statsStore != null)
            {
                _statsStore.Warning += m => SafeRaise(StructuredLog,
                    new RuntimeLogEvent("Warning", "StatisticsStore", "StatisticsWarning", m));
                var snap = _statsStore.Load();
                if (snap != null) _stats.Restore(snap.Total, snap.Ok, snap.Ng, snap.Error); // 重启延续产量
            }

            if (_archiver is IObservableArchiver observableArchiver)
            {
                observableArchiver.Event += m => SafeRaise(StructuredLog,
                    new RuntimeLogEvent("Warning", "Archiver", "ArchiveEvent", m));
            }

            _camera.ConnectionChanged += (s, e) => SafeRaise(CameraConnectionChanged, e.IsConnected);
            _plc.ConnectionChanged += (s, e) => SafeRaise(PlcConnectionChanged, e.IsConnected);

            _handshake = new HandshakeController(plc, store, InspectAndArchive, map, _options.InspectTimeoutMs);
            _handshake.Inspected += OnHandshakeInspected;
            _handshake.Log += m =>
            {
                SafeRaise(Log, m);
                SafeRaise(StructuredLog, new RuntimeLogEvent("Information", "HandshakeController", "HandshakeLog", m));
            };
        }

        public HandshakeAddressMap Map => _handshake.Map;

        private InspectionResult InspectAndArchive(Recipe recipe, CancellationToken cancellationToken)
        {
            var frame = _camera.Grab(_options.GrabTimeoutMs, cancellationToken);
            var result = _inspector.Inspect(frame, recipe, cancellationToken);
            _completedContexts.Enqueue(new CycleContext(frame, recipe, result));

            return result;
        }

        private void OnHandshakeInspected(InspectionResult result)
        {
            var context = TakeContext(result);
            _stats.Record(result);
            PersistStats(false);
            try
            {
                _archiver.Archive(context.Frame, result);
            }
            catch (Exception ex)
            {
                SafeRaise(Alarm, new RuntimeAlarm("WARN", "归档失败：" + ex.Message));
                SafeRaise(StructuredLog, new RuntimeLogEvent("Warning", "RuntimeService", "ArchiveFailed",
                    "归档失败", ex, result?.ModelCode, result?.Outcome.ToString(), result?.MissingCount));
            }
            SafeRaise(Inspected, result);
            SafeRaise(SnapshotReady, new InspectionSnapshot(context.Frame, result, context.Recipe));
            if (result.Outcome != InspectionOutcome.Ok)
                SafeRaise(Alarm, new RuntimeAlarm(
                    result.Outcome == InspectionOutcome.Ng ? "NG" : "ERROR",
                    $"型号 {result.ModelCode}：{result.Outcome}，缺件 {result.MissingCount}"));
            SafeRaise(StructuredLog, new RuntimeLogEvent("Information", "RuntimeService", "InspectionCompleted",
                "检测完成", null, result.ModelCode, result.Outcome.ToString(), result.MissingCount));
        }

        private CycleContext TakeContext(InspectionResult result)
        {
            CycleContext context;
            if (_completedContexts.TryDequeue(out context))
                return context;
            return new CycleContext(null, null, result);
        }

        /// <summary>连接相机与 PLC（不启动运行循环），供界面「连接设备」使用。</summary>
        public void ConnectDevices()
        {
            lock (_lifecycleLock)
            {
                if (!_camera.IsConnected) _camera.Open();
                if (!_plc.IsConnected) _plc.Connect();
                SafeRaise(Log, "设备已连接");
                SafeRaise(StructuredLog, new RuntimeLogEvent("Information", "RuntimeService", "DevicesConnected", "设备已连接"));
            }
        }

        /// <summary>断开相机与 PLC（会先停止运行循环）。</summary>
        public void DisconnectDevices()
        {
            Stop();
            lock (_lifecycleLock)
            {
                try { _camera.Close(); } catch { }
                try { _plc.Disconnect(); } catch { }
                SafeRaise(Log, "设备已断开");
                SafeRaise(StructuredLog, new RuntimeLogEvent("Information", "RuntimeService", "DevicesDisconnected", "设备已断开"));
            }
        }

        /// <summary>执行一次轮询步（生产线程循环调用，亦供测试同步调用）。</summary>
        public bool StepOnce()
            => StepOnce(CancellationToken.None);

        private bool StepOnce(CancellationToken cancellationToken)
        {
            if (Interlocked.CompareExchange(ref _stepInProgress, 1, 0) != 0)
                return false;
            try { return _handshake.ProcessOnce(cancellationToken); }
            finally { Interlocked.Exchange(ref _stepInProgress, 0); }
        }

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
            lock (_lifecycleLock)
            {
                if (_running) return;
                SetState(RuntimeState.Starting);
                if (!_camera.IsConnected) _camera.Open();
                if (!_plc.IsConnected) _plc.Connect();
                _handshake.ResetOutputs();
                _lastHeartbeatTick = Environment.TickCount;
                _runCts = new CancellationTokenSource();
                _running = true;
                _worker = new Thread(Loop) { IsBackground = true, Name = "RuntimeLoop" };
                _worker.Start();
                SetState(RuntimeState.Running);
                SafeRaise(Log, "运行已启动");
                SafeRaise(StructuredLog, new RuntimeLogEvent("Information", "RuntimeService", "RuntimeStarted", "运行已启动"));
            }
        }

        public void Stop()
        {
            Thread worker;
            lock (_lifecycleLock)
            {
                if (!_running) return;
                SetState(RuntimeState.Stopping);
                _running = false;
                _runCts?.Cancel();
                worker = _worker;
            }
            int joinMs = Math.Max(2000, _options.InspectTimeoutMs + _options.GrabTimeoutMs + 1000);
            if (worker != null && !worker.Join(joinMs))
            {
                SafeRaise(Alarm, new RuntimeAlarm("WARN", "运行线程未在超时时间内停止。"));
                SafeRaise(StructuredLog, new RuntimeLogEvent("Warning", "RuntimeService", "StopTimedOut", "运行线程未在超时时间内停止"));
            }
            lock (_lifecycleLock)
            {
                _runCts?.Dispose();
                _runCts = null;
                _worker = null;
                PersistStats(true);
                SetState(RuntimeState.Stopped);
                SafeRaise(Log, "运行已停止");
                SafeRaise(StructuredLog, new RuntimeLogEvent("Information", "RuntimeService", "RuntimeStopped", "运行已停止"));
            }
        }

        private void Loop()
        {
            var token = _runCts != null ? _runCts.Token : CancellationToken.None;
            while (_running && !token.IsCancellationRequested)
            {
                bool stepOk = false;
                try
                {
                    StepOnce(token);
                    stepOk = true;
                    int now = Environment.TickCount;
                    if (unchecked(now - _lastHeartbeatTick) >= _options.HeartbeatIntervalMs)
                    {
                        _handshake.ToggleHeartbeat();
                        _hb = !_hb;
                        SafeRaise(HeartbeatChanged, _hb);
                        _lastHeartbeatTick = now;
                    }
                }
                catch (Exception ex)
                {
                    if (token.IsCancellationRequested || ex is OperationCanceledException)
                        break;

                    // 持续故障只在“首次/信息变化”时告警一次,避免刷屏与日志暴涨。
                    if (!_inFault || ex.Message != _lastFaultMsg)
                    {
                        SafeRaise(Alarm, new RuntimeAlarm("FAULT", "运行异常：" + ex.Message));
                        SafeRaise(StructuredLog, new RuntimeLogEvent("Error", "RuntimeService", "RuntimeFault",
                            "运行异常", ex));
                        _lastFaultMsg = ex.Message;
                    }
                    _inFault = true;
                    SetState(RuntimeState.Faulted);
                    TryRecover();
                }

                if (stepOk && _inFault) // 从故障恢复,提示一次
                {
                    _inFault = false;
                    _lastFaultMsg = null;
                    SetState(RuntimeState.Running);
                    SafeRaise(Log, "运行已从故障恢复");
                    SafeRaise(StructuredLog, new RuntimeLogEvent("Information", "RuntimeService", "RuntimeRecovered", "运行已从故障恢复"));
                }

                // 故障态用更长退避,减轻紧循环下的重连/日志压力。
                int sleepMs = _inFault ? Math.Max(_options.PollIntervalMs, _options.FaultBackoffMs)
                                       : _options.PollIntervalMs;
                if (token.WaitHandle.WaitOne(sleepMs))
                    break;
            }
        }

        private void TryRecover()
        {
            try { if (!_plc.IsConnected) _plc.Connect(); } catch { }
            try { if (!_camera.IsConnected) _camera.Open(); } catch { }
        }

        private static void SafeRaise<T>(Action<T> evt, T value)
        {
            if (evt == null) return;
            foreach (Action<T> h in evt.GetInvocationList())
            {
                try { h(value); }
                catch { }
            }
        }

        private void SetState(RuntimeState state)
        {
            if (_state == state) return;
            _state = state;
            SafeRaise(StateChanged, state);
        }

        public void Dispose()
        {
            Stop();
            PersistStats(true);
            _archiver?.Dispose();
            if (_ownsDevices)
            {
                _camera?.Dispose();
                _plc?.Dispose();
            }
        }
    }
}
