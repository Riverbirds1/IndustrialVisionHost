using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;

using IndustrialVisionHost.Camera;
using IndustrialVisionHost.Utils;
using Mat = OpenCvSharp.Mat;

namespace IndustrialVisionHost.Services
{
    public sealed class CameraAcquisitionService : IDisposable
    {
        private readonly ICamera camera;
        private readonly object frameSync = new object();
        private readonly object lifecycleSync = new object();

        private CancellationTokenSource? acquisitionToken;
        private Task? acquisitionTask;
        private Mat? latestFrame;
        private bool disposed;
        private bool isStopping;
        private volatile bool isRunning;
        private readonly int maximumReconnectAttempts;
        private readonly TimeSpan reconnectDelay;

        public CameraAcquisitionService(
            ICamera camera,
            int maximumReconnectAttempts = 3,
            TimeSpan? reconnectDelay = null)
        {
            this.camera = camera ?? throw new ArgumentNullException(nameof(camera));

            if (maximumReconnectAttempts < 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(maximumReconnectAttempts),
                    "最大重连次数不能为负数。");
            }

            this.maximumReconnectAttempts = maximumReconnectAttempts;
            this.reconnectDelay = reconnectDelay ?? TimeSpan.FromSeconds(2);

            if (this.reconnectDelay < TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(reconnectDelay),
                    "重连等待时间不能为负数。");
            }
        }

        public event Action<BitmapImage>? FrameReceived;

        public event Action<Exception>? AcquisitionFailed;

        public event Action? AcquisitionStopped;

        public event Action<int, int>? Reconnecting;

        public event Action<int>? Reconnected;

        public bool IsConnected => camera.IsConnected;

        public bool IsRunning => isRunning;

        public bool OpenAndStart()
        {
            lock (lifecycleSync)
            {
                if (disposed)
                {
                    throw new ObjectDisposedException(
                        nameof(CameraAcquisitionService));
                }

                if (isStopping)
                {
                    return false;
                }

                if (IsRunning)
                {
                    return true;
                }

                if (!camera.Open())
                {
                    return false;
                }

                acquisitionToken?.Dispose();
                acquisitionToken = new CancellationTokenSource();
                CancellationToken token = acquisitionToken.Token;
                isRunning = true;
                acquisitionTask = Task.Run(() => AcquisitionLoopAsync(token));
                return true;
            }
        }

        public Mat? GetLatestFrame()
        {
            lock (frameSync)
            {
                return latestFrame?.Clone();
            }
        }

        private async Task AcquisitionLoopAsync(CancellationToken token)
        {
            try
            {
                while (true)
                {
                    token.ThrowIfCancellationRequested();

                    try
                    {
                        using Mat image = camera.Capture();
                        BitmapImage bitmap = OpenCvHelper.MatToBitmapImage(image);

                        lock (frameSync)
                        {
                            latestFrame?.Dispose();
                            latestFrame = image.Clone();
                        }

                        FrameReceived?.Invoke(bitmap);
                        await Task.Delay(500, token);
                    }
                    catch (Exception ex) when (
                        ex is not OperationCanceledException ||
                        !token.IsCancellationRequested)
                    {
                        var reconnectResult = await TryReconnectAsync(ex, token);
                        if (!reconnectResult.Success)
                        {
                            AcquisitionFailed?.Invoke(new InvalidOperationException(
                                $"相机重连 {maximumReconnectAttempts} 次仍未恢复：" +
                                reconnectResult.LastError.Message,
                                reconnectResult.LastError));
                            break;
                        }
                    }
                }
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                // 正常停止采集。
            }
            finally
            {
                isRunning = false;
                AcquisitionStopped?.Invoke();
            }
        }

        private async Task<(bool Success, Exception LastError)> TryReconnectAsync(
            Exception initialError,
            CancellationToken token)
        {
            camera.Close();
            Exception lastError = initialError;

            for (int attempt = 1; attempt <= maximumReconnectAttempts; attempt++)
            {
                Reconnecting?.Invoke(attempt, maximumReconnectAttempts);
                await Task.Delay(reconnectDelay, token);

                try
                {
                    if (camera.Open())
                    {
                        Reconnected?.Invoke(attempt);
                        return (true, lastError);
                    }

                    lastError = new InvalidOperationException(
                        $"第 {attempt} 次重连时相机拒绝打开。");
                }
                catch (Exception ex)
                {
                    lastError = ex;
                }
            }

            return (false, lastError);
        }

        public void Stop()
        {
            Task? taskToWait;
            CancellationTokenSource? tokenToCancel;
            bool ownsStop;

            lock (lifecycleSync)
            {
                taskToWait = acquisitionTask;
                if (isStopping)
                {
                    tokenToCancel = null;
                    ownsStop = false;
                }
                else
                {
                    isStopping = true;
                    tokenToCancel = acquisitionToken;
                    ownsStop = true;
                }
            }

            tokenToCancel?.Cancel();
            taskToWait?.GetAwaiter().GetResult();

            if (!ownsStop)
            {
                return;
            }

            lock (lifecycleSync)
            {
                acquisitionTask = null;
                isRunning = false;
                acquisitionToken?.Dispose();
                acquisitionToken = null;
                camera.Close();
                isStopping = false;
            }

            lock (frameSync)
            {
                latestFrame?.Dispose();
                latestFrame = null;
            }
        }

        public void Dispose()
        {
            lock (lifecycleSync)
            {
                if (disposed)
                {
                    return;
                }

                disposed = true;
            }

            Stop();
            camera.Dispose();
        }
    }
}
