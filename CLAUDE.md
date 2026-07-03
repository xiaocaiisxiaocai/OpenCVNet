# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

> 交流与注释使用中文。Windows 路径用反斜杠 `\`。

## 项目简介

底板上 N×M 排布的板件**缺件检测**系统：三菱 FX PLC 触发 → 工业相机取像 → OpenCV 逐工位判有无 → 结果回写 PLC（双向握手闭环）。支持**多产品换型**（配方切换）、开机自启无人值守。板件大小不一，故每工位为**独立 ROI**。

## 常用命令

```bash
# 构建 / 测试（在仓库根目录）
dotnet build VisionInspection.sln
dotnet test  VisionInspection.sln
dotnet build VisionInspection.sln -c Release

# 单个测试类 / 方法
dotnet test --filter "FullyQualifiedName~HandshakeControllerTests"
dotnet test --filter "FullyQualifiedName~SyntheticInspectionTests.One_Missing_Yields_Ng_And_Correct_Index"

# 运行上位机（演示装配：模拟相机 + 模拟 PLC）
dotnet run --project src/VisionInspection.App
```

- 目标框架 **net48**（SDK 风格 csproj，`dotnet build` 可直接编译 WPF，无需 Visual Studio）。
- 可执行项目与测试工程均 `PlatformTarget=x64`（OpenCvSharp native 依赖）；测试工程另固定 `RuntimeIdentifier=win-x64`。
- 运行时在程序目录生成 `recipes\`、`logs\`、`archive\yyyyMMdd\`、`settings.json`、`stats.json`、`heartbeat`。
- **CI**：`.github/workflows/ci.yml` 在 `windows-latest` 上 `dotnet build`+`dotnet test`（Release 配置；net48/WPF/OpenCvSharp 需 Windows runner）。

## 架构（分层）

```
Core ──(接口/领域, 零第三方依赖)
 ├─ Vision   OpenCvSharp：配准 → 逐工位 ROI → 判有无 → 示教（工位并行；任一非“有件”即整体 NG）
 ├─ Camera   ICamera：离线回放 / 模拟工业相机 (+ 海康模板见 docs)
 ├─ Plc      自研 MC 3E 协议客户端 + 模拟 PLC + 握手状态机
 └─ Infrastructure  配方存储(JSON) / 结果留档(CSV+NG图,按天保留) / 统计持久化(stats.json) / Serilog
