using IndustrialVisionHost.Camera;
using OpenCvSharp;

namespace IndustrialVisionHost.Tests;

public sealed class FakeCameraTests
{
    [Fact]
    public void CaptureBeforeOpen_IsRejected()
    {
        using var camera = new FakeCamera();

        InvalidOperationException exception = Assert.Throws<
            InvalidOperationException>(() => camera.Capture());

        Assert.Contains("尚未打开", exception.Message);
    }

    [Fact]
    public void StandardScenario_ProducesExpectedFrameShape()
    {
        using var camera = new FakeCamera();
        Assert.True(camera.Open());

        using Mat frame = camera.Capture();

        Assert.False(frame.Empty());
        Assert.Equal(640, frame.Width);
        Assert.Equal(480, frame.Height);
        Assert.Equal(3, frame.Channels());
    }

    [Fact]
    public void CaptureFailureScenario_DisconnectsAfterFiveFrames()
    {
        using var camera = new FakeCamera
        {
            Scenario = FakeCameraScenario.CaptureFailure
        };
        Assert.True(camera.Open());
        for (int index = 0; index < 5; index++)
        {
            using Mat frame = camera.Capture();
            Assert.False(frame.Empty());
        }

        Assert.Throws<InvalidOperationException>(() => camera.Capture());
        Assert.False(camera.IsConnected);
        Assert.False(camera.Open());

        camera.Scenario = FakeCameraScenario.StandardSingle;
        Assert.True(camera.Open());
    }

    [Theory]
    [InlineData(0, 480, 65)]
    [InlineData(640, 0, 65)]
    [InlineData(640, 480, 0)]
    public void InvalidSettings_AreRejected(
        int width,
        int height,
        int standardRadius)
    {
        var settings = new FakeCameraSettings
        {
            FrameWidth = width,
            FrameHeight = height,
            StandardRadius = standardRadius
        };

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new FakeCamera(settings));
    }
}
