using System;

namespace VisionInspection.Runtime
{
    /// <summary>运行链结构化日志事件：保留异常对象与关键字段，供组合根写入 Serilog。</summary>
    public sealed class RuntimeLogEvent
    {
        public DateTime TimeUtc { get; }
        public string Level { get; }
        public string Source { get; }
        public string EventName { get; }
        public string Message { get; }
        public Exception Exception { get; }
        public string ModelCode { get; }
        public string Outcome { get; }
        public int? MissingCount { get; }

        public RuntimeLogEvent(
            string level,
            string source,
            string eventName,
            string message,
            Exception exception = null,
            string modelCode = null,
            string outcome = null,
            int? missingCount = null)
        {
            TimeUtc = DateTime.UtcNow;
            Level = level;
            Source = source;
            EventName = eventName;
            Message = message;
            Exception = exception;
            ModelCode = modelCode;
            Outcome = outcome;
            MissingCount = missingCount;
        }
    }
}
