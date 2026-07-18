using IndustrialVisionHost.Models;
using IndustrialVisionHost.Services;
using Microsoft.Data.Sqlite;

namespace IndustrialVisionHost.Tests;

public sealed class InspectionHistoryIntegrationTests
{
    [Fact]
    public void SaveQueryPageAndSummary_WorkAgainstRealSqliteDatabase()
    {
        using var directory = new TemporaryDirectory();
        var service = new InspectionHistoryService(directory.File("history.db"));
        Assert.True(service.TryInitialize(out string? error), error);
        DateTime start = new DateTime(2026, 7, 18, 1, 0, 0, DateTimeKind.Utc);

        for (int index = 0; index < 6; index++)
        {
            bool isOk = index % 2 == 0;
            string batch = index < 4 ? "BATCH-A" : "BATCH-B";
            Assert.True(service.TrySave(
                CreateRecord(start.AddMinutes(index), batch, isOk, index),
                out error), error);
        }

        Assert.True(service.TryQueryPage(
            start,
            start.AddHours(1),
            InspectionResultFilter.All,
            string.Empty,
            2,
            2,
            out IReadOnlyList<InspectionHistoryRecord> secondPage,
            out long total,
            out error), error);
        Assert.Equal(6, total);
        Assert.Equal(2, secondPage.Count);
        Assert.Equal("C000004", secondPage[0].CycleId);
        Assert.Equal("C000003", secondPage[1].CycleId);

        Assert.True(service.TryQuery(
            start,
            start.AddHours(1),
            InspectionResultFilter.Ng,
            "BATCH-A",
            20,
            out IReadOnlyList<InspectionHistoryRecord> batchNg,
            out error), error);
        Assert.Equal(2, batchNg.Count);
        Assert.All(batchNg, record =>
        {
            Assert.Equal("BATCH-A", record.BatchNumber);
            Assert.False(record.IsOk);
            Assert.Equal("标准配方", record.RecipeName);
            Assert.Equal(3, record.RecipeRevision);
        });

        Assert.True(service.TryGetSummary(
            start,
            start.AddHours(1),
            "BATCH-A",
            out InspectionHistorySummary summary,
            out error), error);
        Assert.Equal(4, summary.TotalCount);
        Assert.Equal(2, summary.OkCount);
        Assert.Equal(2, summary.NgCount);
        Assert.Equal(50, summary.PassRate);
    }

    [Fact]
    public void Initialize_MigratesLegacyDatabaseWithoutLosingRows()
    {
        using var directory = new TemporaryDirectory();
        string databasePath = directory.File("legacy.db");
        CreateLegacyDatabase(databasePath);
        var service = new InspectionHistoryService(databasePath);

        Assert.True(service.TryInitialize(out string? error), error);
        Assert.True(service.TryQuery(
            null,
            null,
            InspectionResultFilter.All,
            null,
            10,
            out IReadOnlyList<InspectionHistoryRecord> records,
            out error), error);

        InspectionHistoryRecord migrated = Assert.Single(records);
        Assert.Equal("", migrated.BatchNumber);
        Assert.Equal("", migrated.RecipeName);
        Assert.Equal(0, migrated.RecipeRevision);
        Assert.Equal("LEGACY-1", migrated.CycleId);

        using var connection = new SqliteConnection($"Data Source={databasePath}");
        connection.Open();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "PRAGMA user_version;";
        Assert.Equal(3L, (long)(command.ExecuteScalar() ?? 0L));
    }

    private static InspectionHistoryRecord CreateRecord(
        DateTime detectedAtUtc,
        string batch,
        bool isOk,
        int index)
    {
        return new InspectionHistoryRecord
        {
            BatchNumber = batch,
            RecipeName = "标准配方",
            RecipeRevision = 3,
            DetectedAtUtc = detectedAtUtc,
            TriggerSource = index % 2 == 0 ? "TextPlc" : "Modbus",
            CycleId = $"C{index + 1:D6}",
            OperationMode = "Automatic",
            IsOk = isOk,
            JudgementCode = isOk ? "OK" : "AREA_LOW",
            JudgementReason = isOk ? "合格" : "面积过小",
            TargetCount = 1,
            RawContourCount = 1,
            AreaPixels = 12000 + index,
            PhysicalAreaSquareMillimeters = 30 + index,
            CenterXPixel = 320,
            CenterYPixel = 240,
            CenterXMillimeters = 16,
            CenterYMillimeters = 12,
            WidthPixels = 120,
            HeightPixels = 120,
            WidthMillimeters = 6,
            HeightMillimeters = 6,
            ProcessingTimeMilliseconds = 1.2 + index,
            NgImagePath = isOk ? null : $"ng-{index}.jpg"
        };
    }

    private static void CreateLegacyDatabase(string databasePath)
    {
        using var connection = new SqliteConnection($"Data Source={databasePath}");
        connection.Open();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            @"
            CREATE TABLE inspection_records
            (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
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

            INSERT INTO inspection_records
            (detected_at_utc, trigger_source, cycle_id, operation_mode,
             is_ok, judgement_code, judgement_reason, target_count,
             raw_contour_count, area_pixels, physical_area_mm2,
             center_x_pixel, center_y_pixel, center_x_mm, center_y_mm,
             width_pixels, height_pixels, width_mm, height_mm,
             processing_time_ms, ng_image_path)
            VALUES
            ('2026-07-18T01:00:00.0000000Z', 'ManualButton', 'LEGACY-1',
             'Manual', 1, 'OK', '旧记录', 1, 1, 10000, 25,
             320, 240, 16, 12, 120, 120, 6, 6, 1.5, NULL);
            PRAGMA user_version = 1;
            ";
        command.ExecuteNonQuery();
    }
}
