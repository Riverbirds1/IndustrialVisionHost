using System.Globalization;
using IndustrialVisionHost.Models;

namespace IndustrialVisionHost.ViewModels
{
    public sealed class VisionParametersViewModel : ViewModelBase
    {
        private string binaryThresholdText = "100";
        private string expectedTargetCountText = "1";
        private string minimumOkAreaText = "1000";
        private string maximumOkAreaText = "20000";
        private bool isRoiEnabled;
        private string roiXText = "80";
        private string roiYText = "60";
        private string roiWidthText = "480";
        private string roiHeightText = "360";
        private string gaussianKernelSizeText = "3";
        private string morphologyKernelSizeText = "3";
        private bool enableOpening = true;
        private bool enableClosing = true;
        private string minimumContourAreaText = "50";
        private string minimumCircularityText = "0.70";
        private string millimetersPerPixelText = "0.05";
        private string minimumWidthMillimetersText = "5.00";
        private string maximumWidthMillimetersText = "8.00";
        private string minimumHeightMillimetersText = "5.00";
        private string maximumHeightMillimetersText = "8.00";

        public string MillimetersPerPixelText
        {
            get => millimetersPerPixelText;
            set
            {
                if (SetProperty(ref millimetersPerPixelText, value))
                {
                    OnPropertyChanged(nameof(MillimetersPerPixelError));
                }
            }
        }

        public string MinimumWidthMillimetersText
        {
            get => minimumWidthMillimetersText;
            set
            {
                if (SetProperty(ref minimumWidthMillimetersText, value))
                {
                    OnPropertyChanged(nameof(MinimumWidthMillimetersError));
                    OnPropertyChanged(nameof(MaximumWidthMillimetersError));
                }
            }
        }

        public string MaximumWidthMillimetersText
        {
            get => maximumWidthMillimetersText;
            set
            {
                if (SetProperty(ref maximumWidthMillimetersText, value))
                {
                    OnPropertyChanged(nameof(MaximumWidthMillimetersError));
                }
            }
        }

        public string MinimumHeightMillimetersText
        {
            get => minimumHeightMillimetersText;
            set
            {
                if (SetProperty(ref minimumHeightMillimetersText, value))
                {
                    OnPropertyChanged(nameof(MinimumHeightMillimetersError));
                    OnPropertyChanged(nameof(MaximumHeightMillimetersError));
                }
            }
        }

        public string MaximumHeightMillimetersText
        {
            get => maximumHeightMillimetersText;
            set
            {
                if (SetProperty(ref maximumHeightMillimetersText, value))
                {
                    OnPropertyChanged(nameof(MaximumHeightMillimetersError));
                }
            }
        }

        public string BinaryThresholdText
        {
            get => binaryThresholdText;
            set
            {
                if (SetProperty(ref binaryThresholdText, value))
                {
                    OnPropertyChanged(nameof(BinaryThresholdError));
                }
            }
        }

        public string MinimumOkAreaText
        {
            get => minimumOkAreaText;
            set
            {
                if (SetProperty(ref minimumOkAreaText, value))
                {
                    OnPropertyChanged(nameof(MinimumOkAreaError));
                    OnPropertyChanged(nameof(MaximumOkAreaError));
                }
            }
        }

        public string ExpectedTargetCountText
        {
            get => expectedTargetCountText;
            set
            {
                if (SetProperty(ref expectedTargetCountText, value))
                {
                    OnPropertyChanged(nameof(ExpectedTargetCountError));
                }
            }
        }

        public string MaximumOkAreaText
        {
            get => maximumOkAreaText;
            set
            {
                if (SetProperty(ref maximumOkAreaText, value))
                {
                    OnPropertyChanged(nameof(MaximumOkAreaError));
                }
            }
        }

        public bool IsRoiEnabled
        {
            get => isRoiEnabled;
            set
            {
                if (SetProperty(ref isRoiEnabled, value))
                {
                    NotifyAllRoiErrorsChanged();
                }
            }
        }

        public string RoiXText
        {
            get => roiXText;
            set
            {
                if (SetProperty(ref roiXText, value))
                {
                    OnPropertyChanged(nameof(RoiXError));
                }
            }
        }

        public string RoiYText
        {
            get => roiYText;
            set
            {
                if (SetProperty(ref roiYText, value))
                {
                    OnPropertyChanged(nameof(RoiYError));
                }
            }
        }

        public string RoiWidthText
        {
            get => roiWidthText;
            set
            {
                if (SetProperty(ref roiWidthText, value))
                {
                    OnPropertyChanged(nameof(RoiWidthError));
                }
            }
        }

