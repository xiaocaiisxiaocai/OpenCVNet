using System;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VisionInspection.App.Hosting;
using VisionInspection.App.Settings;

namespace VisionInspection.App.ViewModels
{
    /// <summary>
    /// 设置页 VM：编辑 <see cref="AppSettings"/> 的扁平副本，保存经 <see cref="ApplicationHost.ApplySettings"/>
    /// 热重建运行链即时生效（无需重编译/重启）。
    /// </summary>
    public partial class SettingsViewModel : ObservableObject
    {
        private readonly ApplicationHost _host;

        public string[] CameraModes { get; } = { "Simulated", "Offline", "Hikvision" };
        public string[] PlcModes { get; } = { "Simulated", "Melsec" };

        // —— 相机 ——
        [ObservableProperty] private string _cameraMode;
        [ObservableProperty] private string _offlineFolder;

        // —— PLC ——
        [ObservableProperty] private string _plcMode;
        [ObservableProperty] private string _plcHost;
        [ObservableProperty] private int _plcPort;
        [ObservableProperty] private int _plcTimeoutMs;

        // —— 检测 ——
        [ObservableProperty] private int _grayThreshold;
        [ObservableProperty] private bool _darkIsForeground;

        // —— 运行 ——
        [ObservableProperty] private int _pollIntervalMs;
        [ObservableProperty] private int _grabTimeoutMs;
        [ObservableProperty] private int _heartbeatIntervalMs;
        [ObservableProperty] private int _inspectTimeoutMs;
        [ObservableProperty] private int _faultBackoffMs;

        // —— 归档 ——
        [ObservableProperty] private string _archiveDirectory;
        [ObservableProperty] private bool _saveNgImageOnly;
        [ObservableProperty] private int _retentionDays;

        // —— 握手软元件地址 ——
        [ObservableProperty] private string _triggerBit;
        [ObservableProperty] private string _busyBit;
        [ObservableProperty] private string _doneBit;
        [ObservableProperty] private string _okBit;
        [ObservableProperty] private string _ngBit;
        [ObservableProperty] private string _heartbeatBit;
        [ObservableProperty] private string _modelCodeWord;
        [ObservableProperty] private string _defectBitmapWord;
        [ObservableProperty] private int _defectBitmapWordCount;
        [ObservableProperty] private string _errorCodeWord;

        [ObservableProperty] private string _statusMessage = "就绪";

        public SettingsViewModel(ApplicationHost host)
        {
            _host = host ?? throw new ArgumentNullException(nameof(host));
            LoadFrom(_host.Settings);
        }

        private void LoadFrom(AppSettings s)
        {
            CameraMode = s.Camera.Mode;
            OfflineFolder = s.Camera.OfflineFolder;

            PlcMode = s.Plc.Mode;
            PlcHost = s.Plc.Host;
            PlcPort = s.Plc.Port;
            PlcTimeoutMs = s.Plc.TimeoutMs;

            GrayThreshold = s.Detection.GrayThreshold;
            DarkIsForeground = s.Detection.DarkIsForeground;

            PollIntervalMs = s.Runtime.PollIntervalMs;
            GrabTimeoutMs = s.Runtime.GrabTimeoutMs;
            HeartbeatIntervalMs = s.Runtime.HeartbeatIntervalMs;
            InspectTimeoutMs = s.Runtime.InspectTimeoutMs;
            FaultBackoffMs = s.Runtime.FaultBackoffMs;

            ArchiveDirectory = s.Archive.Directory;
            SaveNgImageOnly = s.Archive.SaveNgImageOnly;
            RetentionDays = s.Archive.RetentionDays;

            TriggerBit = s.Handshake.TriggerBit;
            BusyBit = s.Handshake.BusyBit;
            DoneBit = s.Handshake.DoneBit;
            OkBit = s.Handshake.OkBit;
            NgBit = s.Handshake.NgBit;
            HeartbeatBit = s.Handshake.HeartbeatBit;
            ModelCodeWord = s.Handshake.ModelCodeWord;
            DefectBitmapWord = s.Handshake.DefectBitmapWord;
            DefectBitmapWordCount = s.Handshake.DefectBitmapWordCount;
            ErrorCodeWord = s.Handshake.ErrorCodeWord;
        }

        private AppSettings Build() => new AppSettings
        {
            Camera = { Mode = CameraMode, OfflineFolder = OfflineFolder ?? "" },
            Plc = { Mode = PlcMode, Host = PlcHost ?? "", Port = PlcPort, TimeoutMs = PlcTimeoutMs },
            Detection = { GrayThreshold = GrayThreshold, DarkIsForeground = DarkIsForeground },
            Runtime =
            {
                PollIntervalMs = PollIntervalMs, GrabTimeoutMs = GrabTimeoutMs,
                HeartbeatIntervalMs = HeartbeatIntervalMs, InspectTimeoutMs = InspectTimeoutMs,
                FaultBackoffMs = FaultBackoffMs
            },
            Archive = { Directory = ArchiveDirectory ?? "archive", SaveNgImageOnly = SaveNgImageOnly, RetentionDays = RetentionDays },
            Handshake =
            {
                TriggerBit = TriggerBit, BusyBit = BusyBit, DoneBit = DoneBit, OkBit = OkBit, NgBit = NgBit,
                HeartbeatBit = HeartbeatBit, ModelCodeWord = ModelCodeWord, DefectBitmapWord = DefectBitmapWord,
                DefectBitmapWordCount = DefectBitmapWordCount, ErrorCodeWord = ErrorCodeWord
            }
        };

        [RelayCommand]
        private void Save()
        {
            try
            {
                _host.ApplySettings(Build()); // 落盘 settings.json + 热重建运行链
                StatusMessage = "已保存并应用（设备已按新配置重建，请重新连接设备）";
            }
            catch (Exception ex)
            {
                StatusMessage = "保存失败：" + ex.Message;
            }
        }

        [RelayCommand]
        private void Reload()
        {
            LoadFrom(_host.Settings);
            StatusMessage = "已还原为当前生效配置";
        }
    }
}
