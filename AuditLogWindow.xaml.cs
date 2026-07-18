using System;
using System.Collections.Generic;
using System.Windows;
using IndustrialVisionHost.Models;
using IndustrialVisionHost.Services;

namespace IndustrialVisionHost
{
    public partial class AuditLogWindow : Window
    {
        private readonly AuditLogService auditLogService;
        private readonly UserAuthenticationService authenticationService;
        private readonly AuthenticatedUser administrator;

        public AuditLogWindow(
            AuditLogService auditLogService,
            UserAuthenticationService authenticationService,
            AuthenticatedUser administrator)
        {
            this.auditLogService = auditLogService;
            this.authenticationService = authenticationService;
            this.administrator = administrator;
            InitializeComponent();
            StartDatePicker.SelectedDate = DateTime.Today.AddDays(-6);
            EndDatePicker.SelectedDate = DateTime.Today;
            Loaded += (_, _) => QueryAudit();
        }

        private void Query_Click(object sender, RoutedEventArgs e)
        {
            QueryAudit();
        }

        private void QueryAudit()
        {
            if (!authenticationService.TryGetUsers(
                    administrator,
                    out _,
                    out string? authorizationError))
            {
                SetStatus(
                    authorizationError ?? "管理员身份复核失败。",
                    true);
                return;
            }

            if (!StartDatePicker.SelectedDate.HasValue ||
                !EndDatePicker.SelectedDate.HasValue)
            {
                SetStatus("请选择开始和结束日期。", true);
                return;
            }

            DateTime startLocal = DateTime.SpecifyKind(
                StartDatePicker.SelectedDate.Value.Date,
                DateTimeKind.Local);
            DateTime endExclusiveLocal = DateTime.SpecifyKind(
                EndDatePicker.SelectedDate.Value.Date.AddDays(1),
                DateTimeKind.Local);
            if (endExclusiveLocal <= startLocal)
            {
                SetStatus("结束日期不能早于开始日期。", true);
                return;
            }

            if (!auditLogService.TryQuery(
                    administrator,
                    startLocal.ToUniversalTime(),
                    endExclusiveLocal.ToUniversalTime(),
                    UsernameFilterInput.Text,
                    ActionFilterInput.Text,
                    500,
                    out IReadOnlyList<AuditLogRecord> records,
                    out string? errorMessage))
            {
                SetStatus(errorMessage ?? "审计查询失败。", true);
                return;
            }

            AuditGrid.ItemsSource = records;
            SetStatus(
                $"共显示 {records.Count} 条，单次最多500条。数据库：{auditLogService.DatabasePath}",
                false);
        }

        private void SetStatus(string message, bool isError)
        {
            StatusTextBlock.Text = message;
            StatusTextBlock.Foreground = isError
                ? System.Windows.Media.Brushes.DarkRed
                : System.Windows.Media.Brushes.DarkSlateGray;
        }
    }
}
