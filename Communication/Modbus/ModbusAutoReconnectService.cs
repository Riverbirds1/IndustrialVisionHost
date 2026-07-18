using System;
using System.Threading;
using System.Threading.Tasks;

namespace IndustrialVisionHost.Communication.Modbus
{
    public sealed class ModbusAutoReconnectService : IDisposable
    {
        private readonly object stateSync = new object();
        private readonly ModbusTcpClient client;
        private readonly TimeSpan initialDelay;
        private readonly TimeSpan maximumDelay;
        private readonly TimeSpan connectionTimeout;
        private CancellationTokenSource? cancellation;
        private Task? reconnectTask;

        public ModbusAutoReconnectService(
            ModbusTcpClient client,
            TimeSpan? initialDelay = null,
            TimeSpan? maximumDelay = null,
            TimeSpan? connectionTimeout = null)
        {
            this.client = client
                ?? throw new ArgumentNullException(nameof(client));
            this.initialDelay = initialDelay ?? TimeSpan.FromSeconds(1);
            this.maximumDelay = maximumDelay ?? TimeSpan.FromSeconds(5);
            this.connectionTimeout =
                connectionTimeout ?? TimeSpan.FromSeconds(3);

            if (this.initialDelay <= TimeSpan.Zero ||
                this.maximumDelay < this.initialDelay ||
                this.connectionTimeout <= TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(initialDelay),
                    "Modbus 自动重连时间参数无效。");
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
                throw new ArgumentException(
                    "Modbus TCP 地址不能为空。",
                    nameof(host));
            }

            if (port < 1 || port > 65535)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(port),
                    "Modbus TCP 端口必须在 1～65535 之间。");
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
            CancellationTokenSource owner)
        {
            int attempt = 0;
            TimeSpan delay = initialDelay;

            try
            {
                while (IsCurrent(owner))
                {
                    await Task.Delay(delay, owner.Token);

                    if (!IsCurrent(owner))
                    {
                        return;
                    }

                    attempt++;
                    ReconnectAttempting?.Invoke(attempt);

                    try
                    {
                        await client.ConnectAsync(
                            host,
                            port,
                            connectionTimeout,
                            owner.Token);

                        if (!IsCurrent(owner))
                        {
                            client.Disconnect();
                            return;
                        }

                        Reconnected?.Invoke(attempt);
                        return;
                    }
                    catch (OperationCanceledException)
                        when (owner.IsCancellationRequested)
                    {
                        return;
                    }
                    catch (Exception ex)
                    {
                        if (!IsCurrent(owner))
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
                when (owner.IsCancellationRequested)
            {
            }
            finally
            {
                lock (stateSync)
                {
                    if (ReferenceEquals(cancellation, owner))
                    {
                        cancellation = null;
                        reconnectTask = null;
                    }
                }

                owner.Dispose();
            }
        }

        private bool IsCurrent(CancellationTokenSource owner)
        {
            lock (stateSync)
            {
                return ReferenceEquals(cancellation, owner) &&
                    !owner.IsCancellationRequested;
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
