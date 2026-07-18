using System;
using System.Collections.Generic;
using System.Diagnostics;
using IndustrialVisionHost.Models;
using OpenCvSharp;

namespace IndustrialVisionHost.Vision
{
    public static class VisionProcessor
    {
        public static VisionProcessingResult Process(
            Mat image,
            VisionParameters parameters,
            VisionDebugView debugView)
        {
            if (image is null)
            {
                throw new ArgumentNullException(nameof(image));
            }

            if (image.Empty())
            {
                throw new ArgumentException("待检测图像不能为空。", nameof(image));
            }

            if (parameters is null)
            {
                throw new ArgumentNullException(nameof(parameters));
            }

            Stopwatch stopwatch = Stopwatch.StartNew();
            Mat annotatedImage = image.Clone();
            Mat? debugImage = null;

            try
            {
                var inspection = Analyze(
                    image,
                    annotatedImage,
                    parameters,
                    debugView,
                    ref debugImage);
                stopwatch.Stop();
                inspection.ProcessingTimeMs = stopwatch.Elapsed.TotalMilliseconds;
                DrawProcessingTime(annotatedImage, inspection.ProcessingTimeMs);

                if (debugImage is null)
                {
                    return new VisionProcessingResult(
                        inspection,
                        annotatedImage,
                        annotatedImage);
                }

                return new VisionProcessingResult(
                    inspection,
                    annotatedImage,
                    debugImage);
            }
            catch
            {
                annotatedImage.Dispose();
                debugImage?.Dispose();
                throw;
            }
        }

        private static InspectionResult Analyze(
            Mat image,
            Mat annotatedImage,
            VisionParameters parameters,
            VisionDebugView debugView,
            ref Mat? debugImage)
        {
            using Mat gray = new Mat();
            using Mat binary = new Mat();
            using Mat binaryBeforeMorphology = new Mat();

            Rect processingRegion = CreateProcessingRegion(image, parameters);
            using Mat processingImage = new Mat(image, processingRegion);

            if (parameters.IsRoiEnabled)
            {
                Cv2.Rectangle(
                    annotatedImage,
                    processingRegion,
                    new Scalar(255, 255, 0),
                    2);
                Cv2.PutText(
                    annotatedImage,
                    "ROI",
                    new Point(processingRegion.X, Math.Max(20, processingRegion.Y - 8)),
                    HersheyFonts.HersheySimplex,
                    0.6,
                    new Scalar(255, 255, 0),
                    2);
            }

            Cv2.CvtColor(processingImage, gray, ColorConversionCodes.BGR2GRAY);

            if (parameters.GaussianKernelSize > 1)
            {
                Cv2.GaussianBlur(
                    gray,
                    gray,
                    new Size(
                        parameters.GaussianKernelSize,
                        parameters.GaussianKernelSize),
                    0);
            }

            Cv2.Threshold(
                gray,
                binary,
                parameters.BinaryThreshold,
                255,
                ThresholdTypes.BinaryInv);

            if (debugView == VisionDebugView.Binary)
            {
                binary.CopyTo(binaryBeforeMorphology);
            }

            if (parameters.MorphologyKernelSize > 1 &&
                (parameters.EnableOpening || parameters.EnableClosing))
            {
                using Mat morphologyKernel = Cv2.GetStructuringElement(
                    MorphShapes.Ellipse,
                    new Size(
                        parameters.MorphologyKernelSize,
                        parameters.MorphologyKernelSize));

                if (parameters.EnableOpening)
                {
                    Cv2.MorphologyEx(
                        binary,
                        binary,
                        MorphTypes.Open,
                        morphologyKernel);
                }

                if (parameters.EnableClosing)
                {
                    Cv2.MorphologyEx(
                        binary,
                        binary,
                        MorphTypes.Close,
                        morphologyKernel);
                }
            }

            debugImage = debugView switch
            {
                VisionDebugView.Gray => CreateFullSizeDebugImage(
                    gray,
                    image.Size(),
                    processingRegion),
                VisionDebugView.Binary => CreateFullSizeDebugImage(
                    binaryBeforeMorphology,
                    image.Size(),
                    processingRegion),
                VisionDebugView.Morphology => CreateFullSizeDebugImage(
                    binary,
                    image.Size(),
                    processingRegion),
                _ => null
            };

            Cv2.FindContours(
                binary,
                out Point[][] localContours,
                out _,
                RetrievalModes.External,
                ContourApproximationModes.ApproxSimple);

            Point[][] contours = TranslateContoursToImageCoordinates(
                localContours,
                processingRegion.X,
                processingRegion.Y);

            Point[][] validContours = FilterValidContours(contours, parameters);

            var result = new InspectionResult
            {
                RawContourCount = contours.Length,
                Count = validContours.Length
            };

            ApplyJudgement(result, validContours, parameters);

            if (validContours.Length == 0)
            {
                DrawRawContours(annotatedImage, contours);
                DrawSummary(annotatedImage, result);
                return result;
            }

            int maximumContourIndex = FindMaximumContourIndex(
                validContours,
                out double maximumArea);
            if (maximumContourIndex < 0 || maximumArea <= 0)
            {
                DrawRawContours(annotatedImage, contours);
                DrawSummary(annotatedImage, result);
                return result;
            }

            result.Area = maximumArea;
            Rect boundingRectangle = Cv2.BoundingRect(
                validContours[maximumContourIndex]);
            result.WidthPixels = boundingRectangle.Width;
            result.HeightPixels = boundingRectangle.Height;
            Moments moments = Cv2.Moments(validContours[maximumContourIndex]);

            if (Math.Abs(moments.M00) > double.Epsilon)
            {
                result.CenterX = (int)(moments.M10 / moments.M00);
                result.CenterY = (int)(moments.M01 / moments.M00);
            }

            result.PhysicalArea = maximumArea *
                parameters.MillimetersPerPixel *
                parameters.MillimetersPerPixel;
            result.CenterXMillimeters =
                result.CenterX * parameters.MillimetersPerPixel;
            result.CenterYMillimeters =
                result.CenterY * parameters.MillimetersPerPixel;
            result.WidthMillimeters =
                result.WidthPixels * parameters.MillimetersPerPixel;
            result.HeightMillimeters =
                result.HeightPixels * parameters.MillimetersPerPixel;

            DrawAnnotations(
                annotatedImage,
                contours,
                validContours,
                maximumContourIndex,
                result);
            return result;
        }

