using System;
using VisionInspection.Camera;
using VisionInspection.Camera.Simulation;
using VisionInspection.Core.Imaging;
using Xunit;

namespace VisionInspection.Tests
{
    public class CameraFactoryTests
    {
        [Fact]
        public void Create_Simulated_Uses_Injected_Frame_Factory()
        {
            using (var camera = CameraFactory.Create(
                new CameraOptions { Kind = CameraKind.Simulated },
                Frame))
            {
                var sim = Assert.IsType<SimulatedIndustrialCamera>(camera);
                sim.Open();

                var frame = sim.Grab();

                Assert.Equal(4, frame.Width);
                Assert.Equal(4, frame.Height);
            }
        }

        private static ImageFrame Frame()
            => new ImageFrame(4, 4, 12, PixelFormat.Bgr24, new byte[12 * 4], DateTime.UtcNow);
    }
}