Runtime ── 编排：相机→检测→PLC握手回写→归档→统计/报警（后台循环）
App ────── WPF (WPF-UI + CommunityToolkit.Mvvm)：运行监视 / 配方管理 / 系统设置
Watchdog ─ 独立进程，监控主程序崩溃自恢复
```

**关键抽象**（`Core/Abstractions`）：`ICamera`、`IPlcClient`、`IInspector`、`IRecipeStore`。硬件实现与模拟/离线实现可无缝替换——这是无硬件开发与单元测试的基础。

**数据流一次检测**：`ImageFrame`（中立帧，让 Core 零依赖 OpenCV）→ `OpenCvInspector.Inspect(frame, recipe)` → `InspectionResult`（逐工位 `StationResult`）→ `DefectBitmap.Encode` 编成缺件位图 `ushort[]` 回写 PLC D 区。

**配方驱动**：`Recipe`=一种产品，含工位列表（各自 `RoiRect` + 阈值 + 方法）。检测引擎不硬编码 N×M。运行时按 PLC 下发的型号码（`HandshakeAddressMap.ModelCodeWord`）加载对应配方；无匹配则写错误码 + NG（`HandshakeController`）。

**握手是边沿驱动的**：`RuntimeService.StepOnce()` → `HandshakeController.ProcessOnce()` 是整个编排的同步心脏——触发位**上升沿**跑一次周期（置忙 → 取像检测 → 写缺件位图 + OK/NG → 置完成清忙），**下降沿**（PLC 读走结果并清触发）清完成位。生产用后台线程 `RuntimeLoop` 反复调用它并按 `HeartbeatIntervalMs` 翻转心跳位；测试直接同步调用同一入口，故无需线程/硬件即可覆盖握手全流程。异常时 `TryRecover` 重连相机/PLC。

## 从演示切换到现场硬件

`src/VisionInspection.App/App.xaml.cs` 的 `OnStartup` 构造 `ApplicationHost`（组合根），由**程序目录的 `settings.json` 驱动**装配——改配置即可切换硬件/地址，无需重编译（可在 App 的**系统设置**页内编辑并「保存并应用」热生效，或直接改 `settings.json`；首次运行默认 = 全模拟演示）：

| 设置项 | 演示 | 现场 |
|---|---|---|
| `Camera.Mode` | `Simulated` | `Hikvision`（需 MV SDK，`docs/camera-integration.md`）/ `Offline`（图片回放） |
| `Plc.Mode` | `Simulated` | `Melsec`（`MelsecMcClient`，配 `Host/Port`） |
| `Handshake` | 默认地址 | 按 PLC 程序改 `HandshakeAddressMap` |
| `Archive.RetentionDays` | 90 天 | 按需（0 = 永久保留） |

`ApplicationHost.ApplySettings` 会热重建运行链、事件稳定转发，故运行页无需重绑。`App.xaml.cs` 里的 `CreateDemoFrame`/`CreateBoardFrame` 仅为模拟相机造演示底图（运行随机缺件；配方标定按行×列出满件底图）。部署/自启/联调清单见 `docs/deployment.md`。

## 约定与注意

- **C# 语言版本锁 8.0**：全部工程 `LangVersion=8.0`、`Nullable=disable`。勿用 C# 9+ 语法（`record`、`init`、目标类型 `new()`、文件范围命名空间、全局 using、`is not` 模式等），net48 下无法编译。
- **JSON 统一 Newtonsoft.Json**（settings/recipes/stats 序列化均是），不引 System.Text.Json。
- **引用 Vision 的可执行/测试工程**需自带 `OpenCvSharp4.runtime.win` 包拷贝 native DLL（App、Tests 均如此），缺了运行时抛 `DllNotFoundException`。
- **测试无需硬件**：Vision 用 OpenCvSharp 合成底板图；Plc/相机用模拟实现；握手用内存 `SimulatedPlcClient`。
- **判有无方向**：`ForegroundRatioDetector` 默认亮像素为前景；背光场景（有件挡光变暗）构造时传 `darkIsForeground: true`，或用示教按样本自动定阈值。检测方法目前仅实现 `ForegroundRatio`（UI 判定方法下拉已隐藏未实现项）。
- **自动定位**：配方标定支持 `PartLocator`（`Vision.Teaching`）从底图自动识别件位置、生成贴合各件的 ROI（Otsu + 轮廓 + 面积过滤 + 行主序，亮/暗/自动极性），替代等距网格的手动微调。
- **MC 协议**：自研 3E 二进制（`Plc/Mc`），规避 HslCommunication 商用授权；位软元件单点用 **MC 位单位命令（子指令 0x0001）**，不做字读改写，避免与 PLC 并发写同一 16 位字时互相覆盖。真机行为需现场用实际 PLC 验证。
- **握手/运行健壮性**：`HandshakeController` 对检测加**单周期超时**（`RuntimeOptions.InspectTimeoutMs`，卡死写错误码+清忙）；`RuntimeService` 故障态**告警去重+退避**，`MelsecMcClient` 事务失败即断开触发重连。
- **不要用 Windows 服务**跑 App（需 UI + 相机 SDK，Session 0 隔离会踩坑）；用「自动登录 + 启动项 + 看门狗」。
- **开机自启脚本**：`scripts\install-autostart.ps1` / `uninstall-autostart.ps1` 装/卸自启项（配合 `docs\deployment.md`）。
- **看门狗用法**：`VisionInspection.Watchdog.exe [主程序路径]`，缺省监控同目录 `VisionInspection.App.exe`，5 秒轮询。进程不存在→拉起；进程在但心跳文件 `heartbeat`（App 每秒由 UI 线程刷新）超 30s 未更新→判**假死**并结束重启；含重启退避防崩溃风暴。
- **单实例**：App 与 Watchdog 均用 `Global\...SingleInstance` 命名 Mutex 保证单实例；重复启动 App 会弹框并退出。
- **配准**：`OpenCvInspector` 默认注入 `FiducialAlignment`——按 `Recipe.Fiducial` 的搜索区检出基准点(mark/角点)、与标定期望位(搜索区中心)求仿射(≥3 点全仿射/2 点相似/1 点平移),经 `RoiMapper` 补偿底板摆放偏差；`Fiducial.Type=None` 时退化为恒等(演示/严格固定场景)。现场需配置基准搜索区并使基准点位于区中心。
- WPF-UI 采用 **3.0.5**（已验证支持 net48）。
```
