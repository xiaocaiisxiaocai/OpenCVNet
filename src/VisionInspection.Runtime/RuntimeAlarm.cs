using System;

namespace VisionInspection.Runtime
{
    /// <summary>运行报警。</summary>
    public sealed class RuntimeAlarm
    {
        public DateTime TimeUtc { get; }
        public string Level { get; }     // NG / ERROR / FAULT
        public string Message { get; }

        public RuntimeAlarm(string level, string message)
        {
            TimeUtc = DateTime.UtcNow;
            Level = level;
            Message = message;
        }
    }
}
