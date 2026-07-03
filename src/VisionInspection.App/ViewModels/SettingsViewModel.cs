using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
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
    public partial class SettingsViewModel : ObservableObject, INotifyDataErrorInfo
    {
        private readonly ApplicationHost _host;
        private readonly Dictionary<string, List<string>> _errors = new Dictionary<string, List<string>>();

        public string[] CameraModes { get; } = { "Simulated", "Offline" };
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
        [ObservableProperty] private bool _useSequence;
        [ObservableProperty] private string _requestSequenceWord;
        [ObservableProperty] private string _ackSequenceWord;
        [ObservableProperty] private string _defectBitmapWord;
        [ObservableProperty] private int _defectBitmapWordCount;
        [ObservableProperty] private string _errorCodeWord;

        [ObservableProperty] private string _statusMessage = "就绪";
        [ObservableProperty] private bool _canSave = true;

        public IRelayCommand SaveCommand { get; }
        public bool HasErrors => _errors.Count > 0;
        public event EventHandler<DataErrorsChangedEventArgs> ErrorsChanged;

        public SettingsViewModel(ApplicationHost host)
        {
            _host = host ?? throw new ArgumentNullException(nameof(host));
            SaveCommand = new RelayCommand(Save, CanSaveSettings);
            LoadFrom(_host.Settings);
            RefreshCanSave();
        }

        public void RefreshCanSave()
        {
            ValidateAll();
            CanSave = !_host.IsRunning && !HasErrors;
            SaveCommand.NotifyCanExecuteChanged();
        }

        public IEnumerable GetErrors(string propertyName)
        {
            if (string.IsNullOrEmpty(propertyName)) return new string[0];
            if (_errors.TryGetValue(propertyName, out var list)) return list;
            return new string[0];
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
            UseSequence = s.Handshake.UseSequence;
            RequestSequenceWord = s.Handshake.RequestSequenceWord;
            AckSequenceWord = s.Handshake.AckSequenceWord;
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
                HeartbeatBit = HeartbeatBit, ModelCodeWord = ModelCodeWord, UseSequence = UseSequence,
                RequestSequenceWord = RequestSequenceWord, AckSequenceWord = AckSequenceWord,
                DefectBitmapWord = DefectBitmapWord,
                DefectBitmapWordCount = DefectBitmapWordCount, ErrorCodeWord = ErrorCodeWord
            }
        };

        private bool CanSaveSettings() => CanSave;

        partial void OnPlcPortChanged(int value) => ValidateAll();
        partial void OnPlcTimeoutMsChanged(int value) => ValidateAll();
        partial void OnPollIntervalMsChanged(int value) => ValidateAll();
        partial void OnGrabTimeoutMsChanged(int value) => ValidateAll();
        partial void OnHeartbeatIntervalMsChanged(int value) => ValidateAll();
        partial void OnGrayThresholdChanged(int value) => ValidateAll();
        partial void OnDefectBitmapWordCountChanged(int value) => ValidateAll();

        private void ValidateAll()
        {
            SetError(nameof(PlcPort), PlcPort <= 0 || PlcPort > 65535 ? "PLC 端口必须在 1~65535。" : null);
            SetError(nameof(PlcTimeoutMs), PlcTimeoutMs <= 0 ? "PLC 超时必须大于 0。" : null);
            SetError(nameof(PollIntervalMs), PollIntervalMs <= 0 ? "轮询周期必须大于 0。" : null);
            SetError(nameof(GrabTimeoutMs), GrabTimeoutMs <= 0 ? "取像超时必须大于 0。" : null);
            SetError(nameof(HeartbeatIntervalMs), HeartbeatIntervalMs <= 0 ? "心跳周期必须大于 0。" : null);
            SetError(nameof(GrayThreshold), GrayThreshold < 0 || GrayThreshold > 255 ? "灰度阈值必须在 0~255。" : null);
            SetError(nameof(DefectBitmapWordCount), DefectBitmapWordCount <= 0 ? "位图字数必须大于 0。" : null);
            CanSave = !_host.IsRunning && !HasErrors;
            SaveCommand?.NotifyCanExecuteChanged();
        }

        private void SetError(string propertyName, string message)
        {
            bool had = _errors.ContainsKey(propertyName);
            if (message == null)
                _errors.Remove(propertyName);
            else
                _errors[propertyName] = new List<string> { message };
            bool has = _errors.ContainsKey(propertyName);
            if (had != has || has)
            {
                OnPropertyChanged(nameof(HasErrors));
                ErrorsChanged?.Invoke(this, new DataErrorsChangedEventArgs(propertyName));
            }
        }

        private void Save()
        {
            RefreshCanSave();
            if (HasErrors)
            {
                StatusMessage = "保存失败：请先修正红色字段";
                return;
            }
            if (_host.IsRunning)
            {
                StatusMessage = "保存失败：运行中不能应用设置，请先停止运行";
                return;
            }
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
