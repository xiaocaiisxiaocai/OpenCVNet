using System.IO;
using System.Linq;
using VisionInspection.Vision.Alignment;
using Xunit;

namespace VisionInspection.Tests
{
    public class ArchitectureTests
    {
        [Fact]
        public void Runtime_Project_Does_Not_Reference_Infrastructure()
        {
            var root = FindRepoRoot();
            var csproj = File.ReadAllText(Path.Combine(root, "src", "VisionInspection.Runtime", "VisionInspection.Runtime.csproj"));

            Assert.DoesNotContain("VisionInspection.Infrastructure", csproj);
        }

        [Fact]
        public void Solution_Only_Exposes_X64_Platform()
        {
            var root = FindRepoRoot();
            var sln = File.ReadAllText(Path.Combine(root, "VisionInspection.sln"));

            Assert.DoesNotContain("|x86", sln);
            Assert.DoesNotContain("|Any CPU", sln);
        }

        [Fact]
        public void Alignment_Result_Does_Not_Expose_OpenCv_Mat_Lifetime()
        {
            var props = typeof(AlignmentResult).GetProperties();

            Assert.DoesNotContain(props, p => p.PropertyType.FullName == "OpenCvSharp.Mat");
        }

        [Fact]
        public void RoiMapper_Does_Not_Expose_Axis_Aligned_Affine_Map()
        {
            var methods = typeof(RoiMapper).GetMethods().Where(m => m.Name == "Map");

            Assert.Empty(methods);
        }

        private static string FindRepoRoot()
        {
            var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
            while (dir != null)
            {
                if (File.Exists(Path.Combine(dir.FullName, "VisionInspection.sln")))
                    return dir.FullName;
                dir = dir.Parent;
            }
            throw new DirectoryNotFoundException("未找到仓库根目录。");
        }
    }
}
