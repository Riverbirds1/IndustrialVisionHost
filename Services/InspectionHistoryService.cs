using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Data.Sqlite;
using IndustrialVisionHost.Models;

namespace IndustrialVisionHost.Services
{
    public sealed class InspectionHistoryService
    {
        private readonly object databaseSync = new object();
        private readonly string connectionString;

        public InspectionHistoryService(string? databasePath = null)
        {
            DatabasePath = databasePath ?? Path.Combine(
                Environment.GetFolderPath(
                    Environment.SpecialFolder.LocalApplicationData),
                "IndustrialVisionHost",
                "Data",
                "inspection-history.db");

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

                        CREATE TABLE IF NOT EXISTS inspection_records
                        (
                            id INTEGER PRIMARY KEY AUTOINCREMENT,
                            batch_number TEXT NOT NULL DEFAULT '',
                            recipe_name TEXT NOT NULL DEFAULT '',
                            recipe_revision INTEGER NOT NULL DEFAULT 0,
                            detected_at_utc TEXT NOT NULL,
                            trigger_source TEXT NOT NULL,
                            cycle_id TEXT NULL,
                            operation_mode TEXT NOT NULL,
                            is_ok INTEGER NOT NULL,
                            judgement_code TEXT NOT NULL,
                            judgement_reason TEXT NOT NULL,
                            target_count INTEGER NOT NULL,
                            raw_contour_count INTEGER NOT NULL,
                            area_pixels REAL NOT NULL,
                            physical_area_mm2 REAL NOT NULL,
                            center_x_pixel INTEGER NOT NULL,
                            center_y_pixel INTEGER NOT NULL,
                            center_x_mm REAL NOT NULL,
                            center_y_mm REAL NOT NULL,
                            width_pixels INTEGER NOT NULL,
                            height_pixels INTEGER NOT NULL,
                            width_mm REAL NOT NULL,
                            height_mm REAL NOT NULL,
                            processing_time_ms REAL NOT NULL,
                            ng_image_path TEXT NULL
                        );

                        CREATE INDEX IF NOT EXISTS
                            idx_inspection_records_detected_at
                        ON inspection_records(detected_at_utc);

                        CREATE INDEX IF NOT EXISTS
                            idx_inspection_records_is_ok_detected_at
                        ON inspection_records(is_ok, detected_at_utc);
                        ";
                    command.ExecuteNonQuery();

                    EnsureBatchNumberColumn(connection);
                    EnsureRecipeColumns(connection);

                    using SqliteCommand batchIndexCommand =
                        connection.CreateCommand();
                    batchIndexCommand.CommandText =
                        @"
                        CREATE INDEX IF NOT EXISTS
                            idx_inspection_records_batch_detected_at
                        ON inspection_records(batch_number, detected_at_utc);

                        PRAGMA user_version = 3;
                        ";
                    batchIndexCommand.ExecuteNonQuery();
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

        public bool TrySave(
            InspectionHistoryRecord record,
            out string? errorMessage)
        {
            if (record is null)
            {
                throw new ArgumentNullException(nameof(record));
            }

            try
            {
                lock (databaseSync)
                {
                    using SqliteConnection connection = OpenConnection();
                    using SqliteCommand command = connection.CreateCommand();
                    command.CommandText =
                        @"
                        INSERT INTO inspection_records
                        (
                            batch_number,
                            recipe_name,
                            recipe_revision,
                            detected_at_utc,
                            trigger_source,
                            cycle_id,
                            operation_mode,
                            is_ok,
                            judgement_code,
                            judgement_reason,
                            target_count,
                            raw_contour_count,
                            area_pixels,
                            physical_area_mm2,
                            center_x_pixel,
                            center_y_pixel,
                            center_x_mm,
                            center_y_mm,
                            width_pixels,
                            height_pixels,
                            width_mm,
                            height_mm,
                            processing_time_ms,
                            ng_image_path
                        )
                        VALUES
                        (
                            $batchNumber,
                            $recipeName,
                            $recipeRevision,
                            $detectedAtUtc,
                            $triggerSource,
                            $cycleId,
                            $operationMode,
                            $isOk,
                            $judgementCode,
                            $judgementReason,
                            $targetCount,
                            $rawContourCount,
                            $areaPixels,
                            $physicalArea,
                            $centerXPixel,
                            $centerYPixel,
                            $centerXMillimeters,
                            $centerYMillimeters,
                            $widthPixels,
                            $heightPixels,
                            $widthMillimeters,
                            $heightMillimeters,
                            $processingTime,
                            $ngImagePath
                        );
                        ";

                    command.Parameters.AddWithValue(
                        "$batchNumber",
                        record.BatchNumber);
                    command.Parameters.AddWithValue(
                        "$recipeName",
                        record.RecipeName);
                    command.Parameters.AddWithValue(
                        "$recipeRevision",
                        record.RecipeRevision);

                    command.Parameters.AddWithValue(
                        "$detectedAtUtc",
                        record.DetectedAtUtc.ToString("O"));
                    command.Parameters.AddWithValue(
                        "$triggerSource",
                        record.TriggerSource);
                    command.Parameters.AddWithValue(
                        "$cycleId",
                        (object?)record.CycleId ?? DBNull.Value);
                    command.Parameters.AddWithValue(
                        "$operationMode",
                        record.OperationMode);
                    command.Parameters.AddWithValue(
                        "$isOk",
                        record.IsOk ? 1 : 0);
                    command.Parameters.AddWithValue(
                        "$judgementCode",
                        record.JudgementCode);
                    command.Parameters.AddWithValue(
                        "$judgementReason",
                        record.JudgementReason);
                    command.Parameters.AddWithValue(
                        "$targetCount",
                        record.TargetCount);
                    command.Parameters.AddWithValue(
                        "$rawContourCount",
                        record.RawContourCount);
                    command.Parameters.AddWithValue(
                        "$areaPixels",
                        record.AreaPixels);
                    command.Parameters.AddWithValue(
                        "$physicalArea",
                        record.PhysicalAreaSquareMillimeters);
                    command.Parameters.AddWithValue(
                        "$centerXPixel",
                        record.CenterXPixel);
                    command.Parameters.AddWithValue(
                        "$centerYPixel",
                        record.CenterYPixel);
                    command.Parameters.AddWithValue(
                        "$centerXMillimeters",
                        record.CenterXMillimeters);
                    command.Parameters.AddWithValue(
                        "$centerYMillimeters",
                        record.CenterYMillimeters);
                    command.Parameters.AddWithValue(
                        "$widthPixels",
                        record.WidthPixels);
                    command.Parameters.AddWithValue(
                        "$heightPixels",
                        record.HeightPixels);
                    command.Parameters.AddWithValue(
                        "$widthMillimeters",
                        record.WidthMillimeters);
                    command.Parameters.AddWithValue(
                        "$heightMillimeters",
                        record.HeightMillimeters);
                    command.Parameters.AddWithValue(
                        "$processingTime",
                        record.ProcessingTimeMilliseconds);
                    command.Parameters.AddWithValue(
                        "$ngImagePath",
                        (object?)record.NgImagePath ?? DBNull.Value);
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

        public bool TryGetRecordCount(
            out long recordCount,
            out string? errorMessage)
        {
            try
            {
                lock (databaseSync)
                {
                    using SqliteConnection connection = OpenConnection();
                    using SqliteCommand command = connection.CreateCommand();
                    command.CommandText =
                        "SELECT COUNT(*) FROM inspection_records;";
                    recordCount = (long)(command.ExecuteScalar() ?? 0L);
                }

                errorMessage = null;
                return true;
            }
            catch (Exception ex)
            {
                recordCount = 0;
                errorMessage = ex.Message;
                return false;
            }
        }

        public bool TryQuery(
            DateTime? startUtc,
            DateTime? endExclusiveUtc,
            InspectionResultFilter resultFilter,
            string? batchNumber,
            int maximumRecords,
            out IReadOnlyList<InspectionHistoryRecord> records,
            out string? errorMessage)
        {
            return TryQueryPage(
                startUtc,
                endExclusiveUtc,
                resultFilter,
                batchNumber,
                1,
                maximumRecords,
                out records,
                out _,
                out errorMessage);
        }

        public bool TryQueryPage(
            DateTime? startUtc,
            DateTime? endExclusiveUtc,
            InspectionResultFilter resultFilter,
            string? batchNumber,
            int pageNumber,
            int pageSize,
            out IReadOnlyList<InspectionHistoryRecord> records,
            out long totalMatchingRecords,
            out string? errorMessage)
        {
            if (startUtc.HasValue && endExclusiveUtc.HasValue &&
                endExclusiveUtc.Value <= startUtc.Value)
            {
                throw new ArgumentException(
                    "查询结束时间必须晚于开始时间。");
            }

            if (pageNumber < 1)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(pageNumber),
                    "页码必须大于 0。");
            }

            if (pageSize < 1 || pageSize > 200)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(pageSize),
                    "每页数量必须在 1～200 之间。");
            }

            try
            {
                var result = new List<InspectionHistoryRecord>();

                lock (databaseSync)
                {
                    using SqliteConnection connection = OpenConnection();

                    using (SqliteCommand countCommand =
                        connection.CreateCommand())
                    {
                        countCommand.CommandText =
                            @"
                            SELECT COUNT(*)
                            FROM inspection_records
                            WHERE ($startUtc IS NULL OR detected_at_utc >= $startUtc)
                              AND ($endUtc IS NULL OR detected_at_utc < $endUtc)
                              AND ($isOk < 0 OR is_ok = $isOk)
                              AND ($batchNumber = '' OR batch_number = $batchNumber);
                            ";
                        AddQueryParameters(
                            countCommand,
                            startUtc,
                            endExclusiveUtc,
                            resultFilter,
                            batchNumber);
                        totalMatchingRecords =
                            (long)(countCommand.ExecuteScalar() ?? 0L);
                    }

                    using SqliteCommand command = connection.CreateCommand();
                    command.CommandText =
                        @"
                        SELECT
                            id,
                            detected_at_utc,
                            trigger_source,
                            cycle_id,
                            operation_mode,
                            is_ok,
                            judgement_code,
                            judgement_reason,
                            target_count,
                            raw_contour_count,
                            area_pixels,
                            physical_area_mm2,
                            center_x_pixel,
                            center_y_pixel,
                            center_x_mm,
                            center_y_mm,
                            width_pixels,
                            height_pixels,
                            width_mm,
                            height_mm,
                            processing_time_ms,
                            ng_image_path,
                            batch_number,
                            recipe_name,
                            recipe_revision
                        FROM inspection_records
                        WHERE ($startUtc IS NULL OR detected_at_utc >= $startUtc)
                          AND ($endUtc IS NULL OR detected_at_utc < $endUtc)
                          AND ($isOk < 0 OR is_ok = $isOk)
                          AND ($batchNumber = '' OR batch_number = $batchNumber)
                        ORDER BY detected_at_utc DESC, id DESC
                        LIMIT $pageSize OFFSET $offset;
                        ";
                    AddQueryParameters(
                        command,
                        startUtc,
                        endExclusiveUtc,
                        resultFilter,
                        batchNumber);
                    command.Parameters.AddWithValue(
                        "$pageSize",
                        pageSize);
                    command.Parameters.AddWithValue(
                        "$offset",
                        (long)(pageNumber - 1) * pageSize);

                    using SqliteDataReader reader = command.ExecuteReader();
                    while (reader.Read())
                    {
                        result.Add(ReadRecord(reader));
                    }
                }

                records = result;
                errorMessage = null;
                return true;
            }
            catch (Exception ex)
            {
                records = Array.Empty<InspectionHistoryRecord>();
                totalMatchingRecords = 0;
                errorMessage = ex.Message;
                return false;
            }
        }

