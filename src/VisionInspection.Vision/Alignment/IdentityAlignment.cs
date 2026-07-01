using OpenCvSharp;
using VisionInspection.Core.Models;

namespace VisionInspection.Vision.Alignment
{
    /// <summary>
    /// 恒等配准：不做任何变换，直接按标定坐标使用 ROI。
    /// 适用于底板严格固定、或调试 / 单元测试场景。
    /// </summary>
    public sealed class IdentityAlignment : IAlignment
    {
        public AlignmentResult Align(Mat image, FiducialConfig fiducial) => AlignmentResult.Identity();
    }
}
