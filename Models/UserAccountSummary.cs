using System;

namespace IndustrialVisionHost.Models
{
    public sealed class UserAccountSummary
    {
        public long Id { get; init; }

        public string Username { get; init; } = string.Empty;

        public string DisplayName { get; init; } = string.Empty;

        public UserRole Role { get; init; }

        public bool IsEnabled { get; init; }

        public bool MustChangePassword { get; init; }

        public DateTime CreatedAtUtc { get; init; }

        public DateTime? LastLoginAtUtc { get; init; }

        public string RoleText => Role switch
        {
            UserRole.Operator => "操作员",
            UserRole.Engineer => "工程师",
            UserRole.Administrator => "管理员",
            _ => "未知"
        };

        public string EnabledText => IsEnabled ? "启用" : "禁用";

        public string PasswordStatusText => MustChangePassword
            ? "需要修改"
            : "正常";

        public string CreatedAtLocalText =>
            CreatedAtUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm");

        public string LastLoginAtLocalText => LastLoginAtUtc.HasValue
            ? LastLoginAtUtc.Value.ToLocalTime().ToString("yyyy-MM-dd HH:mm")
            : "从未登录";
    }
}