        public bool TryGetSummary(
            DateTime? startUtc,
            DateTime? endExclusiveUtc,
            string? batchNumber,
            out InspectionHistorySummary summary,
            out string? errorMessage)
        {
            if (startUtc.HasValue && endExclusiveUtc.HasValue &&
                endExclusiveUtc.Value <= startUtc.Value)
            {
                throw new ArgumentException(
                    "统计结束时间必须晚于开始时间。");
            }

            try
            {
                lock (databaseSync)
                {
                    using SqliteConnection connection = OpenConnection();
                    using SqliteCommand command = connection.CreateCommand();
                    command.CommandText =
                        @"
                        SELECT
                            COUNT(*),
                            COALESCE(SUM(CASE WHEN is_ok = 1 THEN 1 ELSE 0 END), 0),
                            COALESCE(SUM(CASE WHEN is_ok = 0 THEN 1 ELSE 0 END), 0)
                        FROM inspection_records
                        WHERE ($startUtc IS NULL OR detected_at_utc >= $startUtc)
                          AND ($endUtc IS NULL OR detected_at_utc < $endUtc)
                          AND ($batchNumber = '' OR batch_number = $batchNumber);
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
                        "$batchNumber",
                        NormalizeBatchNumberFilter(batchNumber));

                    using SqliteDataReader reader = command.ExecuteReader();
                    if (!reader.Read())
                    {
                        throw new InvalidDataException(
                            "检测历史统计没有返回结果。");
                    }

                    summary = new InspectionHistorySummary
                    {
                        TotalCount = reader.GetInt64(0),
                        OkCount = reader.GetInt64(1),
                        NgCount = reader.GetInt64(2)
                    };
                }

                errorMessage = null;
                return true;
            }
            catch (Exception ex)
            {
                summary = new InspectionHistorySummary();
                errorMessage = ex.Message;
                return false;
            }
        }

