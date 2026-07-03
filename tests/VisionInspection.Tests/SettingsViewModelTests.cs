using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using VisionInspection.App.Hosting;
using VisionInspection.App.ViewModels;
using VisionInspection.Core.Imaging;
using Xunit;

namespace VisionInspection.Tests
{
    public class SettingsViewModelTests : IDisposable
    {
        private readonly string _baseDir;

        public SettingsViewModelTests()
        {
            _baseDir = Path.Combine(Path.GetTempPath(), "vi_settings_vm_" + Path.GetRandomFileName());
            Directory.CreateDirectory(_baseDir);
        }

        public void Dispose()
        {
            if (Directory.Exists(_baseDir)) Directory.Delete(_baseDir, true);
        }

        [Fact]
        public void CameraModes_Hides_Hikvision_When_Not_Available()
        {
            using (var host = new ApplicationHost(_baseDir, Frame))
            {
                var vm = new SettingsViewModel(host);

                Assert.DoesNotContain("Hikvision", vm.CameraModes);
            }
        }

        [Fact]
        public void CanSave_Is_False_While_Runtime_Is_Running()
        {
            using (var host = new ApplicationHost(_baseDir, Frame))
            {
                var vm = new SettingsViewModel(host);
                host.Start();

                vm.RefreshCanSave();

                Assert.False(vm.CanSave);
                host.Stop();
            }
        }

        [Fact]
        public void Invalid_Port_Adds_Field_Error_And_Disables_Save()
        {
            using (var host = new ApplicationHost(_baseDir, Frame))
            {
                var vm = new SettingsViewModel(host);

                vm.PlcPort = 0;

                Assert.True(vm.HasErrors);
                Assert.Contains("端口", string.Join(";", vm.GetErrors(nameof(vm.PlcPort)).Cast<string>()));
                Assert.False(vm.CanSave);
            }
        }

        private static ImageFrame Frame()
            => new ImageFrame(4, 4, 12, PixelFormat.Bgr24, new byte[48], DateTime.UtcNow);
    }
}
