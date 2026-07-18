using System;

namespace IndustrialVisionHost.Models
{
    public sealed class VisionParameters
    {
        public VisionParameters(
            int binaryThreshold,
            int expectedTargetCount,
            double minimumOkArea,
            double maximumOkArea,
            bool isRoiEnabled,
            int roiX,
            int roiY,
            int roiWidth,
            int roiHeight,
            int gaussianKernelSize,
            int morphologyKernelSize,
            bool enableOpening,
            bool enableClosing,
            double minimumContourArea,
            double minimumCircularity,
            double millimetersPerPixel = 0.05,
            double minimumWidthMillimeters = 0,
            double maximumWidthMillimeters = 1000000,
            double minimumHeightMillimeters = 0,
            double maximumHeightMillimeters = 1000000)
        {
            if (binaryThreshold < 0 || binaryThreshold > 255)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(binaryThreshold),
                    "二值化阈值必须在 0 到 255 之间。");
            }

            if (minimumOkArea <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(minimumOkArea),
                    "最小合格面积必须大于 0。");
            }

            if (expectedTargetCount <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(expectedTargetCount),
                    "期望目标数量必须大于 0。");
            }

            if (maximumOkArea < minimumOkArea)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(maximumOkArea),
                    "最大合格面积不能小于最小合格面积。");
            }

            if (isRoiEnabled &&
                (roiX < 0 || roiY < 0 || roiWidth <= 0 || roiHeight <= 0))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(roiX),
                    "ROI 坐标不能为负数，宽度和高度必须大于 0。");
            }

            ValidateKernelSize(gaussianKernelSize, nameof(gaussianKernelSize));
            ValidateKernelSize(morphologyKernelSize, nameof(morphologyKernelSize));

            if (minimumContourArea < 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(minimumContourArea),
                    "最小轮廓面积不能为负数。");
            }

            if (minimumCircularity < 0 || minimumCircularity > 1)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(minimumCircularity),
                    "最小圆度必须在 0 到 1 之间。");
            }

            if (double.IsNaN(millimetersPerPixel) ||
                double.IsInfinity(millimetersPerPixel) ||
                millimetersPerPixel <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(millimetersPerPixel),
                    "像素当量必须是大于 0 的有限数值。");
            }

            ValidatePhysicalRange(
                minimumWidthMillimeters,
                maximumWidthMillimeters,
                "宽度");
            ValidatePhysicalRange(
                minimumHeightMillimeters,
                maximumHeightMillimeters,
                "高度");

            BinaryThreshold = binaryThreshold;
            ExpectedTargetCount = expectedTargetCount;
            MinimumOkArea = minimumOkArea;
            MaximumOkArea = maximumOkArea;
            IsRoiEnabled = isRoiEnabled;
            RoiX = roiX;
            RoiY = roiY;
            RoiWidth = roiWidth;
            RoiHeight = roiHeight;
            GaussianKernelSize = gaussianKernelSize;
            MorphologyKernelSize = morphologyKernelSize;
            EnableOpening = enableOpening;
            EnableClosing = enableClosing;
            MinimumContourArea = minimumContourArea;
            MinimumCircularity = minimumCircularity;
            MillimetersPerPixel = millimetersPerPixel;
            MinimumWidthMillimeters = minimumWidthMillimeters;
            MaximumWidthMillimeters = maximumWidthMillimeters;
            MinimumHeightMillimeters = minimumHeightMillimeters;
            MaximumHeightMillimeters = maximumHeightMillimeters;
        }

        public int BinaryThreshold { get; }

        public int ExpectedTargetCount { get; }

        public double MinimumOkArea { get; }

        public double MaximumOkArea { get; }

        public bool IsRoiEnabled { get; }

        public int RoiX { get; }

        public int RoiY { get; }

        public int RoiWidth { get; }

        public int RoiHeight { get; }

        public int GaussianKernelSize { get; }

        public int MorphologyKernelSize { get; }

        public bool EnableOpening { get; }

        public bool EnableClosing { get; }

        public double MinimumContourArea { get; }

        public double MinimumCircularity { get; }

        public double MillimetersPerPixel { get; }

        public double MinimumWidthMillimeters { get; }

        public double MaximumWidthMillimeters { get; }

        public double MinimumHeightMillimeters { get; }

        public double MaximumHeightMillimeters { get; }

        private static void ValidateKernelSize(int kernelSize, string parameterName)
        {
            if (kernelSize < 1 || kernelSize > 31 || kernelSize % 2 == 0)
            {
                throw new ArgumentOutOfRangeException(
                    parameterName,
                    "处理核尺寸必须是 1～31 的奇数。");
            }
        }

        private static void ValidatePhysicalRange(
            double minimum,
            double maximum,
            string measurementName)
        {
            if (double.IsNaN(minimum) ||
                double.IsInfinity(minimum) ||
                minimum < 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(minimum),
                    $"最小{measurementName}必须是大于或等于 0 的有限数值。");
            }

            if (double.IsNaN(maximum) ||
                double.IsInfinity(maximum) ||
                maximum < minimum)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(maximum),
                    $"最大{measurementName}必须是有限数值，且不能小于最小值。");
            }
        }
    }
}