        private static void AddQueryParameters(
            SqliteCommand command,
            DateTime? startUtc,
            DateTime? endExclusiveUtc,
            InspectionResultFilter resultFilter,
            string? batchNumber)
        {
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
                "$isOk",
                resultFilter switch
                {
                    InspectionResultFilter.Ok => 1,
                    InspectionResultFilter.Ng => 0,
                    _ => -1
                });
            command.Parameters.AddWithValue(
                "$batchNumber",
                NormalizeBatchNumberFilter(batchNumber));
        }

        private static InspectionHistoryRecord ReadRecord(
            SqliteDataReader reader)
        {
            return new InspectionHistoryRecord
            {
                Id = reader.GetInt64(0),
                DetectedAtUtc = DateTime.Parse(
                    reader.GetString(1),
                    null,
                    System.Globalization.DateTimeStyles.RoundtripKind),
                TriggerSource = reader.GetString(2),
                CycleId = reader.IsDBNull(3) ? null : reader.GetString(3),
                OperationMode = reader.GetString(4),
                IsOk = reader.GetInt64(5) == 1,
                JudgementCode = reader.GetString(6),
                JudgementReason = reader.GetString(7),
                TargetCount = reader.GetInt32(8),
                RawContourCount = reader.GetInt32(9),
                AreaPixels = reader.GetDouble(10),
                PhysicalAreaSquareMillimeters = reader.GetDouble(11),
                CenterXPixel = reader.GetInt32(12),
                CenterYPixel = reader.GetInt32(13),
                CenterXMillimeters = reader.GetDouble(14),
                CenterYMillimeters = reader.GetDouble(15),
                WidthPixels = reader.GetInt32(16),
                HeightPixels = reader.GetInt32(17),
                WidthMillimeters = reader.GetDouble(18),
                HeightMillimeters = reader.GetDouble(19),
                ProcessingTimeMilliseconds = reader.GetDouble(20),
                NgImagePath = reader.IsDBNull(21)
                    ? null
                    : reader.GetString(21),
                BatchNumber = reader.GetString(22),
                RecipeName = reader.GetString(23),
                RecipeRevision = reader.GetInt32(24)
            };
        }

        private static string NormalizeBatchNumberFilter(string? batchNumber)
        {
            return string.IsNullOrWhiteSpace(batchNumber)
                ? string.Empty
                : batchNumber.Trim();
        }

        private static void EnsureBatchNumberColumn(
            SqliteConnection connection)
        {
            EnsureColumn(
                connection,
                "batch_number",
                "TEXT NOT NULL DEFAULT ''");
        }

        private static void EnsureRecipeColumns(
            SqliteConnection connection)
        {
            EnsureColumn(
                connection,
                "recipe_name",
                "TEXT NOT NULL DEFAULT ''");
            EnsureColumn(
                connection,
                "recipe_revision",
                "INTEGER NOT NULL DEFAULT 0");
        }

        private static void EnsureColumn(
            SqliteConnection connection,
            string columnName,
            string columnDefinition)
        {
            bool columnExists = false;
            using (SqliteCommand checkCommand = connection.CreateCommand())
            {
                checkCommand.CommandText =
                    "PRAGMA table_info(inspection_records);";
                using SqliteDataReader reader = checkCommand.ExecuteReader();
                while (reader.Read())
                {
                    if (string.Equals(
                            reader.GetString(1),
                            columnName,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        columnExists = true;
                        break;
                    }
                }
            }

            if (columnExists)
            {
                return;
            }

            using SqliteCommand alterCommand = connection.CreateCommand();
            alterCommand.CommandText =
                $"ALTER TABLE inspection_records " +
                $"ADD COLUMN {columnName} {columnDefinition};";
            alterCommand.ExecuteNonQuery();
        }

        private SqliteConnection OpenConnection()
        {
            var connection = new SqliteConnection(connectionString);
            connection.Open();
            return connection;
        }
    }
}
