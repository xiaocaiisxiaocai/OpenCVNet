using System;
using VisionInspection.Camera.Offline;
using VisionInspection.Camera.Simulation;
using VisionInspection.Core.Abstractions;
using VisionInspection.Core.Imaging;

namespace VisionInspection.Camera
{
    /// <summary>相机类型。</summary>
    public enum CameraKind
    {
        /// <summary>离线图像回放（无硬件开发 / 测试）。</summary>
        Offline = 0,

        /// <summary>模拟工业相机（硬触发 / 断线仿真）。</summary>
        Simulated = 1,

        /// <summary>海康 MVS 工业相机（需现场安装 SDK 并启用 HIKVISION 编译符号）。</summary>
        Hikvision = 2
    }

    /// <summary>相机创建参数。</summary>
    public sealed class CameraOptions
    {
        public CameraKind Kind { get; set; } = CameraKind.Offline;

        /// <summary>离线模式的图像文件夹。</summary>
        public string OfflineFolder { get; set; }

        /// <summary>离线模式是否循环回放。</summary>
        public bool Loop { get; set; } = true;

        /// <summary>海康设备标识（序列号 / IP / 用户自定义名）。</summary>
        public string DeviceIdentifier { get; set; }
    }

    /// <summary>
    /// 相机工厂：按配置创建 <see cref="ICamera"/> 实现，使运行层与具体品牌解耦。
    /// 海康需现场安装 MV SDK 并启用编译符号 <c>HIKVISION</c>（详见 docs/camera-integration.md）。
    /// </summary>
    public static class CameraFactory
    {
        public static ICamera Create(CameraOptions options, Func<ImageFrame> simulatedFrameFactory = null)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));

            switch (options.Kind)
            {
                case CameraKind.Offline:
                    return new OfflineImageCamera(options.OfflineFolder, options.Loop);

                case CameraKind.Simulated:
                    if (simulatedFrameFactory == null)
                        throw new ArgumentException("模拟相机需要提供帧工厂。", nameof(simulatedFrameFactory));
                    return new SimulatedIndustrialCamera(simulatedFrameFactory);

                case CameraKind.Hikvision:
#if HIKVISION
                    return new Hikvision.HikvisionCamera(options.DeviceIdentifier);
#else
                    throw new NotSupportedException(
                        "未启用 HIKVISION 编译符号。请安装海康 MV SDK、加入 HikvisionCamera 实现并定义 HIKVISION 后重编。参见 docs/camera-integration.md。");
#endif
                default:
                    throw new NotSupportedException("未知相机类型：" + options.Kind);
            }
        }
    }
}
