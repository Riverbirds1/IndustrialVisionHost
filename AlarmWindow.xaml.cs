using System;
using System.Collections.Generic;
using System.Windows;
using IndustrialVisionHost.Models;
using IndustrialVisionHost.Services;

namespace IndustrialVisionHost
{
    public partial class AlarmWindow : Window
    {
        private readonly AlarmService alarmService;
        private readonly AuditLogService auditLogService;
        private readonly AuthenticatedUser currentUser;

        public AlarmWindow(
            AlarmService alarmService,
            AuditLogService auditLogService,
            AuthenticatedUser currentUser)
        {
            InitializeComponent();
            this.alarmService = alarmService;
            this.auditLogService = auditLogService;
            this.currentUser = currentUser;

            StartDatePicker.SelectedDate = DateTime.Today.AddDays(-6);
            EndDatePicker.SelectedDate = DateTime.Today;
            AcknowledgeButton.IsEnabled =
                UserAuthorizationPolicy.HasPermission(
                    currentUser.Role,
                    UserPermission.AcknowledgeAlarms);
            Loaded += (_, _) => RefreshRecords();
        }

        private void Refresh_Click(object sender, RoutedEventArgs e)
        {
            RefreshRecords();
        }

        private void Acknowledge_Click(object sender, RoutedEventArgs e)
        {
            if (AlarmDataGrid.SelectedItem is not AlarmRecord selected)
            {
                MessageBox.Show(
                    "请先在表格中选择一条活动报警。",
                    "报警确认",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            if (!alarmService.TryAcknowledge(
                    currentUser,
                    selected.Id,
                    out string? errorMessage))
            {
                auditLogService.TryWrite(
                    currentUser,
                    "AcknowledgeAlarm",
                    "Alarm",
                    selected.Id.ToString(),
                    AuditOutcome.Failure,
                    errorMessage ?? "报警确认失败",
                    out _);
                MessageBox.Show(
                    errorMessage,
                    "报警确认失败",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                RefreshRecords();
                return;
            }

            auditLogService.TryWrite(
                currentUser,
                "AcknowledgeAlarm",
                "Alarm",
                selected.Id.ToString(),
                AuditOutcome.Success,
                $"确认报警 {selected.AlarmCode} / {selected.Source}",
                out _);
            RefreshRecords();
        }

        private void RefreshRecords()
        {
            DateTime? startUtc = StartDatePicker.SelectedDate?
                .Date.ToUniversalTime();
            DateTime? endExclusiveUtc = EndDatePicker.SelectedDate?
                .Date.AddDays(1).ToUniversalTime();

            if (startUtc.HasValue && endExclusiveUtc.HasValue &&
                endExclusiveUtc <= startUtc)
            {
                StatusTextBlock.Text = "结束日期不能早于开始日期。";
                AlarmDataGrid.ItemsSource = Array.Empty<AlarmRecord>();
                return;
            }

            if (!alarmService.TryQuery(
                    currentUser,
                    startUtc,
                    endExclusiveUtc,
                    ActiveOnlyCheckBox.IsChecked == true,
                    1000,
                    out IReadOnlyList<AlarmRecord> records,
                    out string? errorMessage))
            {
                StatusTextBlock.Text = $"报警查询失败：{errorMessage}";
                AlarmDataGrid.ItemsSource = Array.Empty<AlarmRecord>();
                return;
            }

            AlarmDataGrid.ItemsSource = records;
            StatusTextBlock.Text =
                $"查询完成：{records.Count} 条；当前用户：" +
                $"{currentUser.DisplayName}（{currentUser.RoleDisplayName}）" +
                (AcknowledgeButton.IsEnabled
                    ? "，可以确认活动报警。"
                    : "，仅可查看报警。工程师或管理员可执行确认。");
        }
    }
}
