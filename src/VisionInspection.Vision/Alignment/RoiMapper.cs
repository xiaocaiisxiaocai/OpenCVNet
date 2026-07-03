using System;
using VisionInspection.Core.Models;

namespace VisionInspection.Vision.Alignment
{
    /// <summary>
    /// ROI 映射工具：把标定坐标系下的工位 ROI 经仿射矩阵变换到当前图像坐标，并裁剪到图像范围内。
    /// 供检测引擎与示教共用。
    /// </summary>
    public static class RoiMapper
    {
        /// <summary>裁剪 ROI 到 [0,w)×[0,h) 图像范围；越界部分被截断，可能返回空矩形。</summary>
        public static RoiRect Clamp(RoiRect r, int imageWidth, int imageHeight)
        {
            int x = Math.Max(0, r.X);
            int y = Math.Max(0, r.Y);
            int right = Math.Min(imageWidth, r.Right);
            int bottom = Math.Min(imageHeight, r.Bottom);
            int w = Math.Max(0, right - x);
            int h = Math.Max(0, bottom - y);
            return new RoiRect(x, y, w, h);
        }
    }
}
