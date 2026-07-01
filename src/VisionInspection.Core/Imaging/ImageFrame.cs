using System;

namespace VisionInspection.Core.Imaging
{
    /// <summary>
    /// 与具体图像库解耦的中立图像帧。采集层（相机 / 离线回放）产出 <see cref="ImageFrame"/>，
    /// 视觉层再按需转换为 OpenCvSharp 的 Mat 处理，从而让 Core 保持零第三方依赖、便于单元测试。
    /// </summary>
    public sealed class ImageFrame
    {
        public int Width { get; }
        public int Height { get; }

        /// <summary>每行字节数（可能因内存对齐大于 Width * BytesPerPixel）。</summary>
        public int Stride { get; }

        public PixelFormat PixelFormat { get; }

        /// <summary>像素原始字节，长度应 ≥ Stride * Height。</summary>
        public byte[] Data { get; }

        public DateTime CapturedAtUtc { get; }

        public ImageFrame(int width, int height, int stride, PixelFormat pixelFormat, byte[] data, DateTime capturedAtUtc)
        {
            if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width));
            if (height <= 0) throw new ArgumentOutOfRangeException(nameof(height));
            if (stride <= 0) throw new ArgumentOutOfRangeException(nameof(stride));
            if (data == null) throw new ArgumentNullException(nameof(data));
            if (data.Length < (long)stride * height)
                throw new ArgumentException("像素数据长度不足以容纳 Stride * Height。", nameof(data));

            Width = width;
            Height = height;
            Stride = stride;
            PixelFormat = pixelFormat;
            Data = data;
            CapturedAtUtc = capturedAtUtc;
        }

        /// <summary>每像素字节数。</summary>
        public int BytesPerPixel
        {
            get
            {
                switch (PixelFormat)
                {
                    case PixelFormat.Gray8: return 1;
                    case PixelFormat.Bgr24: return 3;
                    case PixelFormat.Bgra32: return 4;
                    default: throw new NotSupportedException("未知像素格式：" + PixelFormat);
                }
            }
        }
    }
}
