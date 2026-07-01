using System;
using VisionInspection.Camera.Simulation;
using VisionInspection.Core.Abstractions;
using VisionInspection.Core.Imaging;
using Xunit;

namespace VisionInspection.Tests
{
    public class SimulatedCameraTests
    {
        private static ImageFrame Frame() =>
            new ImageFrame(2, 2, 6, PixelFormat.Bgr24, new byte[6 * 2], DateTime.UtcNow);

        [Fact]
        public void Open_Raises_ConnectionChanged_And_Connects()
        {
            var cam = new SimulatedIndustrialCamera(Frame);
            bool? state = null;
            cam.ConnectionChanged += (s, e) => state = e.IsConnected;

            cam.Open();

            Assert.True(cam.IsConnected);
            Assert.True(state);
        }

        [Fact]
        public void Grab_Returns_Frame_And_Raises_FrameReceived()
        {
            var cam = new SimulatedIndustrialCamera(Frame);
            cam.Open();
            ImageFrame received = null;
            cam.FrameReceived += (s, e) => received = e.Frame;

            var frame = cam.Grab();

            Assert.NotNull(frame);
            Assert.Same(frame, received);
        }

        [Fact]
        public void HardwareTrigger_Works_Only_In_Hardware_Mode()
        {
            var cam = new SimulatedIndustrialCamera(Frame);
            cam.Open();

            // 默认软触发模式下硬触发应抛异常
            Assert.Throws<InvalidOperationException>(() => cam.FireHardwareTrigger());

            cam.SetTriggerMode(TriggerMode.Hardware);
            ImageFrame received = null;
            cam.FrameReceived += (s, e) => received = e.Frame;
            var frame = cam.FireHardwareTrigger();

            Assert.NotNull(frame);
            Assert.Same(frame, received);
        }

        [Fact]
        public void SimulateDisconnect_Sets_Disconnected()
        {
            var cam = new SimulatedIndustrialCamera(Frame);
            cam.Open();
            bool? state = null;
            cam.ConnectionChanged += (s, e) => state = e.IsConnected;

            cam.SimulateDisconnect();

            Assert.False(cam.IsConnected);
            Assert.False(state);
        }

        [Fact]
        public void Grab_Before_Open_Throws()
        {
            var cam = new SimulatedIndustrialCamera(Frame);
            Assert.Throws<InvalidOperationException>(() => cam.Grab());
        }
    }
}
