using VisionInspection.Plc.Handshake;

namespace VisionInspection.App.Settings
{
    /// <summary>PLC 连接参数。</summary>
    public sealed class PlcSettings
    {
        /// <summary>连接方式：Simulated（模拟，演示）/ Melsec（三菱以太网 MC/SLMP，真机）。</summary>
        public string Mode { get; set; } = "Simulated";
        public string Host { get; set; } = "192.168.1.10";
        public int Port { get; set; } = 5000;
        public int TimeoutMs { get; set; } = 2000;
    }

    /// <summary>相机参数。</summary>
    public sealed class CameraSettings
    {
        /// <summary>Simulated（模拟）/ Offline（图片回放）/ Hikvision（海康 MVS，需 SDK）。</summary>
        public string Mode { get; set; } = "Simulated";
        public string OfflineFolder { get; set; } = "";
        public string DeviceIdentifier { get; set; } = "";
    }

    /// <summary>检测参数。</summary>
    public sealed class DetectionSettings
    {
        /// <summary>固定灰度阈值：灰度 &gt; 此值视为“亮”像素。</summary>
        public int GrayThreshold { get; set; } = 128;
        /// <summary>是否以“暗像素”为前景（背光场景）。</summary>
        public bool DarkIsForeground { get; set; } = false;
    }

    /// <summary>运行参数。</summary>
    public sealed class RuntimeSettings
    {
        public int PollIntervalMs { get; set; } = 20;
        public int GrabTimeoutMs { get; set; } = 2000;
        public int HeartbeatIntervalMs { get; set; } = 1000;
        /// <summary>单次检测超时（毫秒）；卡死写错误码+清忙。0=不超时。</summary>
        public int InspectTimeoutMs { get; set; } = 8000;
        /// <summary>故障态轮询退避（毫秒），避免持续故障刷屏。</summary>
        public int FaultBackoffMs { get; set; } = 2000;
    }

    /// <summary>结果归档参数。</summary>
    public sealed class ArchiveSettings
    {
        public string Directory { get; set; } = "archive";
        public bool SaveNgImageOnly { get; set; } = true;
        /// <summary>留档保留天数;超期按日目录自动清理。0=永久保留。</summary>
        public int RetentionDays { get; set; } = 90;
    }

    /// <summary>应用设置（持久化为 settings.json）。</summary>
    public sealed class AppSettings
    {
        public PlcSettings Plc { get; set; } = new PlcSettings();
        public HandshakeAddressMap Handshake { get; set; } = new HandshakeAddressMap();
        public CameraSettings Camera { get; set; } = new CameraSettings();
        public DetectionSettings Detection { get; set; } = new DetectionSettings();
        public RuntimeSettings Runtime { get; set; } = new RuntimeSettings();
        public ArchiveSettings Archive { get; set; } = new ArchiveSettings();
    }
}
