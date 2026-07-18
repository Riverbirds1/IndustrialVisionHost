using System;
using System.Collections.Generic;
using System.IO;
using IndustrialVisionHost.Models;
using Microsoft.Data.Sqlite;

namespace IndustrialVisionHost.Services
{
    public sealed class AuditLogService
    {
        private readonly object databaseSync = new object();
        private readonly string connectionString;

        public AuditLogService(string? databasePath = null)
        {
            DatabasePath = databasePath ?? Path.Combine(
                Environment.GetFolderPath(
                    Environment.SpecialFolder.LocalApplicationData),
                "IndustrialVisionHost",
                "Data",
                "operation-audit.db");
            connectionString = new SqliteConnectionStringBuilder
            {
                DataSource = DatabasePath,
                Mode = SqliteOpenMode.ReadWriteCreate,
                Cache = SqliteCacheMode.Shared
            }.ToString();
        }

        public string DatabasePath { get; }

        public bool TryInitialize(out string? errorMessage)
        {
            try
            {
                lock (databaseSync)
                {
                    string? directory = Path.GetDirectoryName(DatabasePath);
                    if (!string.IsNullOrWhiteSpace(directory))
                    {
                        Directory.CreateDirectory(directory);
                    }

                    using SqliteConnection connection = OpenConnection();
                    using SqliteCommand command = connection.CreateCommand();
                    command.CommandText =
                        @"
                        PRAGMA journal_mode = WAL;
                        PRAGMA busy_timeout = 3000;

                        CREATE TABLE IF NOT EXISTS operation_audit
                        (
                            id INTEGER PRIMARY KEY AUTOINCREMENT,
                            occurred_at_utc TEXT NOT NULL,
                            user_id INTEGER NULL,
                            username TEXT NOT NULL,
                            display_name TEXT NOT NULL,
                            role TEXT NOT NULL,
                            action_type TEXT NOT NULL,
                            target_type TEXT NOT NULL,
                            target_identifier TEXT NOT NULL,
                            outcome TEXT NOT NULL,
                            details TEXT NOT NULL,
                            workstation TEXT NOT NULL
                        );

                        CREATE INDEX IF NOT EXISTS idx_audit_occurred_at
                        ON operation_audit(occurred_at_utc);

                        CREATE INDEX IF NOT EXISTS idx_audit_user_occurred_at
                        ON operation_audit(username, occurred_at_utc);

                        CREATE INDEX IF NOT EXISTS idx_audit_action_occurred_at
                        ON operation_audit(action_type, occurred_at_utc);

                        PRAGMA user_version = 1;
                        ";
                    command.ExecuteNonQuery();
                }

                errorMessage = null;
                return true;
            }
            catch (Exception ex)
            {
                errorMessage = ex.Message;
                return false;
            }
        }

        public bool TryWrite(
            AuthenticatedUser? user,
            string actionType,
            string targetType,
            string targetIdentifier,
            AuditOutcome outcome,
            string details,
            out string? errorMessage,
            string? attemptedUsername = null)
        {
            try
            {
                lock (databaseSync)
                {
                    using SqliteConnection connection = OpenConnection();
                    using SqliteCommand command = connection.CreateCommand();
                    command.CommandText =
                        @"
                        INSERT INTO operation_audit
                        (
                            occurred_at_utc, user_id, username, display_name,
                            role, action_type, target_type, target_identifier,
                            outcome, details, workstation
                        )
                        VALUES
                        (
                            $occurredAtUtc, $userId, $username, $displayName,
                            $role, $actionType, $targetType, $targetIdentifier,
                            $outcome, $details, $workstation
                        );
                        ";
                    command.Parameters.AddWithValue(
                        "$occurredAtUtc",
                        DateTime.UtcNow.ToString("O"));
                    command.Parameters.AddWithValue(
                        "$userId",
                        user is null ? DBNull.Value : user.Id);
                    command.Parameters.AddWithValue(
                        "$username",
                        LimitText(
                            user?.Username ?? attemptedUsername ?? "anonymous",
                            100));
                    command.Parameters.AddWithValue(
                        "$displayName",
                        LimitText(user?.DisplayName ?? string.Empty, 100));
                    command.Parameters.AddWithValue(
                        "$role",
                        user?.Role.ToString() ?? string.Empty);
                    command.Parameters.AddWithValue(
                        "$actionType",
                        LimitText(actionType, 100));
                    command.Parameters.AddWithValue(
                        "$targetType",
                        LimitText(targetType, 100));
                    command.Parameters.AddWithValue(
                        "$targetIdentifier",
                        LimitText(targetIdentifier, 200));
                    command.Parameters.AddWithValue(
                        "$outcome",
                        outcome.ToString());
                    command.Parameters.AddWithValue(
                        "$details",
                        LimitText(details, 1000));
                    command.Parameters.AddWithValue(
                        "$workstation",
                        LimitText(Environment.MachineName, 100));
                    command.ExecuteNonQuery();
                }

                errorMessage = null;
                return true;
            }
            catch (Exception ex)
            {
                errorMessage = ex.Message;
                return false;
            }
        }

