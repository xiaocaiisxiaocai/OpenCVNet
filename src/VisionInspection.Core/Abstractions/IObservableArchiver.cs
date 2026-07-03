using System;

namespace VisionInspection.Core.Abstractions
{
    public interface IObservableArchiver
    {
        event Action<string> Event;
    }
}
