using System;
using VisionInspection.Core.Imaging;
using VisionInspection.Core.Models;

namespace VisionInspection.Core.Abstractions
{
    public interface IInspectionArchiver : IDisposable
    {
        void Archive(ImageFrame frame, InspectionResult result);
    }
}
