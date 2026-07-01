using System;
using System.Collections.Generic;

namespace VisionInspection.Core.Models
{
    /// <summary>一次检测的完整结果。</summary>
    public sealed class InspectionResult
    {
        public string ModelCode { get; }
        public InspectionOutcome Outcome { get; }
        public IReadOnlyList<StationResult> Stations { get; }
        public DateTime TimestampUtc { get; }
        public int ElapsedMs { get; }

        /// <summary>Outcome = Error 时的错误码（回写 PLC，如 "NO_RECIPE"）。</summary>
        public string ErrorCode { get; }
        public string Message { get; }

        public InspectionResult(
            string modelCode,
            InspectionOutcome outcome,
            IReadOnlyList<StationResult> stations,
            DateTime timestampUtc,
            int elapsedMs,
            string errorCode = null,
            string message = null)
        {
            ModelCode = modelCode;
            Outcome = outcome;
            Stations = stations ?? new List<StationResult>();
            TimestampUtc = timestampUtc;
            ElapsedMs = elapsedMs;
            ErrorCode = errorCode;
            Message = message;
        }

        /// <summary>缺件工位数（仅统计 Absent）。</summary>
        public int MissingCount
        {
            get
            {
                int n = 0;
                foreach (var s in Stations)
                    if (s.State == PresenceState.Absent) n++;
                return n;
            }
        }

        /// <summary>构造一个错误结果（如无匹配配方 / 配准失败）。</summary>
        public static InspectionResult CreateError(string modelCode, string errorCode, string message, DateTime timestampUtc)
            => new InspectionResult(modelCode, InspectionOutcome.Error, new List<StationResult>(), timestampUtc, 0, errorCode, message);
    }
}
