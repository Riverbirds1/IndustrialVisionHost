using System;
using System.Collections.Generic;
using System.IO;
using IndustrialVisionHost.Models;
using Microsoft.Data.Sqlite;

namespace IndustrialVisionHost.Services
{
    public sealed class AlarmService
    {
        private readonly object databaseSync = new object();
        private readonly string connectionString;

        public AlarmService(string? databasePath = null)
        {
            DatabasePath = databasePath ?? Path.Combine(
                Environment.GetFolderPath(
                    Environment.SpecialFolder.LocalApplicationData),
                "IndustrialVisionHost",
                "Data",
                "alarms.db");
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

                        CREATE TABLE IF NOT EXISTS alarm_history
                        (
                            id INTEGER PRIMARY KEY AUTOINCREMENT,
                            alarm_code TEXT NOT NULL,
                            severity TEXT NOT NULL,
                            source TEXT NOT NULL,
                            message TEXT NOT NULL,
                            raised_at_utc TEXT NOT NULL,
                            last_occurred_at_utc TEXT NOT NULL,
                            occurrence_count INTEGER NOT NULL DEFAULT 1,
                            acknowledged_at_utc TEXT NULL,
                            acknowledged_by_user_id INTEGER NULL,
                            acknowledged_by_username TEXT NOT NULL DEFAULT '',
                            cleared_at_utc TEXT NULL,
                            clear_reason TEXT NOT NULL DEFAULT ''
                        );

                        CREATE UNIQUE INDEX IF NOT EXISTS
                            idx_alarm_unique_active
                        ON alarm_history(alarm_code, source)
                        WHERE cleared_at_utc IS NULL;

                        CREATE INDEX IF NOT EXISTS idx_alarm_raised_at
                        ON alarm_history(raised_at_utc);

                        CREATE INDEX IF NOT EXISTS idx_alarm_active_severity
                        ON alarm_history(cleared_at_utc, severity);

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

        public bool TryRaise(
            string alarmCode,
            AlarmSeverity severity,
            string source,
            string message,
            out long alarmId,
            out bool isNewAlarm,
            out string? errorMessage)
        {
            alarmId = 0;
            isNewAlarm = false;
            string code = NormalizeRequired(alarmCode, 100, "报警代码");
            string normalizedSource = NormalizeRequired(source, 100, "报警来源");
            string normalizedMessage = NormalizeRequired(message, 500, "报警消息");

            try
            {
                lock (databaseSync)
                {
                    using SqliteConnection connection = OpenConnection();
                    using SqliteTransaction transaction =
                        connection.BeginTransaction();
                    using SqliteCommand findCommand = connection.CreateCommand();
                    findCommand.Transaction = transaction;
                    findCommand.CommandText =
                        @"
                        SELECT id
                        FROM alarm_history
                        WHERE alarm_code = $code
                          AND source = $source
                          AND cleared_at_utc IS NULL;
                        ";
                    findCommand.Parameters.AddWithValue("$code", code);
                    findCommand.Parameters.AddWithValue("$source", normalizedSource);
                    object? existingId = findCommand.ExecuteScalar();
                    string now = DateTime.UtcNow.ToString("O");

                    if (existingId is not null)
                    {
                        alarmId = (long)existingId;
                        using SqliteCommand updateCommand = connection.CreateCommand();
                        updateCommand.Transaction = transaction;
                        updateCommand.CommandText =
                            @"
                            UPDATE alarm_history
                            SET severity = $severity,
                                message = $message,
                                last_occurred_at_utc = $now,
                                occurrence_count = occurrence_count + 1
                            WHERE id = $id;
                            ";
                        updateCommand.Parameters.AddWithValue("$severity", severity.ToString());
                        updateCommand.Parameters.AddWithValue("$message", normalizedMessage);
                        updateCommand.Parameters.AddWithValue("$now", now);
                        updateCommand.Parameters.AddWithValue("$id", alarmId);
                        updateCommand.ExecuteNonQuery();
                    }
                    else
                    {
                        using SqliteCommand insertCommand = connection.CreateCommand();
                        insertCommand.Transaction = transaction;
                        insertCommand.CommandText =
                            @"
                            INSERT INTO alarm_history
                            (alarm_code, severity, source, message,
                             raised_at_utc, last_occurred_at_utc, occurrence_count)
                            VALUES
                            ($code, $severity, $source, $message, $now, $now, 1);
                            SELECT last_insert_rowid();
                            ";
                        insertCommand.Parameters.AddWithValue("$code", code);
                        insertCommand.Parameters.AddWithValue("$severity", severity.ToString());
                        insertCommand.Parameters.AddWithValue("$source", normalizedSource);
                        insertCommand.Parameters.AddWithValue("$message", normalizedMessage);
                        insertCommand.Parameters.AddWithValue("$now", now);
                        alarmId = (long)(insertCommand.ExecuteScalar() ?? 0L);
                        isNewAlarm = true;
                    }

                    transaction.Commit();
                }

                errorMessage = null;
                return true;
            }
            catch (Exception ex)
            {
                alarmId = 0;
                isNewAlarm = false;
                errorMessage = ex.Message;
                return false;
            }
        }

        public bool TryAcknowledge(
            AuthenticatedUser user,
            long alarmId,
            out string? errorMessage)
        {
            if (!UserAuthorizationPolicy.HasPermission(
                    user.Role,
                    UserPermission.AcknowledgeAlarms))
            {
                errorMessage = "当前账户没有报警确认权限。";
                return false;
            }

            try
            {
                lock (databaseSync)
                {
                    using SqliteConnection connection = OpenConnection();
                    using SqliteCommand command = connection.CreateCommand();
                    command.CommandText =
                        @"
                        UPDATE alarm_history
                        SET acknowledged_at_utc = $now,
                            acknowledged_by_user_id = $userId,
                            acknowledged_by_username = $username
                        WHERE id = $id
                          AND cleared_at_utc IS NULL
                          AND acknowledged_at_utc IS NULL;
                        ";
                    command.Parameters.AddWithValue("$now", DateTime.UtcNow.ToString("O"));
                    command.Parameters.AddWithValue("$userId", user.Id);
                    command.Parameters.AddWithValue("$username", user.Username);
                    command.Parameters.AddWithValue("$id", alarmId);
                    if (command.ExecuteNonQuery() != 1)
                    {
                        errorMessage = "报警不存在、已经恢复或已经确认。";
                        return false;
                    }
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

        public bool TryClearActive(
            string alarmCode,
            string source,
            string clearReason,
            out int clearedCount,
            out string? errorMessage)
        {
            try
            {
                lock (databaseSync)
                {
                    using SqliteConnection connection = OpenConnection();
                    using SqliteCommand command = connection.CreateCommand();
                    command.CommandText =
                        @"
                        UPDATE alarm_history
                        SET cleared_at_utc = $now,
                            clear_reason = $reason
                        WHERE alarm_code = $code
                          AND source = $source
                          AND cleared_at_utc IS NULL;
                        ";
                    command.Parameters.AddWithValue("$now", DateTime.UtcNow.ToString("O"));
                    command.Parameters.AddWithValue("$reason", LimitText(clearReason, 500));
                    command.Parameters.AddWithValue("$code", LimitText(alarmCode, 100));
                    command.Parameters.AddWithValue("$source", LimitText(source, 100));
                    clearedCount = command.ExecuteNonQuery();
                }

                errorMessage = null;
                return true;
            }
            catch (Exception ex)
            {
                clearedCount = 0;
                errorMessage = ex.Message;
                return false;
            }
        }

        public bool TryGetActiveCount(
            out long activeCount,
            out string? errorMessage)
        {
            try
            {
                lock (databaseSync)
                {
                    using SqliteConnection connection = OpenConnection();
                    using SqliteCommand command = connection.CreateCommand();
                    command.CommandText =
                        "SELECT COUNT(*) FROM alarm_history WHERE cleared_at_utc IS NULL;";
                    activeCount = (long)(command.ExecuteScalar() ?? 0L);
                }

                errorMessage = null;
                return true;
            }
            catch (Exception ex)
            {
                activeCount = 0;
                errorMessage = ex.Message;
                return false;
            }
        }

        public bool TryQuery(
            AuthenticatedUser user,
            DateTime? startUtc,
            DateTime? endExclusiveUtc,
            bool activeOnly,
            int maximumRecords,
            out IReadOnlyList<AlarmRecord> records,
            out string? errorMessage)
        {
            if (!UserAuthorizationPolicy.HasPermission(
                    user.Role,
                    UserPermission.ViewAlarms))
            {
                records = Array.Empty<AlarmRecord>();
                errorMessage = "当前账户没有报警查看权限。";
                return false;
            }

            if (maximumRecords < 1 || maximumRecords > 2000)
            {
                throw new ArgumentOutOfRangeException(nameof(maximumRecords));
            }

            try
            {
                var result = new List<AlarmRecord>();
                lock (databaseSync)
                {
                    using SqliteConnection connection = OpenConnection();
                    using SqliteCommand command = connection.CreateCommand();
                    command.CommandText =
                        @"
                        SELECT id, alarm_code, severity, source, message,
                               raised_at_utc, last_occurred_at_utc, occurrence_count,
                               acknowledged_at_utc, acknowledged_by_username,
                               cleared_at_utc, clear_reason
                        FROM alarm_history
                        WHERE ($startUtc IS NULL OR raised_at_utc >= $startUtc)
                          AND ($endUtc IS NULL OR raised_at_utc < $endUtc)
                          AND ($activeOnly = 0 OR cleared_at_utc IS NULL)
                        ORDER BY last_occurred_at_utc DESC, id DESC
                        LIMIT $maximumRecords;
                        ";
                    command.Parameters.AddWithValue("$startUtc", startUtc.HasValue ? startUtc.Value.ToString("O") : DBNull.Value);
                    command.Parameters.AddWithValue("$endUtc", endExclusiveUtc.HasValue ? endExclusiveUtc.Value.ToString("O") : DBNull.Value);
                    command.Parameters.AddWithValue("$activeOnly", activeOnly ? 1 : 0);
                    command.Parameters.AddWithValue("$maximumRecords", maximumRecords);
                    using SqliteDataReader reader = command.ExecuteReader();
                    while (reader.Read())
                    {
                        Enum.TryParse(reader.GetString(2), true, out AlarmSeverity severity);
                        result.Add(new AlarmRecord
                        {
                            Id = reader.GetInt64(0), AlarmCode = reader.GetString(1), Severity = severity,
                            Source = reader.GetString(3), Message = reader.GetString(4),
                            RaisedAtUtc = ParseDate(reader.GetString(5)),
                            LastOccurredAtUtc = ParseDate(reader.GetString(6)),
                            OccurrenceCount = reader.GetInt32(7),
                            AcknowledgedAtUtc = reader.IsDBNull(8) ? null : ParseDate(reader.GetString(8)),
                            AcknowledgedBy = reader.GetString(9),
                            ClearedAtUtc = reader.IsDBNull(10) ? null : ParseDate(reader.GetString(10)),
                            ClearReason = reader.GetString(11)
                        });
                    }
                }

                records = result;
                errorMessage = null;
                return true;
            }
            catch (Exception ex)
            {
                records = Array.Empty<AlarmRecord>();
                errorMessage = ex.Message;
                return false;
            }
        }

        private static DateTime ParseDate(string value) => DateTime.Parse(
            value, null, System.Globalization.DateTimeStyles.RoundtripKind);

        private static string NormalizeRequired(string value, int max, string name)
        {
            string normalized = LimitText(value, max);
            if (normalized.Length == 0) throw new ArgumentException($"{name}不能为空。");
            return normalized;
        }

        private static string LimitText(string? value, int max)
        {
            string normalized = (value ?? string.Empty).Replace("\r", " ").Replace("\n", " ").Trim();
            return normalized.Length <= max ? normalized : normalized.Substring(0, max);
        }

        private SqliteConnection OpenConnection()
        {
            var connection = new SqliteConnection(connectionString);
            connection.Open();
            return connection;
        }
    }
}
