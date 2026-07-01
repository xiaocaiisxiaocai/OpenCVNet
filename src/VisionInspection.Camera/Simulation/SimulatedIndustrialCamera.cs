using System;
using VisionInspection.Core.Abstractions;
using VisionInspection.Core.Imaging;

namespace VisionInspection.Camera.Simulation
{
    /// <summary>
    /// 模拟工业相机：用于无硬件联调硬触发流程、连续采集与断线重连。
    /// <para>软触发：<see cref="Grab"/> 返回帧工厂生成的一帧。</para>
    /// <para>硬触发：<see cref="FireHardwareTrigger"/> 模拟 PLC I/O 外部触发，引发 <see cref="FrameReceived"/>。</para>
    /// </summary>
    public sealed class SimulatedIndustrialCamera : ICamera
    {
        private readonly Func<ImageFrame> _frameFactory;
        private TriggerMode _mode = TriggerMode.Software;
        private bool _connected;

        public SimulatedIndustrialCamera(Func<ImageFrame> frameFactory)
        {
            _frameFactory = frameFactory ?? throw new ArgumentNullException(nameof(frameFactory));
        }

        public bool IsConnected => _connected;
        public TriggerMode Mode => _mode;

        public event EventHandler<CameraFrameEventArgs> FrameReceived;
        public event EventHandler<CameraConnectionEventArgs> ConnectionChanged;

        public void Open()
        {
            _connected = true;
            ConnectionChanged?.Invoke(this, new CameraConnectionEventArgs(true));
        }

        public void Close()
        {
            if (!_connected) return;
            _connected = false;
            ConnectionChanged?.Invoke(this, new CameraConnectionEventArgs(false));
        }

        public void SetTriggerMode(TriggerMode mode) => _mode = mode;

        public ImageFrame Grab(int timeoutMs = 2000)
        {
            EnsureConnected();
            var frame = _frameFactory();
            FrameReceived?.Invoke(this, new CameraFrameEventArgs(frame));
            return frame;
        }

        /// <summary>模拟一次硬触发曝光（硬触发模式下由 PLC I/O 引发）。</summary>
        public ImageFrame FireHardwareTrigger()
        {
            EnsureConnected();
            if (_mode != TriggerMode.Hardware)
                throw new InvalidOperationException("当前非硬触发模式，无法触发硬触发曝光。");
            var frame = _frameFactory();
            FrameReceived?.Invoke(this, new CameraFrameEventArgs(frame));
            return frame;
        }

        /// <summary>模拟断线（用于验证断线重连逻辑）。</summary>
        public void SimulateDisconnect() => Close();

        private void EnsureConnected()
        {
            if (!_connected) throw new InvalidOperationException("相机未连接。");
        }

        public void Dispose() => Close();
    }
}
