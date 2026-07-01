using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using VisionInspection.Core.Imaging;
using VisionInspection.Core.Models;
using VisionInspection.Infrastructure.Archiving;
using Xunit;

namespace VisionInspection.Tests
{
    public class InspectionArchiverTests : IDisposable
    {
        private readonly string _dir;

        public InspectionArchiverTests()
        {
            _dir = Path.Combine(Path.GetTempPath(), "vi_arch_" + Path.GetRandomFileName());
        }

        public void Dispose()
        {
            if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
        }

        private static ImageFrame Frame() =>
            new ImageFrame(4, 4, 12, PixelFormat.Bgr24, new byte[12 * 4], DateTime.UtcNow);

        [Fact]
        public void Ng_Archives_Csv_And_Image()
        {
            var archiver = new InspectionArchiver(_dir, saveNgImageOnly: true);
            var ng = new InspectionResult("M1", InspectionOutcome.Ng,
                new List<StationResult> { new StationResult(1, PresenceState.Absent, 0.0, 0.5) },
                DateTime.UtcNow, 12);

            archiver.Archive(Frame(), ng);

            var csv = Directory.GetFiles(_dir, "results.csv", SearchOption.AllDirectories);
            var png = Directory.GetFiles(_dir, "*.png", SearchOption.AllDirectories);
            Assert.Single(csv);
            Assert.Single(png);
            var text = File.ReadAllText(csv[0]);
            Assert.Contains("M1", text);
            Assert.Contains("Ng", text);
        }

        [Fact]
        public void Ok_Archives_Csv_But_No_Image_When_NgOnly()
        {
            var archiver = new InspectionArchiver(_dir, saveNgImageOnly: true);
            var ok = new InspectionResult("M1", InspectionOutcome.Ok,
                new List<StationResult> { new StationResult(0, PresenceState.Present, 0.9, 0.5) },
                DateTime.UtcNow, 8);

            archiver.Archive(Frame(), ok);

            Assert.Single(Directory.GetFiles(_dir, "results.csv", SearchOption.AllDirectories));
            Assert.Empty(Directory.GetFiles(_dir, "*.png", SearchOption.AllDirectories));
        }

        [Fact]
        public void Multiple_Results_Append_To_Same_Csv()
        {
            var archiver = new InspectionArchiver(_dir, saveNgImageOnly: true);
            for (int i = 0; i < 3; i++)
                archiver.Archive(Frame(), new InspectionResult("M1", InspectionOutcome.Ok,
                    new List<StationResult>(), DateTime.UtcNow, 5));

            var csv = Directory.GetFiles(_dir, "results.csv", SearchOption.AllDirectories).Single();
            // 表头 1 行 + 3 条记录
            Assert.Equal(4, File.ReadAllLines(csv).Length);
        }

        [Fact]
        public void Retention_Deletes_Expired_But_Keeps_Recent()
        {
            Directory.CreateDirectory(_dir);
            var old = Path.Combine(_dir, "20200101");                        // 远超保留期
            var recent = Path.Combine(_dir, DateTime.Now.ToString("yyyyMMdd")); // 今日
            var notADate = Path.Combine(_dir, "misc");                        // 非日期目录不应被删
            Directory.CreateDirectory(old);
            Directory.CreateDirectory(recent);
            Directory.CreateDirectory(notADate);

            // 构造时按保留 30 天清理
            var _ = new InspectionArchiver(_dir, saveNgImageOnly: true, retentionDays: 30);

            Assert.False(Directory.Exists(old));
            Assert.True(Directory.Exists(recent));
            Assert.True(Directory.Exists(notADate));
        }

        [Fact]
        public void Retention_Zero_Keeps_All()
        {
            Directory.CreateDirectory(_dir);
            var old = Path.Combine(_dir, "20200101");
            Directory.CreateDirectory(old);

            var _ = new InspectionArchiver(_dir, saveNgImageOnly: true, retentionDays: 0);

            Assert.True(Directory.Exists(old)); // 0 = 永久保留
        }
    }
}