        private static void ApplyJudgement(
            InspectionResult result,
            Point[][] validContours,
            VisionParameters parameters)
        {
            if (result.Count != parameters.ExpectedTargetCount)
            {
                result.IsOK = false;
                result.JudgementCode =
                    InspectionJudgementCode.TargetCountMismatch;
                result.JudgementReason =
                    $"目标数量不符：期望 {parameters.ExpectedTargetCount}，" +
                    $"实际 {result.Count}";
                return;
            }

            foreach (Point[] contour in validContours)
            {
                double area = Cv2.ContourArea(contour);

                if (area < parameters.MinimumOkArea)
                {
                    result.IsOK = false;
                    result.JudgementCode = InspectionJudgementCode.AreaTooSmall;
                    result.JudgementReason =
                        $"目标面积过小：实测 {area:F0}，" +
                        $"下限 {parameters.MinimumOkArea:F0}";
                    return;
                }

                if (area > parameters.MaximumOkArea)
                {
                    result.IsOK = false;
                    result.JudgementCode = InspectionJudgementCode.AreaTooLarge;
                    result.JudgementReason =
                        $"目标面积过大：实测 {area:F0}，" +
                        $"上限 {parameters.MaximumOkArea:F0}";
                    return;
                }

                Rect boundingRectangle = Cv2.BoundingRect(contour);
                double widthMillimeters =
                    boundingRectangle.Width * parameters.MillimetersPerPixel;
                double heightMillimeters =
                    boundingRectangle.Height * parameters.MillimetersPerPixel;

                if (widthMillimeters < parameters.MinimumWidthMillimeters)
                {
                    result.IsOK = false;
                    result.JudgementCode =
                        InspectionJudgementCode.WidthTooSmall;
                    result.JudgementReason =
                        $"目标宽度过小：实测 {widthMillimeters:F2} mm，" +
                        $"下限 {parameters.MinimumWidthMillimeters:F2} mm";
                    return;
                }

                if (widthMillimeters > parameters.MaximumWidthMillimeters)
                {
                    result.IsOK = false;
                    result.JudgementCode =
                        InspectionJudgementCode.WidthTooLarge;
                    result.JudgementReason =
                        $"目标宽度过大：实测 {widthMillimeters:F2} mm，" +
                        $"上限 {parameters.MaximumWidthMillimeters:F2} mm";
                    return;
                }

                if (heightMillimeters < parameters.MinimumHeightMillimeters)
                {
                    result.IsOK = false;
                    result.JudgementCode =
                        InspectionJudgementCode.HeightTooSmall;
                    result.JudgementReason =
                        $"目标高度过小：实测 {heightMillimeters:F2} mm，" +
                        $"下限 {parameters.MinimumHeightMillimeters:F2} mm";
                    return;
                }

                if (heightMillimeters > parameters.MaximumHeightMillimeters)
                {
                    result.IsOK = false;
                    result.JudgementCode =
                        InspectionJudgementCode.HeightTooLarge;
                    result.JudgementReason =
                        $"目标高度过大：实测 {heightMillimeters:F2} mm，" +
                        $"上限 {parameters.MaximumHeightMillimeters:F2} mm";
                    return;
                }
            }

            result.IsOK = true;
            result.JudgementCode = InspectionJudgementCode.Ok;
            result.JudgementReason = "目标数量、面积和尺寸均符合规格";
        }

