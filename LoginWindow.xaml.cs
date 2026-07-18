using System.Windows;
using IndustrialVisionHost.Models;
using IndustrialVisionHost.Services;

namespace IndustrialVisionHost
{
    public partial class LoginWindow : Window
    {
        private readonly UserAuthenticationService authenticationService;
        private readonly AuditLogService auditLogService;

        public LoginWindow(
            UserAuthenticationService authenticationService,
            AuditLogService auditLogService,
            bool createdDefaultUsers)
        {
            this.authenticationService = authenticationService;
            this.auditLogService = auditLogService;
            InitializeComponent();
            InitializationTextBlock.Text = createdDefaultUsers
                ? "已首次创建演示账户。正式部署前必须修改初始密码。"
                : "账户密码只保存加盐哈希，不在数据库中保存明文。";
            Loaded += (_, _) => PasswordInput.Focus();
        }

        public AuthenticatedUser? AuthenticatedUser { get; private set; }

        private void Login_Click(object sender, RoutedEventArgs e)
        {
            ErrorTextBlock.Text = string.Empty;
            string password = PasswordInput.Password;
            PasswordInput.Clear();

            if (!authenticationService.TryAuthenticate(
                    UsernameTextBox.Text,
                    password,
                    out AuthenticatedUser? authenticatedUser,
                    out string? errorMessage) ||
                authenticatedUser is null)
            {
                auditLogService.TryWrite(
                    null,
                    "Login",
                    "UserAccount",
                    UsernameTextBox.Text.Trim(),
                    AuditOutcome.Failure,
                    errorMessage ?? "登录失败",
                    out _,
                    UsernameTextBox.Text);
                ErrorTextBlock.Text = errorMessage ?? "登录失败。";
                PasswordInput.Focus();
                return;
            }

            AuthenticatedUser = authenticatedUser;
            auditLogService.TryWrite(
                authenticatedUser,
                "Login",
                "UserAccount",
                authenticatedUser.Username,
                AuditOutcome.Success,
                "用户登录成功",
                out _);
            DialogResult = true;
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }
    }
}
