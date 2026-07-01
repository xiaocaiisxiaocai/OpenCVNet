using OpenCvSharp;
using VisionInspection.Core.Models;

namespace VisionInspection.Vision.Alignment
{
    /// <summary>
    /// 配准结果：标定坐标系 → 当前图像 的 2×3 仿射矩阵（CV_64FC1）。
    /// 运行时用它把配方中各工位 ROI 映射到当前实拍图像，消除底板摆放偏差。
    /// </summary>
    public sealed class AlignmentResult
    {
        public bool Success { get; }
        public Mat Affine { get; }
        public string Message { get; }

        public AlignmentResult(bool success, Mat affine, string message = null)
        {
            Success = success;
            Affine = affine;
            Message = message;
        }

        /// <summary>恒等变换（不配准）。</summary>
        public static AlignmentResult Identity() => new AlignmentResult(true, CreateIdentityAffine(), null);

        public static AlignmentResult Fail(string message) => new AlignmentResult(false, null, message);

        /// <summary>构造 2×3 恒等仿射矩阵。</summary>
        public static Mat CreateIdentityAffine()
        {
            var m = new Mat(2, 3, MatType.CV_64FC1);
            m.Set<double>(0, 0, 1.0); m.Set<double>(0, 1, 0.0); m.Set<double>(0, 2, 0.0);
            m.Set<double>(1, 0, 0.0); m.Set<double>(1, 1, 1.0); m.Set<double>(1, 2, 0.0);
            return m;
        }
    }

    /// <summary>
    /// 定位配准抽象：根据基准配置在图像中定位底板并返回仿射变换。
    /// 里程碑 2 提供 <see cref="IdentityAlignment"/>；Mark/角点配准为后续实现预留。
    /// </summary>
    public interface IAlignment
    {
        AlignmentResult Align(Mat image, FiducialConfig fiducial);
    }
}
