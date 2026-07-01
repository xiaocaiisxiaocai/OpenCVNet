# 部署与现场联调指南

## 1. 运行时依赖

- **操作系统**：Windows 10 / 11 或 Win7 SP1（x64）。
- **.NET Framework 4.8 运行时**（Win10/11 自带；Win7 需安装）。
- **VC++ 运行库**：OpenCvSharp native（`OpenCvSharpExtern.dll`）依赖 VC++ 2015-2022 x64 可再发行包。
- 发布为 x64（各可执行项目已设 `PlatformTarget=x64`）。

## 2. 发布

```bash
dotnet build VisionInspection.sln -c Release
# 或发布 App 与看门狗
dotnet publish src/VisionInspection.App/VisionInspection.App.csproj -c Release -r win-x64
dotnet build src/VisionInspection.Watchdog/VisionInspection.Watchdog.csproj -c Release
```

将 `VisionInspection.App.exe`、`VisionInspection.Watchdog.exe` 及依赖 DLL、`scripts\` 一并部署到同一目录，例如 `D:\VisionInspection\`。运行目录会自动生成：
- `recipes\`（配方 JSON）
- `logs\`（Serilog 日志、看门狗日志）
- `archive\yyyyMMdd\`（结果 CSV 与 NG 图）

## 3. 开机自启（无人值守）

三层保障：

1. **自动登录**（开机进入桌面会话，供 UI 与相机 SDK 运行）
   - `netplwiz` → 取消勾选“要使用本计算机，用户必须输入用户名和密码” → 输入自动登录账户密码。
   - 或注册表 `HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon`：`AutoAdminLogon=1`、`DefaultUserName`、`DefaultPassword`。
2. **启动项**（登录后拉起看门狗）
   - 运行 `scripts\install-autostart.ps1`（写入 `HKCU\...\Run`）。
3. **看门狗**（主程序崩溃自动重启）
   - `VisionInspection.Watchdog.exe` 每 5s 检查 `VisionInspection.App` 进程，缺失即拉起。

> 之所以**不用 Windows 服务**：本程序需显示 UI 并访问相机 SDK，服务运行在 Session 0（与桌面隔离）会导致 UI/部分 SDK 异常。故采用「自动登录 + 启动项 + 看门狗」。

## 4. 单实例与异常兜底

- 主程序与看门狗均以命名 Mutex 保证单实例。
- 主程序注册全局异常处理：UI 线程异常记录日志并提示后继续；严重崩溃退出后由看门狗重启。

## 5. 现场装配替换点（从演示切到真机）

`src/VisionInspection.App/App.xaml.cs` 的装配处替换：

| 演示实现 | 现场实现 |
|---|---|
| `SimulatedIndustrialCamera` | 海康 `HikvisionCamera`（见 `docs/camera-integration.md`，启用 `HIKVISION` 编译符号） |
| `SimulatedPlcClient` | `MelsecMcClient(host, port)`（以太网 MC/SLMP） |
| 演示帧工厂 | 相机硬触发实采帧 |

握手软元件地址在 `HandshakeAddressMap` 调整为现场 PLC 实际地址。

## 6. 现场联调清单

- [ ] **光源**：优先背光；锁定相机曝光/白平衡为手动固定值，加遮光罩，确认有件/无件对比稳定。
- [ ] **定位配准**：设置底板基准（Mark/角点），确认底板平移/旋转后边缘工位不误判。
- [ ] **逐工位标定**：为每种产品建配方，逐工位框选 ROI（大小各异），示教满件/缺件样张自动定阈值。
- [ ] **换型**：确认 PLC 下发型号码 → 自动加载对应配方；无匹配配方时报警停线（错误码 `NoRecipe`）。
- [ ] **握手时序**：用 GX Works/PLC 模拟触发，核对 触发→忙→完成→结果软元件→清位 全流程，验证防重复/丢触发/超时/心跳。
- [ ] **误判成本**：确认漏检 vs 误检代价，据此调阈值方向与人工复核机制。
- [ ] **节拍**：确认单件检测耗时满足产线节拍，必要时并行/降分辨率。
- [ ] **稳定性**：断电重启验证开机自启与自恢复；连续老化运行（≥8h）观察内存与稳定性；核对不合格图与 CSV 留档。
