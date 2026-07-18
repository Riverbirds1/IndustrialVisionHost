using System;
using System.Threading;
using System.Threading.Tasks;

namespace IndustrialVisionHost.Communication.Modbus
{
    public sealed class ModbusInspectionPollingService : IDisposable
    {
        private readonly object stateSync = new object();
        private readonly ModbusTcpClient client;
        private readonly Func<ushort, Task<bool>> inspectAsync;
        private readonly TimeSpan pollingInterval;
        private CancellationTokenSource? cancellation;

        public ModbusInspectionPollingService(
            ModbusTcpClient client,
            Func<ushort, Task<bool>> inspectAsync,
            TimeSpan? pollingInterval = null)
        {
            this.client = client
                ?? throw new ArgumentNullException(nameof(client));
            this.inspectAsync = inspectAsync
                ?? throw new ArgumentNullException(nameof(inspectAsync));
            this.pollingInterval = pollingInterval ?? TimeSpan.FromMilliseconds(200);

            if (this.pollingInterval <= TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(pollingInterval),
                    "Modbus 轮询间隔必须大于 0。");
            }
        }

        public event Action<string>? StatusChanged;

        public event Action<Exception>? PollingFailed;

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

                if (!client.IsConnected)
                {
                    throw new InvalidOperationException(
                        "启动轮询前必须先连接 Modbus TCP。" );
                }

                cancellation = new CancellationTokenSource();
                _ = PollAsync(cancellation, cancellation.Token);
            }

            StatusChanged?.Invoke(
                $"轮询中，间隔 {pollingInterval.TotalMilliseconds:F0} ms");
        }

        public void Stop()
        {
            CancellationTokenSource? cancellationToStop;

            lock (stateSync)
            {
                cancellationToStop = cancellation;
                cancellation = null;
            }

            cancellationToStop?.Cancel();
            cancellationToStop?.Dispose();
        }

        private async Task PollAsync(
            CancellationTokenSource owner,
            CancellationToken cancellationToken)
        {
            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    bool[] start = await client.ReadCoilsAsync(
                        ModbusRegisterMap.StartRequestCoil,
                        1,
                        TimeSpan.FromSeconds(1),
                        cancellationToken);

                    if (start[0])
                    {
                        ushort[] cycle =
                            await client.ReadHoldingRegistersAsync(
                                ModbusRegisterMap.CycleIdRegister,
                                1,
                                TimeSpan.FromSeconds(1),
                                cancellationToken);
                        await ProcessInspectionAsync(
                            cycle[0],
                            cancellationToken);
                    }

                    await Task.Delay(pollingInterval, cancellationToken);
                }
            }
            catch (OperationCanceledException)
                when (cancellationToken.IsCancellationRequested)
            {
            }
            catch (Exception ex)
            {
                PollingFailed?.Invoke(ex);
            }
            finally
            {
                lock (stateSync)
                {
                    if (ReferenceEquals(cancellation, owner))
                    {
                        cancellation = null;
                    }
                }
            }
        }

        private async Task ProcessInspectionAsync(
            ushort cycleId,
            CancellationToken cancellationToken)
        {
            TimeSpan timeout = TimeSpan.FromSeconds(1);

            await client.WriteSingleHoldingRegisterAsync(
                ModbusRegisterMap.ResultCodeRegister,
                ModbusRegisterMap.ResultNone,
                timeout,
                cancellationToken);
            await client.WriteSingleCoilAsync(
                ModbusRegisterMap.CompletedCoil,
                false,
                timeout,
                cancellationToken);
            await client.WriteSingleCoilAsync(
                ModbusRegisterMap.ResultOkCoil,
                false,
                timeout,
                cancellationToken);
            await client.WriteSingleCoilAsync(
                ModbusRegisterMap.ResultNgCoil,
                false,
                timeout,
                cancellationToken);
            await client.WriteSingleCoilAsync(
                ModbusRegisterMap.BusyCoil,
                true,
                timeout,
                cancellationToken);
            await client.WriteSingleCoilAsync(
                ModbusRegisterMap.StartRequestCoil,
                false,
                timeout,
                cancellationToken);

            StatusChanged?.Invoke($"周期 {cycleId}：BUSY，正在检测");

            bool isOk;

            try
            {
                isOk = await inspectAsync(cycleId);
            }
            catch
            {
                isOk = false;
            }

            await client.WriteSingleHoldingRegisterAsync(
                ModbusRegisterMap.ResultCodeRegister,
                isOk
                    ? ModbusRegisterMap.ResultOk
                    : ModbusRegisterMap.ResultNg,
                timeout,
                cancellationToken);
            await client.WriteSingleCoilAsync(
                ModbusRegisterMap.ResultOkCoil,
                isOk,
                timeout,
                cancellationToken);
            await client.WriteSingleCoilAsync(
                ModbusRegisterMap.ResultNgCoil,
                !isOk,
                timeout,
                cancellationToken);
            await client.WriteSingleCoilAsync(
                ModbusRegisterMap.BusyCoil,
                false,
                timeout,
                cancellationToken);
            await client.WriteSingleCoilAsync(
                ModbusRegisterMap.CompletedCoil,
                true,
                timeout,
                cancellationToken);

            StatusChanged?.Invoke(
                $"周期 {cycleId}：DONE，结果 {(isOk ? "OK" : "NG")}");
        }

        public void Dispose()
        {
            Stop();
        }
    }
}
