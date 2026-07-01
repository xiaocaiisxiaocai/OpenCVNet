using OpenCvSharp;
using VisionInspection.Core.Models;

namespace VisionInspection.Vision.Detection
{
    /// <summary>
    /// 单工位有无判定器。对已从当前图像裁出的工位子图打分并给出状态。
    /// 不同 <see cref="DetectionMethod"/> 对应不同实现。
    /// </summary>
    public interface IPresenceDetector
    {
        DetectionMethod Method { get; }

        /// <summary>
        /// 对工位 ROI 子图判有无。
        /// </summary>
        /// <param name="roiImage">已裁剪的工位子图（可能为彩色或灰度）。</param>
        /// <param name="station">工位配置（含阈值等）。</param>
        /// <returns>判定状态与得分（得分含义随方法，通常 0~1）。</returns>
        DetectionOutput Detect(Mat roiImage, Station station);
    }

    /// <summary>判定器输出。</summary>
    public struct DetectionOutput
    {
        public PresenceState State { get; }
        public double Score { get; }

        public DetectionOutput(PresenceState state, double score)
        {
            State = state;
            Score = score;
        }
    }
}
