using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VisionInspection.App.Hosting;
using VisionInspection.App.Imaging;
using VisionInspection.Core.Models;
using VisionInspection.Runtime;

namespace VisionInspection.App.ViewModels
{
    /// <summary>
    /// 运行监视 VM：设备连接状态与控制、实时图像 + 工位叠加、逐工位结果、统计、报警、日志。
    /// 订阅 <see cref="RuntimeService"/> 事件并 marshal 到 UI 线程。
    /// </summary>
    public partial class RunViewModel : ObservableObject
    {
        private static readonly Brush GreenBrush = Frozen(Color.FromRgb(0x2E, 0x7D, 0x32));
        private static readonly Brush RedBrush = Frozen(Color.FromRgb(0xC6, 0x28, 0x28));

        private readonly ApplicationHost _host;
        private readonly object _snapshotLock = new object();
        private InspectionSnapshot _latestSnapshot;
        private int _snapshotConvertScheduled;

        [ObservableProperty] private long _total;
        [ObservableProperty] private long _ok;
        [ObservableProperty] private long _ng;
        [ObservableProperty] private double _yieldRate;
        [ObservableProperty] private bool _isRunning;
        [ObservableProperty] private bool _cameraConnected;
        [ObservableProperty] private bool _plcConnected;
        [ObservableProperty] private bool _heartbeat;
        [ObservableProperty] private string _runtimeState = "Stopped";

        [ObservableProperty] private ImageSource _currentImage;
        [ObservableProperty] private double _imageWidth = 200;
        [ObservableProperty] private double _imageHeight = 200;
        [ObservableProperty] private string _currentModel = "—";
        [ObservableProperty] private string _currentOutcome = "—";
        [ObservableProperty] private Brush _currentOutcomeBrush = Brushes.Gray;
        [ObservableProperty] private int _lastCycleMs;
        [ObservableProperty] private string _lastAlarmSummary = "无报警";

        public ObservableCollection<StationOverlayViewModel> Overlays { get; } = new ObservableCollection<StationOverlayViewModel>();
        public ObservableCollection<StationResultRow> StationResults { get; } = new ObservableCollection<StationResultRow>();
        public ObservableCollection<string> Alarms { get; } = new ObservableCollection<string>();
        public ObservableCollection<string> Logs { get; } = new ObservableCollection<string>();

        public bool CanSimulate => _host.IsSimulatedPlc;
        public string DisplayVersion { get; } =
            "v" + (Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "unknown");

        /// <summary>报警发生时触发（含 NG）。View 订阅以弹出 Snackbar，保持 VM 不依赖 UI 控件库。</summary>
        public event Action<RuntimeAlarm> AlarmRaised;

        public RunViewModel(ApplicationHost host)
        {
            _host = host ?? throw new ArgumentNullException(nameof(host));
            _cameraConnected = host.CameraConnected;
            _plcConnected = host.PlcConnected;

            _host.SnapshotReady += OnSnapshot;
            _host.Alarm += OnAlarm;
            _host.Log += OnLog;
            _host.CameraConnectionChanged += c => OnUi(() => CameraConnected = c);
            _host.PlcConnectionChanged += c => OnUi(() => PlcConnected = c);
            _host.HeartbeatChanged += h => OnUi(() => Heartbeat = h);
            _host.StateChanged += s => OnUi(() => RuntimeState = s.ToString());
            _host.RuntimeRebuilt += () => OnUi(() =>
            {
                SyncConnectionState();
                OnPropertyChanged(nameof(CanSimulate));
            });
        }

        private void OnSnapshot(InspectionSnapshot snap)
        {
            lock (_snapshotLock)
            {
                _latestSnapshot = snap;
            }

            if (Interlocked.CompareExchange(ref _snapshotConvertScheduled, 1, 0) == 0)
                QueueSnapshotConversion();
        }

        private void QueueSnapshotConversion()
            => Task.Run((Action)ProcessLatestSnapshot);

        private void ProcessLatestSnapshot()
        {
            InspectionSnapshot snap;
            lock (_snapshotLock)
            {
                snap = _latestSnapshot;
                _latestSnapshot = null;
            }

            if (snap == null)
            {
                Interlocked.Exchange(ref _snapshotConvertScheduled, 0);
                return;
            }

            ImageSource image = null;
            try
            {
                image = snap.Frame != null ? WpfImage.ToBitmapSource(snap.Frame) : null;
            }
            catch
            {
                // 图像转换失败只丢本帧,不影响结果/统计刷新。
            }
            OnUi(() => RenderSnapshot(snap, image));

            lock (_snapshotLock)
            {
                if (_latestSnapshot != null)
                {
                    QueueSnapshotConversion();
                    return;
                }
            }
            Interlocked.Exchange(ref _snapshotConvertScheduled, 0);

            lock (_snapshotLock)
            {
                if (_latestSnapshot != null &&
                    Interlocked.CompareExchange(ref _snapshotConvertScheduled, 1, 0) == 0)
                    QueueSnapshotConversion();
            }
        }

