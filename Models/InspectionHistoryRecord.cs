using System;

namespace IndustrialVisionHost.Models
{
    public sealed class InspectionHistoryRecord
    {
        public long Id { get; init; }

        public string BatchNumber { get; init; } = string.Empty;

        public string BatchNumberText => string.IsNullOrWhiteSpace(BatchNumber)
            ? "历史数据（未设置）"
            : BatchNumber;

        public string RecipeName { get; init; } = string.Empty;

        public int RecipeRevision { get; init; }

        public string RecipeNameText => string.IsNullOrWhiteSpace(RecipeName)
            ? "历史数据（未记录）"
            : RecipeName;

        public string RecipeRevisionText => RecipeRevision > 0
            ? $"V{RecipeRevision}"
            : "未保存参数";

        public DateTime DetectedAtUtc { get; init; }

        public string DetectedAtLocalText =>
            DetectedAtUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss.fff");

        public string TriggerSource { get; init; } = string.Empty;

        public string TriggerSourceText => TriggerSource switch
        {
            "ManualButton" => "手动按钮",
            "TextPlc" => "文本 PLC",
            "Modbus" => "Modbus",
            _ => TriggerSource
        };

        public string? CycleId { get; init; }

        public string OperationMode { get; init; } = string.Empty;

        public string OperationModeText => OperationMode switch
        {
            "Manual" => "手动",
            "Automatic" => "自动",
            _ => OperationMode
        };

        public bool IsOk { get; init; }

        public string ResultText => IsOk ? "OK" : "NG";

        public string JudgementCode { get; init; } = string.Empty;

        public string JudgementReason { get; init; } = string.Empty;

        public int TargetCount { get; init; }

        public int RawContourCount { get; init; }

        public double AreaPixels { get; init; }

        public double PhysicalAreaSquareMillimeters { get; init; }

        public int CenterXPixel { get; init; }

        public int CenterYPixel { get; init; }

        public string PixelCenterText =>
            $"({CenterXPixel},{CenterYPixel})";

        public double CenterXMillimeters { get; init; }

        public double CenterYMillimeters { get; init; }

        public string PhysicalCenterText =>
            $"({CenterXMillimeters:F2},{CenterYMillimeters:F2})";

        public int WidthPixels { get; init; }

        public int HeightPixels { get; init; }

        public string PixelSizeText =>
            $"{WidthPixels}×{HeightPixels}";

        public double WidthMillimeters { get; init; }

        public double HeightMillimeters { get; init; }

        public string PhysicalSizeText =>
            $"{WidthMillimeters:F2}×{HeightMillimeters:F2}";

        public double ProcessingTimeMilliseconds { get; init; }

        public string? NgImagePath { get; init; }
    }

    public enum InspectionResultFilter
    {
        All,
        Ok,
        Ng
    }

    public sealed class InspectionHistorySummary
    {
        public long TotalCount { get; init; }

        public long OkCount { get; init; }

        public long NgCount { get; init; }

        public double PassRate => TotalCount == 0
            ? 0
            : OkCount * 100.0 / TotalCount;
    }
}
