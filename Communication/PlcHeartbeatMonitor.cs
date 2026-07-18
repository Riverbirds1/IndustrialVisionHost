using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace IndustrialVisionHost.Communication
{
    public sealed class PlcHeartbeatMonitor : IDisposable
    {
        private readonly object stateSync = new object();
        private readonly TcpPlcClient plcClient;
        private readonly TimeSpan interval;
        private readonly TimeSpan requestTimeout;
        private CancellationTokenSource? cancellation;
        private Task? monitorTask;

        public PlcHeartbeatMonitor(
            TcpPlcClient plcClient,
            TimeSpan? interval = null,
            TimeSpan? requestTimeout = null)
        {
            this.plcClient = plcClient
                ?? throw new ArgumentNullException(nameof(plcClient));
            this.interval = interval ?? TimeSpan.FromSeconds(2);
            this.requestTimeout = requestTimeout ?? TimeSpan.FromSeconds(1);

            if (this.interval <= TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(interval),
                    "心跳间隔必须大于 0。");
            }

            if (this.requestTimeout <= TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(requestTimeout),
                    "心跳超时时间必须大于 0。");
            }
        }

        public event Action<TimeSpan>? HeartbeatSucceeded;

        public event Action<Exception>? HeartbeatFailed;

        public bool IsRunning
        {
            get
            {
                lock (stateSync)
                {
                    return cancellation is not null;
                }
            }
        }

        public void Start()
        {
            lock (stateSync)
            {
                if (cancellation is not null)
                {
                    return;
                }

                var newCancellation = new CancellationTokenSource();
                cancellation = newCancellation;
                monitorTask = MonitorAsync(newCancellation);
            }
        }

        public void Stop()
        {
            CancellationTokenSource? cancellationToStop;

            lock (stateSync)
            {
                cancellationToStop = cancellation;
                cancellation = null;
                monitorTask = null;
            }

            cancellationToStop?.Cancel();
        }

        private async Task MonitorAsync(CancellationTokenSource activeCancellation)
        {
            try
            {
                while (IsCurrent(activeCancellation))
                {
                    var stopwatch = Stopwatch.StartNew();

                    try
                    {
                        string response = await plcClient.SendRequestAsync(
                            "HEARTBEAT",
                            requestTimeout);
                        stopwatch.Stop();

                        if (!string.Equals(
                                response,
                                "ALIVE",
                                StringComparison.Ordinal))
                        {
                            throw new InvalidDataException(
                                $"心跳响应无效：{response}");
                        }

                        if (!IsCurrent(activeCancellation))
                        {
                            return;
                        }

                        HeartbeatSucceeded?.Invoke(stopwatch.Elapsed);
                    }
                    catch (Exception ex)
                    {
                        if (!IsCurrent(activeCancellation))
                        {
                            return;
                        }

                        plcClient.Disconnect();
                        HeartbeatFailed?.Invoke(ex);
                        return;
                    }

                    await Task.Delay(
                        interval,
                        activeCancellation.Token);
                }
            }
            catch (OperationCanceledException)
                when (activeCancellation.IsCancellationRequested)
            {
            }
            finally
            {
                lock (stateSync)
                {
                    if (ReferenceEquals(cancellation, activeCancellation))
                    {
                        cancellation = null;
                        monitorTask = null;
                    }
                }

                activeCancellation.Dispose();
            }
        }

        private bool IsCurrent(CancellationTokenSource activeCancellation)
        {
            lock (stateSync)
            {
                return ReferenceEquals(cancellation, activeCancellation) &&
                    !activeCancellation.IsCancellationRequested;
            }
        }

        public void Dispose()
        {
            Task? taskToWait;
            CancellationTokenSource? cancellationToStop;

            lock (stateSync)
            {
                taskToWait = monitorTask;
                cancellationToStop = cancellation;
                cancellation = null;
                monitorTask = null;
            }

            cancellationToStop?.Cancel();

            try
            {
                taskToWait?.GetAwaiter().GetResult();
            }
            catch (OperationCanceledException)
            {
            }
        }
    }
}
