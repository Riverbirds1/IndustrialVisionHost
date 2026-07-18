using System;

namespace IndustrialVisionHost.Models
{
    public enum AlarmSeverity
    {
        Warning = 1,
        Error = 2,
        Critical = 3
    }

    public sealed class AlarmRecord
    {
        public long Id { get; init; }
        public string AlarmCode { get; init; } = string.Empty;
        public AlarmSeverity Severity { get; init; }
        public string Source { get; init; } = string.Empty;
        public string Message { get; init; } = string.Empty;
        public DateTime RaisedAtUtc { get; init; }
        public DateTime LastOccurredAtUtc { get; init; }
        public int OccurrenceCount { get; init; }
        public DateTime? AcknowledgedAtUtc { get; init; }
        public string AcknowledgedBy { get; init; } = string.Empty;
        public DateTime? ClearedAtUtc { get; init; }
        public string ClearReason { get; init; } = string.Empty;

        public bool IsActive => !ClearedAtUtc.HasValue;

        public string SeverityText => Severity switch
        {
            AlarmSeverity.Warning => "警告",
            AlarmSeverity.Error => "错误",
            AlarmSeverity.Critical => "严重",
            _ => "未知"
        };

        public string StatusText => IsActive
            ? AcknowledgedAtUtc.HasValue ? "活动（已确认）" : "活动（未确认）"
            : "已恢复";

        public string RaisedAtLocalText =>
            RaisedAtUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");

        public string LastOccurredAtLocalText =>
            LastOccurredAtUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");

        public string AcknowledgedAtLocalText => AcknowledgedAtUtc.HasValue
            ? AcknowledgedAtUtc.Value.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss")
            : string.Empty;

        public string ClearedAtLocalText => ClearedAtUtc.HasValue
            ? ClearedAtUtc.Value.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss")
            : string.Empty;
    }
}
