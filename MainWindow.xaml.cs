using System;
using System.Windows;
using IndustrialVisionHost.Models;
using IndustrialVisionHost.Services;
using IndustrialVisionHost.ViewModels;

namespace IndustrialVisionHost
{
    public partial class MainWindow : Window
    {
        private readonly MainViewModel viewModel;
        private readonly UserAuthenticationService authenticationService;
        private readonly AuditLogService auditLogService;
        private readonly AlarmService alarmService;
        private readonly SystemSettingsService systemSettingsService;
        private SystemSettings systemSettings;
        private AuthenticatedUser currentUser;

        public MainWindow(
            AuthenticatedUser authenticatedUser,
            UserAuthenticationService authenticationService,
            AuditLogService auditLogService,
            AlarmService alarmService,
            SystemSettingsService systemSettingsService,
            SystemSettings systemSettings)
        {
            InitializeComponent();
            currentUser = authenticatedUser;
            this.authenticationService = authenticationService;
            this.auditLogService = auditLogService;
            this.alarmService = alarmService;
            this.systemSettingsService = systemSettingsService;
            this.systemSettings = systemSettings.Copy();
            if (this.systemSettings.RememberWindowState)
            {
                Width = this.systemSettings.MainWindowWidth;
                Height = this.systemSettings.MainWindowHeight;
                WindowState = this.systemSettings.MainWindowMaximized
                    ? WindowState.Maximized
                    : WindowState.Normal;
            }
            viewModel = new MainViewModel(
                authenticatedUser,
                auditLogService,
                alarmService,
                this.systemSettings);
            DataContext = viewModel;
        }

        private void ChangePassword_Click(
            object sender,
            RoutedEventArgs e)
        {
            var window = new ChangePasswordWindow(
                authenticationService,
                auditLogService,
                currentUser,
                false)
            {
                Owner = this,
                WindowStartupLocation = WindowStartupLocation.CenterOwner
            };
            if (window.ShowDialog() != true ||
                window.UpdatedUser is not AuthenticatedUser updatedUser)
            {
                return;
            }

            currentUser = updatedUser;
            viewModel.UpdatePasswordChangedSession(updatedUser);
            MessageBox.Show(
                "密码修改成功。下次登录请使用新密码。",
                "修改密码",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }

        private void ManageUsers_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (!UserAuthorizationPolicy.HasPermission(
                    currentUser.Role,
                    UserPermission.ManageUsers))
            {
                MessageBox.Show(
                    "当前账户没有用户管理权限。",
                    "权限不足",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            var window = new UserManagementWindow(
                authenticationService,
                auditLogService,
                currentUser)
            {
                Owner = this
            };
            auditLogService.TryWrite(
                currentUser,
                "OpenUserManagement",
                "UserManagement",
                "app-users.db",
                AuditOutcome.Success,
                "管理员打开用户管理窗口",
                out _);
            window.ShowDialog();
        }

        private void ViewAuditLog_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (!UserAuthorizationPolicy.HasPermission(
                    currentUser.Role,
                    UserPermission.ManageUsers))
            {
                MessageBox.Show(
                    "当前账户没有操作审计查看权限。",
                    "权限不足",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            auditLogService.TryWrite(
                currentUser,
                "ViewAuditLog",
                "AuditLog",
                "operation-audit.db",
                AuditOutcome.Success,
                "管理员打开操作审计窗口",
                out _);
            var window = new AuditLogWindow(
                auditLogService,
                authenticationService,
                currentUser)
            {
                Owner = this
            };
            window.ShowDialog();
        }

        private void ViewAlarms_Click(object sender, RoutedEventArgs e)
        {
            if (!UserAuthorizationPolicy.HasPermission(
                    currentUser.Role,
                    UserPermission.ViewAlarms))
            {
                MessageBox.Show(
                    "当前账户没有报警查看权限。",
                    "权限不足",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            var window = new AlarmWindow(
                alarmService,
                auditLogService,
                currentUser)
            {
                Owner = this
            };
            window.ShowDialog();
            viewModel.RefreshActiveAlarmCount();
        }

        private void SystemSettings_Click(object sender, RoutedEventArgs e)
        {
            if (!UserAuthorizationPolicy.HasPermission(
                    currentUser.Role,
                    UserPermission.ManageSystemSettings))
            {
                MessageBox.Show(
                    "当前账户没有系统设置修改权限。",
                    "权限不足",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            var window = new SystemSettingsWindow(
                systemSettingsService,
                auditLogService,
                currentUser,
                systemSettings)
            {
                Owner = this
            };
            if (window.ShowDialog() == true &&
                window.SavedSettings is SystemSettings savedSettings)
            {
                systemSettings = savedSettings.Copy();
                ThemeService.Apply(systemSettings.UiTheme);
                viewModel.ApplySystemSettings(savedSettings);
            }
        }

        protected override void OnClosed(EventArgs e)
        {
            if (systemSettings.RememberWindowState)
            {
                Rect bounds = WindowState == WindowState.Normal
                    ? new Rect(Left, Top, ActualWidth, ActualHeight)
                    : RestoreBounds;
                if (!bounds.IsEmpty)
                {
                    systemSettings.MainWindowWidth = Math.Clamp(
                        bounds.Width,
                        900,
                        3840);
                    systemSettings.MainWindowHeight = Math.Clamp(
                        bounds.Height,
                        650,
                        2160);
                }

                systemSettings.MainWindowMaximized =
                    WindowState == WindowState.Maximized;
                systemSettingsService.TrySave(systemSettings, out _);
            }

            viewModel.Dispose();
            base.OnClosed(e);
        }
    }
}
