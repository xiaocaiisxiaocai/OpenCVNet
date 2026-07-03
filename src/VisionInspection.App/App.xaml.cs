using System;
using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using Serilog.Core;
using VisionInspection.App.Hosting;
using VisionInspection.App.Settings;
using VisionInspection.App.ViewModels;
using VisionInspection.App.Views;
using VisionInspection.Camera.Simulation;
using VisionInspection.Core.Abstractions;
using VisionInspection.Core.Imaging;
using VisionInspection.Core.Models;
using VisionInspection.Infrastructure.Archiving;
using VisionInspection.Infrastructure.Logging;
using VisionInspection.Infrastructure.Storage;
using VisionInspection.Plc.Simulation;
using VisionInspection.Runtime;
using VisionInspection.Vision.Inspection;
using CorePixelFormat = VisionInspection.Core.Imaging.PixelFormat;
using WpfUiMessageBox = Wpf.Ui.Controls.MessageBox;

namespace VisionInspection.App
{
    public partial class App : Application
    {
        private const string InstanceMutexName = @"Global\VisionInspection.App.SingleInstance";

        // 演示底板：640×480，3 列 × 2 行白块（缺件随机）。工位 ROI 与块位置匹配。
        private const int DemoWidth = 640, DemoHeight = 480, DemoCols = 3, DemoRows = 2;
        private const int CellW = DemoWidth / DemoCols, CellH = DemoHeight / DemoRows;
        private static readonly Random Rng = new Random();

        private Mutex _instanceMutex;
        private ApplicationHost _host;
        private Logger _logger;
        private DispatcherTimer _heartbeatTimer;

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            _instanceMutex = new Mutex(true, InstanceMutexName, out bool createdNew);
            if (!createdNew)
            {
                MessageBox.Show("程序已在运行。", "板件缺件视觉检测系统",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                Shutdown();
                return;
            }

            var baseDir = AppDomain.CurrentDomain.BaseDirectory;
            _logger = LogSetup.CreateLogger(Path.Combine(baseDir, "logs"));
            _logger.Information("程序启动");
            RegisterGlobalExceptionHandlers();

            // 组合根：设置驱动(settings.json)。默认设置 = 模拟相机 + 模拟 PLC(演示)。
            // 现场改 settings.json 即可切换 海康/Melsec、握手地址、归档保留策略等,无需重编译。
            // 演示相机的两类底图:运行用随机缺件帧;配方标定用按行×列的满件底图。
            try
            {
                _host = new ApplicationHost(baseDir, CreateDemoFrame,
                    (rows, cols) => CreateBoardFrame(cols > 0 ? cols : DemoCols, rows > 0 ? rows : DemoRows, -1));
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "运行链初始化失败，降级为默认模拟配置");
                MessageBox.Show("配置加载失败，已降级为默认模拟配置。\n\n" + ex.Message,
                    "板件缺件视觉检测系统", MessageBoxButton.OK, MessageBoxImage.Warning);
                var settingsPath = Path.Combine(baseDir, "settings.json");
                new AppSettingsStore(settingsPath).Save(new AppSettings());
                _host = new ApplicationHost(baseDir, CreateDemoFrame,
                    (rows, cols) => CreateBoardFrame(cols > 0 ? cols : DemoCols, rows > 0 ? rows : DemoRows, -1));
            }
            EnsureDemoRecipe(_host);

            _host.Log += m => _logger.Information("{Message}", m);
            _host.Alarm += a => _logger.Warning("[{Level}] {Message}", a.Level, a.Message);
            _host.StructuredLog += WriteRuntimeLog;
            if (!string.IsNullOrWhiteSpace(_host.StartupWarning))
                _logger.Warning("{Message}", _host.StartupWarning);

            var runViewModel = new RunViewModel(_host);
            var mainViewModel = new MainWindowViewModel(_host, runViewModel);
            var window = new MainWindow(mainViewModel);
            // 首屏渲染后自动触发一次演示检测,使界面立即有内容(真机由 PLC 触发)。
            window.ContentRendered += (s, ev) => { try { _host.SimulateTrigger(); } catch { } };
            window.Show();
            StartHeartbeat(baseDir);
        }

        private void RegisterGlobalExceptionHandlers()
        {
            DispatcherUnhandledException += OnDispatcherUnhandledException;
            AppDomain.CurrentDomain.UnhandledException += (s, ex) =>
                _logger?.Error(ex.ExceptionObject as Exception, "非 UI 线程未处理异常");
        }