        public string RoiHeightText
        {
            get => roiHeightText;
            set
            {
                if (SetProperty(ref roiHeightText, value))
                {
                    OnPropertyChanged(nameof(RoiHeightError));
                }
            }
        }

        public string GaussianKernelSizeText
        {
            get => gaussianKernelSizeText;
            set
            {
                if (SetProperty(ref gaussianKernelSizeText, value))
                {
                    OnPropertyChanged(nameof(GaussianKernelSizeError));
                }
            }
        }

        public string MorphologyKernelSizeText
        {
            get => morphologyKernelSizeText;
            set
            {
                if (SetProperty(ref morphologyKernelSizeText, value))
                {
                    OnPropertyChanged(nameof(MorphologyKernelSizeError));
                }
            }
        }

        public bool EnableOpening
        {
            get => enableOpening;
            set => SetProperty(ref enableOpening, value);
        }

        public bool EnableClosing
        {
            get => enableClosing;
            set => SetProperty(ref enableClosing, value);
        }

        public string MinimumContourAreaText
        {
            get => minimumContourAreaText;
            set
            {
                if (SetProperty(ref minimumContourAreaText, value))
                {
                    OnPropertyChanged(nameof(MinimumContourAreaError));
                }
            }
        }

        public string MinimumCircularityText
        {
            get => minimumCircularityText;
            set
            {
                if (SetProperty(ref minimumCircularityText, value))
                {
                    OnPropertyChanged(nameof(MinimumCircularityError));
                }
            }
        }

        public string BinaryThresholdError =>
            ValidateIntegerRange(BinaryThresholdText, 0, 255, "阈值");

        public string MinimumOkAreaError
        {
            get
            {
                if (!double.TryParse(
                        MinimumOkAreaText,
                        NumberStyles.Float,
                        CultureInfo.CurrentCulture,
                        out double minimumArea))
                {
                    return "请输入有效的数字。";
                }

                return minimumArea <= 0
                    ? "最小合格面积必须大于 0。"
                    : string.Empty;
            }
        }

        public string ExpectedTargetCountError =>
            ValidatePositiveInteger(ExpectedTargetCountText, "期望目标数量");

        public string MaximumOkAreaError
        {
            get
            {
                if (!double.TryParse(
                        MaximumOkAreaText,
                        NumberStyles.Float,
                        CultureInfo.CurrentCulture,
                        out double maximumArea))
                {
                    return "请输入有效的最大合格面积。";
                }

                if (maximumArea <= 0)
                {
                    return "最大合格面积必须大于 0。";
                }

                if (double.TryParse(
                        MinimumOkAreaText,
                        NumberStyles.Float,
                        CultureInfo.CurrentCulture,
                        out double minimumArea) &&
                    maximumArea < minimumArea)
                {
                    return "最大合格面积不能小于最小合格面积。";
                }

                return string.Empty;
            }
        }

        public string RoiXError =>
            ValidateNonNegativeInteger(RoiXText, "ROI X");

        public string RoiYError =>
            ValidateNonNegativeInteger(RoiYText, "ROI Y");

        public string RoiWidthError =>
            ValidatePositiveInteger(RoiWidthText, "ROI 宽度");

        public string RoiHeightError =>
            ValidatePositiveInteger(RoiHeightText, "ROI 高度");

        public string GaussianKernelSizeError =>
            ValidateKernelSize(GaussianKernelSizeText, "高斯核");

        public string MorphologyKernelSizeError =>
            ValidateKernelSize(MorphologyKernelSizeText, "形态学核");

        public string MinimumContourAreaError =>
            ValidateNonNegativeDouble(MinimumContourAreaText, "最小轮廓面积");

        public string MinimumCircularityError => ValidateDoubleRange(
            MinimumCircularityText,
            0,
            1,
            "最小圆度");

        public string MillimetersPerPixelError => ValidatePositiveDouble(
            MillimetersPerPixelText,
            "像素当量");

        public string MinimumWidthMillimetersError =>
            ValidateNonNegativeDouble(
                MinimumWidthMillimetersText,
                "最小宽度");

        public string MaximumWidthMillimetersError => ValidateMaximumDouble(
            MinimumWidthMillimetersText,
            MaximumWidthMillimetersText,
            "宽度");

        public string MinimumHeightMillimetersError =>
            ValidateNonNegativeDouble(
                MinimumHeightMillimetersText,
                "最小高度");

        public string MaximumHeightMillimetersError => ValidateMaximumDouble(
            MinimumHeightMillimetersText,
            MaximumHeightMillimetersText,
            "高度");

