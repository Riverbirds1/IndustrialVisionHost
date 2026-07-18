using IndustrialVisionHost.Camera;
using IndustrialVisionHost.Models;
using IndustrialVisionHost.Vision;
using OpenCvSharp;

namespace IndustrialVisionHost.Tests;

public sealed class VisionProcessorTests
{
    [Fact]
    public void StandardTarget_IsOkAndProducesPhysicalMeasurements()
    {
        using Mat frame = Capture(FakeCameraScenario.StandardSingle);
        using VisionProcessingResult processing = VisionProcessor.Process(
            frame,
            CreateParameters(),
            VisionDebugView.Annotated);
        InspectionResult result = processing.Inspection;

        Assert.True(result.IsOK);
        Assert.Equal(InspectionJudgementCode.Ok, result.JudgementCode);
        Assert.Equal(1, result.Count);
        Assert.InRange(result.Area, 12500, 13500);
        Assert.InRange(result.CenterX, 318, 322);
        Assert.InRange(result.CenterY, 238, 242);
        Assert.InRange(result.PhysicalArea, 31, 34);
        Assert.InRange(result.WidthMillimeters, 6.4, 6.7);
        Assert.InRange(result.HeightMillimeters, 6.4, 6.7);
        Assert.Same(processing.AnnotatedImage, processing.DisplayImage);
    }

    [Fact]
    public void DoubleTarget_UsesExpectedCountRule()
    {
        using Mat frame = Capture(FakeCameraScenario.DoubleTarget);
        using VisionProcessingResult wrongExpectation = VisionProcessor.Process(
            frame,
            CreateParameters(expectedTargetCount: 1),
            VisionDebugView.Annotated);
        using VisionProcessingResult correctExpectation = VisionProcessor.Process(
            frame,
            CreateParameters(expectedTargetCount: 2),
            VisionDebugView.Annotated);

        Assert.False(wrongExpectation.Inspection.IsOK);
        Assert.Equal(
            InspectionJudgementCode.TargetCountMismatch,
            wrongExpectation.Inspection.JudgementCode);
        Assert.Equal(2, wrongExpectation.Inspection.Count);
        Assert.True(correctExpectation.Inspection.IsOK);
    }

    [Fact]
    public void SmallTarget_IsRejectedByMinimumArea()
    {
        using Mat frame = Capture(FakeCameraScenario.SmallTargetNg);
        using VisionProcessingResult processing = VisionProcessor.Process(
            frame,
            CreateParameters(),
            VisionDebugView.Annotated);

        Assert.False(processing.Inspection.IsOK);
        Assert.Equal(
            InspectionJudgementCode.AreaTooSmall,
            processing.Inspection.JudgementCode);
    }

    [Fact]
    public void AreaAndSizeLimits_ReturnSpecificJudgementCodes()
    {
        using Mat frame = Capture(FakeCameraScenario.StandardSingle);

        AssertCode(frame, CreateParameters(maximumOkArea: 12000),
            InspectionJudgementCode.AreaTooLarge);
        AssertCode(frame, CreateParameters(minimumWidth: 7),
            InspectionJudgementCode.WidthTooSmall);
        AssertCode(frame, CreateParameters(maximumWidth: 6),
            InspectionJudgementCode.WidthTooLarge);
        AssertCode(frame, CreateParameters(minimumHeight: 7),
            InspectionJudgementCode.HeightTooSmall);
        AssertCode(frame, CreateParameters(maximumHeight: 6),
            InspectionJudgementCode.HeightTooLarge);
    }

    [Fact]
    public void Roi_UsesFullImageCoordinatesAndCanExcludeTarget()
    {
        using Mat frame = Capture(FakeCameraScenario.StandardSingle);
        using VisionProcessingResult included = VisionProcessor.Process(
            frame,
            CreateParameters(
                roiEnabled: true,
                roiX: 200,
                roiY: 120,
                roiWidth: 240,
                roiHeight: 240),
            VisionDebugView.Annotated);
        using VisionProcessingResult excluded = VisionProcessor.Process(
            frame,
            CreateParameters(
                roiEnabled: true,
                roiX: 0,
                roiY: 0,
                roiWidth: 150,
                roiHeight: 150),
            VisionDebugView.Annotated);

        Assert.True(included.Inspection.IsOK);
        Assert.InRange(included.Inspection.CenterX, 318, 322);
        Assert.InRange(included.Inspection.CenterY, 238, 242);
        Assert.Equal(0, excluded.Inspection.Count);
        Assert.Equal(
            InspectionJudgementCode.TargetCountMismatch,
            excluded.Inspection.JudgementCode);
    }

    [Fact]
    public void RoiOutsideImage_IsRejectedWithClearError()
    {
        using Mat frame = Capture(FakeCameraScenario.StandardSingle);

        ArgumentException exception = Assert.Throws<ArgumentException>(() =>
            VisionProcessor.Process(
                frame,
                CreateParameters(
                    roiEnabled: true,
                    roiX: 600,
                    roiY: 400,
                    roiWidth: 100,
                    roiHeight: 100),
                VisionDebugView.Annotated));

        Assert.Contains("ROI 超出图像范围", exception.Message);
    }

