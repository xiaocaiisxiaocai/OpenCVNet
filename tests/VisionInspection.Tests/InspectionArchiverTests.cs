using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using VisionInspection.Core.Abstractions;
using VisionInspection.Core.Imaging;
using VisionInspection.Core.Models;
using VisionInspection.Infrastructure.Archiving;
using Xunit;

namespace VisionInspection.Tests
{
    public class InspectionArchiverTests : IDisposable
    {
        private readonly string _dir;

        private sealed class SlowCountingArchiver : IInspectionArchiver
        {
            private readonly int _delayMs;
            private int _count;

            public SlowCountingArchiver(int delayMs)
            {
                _delayMs = delayMs;
            }

            public int Count => _count;

            public void Archive(ImageFrame frame, InspectionResult result)
            {
                Thread.Sleep(_delayMs);
                Interlocked.Increment(ref _count);
            }

            public void Dispose()
            {
            }
        }

        private sealed class BlockingArchiver : IInspectionArchiver
        {
            private readonly ManualResetEventSlim _release;

            public BlockingArchiver(ManualResetEventSlim release)
            {
                _release = release;
            }

            public void Archive(ImageFrame frame, InspectionResult result)
            {
                _release.Wait();
            }

            public void Dispose()
            {
            }
        }

        private sealed class ThrowingArchiver : IInspectionArchiver
        {
            public void Archive(ImageFrame frame, InspectionResult result)
            {
                throw new IOException("disk full");
            }

            public void Dispose()
            {
            }
        }

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
        public void Unknown_Station_Is_Countable_Defect_In_Csv()
        {
            var archiver = new InspectionArchiver(_dir, saveNgImageOnly: true);
            var result = new InspectionResult("M1", InspectionOutcome.Ng,
                new List<StationResult>
                {
                    new StationResult(1, PresenceState.Unknown, 0.0, 0.5),
                    new StationResult(2, PresenceState.Absent, 0.0, 0.5)
                },
                DateTime.UtcNow, 7);

            archiver.Archive(Frame(), result);

            var csv = Directory.GetFiles(_dir, "results.csv", SearchOption.AllDirectories).Single();
            var line = File.ReadAllLines(csv).Last();
            Assert.Contains(",2,1;2,", line);
        }

        [Fact]
        public void Csv_Uses_Quoted_Escaping_Instead_Of_Replacing_Data()
        {
            var archiver = new InspectionArchiver(_dir, saveNgImageOnly: true);
            var ng = new InspectionResult("M,1", InspectionOutcome.Ng,
                new List<StationResult>(), DateTime.UtcNow, 1, "E,1");

            archiver.Archive(Frame(), ng);

            var csv = Directory.GetFiles(_dir, "results.csv", SearchOption.AllDirectories).Single();
            var text = File.ReadAllText(csv);
            Assert.Contains("\"M,1\"", text);
            Assert.Contains("\"E,1\"", text);
            Assert.DoesNotContain("M，1", text);
        }

        [Fact]
        public void Ng_Image_Name_Is_Unique_For_Same_Millisecond()
        {
            var archiver = new InspectionArchiver(_dir, saveNgImageOnly: true);
            var ts = DateTime.UtcNow;
            var ng = new InspectionResult("M1", InspectionOutcome.Ng,
                new List<StationResult>(), ts, 1);

            archiver.Archive(Frame(), ng);
            archiver.Archive(Frame(), ng);

            Assert.Equal(2, Directory.GetFiles(_dir, "*.png", SearchOption.AllDirectories).Length);
        }

        [Fact]
        public void Async_Dispose_Flushes_Queued_Items_Before_Returning()
        {
            var inner = new SlowCountingArchiver(1100);
            var async = new AsyncInspectionArchiver(inner, capacity: 4);
            var result = new InspectionResult("M1", InspectionOutcome.Ok,
                new List<StationResult>(), DateTime.UtcNow, 1);

            async.Archive(Frame(), result);
            async.Archive(Frame(), result);
            async.Dispose();

            Assert.Equal(2, inner.Count);
        }

        [Fact]
        public void Async_Dispose_Does_Not_Wait_Forever_When_Inner_Hangs()
        {
            var release = new ManualResetEventSlim(false);
            var inner = new BlockingArchiver(release);
            var async = new AsyncInspectionArchiver(inner, capacity: 4, disposeTimeoutMs: 500);
            var result = new InspectionResult("M1", InspectionOutcome.Ok,
                new List<StationResult>(), DateTime.UtcNow, 1);

            async.Archive(Frame(), result);
            Thread.Sleep(100);

            var disposed = new ManualResetEventSlim(false);
            var thread = new Thread(() =>
            {
                async.Dispose();
                disposed.Set();
            });
            thread.Start();

            Assert.True(disposed.Wait(1000));
            Assert.True(async.DisposeTimedOut);
            release.Set();
        }

        [Fact]
        public void Async_Archive_After_Dispose_Is_Ignored()
        {
            var inner = new SlowCountingArchiver(0);
            var async = new AsyncInspectionArchiver(inner, capacity: 4);
            var result = new InspectionResult("M1", InspectionOutcome.Ok,
                new List<StationResult>(), DateTime.UtcNow, 1);

            async.Dispose();
            async.Archive(Frame(), result);

            Assert.Equal(0, inner.Count);
        }

        [Fact]
        public void Async_Archiver_Emits_Event_When_Inner_Fails()
        {
            var inner = new ThrowingArchiver();
            var async = new AsyncInspectionArchiver(inner, capacity: 4);
            var events = new List<string>();
            async.Event += e => events.Add(e);
            var result = new InspectionResult("M1", InspectionOutcome.Ok,
                new List<StationResult>(), DateTime.UtcNow, 1);

            async.Archive(Frame(), result);
            async.Dispose();

            Assert.Contains(events, e => e.Contains("ArchiveFailed"));
        }

        [Fact]
        public void Spooling_Archiver_Replays_Leftover_Items_On_Restart()
        {
            var spool = Path.Combine(_dir, "spool");
            var inner = new SlowCountingArchiver(0);
            var result = new InspectionResult("M1", InspectionOutcome.Ok,
                new List<StationResult>(), DateTime.UtcNow, 1);

            using (var first = new SpoolingInspectionArchiver(inner, spool, autoStart: false))
                first.Archive(Frame(), result);

            Assert.Equal(0, inner.Count);

            using (var second = new SpoolingInspectionArchiver(inner, spool))
            {
                Assert.True(second.Flush(TimeSpan.FromSeconds(2)));
            }

            Assert.Equal(1, inner.Count);
            Assert.Empty(Directory.GetFiles(spool, "*.json"));
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
