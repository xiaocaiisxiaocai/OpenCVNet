using System;
using System.Collections.Concurrent;
using System.Threading;
using VisionInspection.Core.Abstractions;
using VisionInspection.Core.Imaging;
using VisionInspection.Core.Models;

namespace VisionInspection.Infrastructure.Archiving
{
    /// <summary>有界后台归档队列；队列满时丢弃新归档，检测判定不受 IO 拖慢。</summary>
    public sealed class AsyncInspectionArchiver : IInspectionArchiver, IObservableArchiver
    {
        private sealed class Item
        {
            public ImageFrame Frame;
            public InspectionResult Result;
        }

        private readonly IInspectionArchiver _inner;
        private readonly BlockingCollection<Item> _queue;
        private readonly Thread _worker;
        private readonly int _disposeTimeoutMs;
        private volatile bool _disposed;
        private int _droppedCount;
        private int _failedCount;
        private int _disposeTimedOut;

        public int DroppedCount => _droppedCount;
        public int FailedCount => _failedCount;
        public bool DisposeTimedOut => _disposeTimedOut != 0;
        public event Action<string> Event;

        public AsyncInspectionArchiver(IInspectionArchiver inner, int capacity = 256, int disposeTimeoutMs = 5000)
        {
            if (inner == null) throw new ArgumentNullException(nameof(inner));
            if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity));
            if (disposeTimeoutMs <= 0) throw new ArgumentOutOfRangeException(nameof(disposeTimeoutMs));
            _inner = inner;
            _disposeTimeoutMs = disposeTimeoutMs;
            _queue = new BlockingCollection<Item>(capacity);
            _worker = new Thread(Consume) { IsBackground = true, Name = "ArchiveWriter" };
            _worker.Start();
        }

        public void Archive(ImageFrame frame, InspectionResult result)
        {
            if (_disposed || result == null) return;
            if (!_queue.TryAdd(new Item { Frame = frame, Result = result }))
            {
                Interlocked.Increment(ref _droppedCount);
                Event?.Invoke("ArchiveDropped: queue full");
            }
        }

        private void Consume()
        {
            foreach (var item in _queue.GetConsumingEnumerable())
            {
                try { _inner.Archive(item.Frame, item.Result); }
                catch (Exception ex)
                {
                    Interlocked.Increment(ref _failedCount);
                    Event?.Invoke("ArchiveFailed: " + ex.Message);
                }
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _queue.CompleteAdding();
            if (!_worker.Join(_disposeTimeoutMs))
            {
                Interlocked.Exchange(ref _disposeTimedOut, 1);
                Event?.Invoke("ArchiveDisposeTimedOut");
                return;
            }
            _queue.Dispose();
            _inner.Dispose();
        }
    }
}