    [Fact]
    public void Morphology_CleansNoisyTargetAndKeepsItDetectable()
    {
        using Mat frame = Capture(FakeCameraScenario.NoisyTarget);
        using VisionProcessingResult withoutMorphology = VisionProcessor.Process(
            frame,
            CreateParameters(
                morphologyKernel: 1,
                opening: false,
                closing: false),
            VisionDebugView.Annotated);
        using VisionProcessingResult cleaned = VisionProcessor.Process(
            frame,
            CreateParameters(
                morphologyKernel: 5,
                opening: true,
                closing: true),
            VisionDebugView.Morphology);

        Assert.True(cleaned.Inspection.IsOK);
        Assert.Equal(1, cleaned.Inspection.Count);
        Assert.True(
            cleaned.Inspection.RawContourCount <=
            withoutMorphology.Inspection.RawContourCount);
    }

    [Theory]
    [InlineData(VisionDebugView.Gray)]
    [InlineData(VisionDebugView.Binary)]
    [InlineData(VisionDebugView.Morphology)]
    public void DebugViews_ReturnFullSizeSingleChannelImage(VisionDebugView view)
    {
        using Mat frame = Capture(FakeCameraScenario.StandardSingle);
        using VisionProcessingResult processing = VisionProcessor.Process(
            frame,
            CreateParameters(),
            view);

        Assert.NotSame(processing.AnnotatedImage, processing.DisplayImage);
        Assert.Equal(frame.Width, processing.DisplayImage.Width);
        Assert.Equal(frame.Height, processing.DisplayImage.Height);
        Assert.Equal(1, processing.DisplayImage.Channels());
        Assert.Equal(3, processing.AnnotatedImage.Channels());
    }

    [Fact]
    public void MovingTarget_ChangesMeasuredCenter()
    {
        using var camera = new FakeCamera
        {
            Scenario = FakeCameraScenario.MovingTarget
        };
        Assert.True(camera.Open());
        using Mat firstFrame = camera.Capture();
        using VisionProcessingResult first = VisionProcessor.Process(
            firstFrame, CreateParameters(), VisionDebugView.Annotated);

        Mat? laterFrame = null;
        for (int index = 0; index < 12; index++)
        {
            laterFrame?.Dispose();
            laterFrame = camera.Capture();
        }

        using (laterFrame)
        using (VisionProcessingResult later = VisionProcessor.Process(
            laterFrame!, CreateParameters(), VisionDebugView.Annotated))
        {
            Assert.NotEqual(first.Inspection.CenterX, later.Inspection.CenterX);
        }
    }

    [Fact]
    public void RepeatedProcessing_DisposesEveryReturnedMat()
    {
        using var camera = new FakeCamera
        {
            Scenario = FakeCameraScenario.DynamicDemo
        };
        Assert.True(camera.Open());

        for (int index = 0; index < 100; index++)
        {
            using Mat frame = camera.Capture();
            var processing = VisionProcessor.Process(
                frame,
                CreateParameters(maximumOkArea: 50000, maximumWidth: 20,
                    maximumHeight: 20, expectedTargetCount: index / 20 % 3 + 1),
                index % 2 == 0
                    ? VisionDebugView.Annotated
                    : VisionDebugView.Binary);
            Mat annotated = processing.AnnotatedImage;
            Mat display = processing.DisplayImage;

            processing.Dispose();

            Assert.True(annotated.IsDisposed);
            Assert.True(display.IsDisposed);
        }
    }

    private static Mat Capture(FakeCameraScenario scenario)
    {
        using var camera = new FakeCamera { Scenario = scenario };
        Assert.True(camera.Open());
        return camera.Capture();
    }

    private static void AssertCode(
        Mat frame,
        VisionParameters parameters,
        InspectionJudgementCode expectedCode)
    {
        using VisionProcessingResult processing = VisionProcessor.Process(
            frame,
            parameters,
            VisionDebugView.Annotated);
        Assert.False(processing.Inspection.IsOK);
        Assert.Equal(expectedCode, processing.Inspection.JudgementCode);
    }

    private static VisionParameters CreateParameters(
        int expectedTargetCount = 1,
        double minimumOkArea = 1000,
        double maximumOkArea = 20000,
        bool roiEnabled = false,
        int roiX = 0,
        int roiY = 0,
        int roiWidth = 640,
        int roiHeight = 480,
        int morphologyKernel = 3,
        bool opening = true,
        bool closing = true,
        double minimumWidth = 5,
        double maximumWidth = 8,
        double minimumHeight = 5,
        double maximumHeight = 8)
    {
        return new VisionParameters(
            binaryThreshold: 100,
            expectedTargetCount: expectedTargetCount,
            minimumOkArea: minimumOkArea,
            maximumOkArea: maximumOkArea,
            isRoiEnabled: roiEnabled,
            roiX: roiX,
            roiY: roiY,
            roiWidth: roiWidth,
            roiHeight: roiHeight,
            gaussianKernelSize: 1,
            morphologyKernelSize: morphologyKernel,
            enableOpening: opening,
            enableClosing: closing,
            minimumContourArea: 50,
            minimumCircularity: 0.7,
            millimetersPerPixel: 0.05,
            minimumWidthMillimeters: minimumWidth,
            maximumWidthMillimeters: maximumWidth,
            minimumHeightMillimeters: minimumHeight,
            maximumHeightMillimeters: maximumHeight);
    }
}
