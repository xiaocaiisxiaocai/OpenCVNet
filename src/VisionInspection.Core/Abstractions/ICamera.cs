using System;
using System.Threading;
using VisionInspection.Core.Imaging;

namespace VisionInspection.Core.Abstractions
{
    /// <summary>相机触发模式。</summary>
    public enum TriggerMode
    {
        /// <summary>软件触发：由上位机主动调用 <see cref="ICamera.Grab"/> 取帧。</summary>
        Software = 0,

        /// <summary>硬件触发：由 PLC I/O 触发相机曝光，帧经 <see cref="ICamera.FrameReceived"/> 上报。</summary>
        Hardware = 1
    }

    /// <summary>
    /// 相机抽象。离线回放、海康 MVS 等实现统一此接口，使检测流程与具体硬件解耦、便于无硬件测试。
    /// </summary>
    public interface ICamera : IDisposable
    {
        bool IsConnected { get; }

        void Open();
        void Close();

        /// <summary>设置软 / 硬触发模式。</summary>
        void SetTriggerMode(TriggerMode mode);

        /// <summary>软触发同步取一帧；超时未取到应抛异常。</summary>
        ImageFrame Grab(int timeoutMs = 2000);

        /// <summary>软触发同步取一帧；支持外部取消。</summary>
        ImageFrame Grab(int timeoutMs, CancellationToken cancellationToken);

        /// <summary>硬触发 / 连续模式下每采到一帧上报。</summary>
        event EventHandler<CameraFrameEventArgs> FrameReceived;

        /// <summary>相机连接状态变化（供断线重连监控）。</summary>
        event EventHandler<CameraConnectionEventArgs> ConnectionChanged;
    }

    public sealed class CameraFrameEventArgs : EventArgs
    {
        public ImageFrame Frame { get; }
        public CameraFrameEventArgs(ImageFrame frame) { Frame = frame; }
    }

    public sealed class CameraConnectionEventArgs : EventArgs
    {
        public bool IsConnected { get; }
        public CameraConnectionEventArgs(bool isConnected) { IsConnected = isConnected; }
    }
}
