using System;
using System.IO;
using VisionInspection.App.ViewModels;
using VisionInspection.Core.Models;
using VisionInspection.Infrastructure.Storage;
using Xunit;

namespace VisionInspection.Tests
{
    public class RecipeManagementViewModelTests : IDisposable
    {
        private readonly string _dir;

        public RecipeManagementViewModelTests()
        {
            _dir = Path.Combine(Path.GetTempPath(), "vi_recipe_vm_" + Path.GetRandomFileName());
            Directory.CreateDirectory(_dir);
        }

        public void Dispose()
        {
            if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
        }

        [Fact]
        public void Export_Existing_File_Keeps_Backup()
        {
            var store = new JsonRecipeStore(Path.Combine(_dir, "recipes"));
            var vm = new RecipeManagementViewModel(store);
            var path = Path.Combine(_dir, "export.json");
            File.WriteAllText(path, "old-content");

            vm.ModelCode = "1";
            vm.Stations.Add(new StationRowViewModel(new Station
            {
                Index = 0,
                Roi = new RoiRect(0, 0, 10, 10),
                Threshold = 0.5,
                Enabled = true
            }));
            vm.ExportTo(path);

            Assert.True(File.Exists(path + ".bak"));
            Assert.Equal("old-content", File.ReadAllText(path + ".bak"));
        }

        [Fact]
        public void Save_Persists_Fiducial_Quality_Gates()
        {
            var store = new JsonRecipeStore(Path.Combine(_dir, "recipes"));
            var vm = new RecipeManagementViewModel(store);

            vm.ModelCode = "2";
            vm.FiducialType = FiducialType.MarkPoints;
            vm.MinDetectedMarks = 2;
            vm.MaxResidualPixels = 4.5;
            vm.MaxRmsResidualPixels = 3.2;
            vm.MinScale = 0.95;
            vm.MaxScale = 1.05;
            vm.MaxRotationDegrees = 8.0;
            vm.Stations.Add(new StationRowViewModel(new Station
            {
                Index = 0,
                Roi = new RoiRect(0, 0, 10, 10),
                Threshold = 0.5,
                Enabled = true
            }));

            vm.SaveCommand.Execute(null);

            Assert.True(store.TryLoad("2", out var saved));
            Assert.Equal(FiducialType.MarkPoints, saved.Fiducial.Type);
            Assert.Equal(2, saved.Fiducial.MinDetectedMarks);
            Assert.Equal(4.5, saved.Fiducial.MaxResidualPixels, 3);
            Assert.Equal(3.2, saved.Fiducial.MaxRmsResidualPixels, 3);
            Assert.Equal(0.95, saved.Fiducial.MinScale, 3);
            Assert.Equal(1.05, saved.Fiducial.MaxScale, 3);
            Assert.Equal(8.0, saved.Fiducial.MaxRotationDegrees, 3);
        }
    }
}
