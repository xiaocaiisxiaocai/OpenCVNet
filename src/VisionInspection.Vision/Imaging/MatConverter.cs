using System;
using System.Runtime.InteropServices;
using OpenCvSharp;
using VisionInspection.Core.Imaging;
using CorePixelFormat = VisionInspection.Core.Imaging.PixelFormat;

namespace VisionInspection.Vision.Imaging
{
    /// <summary>
    /// 中立 <see cref="ImageFrame"/> 与 OpenCvSharp <see cref="Mat"/> 的相互转换。
    /// 转换均复制像素数据，避免外部缓冲区生命周期导致的悬垂。
    /// </summary>
    public static class MatConverter
    {
        /// <summary>ImageFrame → Mat（返回独立、连续的 Mat，调用方负责 Dispose）。</summary>
        public static Mat ToMat(ImageFrame frame)
        {
            if (frame == null) throw new ArgumentNullException(nameof(frame));

            MatType type;
            switch (frame.PixelFormat)
            {
                case CorePixelFormat.Gray8: type = MatType.CV_8UC1; break;
                case CorePixelFormat.Bgr24: type = MatType.CV_8UC3; break;
                case CorePixelFormat.Bgra32: type = MatType.CV_8UC4; break;
                default: throw new NotSupportedException("不支持的像素格式：" + frame.PixelFormat);
            }

            // 以带 stride 的像素数据包裹外部字节，再 Clone 出独立连续 Mat。
            using (var view = Mat.FromPixelData(frame.Height, frame.Width, type, frame.Data, frame.Stride))
            {
                return view.Clone();
            }
        }

        /// <summary>Mat → ImageFrame（复制像素，调用方后续可安全释放 Mat）。</summary>
        public static ImageFrame ToFrame(Mat mat)
        {
            if (mat == null) throw new ArgumentNullException(nameof(mat));

            CorePixelFormat fmt;
            switch (mat.Channels())
            {
                case 1: fmt = CorePixelFormat.Gray8; break;
                case 3: fmt = CorePixelFormat.Bgr24; break;
                case 4: fmt = CorePixelFormat.Bgra32; break;
                default: throw new NotSupportedException("不支持的通道数：" + mat.Channels());
            }

            var continuous = mat.IsContinuous() ? mat : mat.Clone();
            try
            {
                int stride = (int)continuous.Step();
                var data = new byte[stride * continuous.Rows];
                Marshal.Copy(continuous.Data, data, 0, data.Length);
                return new ImageFrame(continuous.Cols, continuous.Rows, stride, fmt, data, DateTime.UtcNow);
            }
            finally
            {
                if (!ReferenceEquals(continuous, mat)) continuous.Dispose();
            }
        }
    }
}
