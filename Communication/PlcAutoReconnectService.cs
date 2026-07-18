using System;
using System.Threading;
using System.Threading.Tasks;

namespace IndustrialVisionHost.Communication
{
    public sealed class PlcAutoReconnectService : IDisposable
    {
        private readonly object stateSync = new object();
        private readonly TcpPlcClient plcClient;
        private readonly TimeSpan initialDelay;
        private readonly TimeSpan maximumDelay;
        private readonly TimeSpan connectionTimeout;
        private CancellationTokenSource? cancellation;
        private Task? reconnectTask;

        public PlcAutoReconnectService(
            TcpPlcClient plcClient,
            TimeSpan? initialDelay = null,
            TimeSpan? maximumDelay = null,
            TimeSpan? connectionTimeout = null)
        {
            this.plcClient = plcClient
                ?? throw new ArgumentNullException(nameof(plcClient));
            this.initialDelay = initialDelay ?? TimeSpan.FromSeconds(1);
            this.maximumDelay = maximumDelay ?? TimeSpan.FromSeconds(5);
            this.connectionTimeout = connectionTimeout ?? TimeSpan.FromSeconds(3);

            if (this.initialDelay <= TimeSpan.Zero ||
                this.maximumDelay < this.initialDelay ||
                this.connectionTimeout <= TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(initialDelay),
                    "自动重连时间参数无效。");
            }
        }

        public event Action<int>? ReconnectAttempting;

        public event Action<int, Exception, TimeSpan>? ReconnectAttemptFailed;

        public event Action<int>? Reconnected;

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

        public void Start(string host, int port)
        {
            if (string.IsNullOrWhiteSpace(host))
            {
                throw new ArgumentException("PLC 地址不能为空。", nameof(host));
            }

            if (port < 1 || port > 65535)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(port),
                    "TCP 端口必须在 1～65535 之间。");
            }

            lock (stateSync)
            {
                if (cancellation is not null)
                {
                    return;
                }

                var newCancellation = new CancellationTokenSource();
                cancellation = newCancellation;
                reconnectTask = ReconnectAsync(
                    host.Trim(),
                    port,
                    newCancellation);
            }
        }

        public void Stop()
        {
            CancellationTokenSource? cancellationToStop;

            lock (stateSync)
            {
                cancellationToStop = cancellation;
                cancellation = null;
                reconnectTask = null;
            }

            cancellationToStop?.Cancel();
        }

        private async Task ReconnectAsync(
            string host,
            int port,
            CancellationTokenSource activeCancellation)
        {
            int attempt = 0;
            TimeSpan delay = initialDelay;

            try
            {
                while (IsCurrent(activeCancellation))
                {
                    await Task.Delay(delay, activeCancellation.Token);

                    if (!IsCurrent(activeCancellation))
                    {
                        return;
                    }

                    attempt++;
                    ReconnectAttempting?.Invoke(attempt);

                    try
                    {
                        await plcClient.ConnectAsync(
                            host,
                            port,
                            connectionTimeout,
                            activeCancellation.Token);

                        if (!IsCurrent(activeCancellation))
                        {
                            plcClient.Disconnect();
                            return;
                        }

                        Reconnected?.Invoke(attempt);
                        return;
                    }
                    catch (OperationCanceledException)
                        when (activeCancellation.IsCancellationRequested)
                    {
                        return;
                    }
                    catch (Exception ex)
                    {
                        if (!IsCurrent(activeCancellation))
                        {
                            return;
                        }

                        TimeSpan nextDelay = TimeSpan.FromMilliseconds(
                            Math.Min(
                                maximumDelay.TotalMilliseconds,
                                delay.TotalMilliseconds * 2));
                        ReconnectAttemptFailed?.Invoke(
                            attempt,
                            ex,
                            nextDelay);
                        delay = nextDelay;
                    }
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
                        reconnectTask = null;
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
                taskToWait = reconnectTask;
                cancellationToStop = cancellation;
                cancellation = null;
                reconnectTask = null;
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
