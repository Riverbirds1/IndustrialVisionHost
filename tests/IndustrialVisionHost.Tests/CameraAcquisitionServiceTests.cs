using System.Diagnostics;
using IndustrialVisionHost.Camera;
using IndustrialVisionHost.Services;
using OpenCvSharp;

namespace IndustrialVisionHost.Tests;

public sealed class CameraAcquisitionServiceTests
{
    [Fact]
    public async Task StartCaptureStop_ManagesFrameAndLifecycle()
    {
        var camera = new ControlledCamera();
        using var service = new CameraAcquisitionService(camera);
        var frameReceived = NewCompletion();
        int stoppedCount = 0;
        service.FrameReceived += _ => frameReceived.TrySetResult();
        service.AcquisitionStopped += () => Interlocked.Increment(ref stoppedCount);

        Assert.True(service.OpenAndStart());
        await frameReceived.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.True(service.IsRunning);
        Assert.True(service.IsConnected);
        using Mat? latest = service.GetLatestFrame();
        Assert.NotNull(latest);
        Assert.False(latest!.Empty());

        service.Stop();

        Assert.False(service.IsRunning);
        Assert.False(service.IsConnected);
        Assert.Null(service.GetLatestFrame());
        Assert.Equal(1, Volatile.Read(ref stoppedCount));
        Assert.True(camera.CloseCount >= 1);
    }

    [Fact]
    public async Task ConcurrentOpenAndStart_CreatesOnlyOneCaptureLoop()
    {
        var camera = new ControlledCamera(openDelay: TimeSpan.FromMilliseconds(30));
        using var service = new CameraAcquisitionService(camera);
        var frameReceived = NewCompletion();
        service.FrameReceived += _ => frameReceived.TrySetResult();

        bool[] results = await Task.WhenAll(
            Enumerable.Range(0, 12)
                .Select(_ => Task.Run(service.OpenAndStart)));
        await frameReceived.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.All(results, Assert.True);
        Assert.Equal(1, camera.OpenCount);
        Assert.True(service.IsRunning);
        service.Stop();
    }

    [Fact]
    public async Task CaptureFailure_ReconnectsAndContinuesFrames()
    {
        var camera = new ControlledCamera(captureFailures: 1);
        using var service = new CameraAcquisitionService(
            camera,
            maximumReconnectAttempts: 2,
            reconnectDelay: TimeSpan.FromMilliseconds(10));
        var reconnected = NewCompletion<int>();
        var frameReceived = NewCompletion();
        int failureCount = 0;
        service.Reconnected += attempt => reconnected.TrySetResult(attempt);
        service.FrameReceived += _ => frameReceived.TrySetResult();
        service.AcquisitionFailed += _ => Interlocked.Increment(ref failureCount);

        Assert.True(service.OpenAndStart());
        Assert.Equal(
            1,
            await reconnected.Task.WaitAsync(TimeSpan.FromSeconds(2)));
        await frameReceived.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal(2, camera.OpenCount);
        Assert.Equal(0, Volatile.Read(ref failureCount));
        Assert.True(service.IsRunning);
        service.Stop();
    }

    [Fact]
    public async Task ReconnectExhaustion_RaisesOneFailureAndStops()
    {
        var camera = new ControlledCamera(
            captureFailures: int.MaxValue,
            rejectOpenAfterFirst: true);
        using var service = new CameraAcquisitionService(
            camera,
            maximumReconnectAttempts: 2,
            reconnectDelay: TimeSpan.FromMilliseconds(10));
        var failed = new TaskCompletionSource<Exception>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        int failureCount = 0;
        int reconnectingCount = 0;
        service.Reconnecting += (_, _) =>
            Interlocked.Increment(ref reconnectingCount);
        service.AcquisitionFailed += error =>
        {
            Interlocked.Increment(ref failureCount);
            failed.TrySetResult(error);
        };

        Assert.True(service.OpenAndStart());
        Exception exception =
            await failed.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await WaitUntilAsync(() => !service.IsRunning, TimeSpan.FromSeconds(2));

        Assert.Contains("重连 2 次仍未恢复", exception.Message);
        Assert.Equal(1, Volatile.Read(ref failureCount));
        Assert.Equal(2, Volatile.Read(ref reconnectingCount));
        Assert.False(service.IsRunning);
    }

