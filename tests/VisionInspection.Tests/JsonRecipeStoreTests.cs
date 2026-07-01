using System.IO;
using VisionInspection.Core.Models;
using VisionInspection.Infrastructure.Storage;
using Xunit;

namespace VisionInspection.Tests
{
    public class JsonRecipeStoreTests
    {
        private static string NewTempDir()
        {
            var dir = Path.Combine(Path.GetTempPath(), "vi_recipes_" + Path.GetRandomFileName());
            Directory.CreateDirectory(dir);
            return dir;
        }

        [Fact]
        public void Save_Then_Load_Roundtrips()
        {
            var dir = NewTempDir();
            try
            {
                var store = new JsonRecipeStore(dir);
                var recipe = new Recipe
                {
                    ModelCode = "A1",
                    Name = "产品A",
                    Rows = 1,
                    Columns = 2,
                    Stations =
                    {
                        new Station { Index = 0, Row = 0, Column = 0, Roi = new RoiRect(10, 20, 30, 40), Threshold = 0.6 },
                        new Station { Index = 1, Row = 0, Column = 1, Roi = new RoiRect(50, 20, 25, 25), Method = DetectionMethod.BaselineDiff }
                    }
                };
                store.Save(recipe);

                Assert.True(store.Exists("A1"));
                var loaded = store.Load("A1");
                Assert.Equal("产品A", loaded.Name);
                Assert.Equal(2, loaded.StationCount);
                Assert.Equal(new RoiRect(10, 20, 30, 40), loaded.Stations[0].Roi);
                Assert.Equal(DetectionMethod.BaselineDiff, loaded.Stations[1].Method);
                Assert.Contains("A1", store.ListModelCodes());
            }
            finally
            {
                Directory.Delete(dir, true);
            }
        }

        [Fact]
        public void TryLoad_Returns_False_When_Missing()
        {
            var dir = NewTempDir();
            try
            {
                var store = new JsonRecipeStore(dir);
                Assert.False(store.TryLoad("NOPE", out var r));
                Assert.Null(r);
            }
            finally
            {
                Directory.Delete(dir, true);
            }
        }

        [Fact]
        public void Delete_Removes_Recipe()
        {
            var dir = NewTempDir();
            try
            {
                var store = new JsonRecipeStore(dir);
                store.Save(new Recipe { ModelCode = "X" });
                Assert.True(store.Exists("X"));
                store.Delete("X");
                Assert.False(store.Exists("X"));
            }
            finally
            {
                Directory.Delete(dir, true);
            }
        }
    }
}
