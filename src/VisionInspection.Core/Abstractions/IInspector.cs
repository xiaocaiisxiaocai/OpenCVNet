using System.Threading;
using VisionInspection.Core.Imaging;
using VisionInspection.Core.Models;

namespace VisionInspection.Core.Abstractions
{
    /// <summary>
    /// 检测引擎抽象：给定一帧图像与当前配方，产出逐工位有无判定结果。
    /// </summary>
    public interface IInspector
    {
        InspectionResult Inspect(ImageFrame frame, Recipe recipe);

        InspectionResult Inspect(ImageFrame frame, Recipe recipe, CancellationToken cancellationToken);
    }
}