    [Fact]
    public async Task Stop_CancelsLongReconnectDelayImmediately()
    {
        var camera = new ControlledCamera(
            captureFailures: int.MaxValue,
            rejectOpenAfterFirst: true);
        using var service = new CameraAcquisitionService(
            camera,
            maximumReconnectAttempts: 3,
            reconnectDelay: TimeSpan.FromSeconds(5));
        var reconnecting = NewCompletion();
        int failureCount = 0;
        service.Reconnecting += (_, _) => reconnecting.TrySetResult();
        service.AcquisitionFailed += _ => Interlocked.Increment(ref failureCount);
        Assert.True(service.OpenAndStart());
        await reconnecting.Task.WaitAsync(TimeSpan.FromSeconds(2));

        var stopwatch = Stopwatch.StartNew();
        service.Stop();
        stopwatch.Stop();

        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(1));
        Assert.Equal(0, Volatile.Read(ref failureCount));
        Assert.False(service.IsRunning);
    }

    [Fact]
    public async Task ConcurrentStop_IsIdempotent()
    {
        var camera = new ControlledCamera();
        using var service = new CameraAcquisitionService(camera);
        var frameReceived = NewCompletion();
        service.FrameReceived += _ => frameReceived.TrySetResult();
        Assert.True(service.OpenAndStart());
        await frameReceived.Task.WaitAsync(TimeSpan.FromSeconds(2));

        await Task.WhenAll(
            Enumerable.Range(0, 8)
                .Select(_ => Task.Run(service.Stop)));

        Assert.False(service.IsRunning);
        Assert.False(service.IsConnected);
        Assert.Null(service.GetLatestFrame());
    }

    [Fact]
    public void Dispose_ReleasesCameraAndRejectsRestart()
    {
        var camera = new ControlledCamera();
        var service = new CameraAcquisitionService(camera);

        service.Dispose();
        service.Dispose();

        Assert.Equal(1, camera.DisposeCount);
        Assert.Throws<ObjectDisposedException>(() => service.OpenAndStart());
    }

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(1, -1)]
    public void InvalidReconnectConfiguration_IsRejected(
        int maximumAttempts,
        int delayMilliseconds)
    {
        using var camera = new ControlledCamera();

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new CameraAcquisitionService(
                camera,
                maximumAttempts,
                TimeSpan.FromMilliseconds(delayMilliseconds)));
    }

    private static TaskCompletionSource NewCompletion()
    {
        return new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private static TaskCompletionSource<T> NewCompletion<T>()
    {
        return new TaskCompletionSource<T>(
            TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private static async Task WaitUntilAsync(
        Func<bool> condition,
        TimeSpan timeout)
    {
        DateTime deadline = DateTime.UtcNow + timeout;
        while (!condition())
        {
            if (DateTime.UtcNow >= deadline)
            {
                throw new TimeoutException("等待条件超时。");
            }

            await Task.Delay(10);
        }
    }

    private sealed class ControlledCamera : ICamera
    {
        private readonly object sync = new();
        private readonly TimeSpan openDelay;
        private readonly bool rejectOpenAfterFirst;
        private int remainingCaptureFailures;
        private bool disposed;
        private int openCount;
        private int closeCount;
        private int disposeCount;

        public ControlledCamera(
            int captureFailures = 0,
            bool rejectOpenAfterFirst = false,
            TimeSpan? openDelay = null)
        {
            remainingCaptureFailures = captureFailures;
            this.rejectOpenAfterFirst = rejectOpenAfterFirst;
            this.openDelay = openDelay ?? TimeSpan.Zero;
        }

        public bool IsConnected { get; private set; }
        public int OpenCount => Volatile.Read(ref openCount);
        public int CloseCount => Volatile.Read(ref closeCount);
        public int DisposeCount => Volatile.Read(ref disposeCount);

        public bool Open()
        {
            lock (sync)
            {
                if (disposed)
                {
                    throw new ObjectDisposedException(nameof(ControlledCamera));
                }
                Thread.Sleep(openDelay);
                int count = Interlocked.Increment(ref openCount);
                IsConnected = !rejectOpenAfterFirst || count == 1;
                return IsConnected;
            }
        }

        public Mat Capture()
        {
            lock (sync)
            {
                if (disposed)
                {
                    throw new ObjectDisposedException(nameof(ControlledCamera));
                }
                if (!IsConnected)
                {
                    throw new InvalidOperationException("测试相机未连接。");
                }

                if (remainingCaptureFailures > 0)
                {
                    remainingCaptureFailures--;
                    IsConnected = false;
                    throw new InvalidOperationException("测试采集失败。");
                }

                return new Mat(
                    48,
                    64,
                    MatType.CV_8UC3,
                    new Scalar(255, 255, 255));
            }
        }

        public void Close()
        {
            lock (sync)
            {
                IsConnected = false;
                Interlocked.Increment(ref closeCount);
            }
        }

        public void Dispose()
        {
            lock (sync)
            {
                if (disposed)
                {
                    return;
                }

                disposed = true;
                IsConnected = false;
                Interlocked.Increment(ref disposeCount);
            }
        }
    }
}