        private static Mat CreateFullSizeDebugImage(
            Mat processingStage,
            Size fullImageSize,
            Rect processingRegion)
        {
            var fullImage = new Mat(
                fullImageSize,
                processingStage.Type(),
                Scalar.Black);

            using Mat destinationRegion = new Mat(fullImage, processingRegion);
            processingStage.CopyTo(destinationRegion);
            return fullImage;
        }

        private static Point[][] FilterValidContours(
            Point[][] contours,
            VisionParameters parameters)
        {
            var validContours = new List<Point[]>();

            foreach (Point[] contour in contours)
            {
                double area = Cv2.ContourArea(contour);
                if (area < parameters.MinimumContourArea)
                {
                    continue;
                }

                double perimeter = Cv2.ArcLength(contour, true);
                if (perimeter <= double.Epsilon)
                {
                    continue;
                }

                double circularity = 4 * Math.PI * area / (perimeter * perimeter);
                if (circularity < parameters.MinimumCircularity)
                {
                    continue;
                }

                validContours.Add(contour);
            }

            return validContours.ToArray();
        }

        private static Rect CreateProcessingRegion(
            Mat image,
            VisionParameters parameters)
        {
            if (!parameters.IsRoiEnabled)
            {
                return new Rect(0, 0, image.Width, image.Height);
            }

            long right = (long)parameters.RoiX + parameters.RoiWidth;
            long bottom = (long)parameters.RoiY + parameters.RoiHeight;

            if (right > image.Width || bottom > image.Height)
            {
                throw new ArgumentException(
                    $"ROI 超出图像范围：图像={image.Width}×{image.Height}，" +
                    $"ROI=({parameters.RoiX},{parameters.RoiY}," +
                    $"{parameters.RoiWidth},{parameters.RoiHeight})。");
            }

            return new Rect(
                parameters.RoiX,
                parameters.RoiY,
                parameters.RoiWidth,
                parameters.RoiHeight);
        }

        private static Point[][] TranslateContoursToImageCoordinates(
            Point[][] localContours,
            int offsetX,
            int offsetY)
        {
            var translatedContours = new Point[localContours.Length][];

            for (int contourIndex = 0;
                 contourIndex < localContours.Length;
                 contourIndex++)
            {
                Point[] localContour = localContours[contourIndex];
                var translatedContour = new Point[localContour.Length];

                for (int pointIndex = 0;
                     pointIndex < localContour.Length;
                     pointIndex++)
                {
                    translatedContour[pointIndex] = new Point(
                        localContour[pointIndex].X + offsetX,
                        localContour[pointIndex].Y + offsetY);
                }

                translatedContours[contourIndex] = translatedContour;
            }

            return translatedContours;
        }

        private static int FindMaximumContourIndex(
            Point[][] contours,
            out double maximumArea)
        {
            int maximumContourIndex = -1;
            maximumArea = 0;

            for (int index = 0; index < contours.Length; index++)
            {
                double area = Cv2.ContourArea(contours[index]);
                if (area > maximumArea)
                {
                    maximumArea = area;
                    maximumContourIndex = index;
                }
            }

            return maximumContourIndex;
        }

