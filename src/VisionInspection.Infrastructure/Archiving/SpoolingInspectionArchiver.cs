using System;
using System.IO;
using System.Threading;
using Newtonsoft.Json;
using VisionInspection.Core.Abstractions;
using VisionInspection.Core.Imaging;
using VisionInspection.Core.Models;
using VisionInspection.Infrastructure.Storage;

namespace VisionInspection.Infrastructure.Archiving
{
    /// <summary>
    /// 磁盘 spool 归档器：先把结果落到本地队列文件，再后台消费。
    /// 进程崩溃时未消费的结果会在下次启动继续写 CSV；图像不进入 spool，避免大文件阻塞关键路径。
    /// </summary>
    public sealed class SpoolingInspectionArchiver : IInspectionArchiver, IObservableArchiver
    {
        private sealed class SpoolItem
        {
            public InspectionResult Result { get; set; }
        }

        private readonly IInspectionArchiver _inner;
        private readonly string _spoolDir;
        private readonly Thread _worker;
        private readonly AutoResetEvent _signal = new AutoResetEvent(false);
        private readonly object _consumeLock = new object();
        private volatile bool _disposed;
        private int _active;

        public event Action<string> Event;

        public SpoolingInspectionArchiver(IInspectionArchiver inner, string spoolDir, bool autoStart = true)
        {
            _inner = inner ?? throw new ArgumentNullException(nameof(inner));
            if (string.IsNullOrWhiteSpace(spoolDir)) throw new ArgumentException("spool 目录不能为空。", nameof(spoolDir));
            _spoolDir = spoolDir;
            Directory.CreateDirectory(_spoolDir);

            if (autoStart)
            {
                _worker = new Thread(Loop) { IsBackground = true, Name = "ArchiveSpoolWriter" };
                _worker.Start();
            }
        }

        public void Archive(ImageFrame frame, InspectionResult result)
        {
            if (_disposed || result == null) return;
            var path = Path.Combine(_spoolDir, DateTime.UtcNow.ToString("yyyyMMddHHmmssfff") + "_" + Guid.NewGuid().ToString("N") + ".json");
            AtomicFile.WriteText(path, JsonConvert.SerializeObject(new SpoolItem { Result = result }), backup: false);
            _signal.Set();

            // 当前周期有图像时立即尝试一次，保留 NG 图；失败时 spool 文件仍在。
            if (_worker != null)
                TryConsumeOne(path, frame);
        }

        public bool Flush(TimeSpan timeout)
        {
            var deadline = DateTime.UtcNow + timeout;
            while (DateTime.UtcNow < deadline)
            {
                DrainOnce();
                if (Directory.GetFiles(_spoolDir, "*.json").Length == 0 && Interlocked.CompareExchange(ref _active, 0, 0) == 0)
                    return true;
                Thread.Sleep(20);
            }
            return false;
        }

        private void Loop()
        {
            while (!_disposed)
            {
                DrainOnce();
                _signal.WaitOne(200);
            }
            DrainOnce();
        }

        private void DrainOnce()
        {
            foreach (var path in Directory.GetFiles(_spoolDir, "*.json"))
                TryConsumeOne(path, null);
        }

        private void TryConsumeOne(string path, ImageFrame frame)
        {
            lock (_consumeLock)
            {
                if (!File.Exists(path)) return;
                Interlocked.Increment(ref _active);
                try
                {
                    var item = JsonConvert.DeserializeObject<SpoolItem>(File.ReadAllText(path));
                    if (item?.Result == null)
                    {
                        File.Move(path, path + ".bad");
                        Event?.Invoke("ArchiveSpoolBadItem: " + Path.GetFileName(path));
                        return;
                    }
                    _inner.Archive(frame, item.Result);
                    File.Delete(path);
                }
                catch (Exception ex)
                {
                    Event?.Invoke("ArchiveSpoolFailed: " + ex.Message);
                }
                finally
                {
                    Interlocked.Decrement(ref _active);
                }
            }
        }

        public void Dispose()
        {
            _disposed = true;
            _signal.Set();
            _worker?.Join(2000);
            _signal.Dispose();
            _inner.Dispose();
        }
    }
}
