namespace VisionInspection.Core.Imaging
{
    /// <summary>
    /// 中立像素格式定义，避免 Core 依赖具体图像库（OpenCvSharp / System.Drawing）。
    /// </summary>
    public enum PixelFormat
    {
        /// <summary>8 位单通道灰度。</summary>
        Gray8 = 0,

        /// <summary>24 位 BGR（每通道 8 位，OpenCV 默认通道序）。</summary>
        Bgr24 = 1,

        /// <summary>32 位 BGRA。</summary>
        Bgra32 = 2
    }
}
