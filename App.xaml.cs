using System.Windows;
using IndustrialVisionHost.Models;
using IndustrialVisionHost.Services;

namespace IndustrialVisionHost
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            var authenticationService = new UserAuthenticationService();
            if (!authenticationService.TryInitialize(
                    out bool createdDefaultUsers,
                    out string? initializeError))
            {
                MessageBox.Show(
                    $"用户数据库初始化失败：{initializeError}",
                    "程序无法启动",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                Shutdown(1);
                return;
            }

            var auditLogService = new AuditLogService();
            if (!auditLogService.TryInitialize(out string? auditError))
            {
                MessageBox.Show(
                    $"操作审计初始化失败，本次运行仍可继续：{auditError}",
                    "审计功能警告",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }

            var alarmService = new AlarmService();
            if (!alarmService.TryInitialize(out string? alarmError))
            {
                MessageBox.Show(
                    $"报警数据库初始化失败，本次运行仍可继续：{alarmError}",
                    "报警功能警告",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }

            var systemSettingsService = new SystemSettingsService();
            if (!systemSettingsService.TryLoad(
                    out SystemSettings systemSettings,
                    out _,
                    out string? settingsError))
            {
                MessageBox.Show(
                    $"系统设置加载失败，本次使用内置默认值：{settingsError}",
                    "系统设置警告",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
            ThemeService.Apply(systemSettings.UiTheme);

            var loginWindow = new LoginWindow(
                authenticationService,
                auditLogService,
                createdDefaultUsers);
            if (loginWindow.ShowDialog() != true ||
                loginWindow.AuthenticatedUser is not
                    AuthenticatedUser authenticatedUser)
            {
                Shutdown();
                return;
            }

            if (authenticatedUser.MustChangePassword)
            {
#if DEBUG
                const bool canSkipInitialPasswordChange = true;
#else
                const bool canSkipInitialPasswordChange = false;
#endif
                var changePasswordWindow = new ChangePasswordWindow(
                    authenticationService,
                    auditLogService,
                    authenticatedUser,
                    true,
                    canSkipInitialPasswordChange);
                if (changePasswordWindow.ShowDialog() != true)
                {
                    Shutdown();
                    return;
                }

                if (changePasswordWindow.UpdatedUser is
                    AuthenticatedUser updatedUser)
                {
                    authenticatedUser = updatedUser;
                }
                else if (!changePasswordWindow.WasSkipped)
                {
                    Shutdown();
                    return;
                }
                else
                {
                    auditLogService.TryWrite(
                        authenticatedUser,
                        "SkipInitialPasswordChange",
                        "UserAccount",
                        authenticatedUser.Username,
                        AuditOutcome.Success,
                        "Debug开发模式暂时跳过初始密码修改",
                        out _);
                }
            }

            var mainWindow = new MainWindow(
                authenticatedUser,
                authenticationService,
                auditLogService,
                alarmService,
                systemSettingsService,
                systemSettings);
            MainWindow = mainWindow;
            ShutdownMode = ShutdownMode.OnMainWindowClose;
            mainWindow.Show();
        }
    }
}
