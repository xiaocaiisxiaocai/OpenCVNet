using System;
using OpenCvSharp;
using VisionInspection.Core.Models;

namespace VisionInspection.Vision.Alignment
{
    /// <summary>
    /// ROI 映射工具：把标定坐标系下的工位 ROI 经仿射矩阵变换到当前图像坐标，并裁剪到图像范围内。
    /// 供检测引擎与示教共用。
    /// </summary>
    public static class RoiMapper
    {
        /// <summary>用 2×3 仿射矩阵变换 ROI 四角，返回其轴对齐包围盒。</summary>
        public static RoiRect Map(RoiRect roi, Mat affine)
        {
            if (affine == null) throw new ArgumentNullException(nameof(affine));

            double a00 = affine.At<double>(0, 0), a01 = affine.At<double>(0, 1), a02 = affine.At<double>(0, 2);
            double a10 = affine.At<double>(1, 0), a11 = affine.At<double>(1, 1), a12 = affine.At<double>(1, 2);

            var xs = new double[4];
            var ys = new double[4];
            int[,] corners = { { roi.X, roi.Y }, { roi.Right, roi.Y }, { roi.Right, roi.Bottom }, { roi.X, roi.Bottom } };
            for (int i = 0; i < 4; i++)
            {
                int x = corners[i, 0], y = corners[i, 1];
                xs[i] = a00 * x + a01 * y + a02;
                ys[i] = a10 * x + a11 * y + a12;
            }

            double minX = Min(xs), minY = Min(ys), maxX = Max(xs), maxY = Max(ys);
            int ix = (int)Math.Floor(minX);
            int iy = (int)Math.Floor(minY);
            int iw = (int)Math.Ceiling(maxX) - ix;
            int ih = (int)Math.Ceiling(maxY) - iy;
            return new RoiRect(ix, iy, iw, ih);
        }

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

        private static double Min(double[] v)
        {
            double m = v[0];
            for (int i = 1; i < v.Length; i++) if (v[i] < m) m = v[i];
            return m;
        }

        private static double Max(double[] v)
        {
            double m = v[0];
            for (int i = 1; i < v.Length; i++) if (v[i] > m) m = v[i];
            return m;
        }
    }
}