        public bool TryQuery(
            AuthenticatedUser requester,
            DateTime? startUtc,
            DateTime? endExclusiveUtc,
            string? username,
            string? actionType,
            int maximumRecords,
            out IReadOnlyList<AuditLogRecord> records,
            out string? errorMessage)
        {
            if (!UserAuthorizationPolicy.HasPermission(
                    requester.Role,
                    UserPermission.ManageUsers))
            {
                records = Array.Empty<AuditLogRecord>();
                errorMessage = "当前账户没有操作审计查看权限。";
                return false;
            }

            if (maximumRecords < 1 || maximumRecords > 2000)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(maximumRecords),
                    "审计查询数量必须在1～2000之间。");
            }

            try
            {
                var result = new List<AuditLogRecord>();
                lock (databaseSync)
                {
                    using SqliteConnection connection = OpenConnection();
                    using SqliteCommand command = connection.CreateCommand();
                    command.CommandText =
                        @"
                        SELECT id, occurred_at_utc, user_id, username,
                               display_name, role, action_type, target_type,
                               target_identifier, outcome, details, workstation
                        FROM operation_audit
                        WHERE ($startUtc IS NULL OR occurred_at_utc >= $startUtc)
                          AND ($endUtc IS NULL OR occurred_at_utc < $endUtc)
                          AND ($username = '' OR username = $username)
                          AND ($actionType = '' OR action_type = $actionType)
                        ORDER BY occurred_at_utc DESC, id DESC
                        LIMIT $maximumRecords;
                        ";
                    command.Parameters.AddWithValue(
                        "$startUtc",
                        startUtc.HasValue
                            ? startUtc.Value.ToString("O")
                            : DBNull.Value);
                    command.Parameters.AddWithValue(
                        "$endUtc",
                        endExclusiveUtc.HasValue
                            ? endExclusiveUtc.Value.ToString("O")
                            : DBNull.Value);
                    command.Parameters.AddWithValue(
                        "$username",
                        (username ?? string.Empty).Trim());
                    command.Parameters.AddWithValue(
                        "$actionType",
                        (actionType ?? string.Empty).Trim());
                    command.Parameters.AddWithValue(
                        "$maximumRecords",
                        maximumRecords);

                    using SqliteDataReader reader = command.ExecuteReader();
                    while (reader.Read())
                    {
                        if (!Enum.TryParse(
                                reader.GetString(9),
                                true,
                                out AuditOutcome outcome))
                        {
                            outcome = AuditOutcome.Failure;
                        }

                        result.Add(new AuditLogRecord
                        {
                            Id = reader.GetInt64(0),
                            OccurredAtUtc = DateTime.Parse(
                                reader.GetString(1),
                                null,
                                System.Globalization.DateTimeStyles.RoundtripKind),
                            UserId = reader.IsDBNull(2) ? null : reader.GetInt64(2),
                            Username = reader.GetString(3),
                            DisplayName = reader.GetString(4),
                            Role = reader.GetString(5),
                            ActionType = reader.GetString(6),
                            TargetType = reader.GetString(7),
                            TargetIdentifier = reader.GetString(8),
                            Outcome = outcome,
                            Details = reader.GetString(10),
                            Workstation = reader.GetString(11)
                        });
                    }
                }

                records = result;
                errorMessage = null;
                return true;
            }
            catch (Exception ex)
            {
                records = Array.Empty<AuditLogRecord>();
                errorMessage = ex.Message;
                return false;
            }
        }

        private static string LimitText(string? value, int maximumLength)
        {
            string normalized = (value ?? string.Empty)
                .Replace("\r", " ")
                .Replace("\n", " ")
                .Trim();
            return normalized.Length <= maximumLength
                ? normalized
                : normalized.Substring(0, maximumLength);
        }

        private SqliteConnection OpenConnection()
        {
            var connection = new SqliteConnection(connectionString);
            connection.Open();
            return connection;
        }
    }
}
