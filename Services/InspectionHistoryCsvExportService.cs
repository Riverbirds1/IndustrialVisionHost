using System;
using System.Globalization;
using System.IO;
using System.Text;
using IndustrialVisionHost.Models;
using Microsoft.Data.Sqlite;

namespace IndustrialVisionHost.Services
{
    public sealed class InspectionHistoryCsvExportService
    {
        private readonly string connectionString;

        public InspectionHistoryCsvExportService(string databasePath)
        {
            if (string.IsNullOrWhiteSpace(databasePath))
            {
                throw new ArgumentException(
                    "数据库路径不能为空。",
                    nameof(databasePath));
            }

            connectionString = new SqliteConnectionStringBuilder
            {
                DataSource = databasePath,
                Mode = SqliteOpenMode.ReadOnly,
                Cache = SqliteCacheMode.Shared
            }.ToString();
        }

        public bool TryExport(
            string outputPath,
            DateTime startUtc,
            DateTime endExclusiveUtc,
            InspectionResultFilter resultFilter,
            string? batchNumber,
            out long exportedCount,
            out string? errorMessage)
        {
            if (string.IsNullOrWhiteSpace(outputPath))
            {
                throw new ArgumentException(
                    "CSV 输出路径不能为空。",
                    nameof(outputPath));
            }

            if (endExclusiveUtc <= startUtc)
            {
                throw new ArgumentException(
                    "CSV 导出结束时间必须晚于开始时间。");
            }

            string fullOutputPath = Path.GetFullPath(outputPath);
            string temporaryPath =
                fullOutputPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            exportedCount = 0;

            try
            {
                string? directory = Path.GetDirectoryName(fullOutputPath);
                if (!string.IsNullOrWhiteSpace(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                using var connection = new SqliteConnection(connectionString);
                connection.Open();
                using SqliteTransaction transaction = connection.BeginTransaction();
                using SqliteCommand command = connection.CreateCommand();
                command.Transaction = transaction;
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
                    WHERE detected_at_utc >= $startUtc
                      AND detected_at_utc < $endUtc
                      AND ($isOk < 0 OR is_ok = $isOk)
                      AND ($batchNumber = '' OR batch_number = $batchNumber)
                    ORDER BY detected_at_utc DESC, id DESC;
                    ";
                command.Parameters.AddWithValue(
                    "$startUtc",
                    startUtc.ToString("O"));
                command.Parameters.AddWithValue(
                    "$endUtc",
                    endExclusiveUtc.ToString("O"));
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
                    string.IsNullOrWhiteSpace(batchNumber)
                        ? string.Empty
                        : batchNumber.Trim());

                using SqliteDataReader reader = command.ExecuteReader();
                using var writer = new StreamWriter(
                    temporaryPath,
                    false,
                    new UTF8Encoding(true));
                writer.WriteLine(
                    "ID,批次号,配方名称,配方版本,检测时间,结果,触发来源,运行模式,周期号," +
                    "判定代码,判定原因,目标数量,原始轮廓数," +
                    "像素面积,物理面积(mm²),中心X(px),中心Y(px)," +
                    "中心X(mm),中心Y(mm),宽度(px),高度(px)," +
                    "宽度(mm),高度(mm),处理耗时(ms),NG图片路径");

                while (reader.Read())
                {
                    string detectedAt = DateTime.Parse(
                            reader.GetString(1),
                            null,
                            DateTimeStyles.RoundtripKind)
                        .ToLocalTime()
                        .ToString("yyyy-MM-dd HH:mm:ss.fff");
                    bool isOk = reader.GetInt64(5) == 1;
                    string[] values =
                    {
                        reader.GetInt64(0).ToString(CultureInfo.InvariantCulture),
                        reader.GetString(22),
                        reader.GetString(23),
                        reader.GetInt32(24) > 0
                            ? $"V{reader.GetInt32(24)}"
                            : "未保存参数",
                        detectedAt,
                        isOk ? "OK" : "NG",
                        GetTriggerSourceText(reader.GetString(2)),
                        GetOperationModeText(reader.GetString(4)),
                        GetNullableString(reader, 3),
                        reader.GetString(6),
                        reader.GetString(7),
                        reader.GetInt32(8).ToString(CultureInfo.InvariantCulture),
                        reader.GetInt32(9).ToString(CultureInfo.InvariantCulture),
                        reader.GetDouble(10).ToString("F2", CultureInfo.InvariantCulture),
                        reader.GetDouble(11).ToString("F4", CultureInfo.InvariantCulture),
                        reader.GetInt32(12).ToString(CultureInfo.InvariantCulture),
                        reader.GetInt32(13).ToString(CultureInfo.InvariantCulture),
                        reader.GetDouble(14).ToString("F4", CultureInfo.InvariantCulture),
                        reader.GetDouble(15).ToString("F4", CultureInfo.InvariantCulture),
                        reader.GetInt32(16).ToString(CultureInfo.InvariantCulture),
                        reader.GetInt32(17).ToString(CultureInfo.InvariantCulture),
                        reader.GetDouble(18).ToString("F4", CultureInfo.InvariantCulture),
                        reader.GetDouble(19).ToString("F4", CultureInfo.InvariantCulture),
                        reader.GetDouble(20).ToString("F3", CultureInfo.InvariantCulture),
                        GetNullableString(reader, 21)
                    };
                    writer.WriteLine(string.Join(",", Array.ConvertAll(
                        values,
                        EscapeCsvField)));
                    exportedCount++;
                }

                writer.Flush();
                writer.Dispose();
                File.Move(temporaryPath, fullOutputPath, true);
                errorMessage = null;
                return true;
            }
            catch (Exception ex)
            {
                TryDeleteTemporaryFile(temporaryPath);
                exportedCount = 0;
                errorMessage = ex.Message;
                return false;
            }
        }

        private static string EscapeCsvField(string value)
        {
            if (value.IndexOfAny(new[] { ',', '"', '\r', '\n' }) < 0)
            {
                return value;
            }

            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }

        private static string GetNullableString(
            SqliteDataReader reader,
            int ordinal)
        {
            return reader.IsDBNull(ordinal) ? string.Empty : reader.GetString(ordinal);
        }

        private static string GetTriggerSourceText(string source)
        {
            return source switch
            {
                "ManualButton" => "手动按钮",
                "TextPlc" => "文本 PLC",
                "Modbus" => "Modbus",
                _ => source
            };
        }

        private static string GetOperationModeText(string mode)
        {
            return mode switch
            {
                "Manual" => "手动",
                "Automatic" => "自动",
                _ => mode
            };
        }

        private static void TryDeleteTemporaryFile(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch
            {
            }
        }
    }
}
