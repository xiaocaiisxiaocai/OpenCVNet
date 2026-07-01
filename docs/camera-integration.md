# 相机集成指南（海康 MVS / 工业相机）

本项目通过 `ICamera` 抽象（见 `VisionInspection.Core/Abstractions/ICamera.cs`）将采集层与具体品牌解耦。
已提供两种无硬件实现用于开发与测试：

- `OfflineImageCamera`（`Offline/`）：从文件夹回放图片。
- `SimulatedIndustrialCamera`（`Simulation/`）：模拟软/硬触发、断线重连。

真实工业相机（推荐海康机器人 MVS）按以下步骤接入，**无需改动上层任何代码**。

## 1. 安装 SDK

1. 安装「海康机器人 MVS」客户端（含相机驱动与 SDK）。
2. SDK 的 .NET 封装 `MvCameraControl.Net.dll` 通常位于：
   `C:\Program Files (x86)\MVS\Development\DotNet\`
   对应原生库（x64）：`...\Runtime\Win64_x64\` 下的 `MvCameraControl.dll` 等。

## 2. 引用 SDK

在 `VisionInspection.Camera.csproj` 增加（路径按实际安装调整）：

```xml
<ItemGroup Condition="'$(DefineConstants.Contains(HIKVISION))'=='true'">
  <Reference Include="MvCameraControl.Net">
    <HintPath>C:\Program Files (x86)\MVS\Development\DotNet\MvCameraControl.Net.dll</HintPath>
  </Reference>
</ItemGroup>
```

并在需要启用真机时定义编译符号 `HIKVISION`（`<DefineConstants>HIKVISION</DefineConstants>` 或 `dotnet build -p:DefineConstants=HIKVISION`）。
未定义时项目照常编译（走模拟/离线实现），因此源码库无需依赖 SDK 即可构建。

## 3. 实现 `HikvisionCamera`

在 `Simulation` 同级新建 `Hikvision/HikvisionCamera.cs`，整体用 `#if HIKVISION ... #endif` 包裹，实现 `ICamera`：

```csharp
#if HIKVISION
using System;
using MvCamCtrl.NET;               // 具体命名空间以所装 SDK 版本为准
using VisionInspection.Core.Abstractions;
using VisionInspection.Core.Imaging;
using CorePixelFormat = VisionInspection.Core.Imaging.PixelFormat;

namespace VisionInspection.Camera.Hikvision
{
    public sealed class HikvisionCamera : ICamera
    {
        private readonly MyCamera _cam = new MyCamera();
        private bool _connected;

        public HikvisionCamera(string deviceIdentifier) { /* 保存序列号/IP */ }

        public bool IsConnected => _connected;
        public event EventHandler<CameraFrameEventArgs> FrameReceived;
        public event EventHandler<CameraConnectionEventArgs> ConnectionChanged;

        public void Open()
        {
            // 1) MyCamera.MV_CC_EnumDevices 枚举设备，按 deviceIdentifier 选中
            // 2) _cam.MV_CC_CreateDevice / MV_CC_OpenDevice
            // 3) 关闭自动曝光/自动白平衡（工业检测必须锁定曝光）
            // 4) 注册回调 MV_CC_RegisterImageCallBackEx → 在回调里把帧转 ImageFrame 并触发 FrameReceived
            _connected = true;
            ConnectionChanged?.Invoke(this, new CameraConnectionEventArgs(true));
        }

        public void SetTriggerMode(TriggerMode mode)
        {
            // 硬触发：设置 TriggerMode=On、TriggerSource=Line0（PLC I/O 接入的触发线）
            // 软触发：TriggerMode=On、TriggerSource=Software；Grab 时 MV_CC_SetCommandValue("TriggerSoftware")
        }

        public ImageFrame Grab(int timeoutMs = 2000)
        {
            // 软触发：发送 TriggerSoftware，MV_CC_GetOneFrameTimeout 取帧 → 转 ImageFrame（BGR/Gray）
            throw new NotImplementedException();
        }

        public void Close() { /* MV_CC_StopGrabbing / CloseDevice / DestroyDevice */ }
        public void Dispose() => Close();
    }
}
#endif
```

### 帧转换要点

海康帧像素数据 → `ImageFrame`：
- 灰度相机：`PixelType_Gvsp_Mono8` → `CorePixelFormat.Gray8`，`stride = width`。
- 彩色相机：转为 `BGR8`（`MV_CC_ConvertPixelType` → `PixelType_Gvsp_BGR8_Packed`），`stride = width*3`，标 `Bgr24`。
- `ImageFrame` 已是中立帧，视觉层 `MatConverter` 会转 `Mat`。

## 4. 硬触发接线（与 PLC 同步）

- 相机触发线（如 Line0）接 PLC 输出点；板件到位时 PLC 输出脉冲 → 相机曝光。
- 相机曝光/白平衡**锁定为手动固定值**，避免自动算法导致帧间亮度漂移影响判有无。
- 采用硬触发可消除软触发的数十毫秒抖动，保证运动/快节拍下取像时机稳定。

## 5. 选型建议

| 接口 | 适用 | 说明 |
|---|---|---|
| GigE Vision | 多相机 / 远距离 | 线缆长（≤100m）、带宽中等 |
| USB3 Vision | 单相机 / 近距离 | 带宽高、线缆短（≤5m） |

- 面阵相机分辨率按「最小板件在图像中占用像素」反推，保证缺件判定有足够像素差异。
- **光源优先背光**：判有无对比最大（有件挡光变暗 / 无件透光变亮）。
