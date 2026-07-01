using System;
using System.Collections.ObjectModel;
using System.Linq;
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

        [ObservableProperty] private long _total;
        [ObservableProperty] private long _ok;
        [ObservableProperty] private long _ng;
        [ObservableProperty] private double _yieldRate;
        [ObservableProperty] private bool _isRunning;
        [ObservableProperty] private bool _cameraConnected;
        [ObservableProperty] private bool _plcConnected;
        [ObservableProperty] private bool _heartbeat;

        [ObservableProperty] private ImageSource _currentImage;
        [ObservableProperty] private double _imageWidth = 200;
        [ObservableProperty] private double _imageHeight = 200;
        [ObservableProperty] private string _currentModel = "—";
        [ObservableProperty] private string _currentOutcome = "—";
        [ObservableProperty] private Brush _currentOutcomeBrush = Brushes.Gray;
        [ObservableProperty] private int _lastCycleMs;

        public ObservableCollection<StationOverlayViewModel> Overlays { get; } = new ObservableCollection<StationOverlayViewModel>();
        public ObservableCollection<StationResultRow> StationResults { get; } = new ObservableCollection<StationResultRow>();
        public ObservableCollection<string> Alarms { get; } = new ObservableCollection<string>();
        public ObservableCollection<string> Logs { get; } = new ObservableCollection<string>();

        public bool CanSimulate => _host.IsSimulatedPlc;

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
        }

        private void OnSnapshot(InspectionSnapshot snap) => OnUi(() =>
        {
            if (snap.Frame != null)
            {
                CurrentImage = WpfImage.ToBitmapSource(snap.Frame);
                ImageWidth = snap.Frame.Width;
                ImageHeight = snap.Frame.Height;
            }

            var r = snap.Result;
            CurrentModel = string.IsNullOrEmpty(r.ModelCode) ? "—" : r.ModelCode;
            LastCycleMs = r.ElapsedMs;
            CurrentOutcome = OutcomeText(r.Outcome);
            CurrentOutcomeBrush = r.Outcome == InspectionOutcome.Ok ? GreenBrush : RedBrush;

            var byId = r.Stations.ToDictionary(s => s.StationIndex);
            Overlays.Clear();
            StationResults.Clear();
            if (snap.Recipe?.Stations != null)
            {
                foreach (var st in snap.Recipe.Stations)
                {
                    bool present = byId.TryGetValue(st.Index, out var sr) && sr.IsPresent;
                    double score = byId.TryGetValue(st.Index, out var s2) ? s2.Score : 0;
                    var brush = present ? GreenBrush : RedBrush;

                    Overlays.Add(new StationOverlayViewModel
                    {
                        X = st.Roi.X, Y = st.Roi.Y, Width = st.Roi.Width, Height = st.Roi.Height,
                        Stroke = brush, Label = st.Index.ToString()
                    });
                    StationResults.Add(new StationResultRow
                    {
                        Index = st.Index, State = present ? "有件" : "缺件",
                        Score = Math.Round(score, 3), Ok = present
                    });
                }
            }

            RefreshStats();
        });

        private void OnAlarm(RuntimeAlarm a) => OnUi(() =>
        {
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