        private void WriteRuntimeLog(RuntimeLogEvent e)
        {
            if (e == null || _logger == null) return;
            var log = _logger
                .ForContext("Source", e.Source)
                .ForContext("EventName", e.EventName)
                .ForContext("ModelCode", e.ModelCode)
                .ForContext("Outcome", e.Outcome)
                .ForContext("MissingCount", e.MissingCount);

            if (string.Equals(e.Level, "Error", StringComparison.OrdinalIgnoreCase))
                log.Error(e.Exception, "{Message}", e.Message);
            else if (string.Equals(e.Level, "Warning", StringComparison.OrdinalIgnoreCase))
                log.Warning(e.Exception, "{Message}", e.Message);
            else
                log.Information(e.Exception, "{Message}", e.Message);
        }

        // UI 线程心跳文件:每秒由 Dispatcher 触发刷新;UI 卡死则文件不再更新,
        // 供看门狗判"假死"并重启(区别于仅进程存活检测)。
        private void StartHeartbeat(string baseDir)
        {
            var file = Path.Combine(baseDir, "heartbeat");
            _heartbeatTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _heartbeatTimer.Tick += (s, e) =>
            {
                try { File.WriteAllText(file, DateTime.Now.ToString("O")); }
                catch { /* 心跳写失败不影响运行 */ }
            };
            _heartbeatTimer.Start();
        }

        private async void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            _logger?.Error(e.Exception, "UI 线程未处理异常");
            e.Handled = true; // 先同步标记已处理，程序继续运行；再弹主题化提示。
            await new WpfUiMessageBox
            {
                Title = "板件缺件视觉检测系统",
                Content = "发生未处理异常，已记录日志。程序将尝试继续运行。\n\n" + e.Exception.Message,
                CloseButtonText = "确定"
            }.ShowDialogAsync();
        }

        protected override void OnExit(ExitEventArgs e)
        {
            _host?.Dispose();
            _logger?.Information("程序退出");
            _logger?.Dispose();
            _instanceMutex?.Dispose();
            base.OnExit(e);
        }

        private static void EnsureDemoRecipe(ApplicationHost host)
        {
            var store = host.RecipeStore;
            if (host.Settings.Plc.Mode == "Melsec" || host.Settings.Camera.Mode != "Simulated")
                return;

            // 型号码 "1" 已存在时一律不覆盖，避免重启改写现场生产配方。
            if (store.TryLoad("1", out _))
                return;

            var recipe = new Recipe { ModelCode = "1", Name = "演示配方 2×3", Rows = DemoRows, Columns = DemoCols };
            for (int r = 0; r < DemoRows; r++)
                for (int c = 0; c < DemoCols; c++)
                    recipe.Stations.Add(new Station
                    {
                        Index = r * DemoCols + c,
                        Row = r,
                        Column = c,
                        Name = $"工位{r * DemoCols + c}",
                        Roi = new RoiRect(c * CellW + 30, r * CellH + 30, CellW - 60, CellH - 60),
                        Threshold = 0.1,
                        Enabled = true
                    });
            store.Save(recipe);
        }

        private static ImageFrame CreateDemoFrame()
            => CreateBoardFrame(DemoCols, DemoRows, Rng.Next(0, DemoCols * DemoRows)); // 运行演示：随机缺 1 件

        /// <summary>
        /// 生成演示底图：cols×rows 等距白块、深灰底。missIndex≥0 时该位缺件（-1=满件）。
        /// 件尺寸与边距随格数自适应，故 2×3、4×4 等不同排布都能得到分离清晰的块。
        /// </summary>
        private static ImageFrame CreateBoardFrame(int cols, int rows, int missIndex)
        {
            cols = Math.Max(1, cols);
            rows = Math.Max(1, rows);
            const int stride = DemoWidth * 3;
            var data = new byte[stride * DemoHeight];
            for (int i = 0; i < data.Length; i++) data[i] = 60; // 深灰底

            int cellW = DemoWidth / cols, cellH = DemoHeight / rows;
            int m0 = Math.Min(cellW, cellH);
            int margin = Math.Min(Math.Max(4, m0 / 5), m0 / 3); // 边距自适应，件间始终留缝且块不退化

            for (int r = 0; r < rows; r++)
                for (int c = 0; c < cols; c++)
                {
                    if (r * cols + c == missIndex) continue;
                    FillRect(data, stride, c * cellW + margin, r * cellH + margin,
                        cellW - 2 * margin, cellH - 2 * margin, 220);
                }
            return new ImageFrame(DemoWidth, DemoHeight, stride, CorePixelFormat.Bgr24, data, DateTime.UtcNow);
        }

        private static void FillRect(byte[] data, int stride, int x, int y, int w, int h, byte value)
        {
            for (int yy = y; yy < y + h; yy++)
                for (int xx = x; xx < x + w; xx++)
                {
                    int i = yy * stride + xx * 3;
                    data[i] = value; data[i + 1] = value; data[i + 2] = value;
                }
        }
    }
}