        private void RenderSnapshot(InspectionSnapshot snap, ImageSource image)
        {
            if (snap.Frame != null)
            {
                CurrentImage = image;
                ImageWidth = snap.Frame.Width;
                ImageHeight = snap.Frame.Height;
            }

            var r = snap.Result;
            CurrentModel = string.IsNullOrEmpty(r.ModelCode) ? "—" : r.ModelCode;
            LastCycleMs = r.ElapsedMs;
            CurrentOutcome = OutcomeText(r.Outcome);
            CurrentOutcomeBrush = r.Outcome == InspectionOutcome.Ok ? GreenBrush : RedBrush;

            var byId = r.Stations.ToDictionary(s => s.StationIndex);
            int row = 0;
            if (snap.Recipe?.Stations != null)
            {
                foreach (var st in snap.Recipe.Stations)
                {
                    var hasResult = byId.TryGetValue(st.Index, out var sr);
                    bool present = hasResult && sr.IsPresent;
                    double score = hasResult ? sr.Score : 0;
                    var brush = present ? GreenBrush : RedBrush;

                    UpsertOverlay(row, st, brush);
                    UpsertStationResult(row, st.Index, hasResult ? sr.State : PresenceState.Unknown, Math.Round(score, 3));
                    row++;
                }
            }
            TrimTail(Overlays, row);
            TrimTail(StationResults, row);

            RefreshStats();
        }

        private void UpsertOverlay(int row, Station st, Brush brush)
        {
            StationOverlayViewModel item;
            if (row < Overlays.Count) item = Overlays[row];
            else
            {
                item = new StationOverlayViewModel();
                Overlays.Add(item);
            }

            item.X = st.Roi.X;
            item.Y = st.Roi.Y;
            item.Width = st.Roi.Width;
            item.Height = st.Roi.Height;
            item.Stroke = brush;
            item.Label = st.Index.ToString();
        }

        private void UpsertStationResult(int row, int index, PresenceState state, double score)
        {
            StationResultRow item;
            if (row < StationResults.Count) item = StationResults[row];
            else
            {
                item = new StationResultRow();
                StationResults.Add(item);
            }

            item.Index = index;
            item.State = StateText(state);
            item.Score = score;
            item.Ok = state == PresenceState.Present;
        }

        private void OnAlarm(RuntimeAlarm a) => OnUi(() =>
        {
            LastAlarmSummary = $"{a.TimeUtc.ToLocalTime():HH:mm:ss} [{a.Level}] {a.Message}";
            Alarms.Insert(0, $"{a.TimeUtc.ToLocalTime():HH:mm:ss} [{a.Level}] {a.Message}");
            Trim(Alarms, 100);
            AlarmRaised?.Invoke(a);
        });

        private void OnLog(string msg) => OnUi(() =>
        {
            Logs.Insert(0, $"{DateTime.Now:HH:mm:ss} {msg}");
            Trim(Logs, 200);
        });

        private void RefreshStats()
        {
            Total = _host.Statistics.Total;
            Ok = _host.Statistics.Ok;
            Ng = _host.Statistics.Ng;
            YieldRate = _host.Statistics.YieldRate;
        }

        private void SyncConnectionState()
        {
            CameraConnected = _host.CameraConnected;
            PlcConnected = _host.PlcConnected;
            IsRunning = _host.IsRunning;
            RuntimeState = _host.State.ToString();
        }

        [RelayCommand]
        private void Connect() { _host.ConnectDevices(); SyncConnectionState(); }

        [RelayCommand]
        private void Disconnect() { _host.DisconnectDevices(); SyncConnectionState(); }

        [RelayCommand]
        private void Start() { _host.Start(); SyncConnectionState(); }

        [RelayCommand]
        private void Stop() { _host.Stop(); SyncConnectionState(); }

        [RelayCommand]
        private void ResetStats() { _host.ResetStatistics(); RefreshStats(); }

        [RelayCommand]
        private void SimulateTrigger() { _host.SimulateTrigger(); SyncConnectionState(); }

        private static void Trim(ObservableCollection<string> list, int max)
        {
            while (list.Count > max) list.RemoveAt(list.Count - 1);
        }

        private static void TrimTail<T>(ObservableCollection<T> list, int count)
        {
            while (list.Count > count) list.RemoveAt(list.Count - 1);
        }

        private static void OnUi(Action action)
        {
            var d = Application.Current?.Dispatcher;
            // 用 BeginInvoke(非阻塞):后台检测线程回推 UI 时不阻塞,避免与握手单周期超时的
            // task.Wait 在 UI 线程直接触发检测时相互死锁,也不拖慢检测节拍。
            if (d != null && !d.CheckAccess()) d.BeginInvoke(action);
            else action();
        }

        private static Brush Frozen(Color c)
        {
            var b = new SolidColorBrush(c);
            b.Freeze();
            return b;
        }

        private static string OutcomeText(InspectionOutcome o)
            => o == InspectionOutcome.Ok ? "OK" : o == InspectionOutcome.Ng ? "NG" : "异常";

        private static string StateText(PresenceState state)
            => state == PresenceState.Present ? "有件" : state == PresenceState.Absent ? "缺件" : "未知";
    }

    /// <summary>逐工位结果行。</summary>
    public partial class StationResultRow : ObservableObject
    {
        [ObservableProperty] private int _index;
        [ObservableProperty] private string _state;
        [ObservableProperty] private double _score;
        [ObservableProperty] private bool _ok;
    }
}
