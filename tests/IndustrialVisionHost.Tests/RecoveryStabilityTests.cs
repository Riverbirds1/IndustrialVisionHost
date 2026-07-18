using IndustrialVisionHost.Camera;
using IndustrialVisionHost.Communication;
using IndustrialVisionHost.Services;

namespace IndustrialVisionHost.Tests;

public sealed class RecoveryStabilityTests
{
    [Fact]
    [Trait("Category", "Stability")]
    public async Task Camera_CanStartAndStopTenConsecutiveCycles()
    {
        using var camera = new FakeCamera();
        using var service = new CameraAcquisitionService(camera);

        for (int cycle = 0; cycle < 10; cycle++)
        {
            var frame = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
            void OnFrame(System.Windows.Media.Imaging.BitmapImage _) =>
                frame.TrySetResult();
            service.FrameReceived += OnFrame;
            Assert.True(service.OpenAndStart());
            await frame.Task.WaitAsync(TimeSpan.FromSeconds(2));
            service.FrameReceived -= OnFrame;
            service.Stop();

            Assert.False(service.IsRunning);
            Assert.False(service.IsConnected);
            Assert.Null(service.GetLatestFrame());
        }
    }

    [Fact]
    [Trait("Category", "Stability")]
    public async Task TextPlc_CanRecoverAcrossTenServerDisruptions()
    {
        for (int cycle = 0; cycle < 10; cycle++)
        {
            using var server = new SimulatedPlcServer();
            using var client = new TcpPlcClient();
            var connectionLost = new TaskCompletionSource<Exception>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            client.ConnectionLost += error => connectionLost.TrySetResult(error);
            server.Start(0);
            await client.ConnectAsync(
                "127.0.0.1",
                server.ListeningPort,
                TimeSpan.FromSeconds(2));
            Assert.Equal("PONG", await client.SendRequestAsync(
                "PING", TimeSpan.FromSeconds(2)));

            server.Stop();
            await connectionLost.Task.WaitAsync(TimeSpan.FromSeconds(2));
            Assert.False(client.IsConnected);
        }
    }
}
