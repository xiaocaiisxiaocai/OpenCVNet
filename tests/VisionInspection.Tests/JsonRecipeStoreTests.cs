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
                    ModelCode = "1",
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

                Assert.True(store.Exists("1"));
                var loaded = store.Load("1");
                Assert.Equal("产品A", loaded.Name);
                Assert.Equal(2, loaded.StationCount);
                Assert.Equal(new RoiRect(10, 20, 30, 40), loaded.Stations[0].Roi);
                Assert.Equal(DetectionMethod.BaselineDiff, loaded.Stations[1].Method);
                Assert.Contains("1", store.ListModelCodes());
            }
            finally
            {
                Directory.Delete(dir, true);
            }
        }

        [Fact]
        public void RoiRect_Deserializes_From_Json_Constructor()
        {
            var json = "{\"X\":10,\"Y\":20,\"Width\":30,\"Height\":40}";

            var roi = Newtonsoft.Json.JsonConvert.DeserializeObject<RoiRect>(json);

            Assert.Equal(new RoiRect(10, 20, 30, 40), roi);
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
        public void TryLoad_Returns_False_When_Json_Is_Damaged()
        {
            var dir = NewTempDir();
            try
            {
                File.WriteAllText(Path.Combine(dir, "1.json"), "{ bad json");
                var store = new JsonRecipeStore(dir);

                Assert.False(store.TryLoad("1", out var r));
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
                store.Save(new Recipe
                {
                    ModelCode = "2",
                    Stations = { new Station { Index = 0, Roi = new RoiRect(0, 0, 10, 10), Threshold = 0.5 } }
                });
                Assert.True(store.Exists("2"));
                store.Delete("2");
                Assert.False(store.Exists("2"));
            }
            finally
            {
                Directory.Delete(dir, true);
            }
        }

        [Fact]
        public void Save_Existing_Recipe_Keeps_Backup()
        {
            var dir = NewTempDir();
            try
            {
                var store = new JsonRecipeStore(dir);
                store.Save(new Recipe
                {
                    ModelCode = "3",
                    Name = "old",
                    Stations = { new Station { Index = 0, Roi = new RoiRect(0, 0, 10, 10), Threshold = 0.5 } }
                });
                store.Save(new Recipe
                {
                    ModelCode = "3",
                    Name = "new",
                    Stations = { new Station { Index = 0, Roi = new RoiRect(0, 0, 10, 10), Threshold = 0.5 } }
                });

                Assert.True(File.Exists(Path.Combine(dir, "3.json.bak")));
                Assert.Contains("old", File.ReadAllText(Path.Combine(dir, "3.json.bak")));
            }
            finally
            {
                Directory.Delete(dir, true);
            }
        }
    }
}
