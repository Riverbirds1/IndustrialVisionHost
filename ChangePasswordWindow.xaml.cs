using System.Windows;
using IndustrialVisionHost.Models;
using IndustrialVisionHost.Services;

namespace IndustrialVisionHost
{
    public partial class ChangePasswordWindow : Window
    {
        private readonly UserAuthenticationService authenticationService;
        private readonly AuditLogService auditLogService;
        private readonly AuthenticatedUser currentUser;

        public ChangePasswordWindow(
            UserAuthenticationService authenticationService,
            AuditLogService auditLogService,
            AuthenticatedUser currentUser,
            bool isRequired,
            bool canSkip = false)
        {
            this.authenticationService = authenticationService;
            this.auditLogService = auditLogService;
            this.currentUser = currentUser;
            InitializeComponent();

            UserTextBlock.Text =
                $"当前账户：{currentUser.DisplayName}（{currentUser.Username}）";
            InstructionTextBlock.Text = isRequired
                ? canSkip
                    ? "当前账户仍在使用初始密码。开发调试模式可以暂时跳过。"
                    : "当前账户仍在使用初始密码，必须修改后才能进入系统。"
                : "修改成功后，下次登录需要使用新密码。";
            CanSkip = isRequired && canSkip;
            CancelButton.Content = CanSkip
                ? "暂不修改"
                : isRequired ? "退出系统" : "取消";
            Loaded += (_, _) => CurrentPasswordInput.Focus();
        }

        public AuthenticatedUser? UpdatedUser { get; private set; }

        public bool CanSkip { get; }

        public bool WasSkipped { get; private set; }

        private void Confirm_Click(object sender, RoutedEventArgs e)
        {
            ErrorTextBlock.Text = string.Empty;
            string currentPassword = CurrentPasswordInput.Password;
            string newPassword = NewPasswordInput.Password;
            string confirmedPassword = ConfirmPasswordInput.Password;
            CurrentPasswordInput.Clear();
            NewPasswordInput.Clear();
            ConfirmPasswordInput.Clear();

            if (!string.Equals(
                    newPassword,
                    confirmedPassword,
                    System.StringComparison.Ordinal))
            {
                auditLogService.TryWrite(
                    currentUser,
                    "ChangePassword",
                    "UserAccount",
                    currentUser.Username,
                    AuditOutcome.Failure,
                    "两次输入的新密码不一致",
                    out _);
                ErrorTextBlock.Text = "两次输入的新密码不一致。";
                CurrentPasswordInput.Focus();
                return;
            }

            if (!authenticationService.TryChangePassword(
                    currentUser.Id,
                    currentPassword,
                    newPassword,
                    out AuthenticatedUser? updatedUser,
                    out string? errorMessage) ||
                updatedUser is null)
            {
                auditLogService.TryWrite(
                    currentUser,
                    "ChangePassword",
                    "UserAccount",
                    currentUser.Username,
                    AuditOutcome.Failure,
                    errorMessage ?? "密码修改失败",
                    out _);
                ErrorTextBlock.Text = errorMessage ?? "密码修改失败。";
                CurrentPasswordInput.Focus();
                return;
            }

            UpdatedUser = updatedUser;
            auditLogService.TryWrite(
                updatedUser,
                "ChangePassword",
                "UserAccount",
                updatedUser.Username,
                AuditOutcome.Success,
                "用户修改自己的密码",
                out _);
            DialogResult = true;
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            if (CanSkip)
            {
                WasSkipped = true;
                DialogResult = true;
                return;
            }

            DialogResult = false;
        }
    }
}
