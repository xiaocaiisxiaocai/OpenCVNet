using System.IO;
using Xunit;

namespace VisionInspection.Tests
{
    public class CiWorkflowTests
    {
        [Fact]
        public void Ci_Workflow_Publishes_Test_Results_And_Release_Artifacts()
        {
            var yaml = File.ReadAllText(Path.Combine(FindRepoRoot(), ".github", "workflows", "ci.yml"));

            Assert.Contains("--logger \"trx;LogFileName=test-results.trx\"", yaml);
            Assert.Contains("actions/upload-artifact@v4", yaml);
            Assert.Contains("dotnet publish src/VisionInspection.App/VisionInspection.App.csproj", yaml);
            Assert.Contains("dotnet publish src/VisionInspection.Watchdog/VisionInspection.Watchdog.csproj", yaml);
            Assert.Contains("dotnet publish src/VisionInspection.PlcProbe/VisionInspection.PlcProbe.csproj", yaml);
            Assert.Contains("artifacts/build-info.txt", yaml);
            Assert.Contains("Smoke published artifacts", yaml);
            Assert.Contains("OpenCvSharpExtern.dll", yaml);
            Assert.Contains("FileVersionInfo", yaml);
            Assert.Contains("Length -le 0", yaml);
            Assert.Contains("/p:InformationalVersion=", yaml);
            Assert.Contains("Version=${{ env.VERSION_PREFIX }}", yaml);
        }

        [Fact]
        public void Gitignore_Excludes_Local_Publish_Artifacts()
        {
            var ignore = File.ReadAllText(Path.Combine(FindRepoRoot(), ".gitignore"));

            Assert.Contains("/artifacts/", ignore);
        }

        [Fact]
        public void Repository_Provides_Local_Ci_And_Release_Packaging_Scripts()
        {
            var root = FindRepoRoot();
            var ci = File.ReadAllText(Path.Combine(root, "scripts", "ci-local.ps1"));
            var pack = File.ReadAllText(Path.Combine(root, "scripts", "package-release.ps1"));

            Assert.Contains("dotnet test", ci);
            Assert.DoesNotContain("dotnet test tests\\VisionInspection.Tests\\VisionInspection.Tests.csproj -c $Configuration --no-build", ci);
            Assert.Contains("dotnet publish", ci);
            Assert.Contains("OpenCvSharpExtern.dll", ci);
            Assert.Contains("Get-FileHash", pack);
            Assert.Contains("Compress-Archive", pack);
            Assert.Contains("release-manifest.txt", pack);
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
