using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using VisionInspection.Core.Imaging;
using VisionInspection.Core.Models;
using CorePixelFormat = VisionInspection.Core.Imaging.PixelFormat;
using GdiPixelFormat = System.Drawing.Imaging.PixelFormat;

namespace VisionInspection.Infrastructure.Archiving
{
    /// <summary>
    /// 结果留档：按日期（yyyyMMdd）分目录，将每次检测结果追加到 results.csv，
    /// 并对不合格（NG / Error）保存原始图像用于追溯。
    /// </summary>
    public sealed class InspectionArchiver
    {
        private readonly string _root;
        private readonly bool _saveNgImageOnly;
        private readonly int _retentionDays;
        private readonly object _sync = new object();
        private DateTime _lastCleanupDate = DateTime.MinValue;

        public InspectionArchiver(string root, bool saveNgImageOnly = true, int retentionDays = 0)
        {
            if (string.IsNullOrWhiteSpace(root))
                throw new ArgumentException("归档根目录不能为空。", nameof(root));
            _root = root;
            _saveNgImageOnly = saveNgImageOnly;
            _retentionDays = retentionDays; // 0 = 永久保留
            Directory.CreateDirectory(_root);
            CleanupExpired();
        }

        public void Archive(ImageFrame frame, InspectionResult result)
        {
            if (result == null) throw new ArgumentNullException(nameof(result));

            var local = result.TimestampUtc.ToLocalTime();
            // 跨天时清理过期留档,避免长期运行磁盘写满。
            if (_retentionDays > 0 && _lastCleanupDate != DateTime.Now.Date) CleanupExpired();

            var dir = Path.Combine(_root, local.ToString("yyyyMMdd"));
            Directory.CreateDirectory(dir);

            AppendCsv(dir, result, local);

            bool ng = result.Outcome != InspectionOutcome.Ok;
            if (frame != null && (ng || !_saveNgImageOnly))
                SaveImage(frame, Path.Combine(dir, ImageName(result, local)));
        }

        /// <summary>删除早于保留期的按日目录(yyyyMMdd)。retentionDays≤0 时不清理。</summary>
        private void CleanupExpired()
        {
            _lastCleanupDate = DateTime.Now.Date;
            if (_retentionDays <= 0 || !Directory.Exists(_root)) return;

            var cutoff = DateTime.Now.Date.AddDays(-_retentionDays);
            foreach (var dir in Directory.GetDirectories(_root))
            {
                var name = Path.GetFileName(dir);
                if (DateTime.TryParseExact(name, "yyyyMMdd", CultureInfo.InvariantCulture,
                        DateTimeStyles.None, out var d) && d.Date < cutoff)
                {
                    try { Directory.Delete(dir, true); }
                    catch { /* 占用/权限失败忽略,下次再清 */ }
                }
            }
        }

        private void AppendCsv(string dir, InspectionResult r, DateTime local)
        {
            var csv = Path.Combine(dir, "results.csv");
            var missing = string.Join(";", r.Stations.Where(s => s.IsMissing).Select(s => s.StationIndex));
            var line = string.Join(",",
                local.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture),
                Csv(r.ModelCode), r.Outcome, r.MissingCount, missing, Csv(r.ErrorCode), r.ElapsedMs);

            lock (_sync)
            {
                bool exists = File.Exists(csv);
                using (var w = new StreamWriter(csv, append: true, encoding: new UTF8Encoding(true)))
                {
                    if (!exists) w.WriteLine("时间,型号码,结论,缺件数,缺件工位,错误码,耗时ms");
                    w.WriteLine(line);
                }
            }
        }

        private static string Csv(string s) => string.IsNullOrEmpty(s) ? "" : s.Replace(",", "，");

        private static string ImageName(InspectionResult r, DateTime local)
            => $"{local:HHmmss_fff}_{r.ModelCode}_{r.Outcome}.png";

        private static void SaveImage(ImageFrame f, string path)
        {
            using (var bmp = ToBitmap(f))
                bmp.Save(path, ImageFormat.Png);
        }

        private static Bitmap ToBitmap(ImageFrame f)
        {
            // Gray8 先扩展为 BGR24，其余按原格式落图。
            if (f.PixelFormat == CorePixelFormat.Gray8)
                return GrayToBgrBitmap(f);

            var gpf = f.PixelFormat == CorePixelFormat.Bgra32 ? GdiPixelFormat.Format32bppArgb : GdiPixelFormat.Format24bppRgb;
            var bmp = new Bitmap(f.Width, f.Height, gpf);
            var data = bmp.LockBits(new Rectangle(0, 0, f.Width, f.Height), ImageLockMode.WriteOnly, gpf);
            try
            {
                int copy = Math.Min(f.Stride, data.Stride);
                for (int y = 0; y < f.Height; y++)
                    Marshal.Copy(f.Data, y * f.Stride, data.Scan0 + y * data.Stride, copy);
            }
            finally
            {
                bmp.UnlockBits(data);
            }
            return bmp;
        }

        private static Bitmap GrayToBgrBitmap(ImageFrame f)
        {
            var bmp = new Bitmap(f.Width, f.Height, GdiPixelFormat.Format24bppRgb);
            var data = bmp.LockBits(new Rectangle(0, 0, f.Width, f.Height), ImageLockMode.WriteOnly, GdiPixelFormat.Format24bppRgb);
            try
            {
                var row = new byte[data.Stride];
                for (int y = 0; y < f.Height; y++)
                {
                    for (int x = 0; x < f.Width; x++)
                    {
                        byte g = f.Data[y * f.Stride + x];
                        row[x * 3] = g; row[x * 3 + 1] = g; row[x * 3 + 2] = g;
                    }
                    Marshal.Copy(row, 0, data.Scan0 + y * data.Stride, data.Stride);
                }
            }
            finally
            {
                bmp.UnlockBits(data);
            }
            return bmp;
        }
    }
}
