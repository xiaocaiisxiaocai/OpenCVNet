namespace VisionInspection.Runtime
{
    /// <summary>运行参数。</summary>
    public sealed class RuntimeOptions
    {
        /// <summary>握手轮询周期（毫秒）。</summary>
        public int PollIntervalMs { get; set; } = 20;

        /// <summary>取像超时（毫秒）。</summary>
        public int GrabTimeoutMs { get; set; } = 2000;

        /// <summary>心跳翻转周期（毫秒）。</summary>
        public int HeartbeatIntervalMs { get; set; } = 1000;

        /// <summary>进入故障后的轮询退避周期（毫秒），避免持续故障时紧循环刷屏。</summary>
        public int FaultBackoffMs { get; set; } = 2000;

        /// <summary>单次检测超时（毫秒）；相机/检测卡死时超时返回错误,避免整线死等。0=不超时。</summary>
        public int InspectTimeoutMs { get; set; } = 8000;
    }
}