        public bool TryCreateParameters(
            out VisionParameters? parameters,
            out string? errorMessage)
        {
            string[] errors =
            {
                MillimetersPerPixelError,
                MinimumWidthMillimetersError,
                MaximumWidthMillimetersError,
                MinimumHeightMillimetersError,
                MaximumHeightMillimetersError,
                BinaryThresholdError,
                ExpectedTargetCountError,
                MinimumOkAreaError,
                MaximumOkAreaError,
                RoiXError,
                RoiYError,
                RoiWidthError,
                RoiHeightError,
                GaussianKernelSizeError,
                MorphologyKernelSizeError,
                MinimumContourAreaError,
                MinimumCircularityError
            };

            foreach (string error in errors)
            {
                if (!string.IsNullOrEmpty(error))
                {
                    parameters = null;
                    errorMessage = error;
                    return false;
                }
            }

            int threshold = int.Parse(
                BinaryThresholdText,
                CultureInfo.CurrentCulture);
            int expectedTargetCount = int.Parse(
                ExpectedTargetCountText,
                CultureInfo.CurrentCulture);
            double minimumArea = double.Parse(
                MinimumOkAreaText,
                CultureInfo.CurrentCulture);
            double maximumArea = double.Parse(
                MaximumOkAreaText,
                CultureInfo.CurrentCulture);

            int roiX = int.Parse(RoiXText, CultureInfo.CurrentCulture);
            int roiY = int.Parse(RoiYText, CultureInfo.CurrentCulture);
            int roiWidth = int.Parse(RoiWidthText, CultureInfo.CurrentCulture);
            int roiHeight = int.Parse(RoiHeightText, CultureInfo.CurrentCulture);
            int gaussianKernelSize = int.Parse(
                GaussianKernelSizeText,
                CultureInfo.CurrentCulture);
            int morphologyKernelSize = int.Parse(
                MorphologyKernelSizeText,
                CultureInfo.CurrentCulture);
            double minimumContourArea = double.Parse(
                MinimumContourAreaText,
                CultureInfo.CurrentCulture);
            double minimumCircularity = double.Parse(
                MinimumCircularityText,
                CultureInfo.CurrentCulture);
            double millimetersPerPixel = double.Parse(
                MillimetersPerPixelText,
                CultureInfo.CurrentCulture);
            double minimumWidthMillimeters = double.Parse(
                MinimumWidthMillimetersText,
                CultureInfo.CurrentCulture);
            double maximumWidthMillimeters = double.Parse(
                MaximumWidthMillimetersText,
                CultureInfo.CurrentCulture);
            double minimumHeightMillimeters = double.Parse(
                MinimumHeightMillimetersText,
                CultureInfo.CurrentCulture);
            double maximumHeightMillimeters = double.Parse(
                MaximumHeightMillimetersText,
                CultureInfo.CurrentCulture);

            parameters = new VisionParameters(
                threshold,
                expectedTargetCount,
                minimumArea,
                maximumArea,
                IsRoiEnabled,
                roiX,
                roiY,
                roiWidth,
                roiHeight,
                gaussianKernelSize,
                morphologyKernelSize,
                EnableOpening,
                EnableClosing,
                minimumContourArea,
                minimumCircularity,
                millimetersPerPixel,
                minimumWidthMillimeters,
                maximumWidthMillimeters,
                minimumHeightMillimeters,
                maximumHeightMillimeters);
            errorMessage = null;
            return true;
        }

        public void ApplyParameters(VisionParameters parameters)
        {
            MillimetersPerPixelText = parameters.MillimetersPerPixel.ToString(
                CultureInfo.CurrentCulture);
            MinimumWidthMillimetersText =
                parameters.MinimumWidthMillimeters.ToString(
                    CultureInfo.CurrentCulture);
            MaximumWidthMillimetersText =
                parameters.MaximumWidthMillimeters.ToString(
                    CultureInfo.CurrentCulture);
            MinimumHeightMillimetersText =
                parameters.MinimumHeightMillimeters.ToString(
                    CultureInfo.CurrentCulture);
            MaximumHeightMillimetersText =
                parameters.MaximumHeightMillimeters.ToString(
                    CultureInfo.CurrentCulture);
            BinaryThresholdText = parameters.BinaryThreshold.ToString(
                CultureInfo.CurrentCulture);
            ExpectedTargetCountText = parameters.ExpectedTargetCount.ToString(
                CultureInfo.CurrentCulture);
            MinimumOkAreaText = parameters.MinimumOkArea.ToString(
                CultureInfo.CurrentCulture);
            MaximumOkAreaText = parameters.MaximumOkArea.ToString(
                CultureInfo.CurrentCulture);
            RoiXText = parameters.RoiX.ToString(CultureInfo.CurrentCulture);
            RoiYText = parameters.RoiY.ToString(CultureInfo.CurrentCulture);
            RoiWidthText = parameters.RoiWidth.ToString(
                CultureInfo.CurrentCulture);
            RoiHeightText = parameters.RoiHeight.ToString(
                CultureInfo.CurrentCulture);
            GaussianKernelSizeText = parameters.GaussianKernelSize.ToString(
                CultureInfo.CurrentCulture);
            MorphologyKernelSizeText = parameters.MorphologyKernelSize.ToString(
                CultureInfo.CurrentCulture);
            EnableOpening = parameters.EnableOpening;
            EnableClosing = parameters.EnableClosing;
            MinimumContourAreaText = parameters.MinimumContourArea.ToString(
                CultureInfo.CurrentCulture);
            MinimumCircularityText = parameters.MinimumCircularity.ToString(
                CultureInfo.CurrentCulture);
            IsRoiEnabled = parameters.IsRoiEnabled;
        }