        private static void DrawAnnotations(
            Mat annotatedImage,
            Point[][] rawContours,
            Point[][] validContours,
            int maximumContourIndex,
            InspectionResult result)
        {
            Scalar resultColor = result.IsOK
                ? new Scalar(0, 180, 0)
                : new Scalar(0, 140, 255);

            DrawRawContours(annotatedImage, rawContours);

            Cv2.DrawContours(
                annotatedImage,
                validContours,
                -1,
                new Scalar(255, 120, 0),
                2);

            Cv2.DrawContours(
                annotatedImage,
                validContours,
                maximumContourIndex,
                resultColor,
                3);

            Rect boundingRectangle = Cv2.BoundingRect(
                validContours[maximumContourIndex]);
            Cv2.Rectangle(
                annotatedImage,
                boundingRectangle,
                new Scalar(0, 215, 255),
                2);

            Cv2.DrawMarker(
                annotatedImage,
                new Point(result.CenterX, result.CenterY),
                resultColor,
                MarkerTypes.Cross,
                28,
                2);

            DrawSummary(annotatedImage, result);
        }

        private static void DrawRawContours(
            Mat annotatedImage,
            Point[][] rawContours)
        {
            if (rawContours.Length == 0)
            {
                return;
            }

            Cv2.DrawContours(
                annotatedImage,
                rawContours,
                -1,
                new Scalar(140, 140, 140),
                1);
        }

        private static void DrawSummary(
            Mat annotatedImage,
            InspectionResult result)
        {
            Scalar resultColor = result.IsOK
                ? new Scalar(0, 180, 0)
                : new Scalar(0, 0, 255);

            string resultText = result.IsOK ? "OK" : "NG";
            Cv2.PutText(
                annotatedImage,
                resultText,
                new Point(20, 45),
                HersheyFonts.HersheySimplex,
                1.2,
                resultColor,
                3);

            Cv2.PutText(
                annotatedImage,
                $"Raw: {result.RawContourCount}  Valid: {result.Count}  " +
                $"Area: {result.Area:F0}",
                new Point(20, 80),
                HersheyFonts.HersheySimplex,
                0.7,
                resultColor,
                2);

            Cv2.PutText(
                annotatedImage,
                $"Center: ({result.CenterX}, {result.CenterY})",
                new Point(20, 112),
                HersheyFonts.HersheySimplex,
                0.7,
                resultColor,
                2);

            Cv2.PutText(
                annotatedImage,
                $"Rule: {GetJudgementCodeText(result.JudgementCode)}",
                new Point(20, 144),
                HersheyFonts.HersheySimplex,
                0.7,
                resultColor,
                2);

            Cv2.PutText(
                annotatedImage,
                $"Metric: {result.PhysicalArea:F2} mm^2  " +
                $"({result.CenterXMillimeters:F2}, " +
                $"{result.CenterYMillimeters:F2}) mm",
                new Point(20, 176),
                HersheyFonts.HersheySimplex,
                0.65,
                resultColor,
                2);

            Cv2.PutText(
                annotatedImage,
                $"Size: {result.WidthPixels}x{result.HeightPixels} px  " +
                $"{result.WidthMillimeters:F2}x" +
                $"{result.HeightMillimeters:F2} mm",
                new Point(20, 208),
                HersheyFonts.HersheySimplex,
                0.65,
                new Scalar(0, 180, 220),
                2);
        }

        private static string GetJudgementCodeText(
            InspectionJudgementCode judgementCode)
        {
            return judgementCode switch
            {
                InspectionJudgementCode.Ok => "PASS",
                InspectionJudgementCode.TargetCountMismatch => "COUNT",
                InspectionJudgementCode.AreaTooSmall => "AREA LOW",
                InspectionJudgementCode.AreaTooLarge => "AREA HIGH",
                InspectionJudgementCode.WidthTooSmall => "WIDTH LOW",
                InspectionJudgementCode.WidthTooLarge => "WIDTH HIGH",
                InspectionJudgementCode.HeightTooSmall => "HEIGHT LOW",
                InspectionJudgementCode.HeightTooLarge => "HEIGHT HIGH",
                _ => "UNKNOWN"
            };
        }

        private static void DrawProcessingTime(
            Mat annotatedImage,
            double processingTimeMs)
        {
            Cv2.PutText(
                annotatedImage,
                $"Time: {processingTimeMs:F2} ms",
                new Point(20, Math.Max(25, annotatedImage.Rows - 20)),
                HersheyFonts.HersheySimplex,
                0.65,
                new Scalar(255, 0, 255),
                2);
        }
    }
}
