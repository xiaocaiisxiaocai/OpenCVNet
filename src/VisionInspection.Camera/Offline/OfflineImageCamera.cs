using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using VisionInspection.Core.Abstractions;
using VisionInspection.Core.Imaging;
using CorePixelFormat = VisionInspection.Core.Imaging.PixelFormat;
using GdiPixelFormat = System.Drawing.Imaging.PixelFormat;

namespace VisionInspection.Camera.Offline
{
    /// <summary>
    /// 离线图像相机：从指定文件夹按文件名顺序回放图片，实现 <see cref="ICamera"/>，
    /// 使检测流程在无硬件时即可开发与测试。软触发每次返回下一张（可循环）。
    /// </summary>
    public sealed class OfflineImageCamera : ICamera
    {
        private static readonly string[] SupportedExtensions = { ".png", ".jpg", ".jpeg", ".bmp" };

        private readonly List<string> _files;
        private readonly string _folder;
        private readonly bool _loop;
        private int _cursor;
        private bool _connected;

        public OfflineImageCamera(string folder, bool loop = true)
        {
            if (string.IsNullOrWhiteSpace(folder))
                throw new ArgumentException("图像文件夹不能为空。", nameof(folder));
            _folder = folder;
            _files = new List<string>();
            _loop = loop;
        }

        public bool IsConnected => _connected;

        public event EventHandler<CameraFrameEventArgs> FrameReceived;
        public event EventHandler<CameraConnectionEventArgs> ConnectionChanged;

        public void Open()
        {
            LoadFiles();
            _connected = true;
            ConnectionChanged?.Invoke(this, new CameraConnectionEventArgs(true));
        }

        public void Close()
        {
            _connected = false;
            ConnectionChanged?.Invoke(this, new CameraConnectionEventArgs(false));
        }

        /// <summary>离线相机忽略触发模式设置。</summary>
        public void SetTriggerMode(TriggerMode mode) { }

        public ImageFrame Grab(int timeoutMs = 2000)
            => Grab(timeoutMs, CancellationToken.None);

        public ImageFrame Grab(int timeoutMs, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!_connected) throw new InvalidOperationException("相机未打开。");
            if (_cursor >= _files.Count)
            {
                if (!_loop) throw new TimeoutException("离线图像已回放完毕。");
                _cursor = 0;
            }

            var path = _files[_cursor++];
            cancellationToken.ThrowIfCancellationRequested();
            var frame = LoadAsFrame(path);
            cancellationToken.ThrowIfCancellationRequested();
            FrameReceived?.Invoke(this, new CameraFrameEventArgs(frame));
            return frame;
        }

        /// <summary>用 System.Drawing 解码图片并转换为中立的 <see cref="ImageFrame"/>（BGR24）。</summary>
        public static ImageFrame LoadAsFrame(string path)
        {
            using (var bmp = new Bitmap(path))
            using (var bmp24 = EnsureBgr24(bmp))
            {
                var rect = new Rectangle(0, 0, bmp24.Width, bmp24.Height);
                var data = bmp24.LockBits(rect, ImageLockMode.ReadOnly, GdiPixelFormat.Format24bppRgb);
                try
                {
                    int stride = data.Stride;
                    var bytes = new byte[stride * bmp24.Height];
                    Marshal.Copy(data.Scan0, bytes, 0, bytes.Length);
                    // GDI+ 的 Format24bppRgb 在内存中的通道序即为 BGR。
                    return new ImageFrame(bmp24.Width, bmp24.Height, stride, CorePixelFormat.Bgr24, bytes, DateTime.UtcNow);
                }
                finally
                {
                    bmp24.UnlockBits(data);
                }
            }
        }

        private static Bitmap EnsureBgr24(Bitmap src)
        {
            if (src.PixelFormat == GdiPixelFormat.Format24bppRgb)
                return (Bitmap)src.Clone();

            var clone = new Bitmap(src.Width, src.Height, GdiPixelFormat.Format24bppRgb);
            using (var g = Graphics.FromImage(clone))
                g.DrawImage(src, new Rectangle(0, 0, src.Width, src.Height));
            return clone;
        }

        public void Dispose() => Close();

        private void LoadFiles()
        {
            if (!Directory.Exists(_folder))
                throw new DirectoryNotFoundException("图像文件夹不存在：" + _folder);

            _files.Clear();
            _files.AddRange(Directory.GetFiles(_folder)
                .Where(f => SupportedExtensions.Contains(Path.GetExtension(f).ToLowerInvariant()))
                .OrderBy(f => f, StringComparer.OrdinalIgnoreCase));
            if (_files.Count == 0)
                throw new InvalidOperationException("文件夹中没有受支持的图像文件：" + _folder);
            _cursor = 0;
        }
    }
}
