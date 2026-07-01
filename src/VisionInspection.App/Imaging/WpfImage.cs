using System.Windows.Media;
using System.Windows.Media.Imaging;
using VisionInspection.Core.Imaging;
using CorePixelFormat = VisionInspection.Core.Imaging.PixelFormat;
using WpfPixelFormat = System.Windows.Media.PixelFormat;

namespace VisionInspection.App.Imaging
{
    /// <summary>中立 <see cref="ImageFrame"/> → WPF <see cref="BitmapSource"/> 转换（用于界面实时显示）。</summary>
    public static class WpfImage
    {
        public static BitmapSource ToBitmapSource(ImageFrame f)
        {
            if (f == null) return null;

            WpfPixelFormat pf;
            switch (f.PixelFormat)
            {
                case CorePixelFormat.Gray8: pf = PixelFormats.Gray8; break;
                case CorePixelFormat.Bgra32: pf = PixelFormats.Bgra32; break;
                default: pf = PixelFormats.Bgr24; break;
            }

            var bmp = BitmapSource.Create(f.Width, f.Height, 96, 96, pf, null, f.Data, f.Stride);
            bmp.Freeze(); // 允许跨线程访问
            return bmp;
        }
    }
}
