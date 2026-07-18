using System;
using System.Windows;
using IndustrialVisionHost.Models;
using IndustrialVisionHost.Services;

namespace IndustrialVisionHost
{
    public partial class SystemSettingsWindow : Window
    {
        private readonly SystemSettingsService settingsService;
        private readonly AuditLogService auditLogService;
        private readonly AuthenticatedUser currentUser;
        private readonly SystemSettings currentSettings;

        public SystemSettingsWindow(
            SystemSettingsService settingsService,
            AuditLogService auditLogService,
            AuthenticatedUser currentUser,
            SystemSettings currentSettings)
        {
            InitializeComponent();
            this.settingsService = settingsService;
            this.auditLogService = auditLogService;
            this.currentUser = currentUser;
            this.currentSettings = currentSettings.Copy();

            PlcHostTextBox.Text = currentSettings.PlcHost;
            PlcPortTextBox.Text = currentSettings.PlcTextPort.ToString();
            RetentionDaysTextBox.Text =
                currentSettings.NgImageRetentionDays.ToString();
            MaximumMegabytesTextBox.Text =
                currentSettings.NgImageMaximumMegabytes.ToString();
            ThemeComboBox.SelectedValue = currentSettings.UiTheme;
            RememberWindowStateCheckBox.IsChecked =
                currentSettings.RememberWindowState;
            UpdateModbusPortPreview();
            PlcPortTextBox.TextChanged += (_, _) => UpdateModbusPortPreview();
        }

        public SystemSettings? SavedSettings { get; private set; }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            if (!UserAuthorizationPolicy.HasPermission(
                    currentUser.Role,
                    UserPermission.ManageSystemSettings))
            {
                ErrorTextBlock.Text = "当前账户没有系统设置修改权限。";
                WriteAudit(AuditOutcome.Denied, ErrorTextBlock.Text);
                return;
            }

            if (!int.TryParse(PlcPortTextBox.Text, out int plcPort) ||
                !int.TryParse(RetentionDaysTextBox.Text, out int retentionDays) ||
                !int.TryParse(MaximumMegabytesTextBox.Text, out int maximumMegabytes))
            {
                ErrorTextBlock.Text = "端口、保留天数和容量上限必须填写整数。";
                return;
            }

            var settings = new SystemSettings
            {
                PlcHost = PlcHostTextBox.Text,
                PlcTextPort = plcPort,
                NgImageRetentionDays = retentionDays,
                NgImageMaximumMegabytes = maximumMegabytes,
                UiTheme = ThemeComboBox.SelectedValue as string ?? "Light",
                RememberWindowState =
                    RememberWindowStateCheckBox.IsChecked == true,
                MainWindowWidth = currentSettings.MainWindowWidth,
                MainWindowHeight = currentSettings.MainWindowHeight,
                MainWindowMaximized = currentSettings.MainWindowMaximized
            };

            if (!settingsService.TrySave(settings, out string? errorMessage))
            {
                ErrorTextBlock.Text = $"系统设置保存失败：{errorMessage}";
                WriteAudit(AuditOutcome.Failure, ErrorTextBlock.Text);
                return;
            }

            settings.PlcHost = settings.PlcHost.Trim();
            SavedSettings = settings;
            WriteAudit(
                AuditOutcome.Success,
                $"PLC={settings.PlcHost}:{settings.PlcTextPort}，" +
                $"Modbus={settings.PlcTextPort + 1}，" +
                $"NG保留={settings.NgImageRetentionDays}天/" +
                $"{settings.NgImageMaximumMegabytes}MB，" +
                $"主题={settings.UiTheme}，" +
                $"记住窗口={settings.RememberWindowState}");
            DialogResult = true;
        }

        private void UpdateModbusPortPreview()
        {
            ModbusPortTextBlock.Text =
                int.TryParse(PlcPortTextBox.Text, out int port) &&
                port >= 1 && port <= 65534
                    ? $"Modbus TCP 端口：{port + 1}"
                    : "Modbus TCP 端口：等待有效的文本协议端口";
        }

        private void WriteAudit(AuditOutcome outcome, string details)
        {
            auditLogService.TryWrite(
                currentUser,
                "SaveSystemSettings",
                "SystemSettings",
                settingsService.SettingsPath,
                outcome,
                details,
                out _);
        }
    }
}
