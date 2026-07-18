using System.Collections.Generic;
using System.Windows;
using IndustrialVisionHost.Models;
using IndustrialVisionHost.Services;

namespace IndustrialVisionHost
{
    public partial class UserManagementWindow : Window
    {
        private readonly UserAuthenticationService authenticationService;
        private readonly AuditLogService auditLogService;
        private readonly AuthenticatedUser administrator;

        public UserManagementWindow(
            UserAuthenticationService authenticationService,
            AuditLogService auditLogService,
            AuthenticatedUser administrator)
        {
            this.authenticationService = authenticationService;
            this.auditLogService = auditLogService;
            this.administrator = administrator;
            InitializeComponent();
            NewRoleCombo.ItemsSource = new[]
            {
                new KeyValuePair<UserRole, string>(UserRole.Operator, "操作员"),
                new KeyValuePair<UserRole, string>(UserRole.Engineer, "工程师"),
                new KeyValuePair<UserRole, string>(UserRole.Administrator, "管理员")
            };
            NewRoleCombo.SelectedValue = UserRole.Operator;
            Loaded += (_, _) => RefreshUsers();
        }

        private UserAccountSummary? SelectedUser =>
            UsersGrid.SelectedItem as UserAccountSummary;

        private void Refresh_Click(object sender, RoutedEventArgs e)
        {
            RefreshUsers();
        }

        private void RefreshUsers()
        {
            if (!authenticationService.TryGetUsers(
                    administrator,
                    out IReadOnlyList<UserAccountSummary> users,
                    out string? errorMessage))
            {
                SetStatus(errorMessage ?? "用户列表读取失败。", true);
                return;
            }

            UsersGrid.ItemsSource = users;
            SetStatus($"已加载 {users.Count} 个用户。", false);
        }

        private void CreateUser_Click(object sender, RoutedEventArgs e)
        {
            string password = NewPasswordInput.Password;
            NewPasswordInput.Clear();
            if (NewRoleCombo.SelectedValue is not UserRole role)
            {
                SetStatus("请选择用户角色。", true);
                return;
            }

            if (!authenticationService.TryCreateUser(
                    administrator,
                    NewUsernameInput.Text,
                    NewDisplayNameInput.Text,
                    role,
                    password,
                    out string? errorMessage))
            {
                WriteAudit(
                    "CreateUser",
                    NewUsernameInput.Text,
                    AuditOutcome.Failure,
                    errorMessage ?? "用户创建失败");
                SetStatus(errorMessage ?? "用户创建失败。", true);
                return;
            }

            string username = NewUsernameInput.Text.Trim();
            WriteAudit(
                "CreateUser",
                username,
                AuditOutcome.Success,
                $"创建角色 {role} 的用户");
            NewUsernameInput.Clear();
            NewDisplayNameInput.Clear();
            RefreshUsers();
            SetStatus($"用户 {username} 创建成功。", false);
        }

        private void EnableUser_Click(object sender, RoutedEventArgs e)
        {
            SetSelectedUserEnabled(true);
        }

        private void DisableUser_Click(object sender, RoutedEventArgs e)
        {
            SetSelectedUserEnabled(false);
        }

        private void SetSelectedUserEnabled(bool isEnabled)
        {
            UserAccountSummary? selected = SelectedUser;
            if (selected is null)
            {
                SetStatus("请先在列表中选择用户。", true);
                return;
            }

            if (!authenticationService.TrySetUserEnabled(
                    administrator,
                    selected.Id,
                    isEnabled,
                    out string? errorMessage))
            {
                WriteAudit(
                    isEnabled ? "EnableUser" : "DisableUser",
                    selected.Username,
                    AuditOutcome.Failure,
                    errorMessage ?? "账户状态更新失败");
                SetStatus(errorMessage ?? "账户状态更新失败。", true);
                return;
            }

            WriteAudit(
                isEnabled ? "EnableUser" : "DisableUser",
                selected.Username,
                AuditOutcome.Success,
                isEnabled ? "启用用户账户" : "禁用用户账户");
            RefreshUsers();
            SetStatus(
                $"用户 {selected.Username} 已{(isEnabled ? "启用" : "禁用")}。",
                false);
        }

        private void ResetPassword_Click(object sender, RoutedEventArgs e)
        {
            UserAccountSummary? selected = SelectedUser;
            string password = ResetPasswordInput.Password;
            ResetPasswordInput.Clear();
            if (selected is null)
            {
                SetStatus("请先在列表中选择用户。", true);
                return;
            }

            if (!authenticationService.TryResetPassword(
                    administrator,
                    selected.Id,
                    password,
                    out string? errorMessage))
            {
                WriteAudit(
                    "ResetPassword",
                    selected.Username,
                    AuditOutcome.Failure,
                    errorMessage ?? "密码重置失败");
                SetStatus(errorMessage ?? "密码重置失败。", true);
                return;
            }

            WriteAudit(
                "ResetPassword",
                selected.Username,
                AuditOutcome.Success,
                "管理员重置用户临时密码");
            RefreshUsers();
            SetStatus($"用户 {selected.Username} 的临时密码已重置。", false);
        }

        private void SetStatus(string message, bool isError)
        {
            StatusTextBlock.Text = message;
            StatusTextBlock.Foreground = isError
                ? System.Windows.Media.Brushes.DarkRed
                : System.Windows.Media.Brushes.DarkGreen;
        }

        private void WriteAudit(
            string actionType,
            string targetIdentifier,
            AuditOutcome outcome,
            string details)
        {
            auditLogService.TryWrite(
                administrator,
                actionType,
                "UserAccount",
                targetIdentifier,
                outcome,
                details,
                out _);
        }
    }
}
