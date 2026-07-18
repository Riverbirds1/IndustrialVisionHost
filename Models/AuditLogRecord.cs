using System;

namespace IndustrialVisionHost.Models
{
    public enum AuditOutcome
    {
        Success,
        Denied,
        Failure
    }

    public sealed class AuditLogRecord
    {
        public long Id { get; init; }

        public DateTime OccurredAtUtc { get; init; }

        public long? UserId { get; init; }

        public string Username { get; init; } = string.Empty;

        public string DisplayName { get; init; } = string.Empty;

        public string Role { get; init; } = string.Empty;

        public string ActionType { get; init; } = string.Empty;

        public string TargetType { get; init; } = string.Empty;

        public string TargetIdentifier { get; init; } = string.Empty;

        public AuditOutcome Outcome { get; init; }

        public string Details { get; init; } = string.Empty;

        public string Workstation { get; init; } = string.Empty;

        public string OccurredAtLocalText =>
            OccurredAtUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss.fff");

        public string UserText => string.IsNullOrWhiteSpace(DisplayName)
            ? Username
            : $"{DisplayName}（{Username}）";

        public string OutcomeText => Outcome switch
        {
            AuditOutcome.Success => "成功",
            AuditOutcome.Denied => "拒绝",
            AuditOutcome.Failure => "失败",
            _ => "未知"
        };
    }
}
