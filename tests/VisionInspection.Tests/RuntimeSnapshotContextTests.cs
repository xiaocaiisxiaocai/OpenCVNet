using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading;
using VisionInspection.Core.Abstractions;
using VisionInspection.Core.Imaging;
using VisionInspection.Core.Models;
using VisionInspection.Infrastructure.Archiving;
using VisionInspection.Infrastructure.Storage;
using VisionInspection.Plc.Simulation;
using VisionInspection.Runtime;
using Xunit;

namespace VisionInspection.Tests
{
    public class RuntimeSnapshotContextTests : IDisposable
    {
        private readonly string _dir;

        public RuntimeSnapshotContextTests()
        {
            _dir = Path.Combine(Path.GetTempPath(), "vi_ctx_" + Path.GetRandomFileName());
            Directory.CreateDirectory(_dir);
        }

        public void Dispose()
        {
            if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
        }

        [Fact]
        public void Snapshot_Uses_Inspection_Local_Frame_And_Recipe()
        {
            var r1 = Result("1", 0);
            var r2 = Result("2", 1);
            var cam = new SequencedCamera(Frame(4), Frame(8));
            var inspector = new SequencedInspector(r1, r2);
            var store = new JsonRecipeStore(Path.Combine(_dir, "recipes"));
            var plc = new SimulatedPlcClient();
            plc.Connect();
            var svc = new RuntimeService(cam, inspector, plc, store, new InspectionArchiver(Path.Combine(_dir, "archive")));
            var recipe1 = Recipe("1", 0);
            var recipe2 = Recipe("2", 1);

            InvokePrivate(svc, "InspectAndArchive", recipe1, CancellationToken.None);
            InvokePrivate(svc, "InspectAndArchive", recipe2, CancellationToken.None);

            InspectionSnapshot snapshot = null;
            svc.SnapshotReady += s => snapshot = s;
            InvokePrivate(svc, "OnHandshakeInspected", r1);

            Assert.NotNull(snapshot);
            Assert.Equal("1", snapshot.Recipe.ModelCode);
            Assert.Equal(4, snapshot.Frame.Width);
        }

        private static object InvokePrivate(object target, string name, params object[] args)
        {
            var method = target.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic);
            return method.Invoke(target, args);
        }

        private static InspectionResult Result(string model, int index)
        {
            return new InspectionResult(model, InspectionOutcome.Ok,
                new List<StationResult> { new StationResult(index, PresenceState.Present, 0.9, 0.5) },
                DateTime.UtcNow, 5);
        }

        private static Recipe Recipe(string model, int index)
        {
            var recipe = new Recipe { ModelCode = model };
            recipe.Stations.Add(new Station { Index = index, Roi = new RoiRect(0, 0, 4, 4) });
            return recipe;
        }

        private static ImageFrame Frame(int width)
            => new ImageFrame(width, 4, width * 3, PixelFormat.Bgr24, new byte[width * 12], DateTime.UtcNow);

        private sealed class SequencedCamera : ICamera
        {
            private readonly ImageFrame[] _frames;
            private int _index;

            public SequencedCamera(params ImageFrame[] frames)
            {
                _frames = frames;
            }

            public bool IsConnected { get; private set; } = true;
#pragma warning disable CS0067
            public event EventHandler<CameraFrameEventArgs> FrameReceived;
            public event EventHandler<CameraConnectionEventArgs> ConnectionChanged;
#pragma warning restore CS0067
            public ImageFrame Grab(int timeoutMs = 2000) => _frames[_index++];
            public ImageFrame Grab(int timeoutMs, CancellationToken cancellationToken) => Grab(timeoutMs);
            public void SetTriggerMode(TriggerMode mode) { }
            public void Open() { IsConnected = true; }
            public void Close() { IsConnected = false; }
            public void Dispose() { }
        }

        private sealed class SequencedInspector : IInspector
        {
            private readonly InspectionResult[] _results;
            private int _index;

            public SequencedInspector(params InspectionResult[] results)
            {
                _results = results;
            }

            public InspectionResult Inspect(ImageFrame frame, Recipe recipe) => _results[_index++];
            public InspectionResult Inspect(ImageFrame frame, Recipe recipe, CancellationToken cancellationToken) => Inspect(frame, recipe);
        }
    }
}