        private static string ValidateIntegerRange(
            string text,
            int minimum,
            int maximum,
            string parameterName)
        {
            if (!int.TryParse(text, out int value))
            {
                return $"{parameterName} 必须是整数。";
            }

            return value < minimum || value > maximum
                ? $"{parameterName} 范围必须是 {minimum}～{maximum}。"
                : string.Empty;
        }

        private static string ValidateNonNegativeInteger(
            string text,
            string parameterName)
        {
            if (!int.TryParse(text, out int value))
            {
                return $"{parameterName} 必须是整数。";
            }

            return value < 0
                ? $"{parameterName} 不能为负数。"
                : string.Empty;
        }

        private static string ValidatePositiveInteger(
            string text,
            string parameterName)
        {
            if (!int.TryParse(text, out int value))
            {
                return $"{parameterName} 必须是整数。";
            }

            return value <= 0
                ? $"{parameterName} 必须大于 0。"
                : string.Empty;
        }

        private static string ValidateKernelSize(
            string text,
            string parameterName)
        {
            if (!int.TryParse(text, out int kernelSize))
            {
                return $"{parameterName}必须是整数。";
            }

            return kernelSize < 1 || kernelSize > 31 || kernelSize % 2 == 0
                ? $"{parameterName}必须是 1～31 的奇数。"
                : string.Empty;
        }

        private static string ValidateDoubleRange(
            string text,
            double minimum,
            double maximum,
            string parameterName)
        {
            if (!double.TryParse(
                    text,
                    NumberStyles.Float,
                    CultureInfo.CurrentCulture,
                    out double value))
            {
                return $"{parameterName}必须是数字。";
            }

            return value < minimum || value > maximum
                ? $"{parameterName}范围必须是 {minimum}～{maximum}。"
                : string.Empty;
        }

        private static string ValidateNonNegativeDouble(
            string text,
            string parameterName)
        {
            if (!double.TryParse(
                    text,
                    NumberStyles.Float,
                    CultureInfo.CurrentCulture,
                    out double value))
            {
                return $"{parameterName}必须是数字。";
            }

            return value < 0
                ? $"{parameterName}不能为负数。"
                : string.Empty;
        }

        private static string ValidatePositiveDouble(
            string text,
            string parameterName)
        {
            if (!double.TryParse(
                    text,
                    NumberStyles.Float,
                    CultureInfo.CurrentCulture,
                    out double value) ||
                double.IsNaN(value) ||
                double.IsInfinity(value))
            {
                return $"{parameterName}必须是有限数值。";
            }

            return value <= 0
                ? $"{parameterName}必须大于 0。"
                : string.Empty;
        }

        private static string ValidateMaximumDouble(
            string minimumText,
            string maximumText,
            string measurementName)
        {
            if (!double.TryParse(
                    maximumText,
                    NumberStyles.Float,
                    CultureInfo.CurrentCulture,
                    out double maximum) ||
                double.IsNaN(maximum) ||
                double.IsInfinity(maximum))
            {
                return $"最大{measurementName}必须是有限数值。";
            }

            if (maximum < 0)
            {
                return $"最大{measurementName}不能为负数。";
            }

            if (double.TryParse(
                    minimumText,
                    NumberStyles.Float,
                    CultureInfo.CurrentCulture,
                    out double minimum) &&
                maximum < minimum)
            {
                return $"最大{measurementName}不能小于最小{measurementName}。";
            }

            return string.Empty;
        }

        private void NotifyAllRoiErrorsChanged()
        {
            OnPropertyChanged(nameof(RoiXError));
            OnPropertyChanged(nameof(RoiYError));
            OnPropertyChanged(nameof(RoiWidthError));
            OnPropertyChanged(nameof(RoiHeightError));
        }
    }
}
