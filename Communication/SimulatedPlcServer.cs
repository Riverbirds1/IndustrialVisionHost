using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using IndustrialVisionHost.Communication.Modbus;

namespace IndustrialVisionHost.Communication
{
    public sealed class SimulatedPlcServer : IDisposable
    {
        private readonly object stateSync = new object();
        private readonly List<ClientSession> sessions = new List<ClientSession>();
        private TcpListener? listener;
        private CancellationTokenSource? cancellation;
        private long cycleNumber;
        private long modbusCycleNumber;
        private string? activeCycleId;

        public SimulatedPlcServer(ModbusDataStore? dataStore = null)
        {
            DataStore = dataStore ?? new ModbusDataStore();
            ResetHandshakeRegisters();
        }

        public ModbusDataStore DataStore { get; }

        public event Action<string, string>? MessageProcessed;

        public event Action<Exception>? ClientFaulted;

        public event Action<string>? HandshakeStateChanged;

        public bool IsRunning
        {
            get
            {
                lock (stateSync)
                {
                    return listener is not null;
                }
            }
        }

        public int ListeningPort { get; private set; }

        public void Start(int port)
        {
            if (port < 0 || port > 65535)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(port),
                    "TCP 端口必须在 0～65535 之间。");
            }

            lock (stateSync)
            {
                if (listener is not null)
                {
                    throw new InvalidOperationException("模拟 PLC 已经在运行。");
                }

                var newListener = new TcpListener(IPAddress.Loopback, port);
                newListener.Start();
                var newCancellation = new CancellationTokenSource();
                listener = newListener;
                cancellation = newCancellation;
                ListeningPort = ((IPEndPoint)newListener.LocalEndpoint).Port;
                _ = AcceptClientsAsync(newListener, newCancellation.Token);
            }
        }

        public void Stop()
        {
            TcpListener? listenerToStop;
            CancellationTokenSource? cancellationToStop;
            ClientSession[] sessionsToClose;

            lock (stateSync)
            {
                listenerToStop = listener;
                cancellationToStop = cancellation;
                listener = null;
                cancellation = null;
                ListeningPort = 0;
                activeCycleId = null;
                sessionsToClose = sessions.ToArray();
                sessions.Clear();
            }

            cancellationToStop?.Cancel();
            listenerToStop?.Stop();

            foreach (ClientSession session in sessionsToClose)
            {
                session.Dispose();
            }

            cancellationToStop?.Dispose();
            ResetHandshakeRegisters();
        }

        public async Task<string> TriggerInspectionAsync(
            CancellationToken cancellationToken = default)
        {
            ClientSession[] activeSessions;
            string cycleId;

            lock (stateSync)
            {
                if (activeCycleId is not null)
                {
                    throw new InvalidOperationException(
                        $"检测周期 {activeCycleId} 尚未完成。");
                }

                activeSessions = sessions.ToArray();
                if (activeSessions.Length == 0)
                {
                    throw new InvalidOperationException(
                        "没有已连接的上位机客户端。");
                }

                cycleId = $"C{Interlocked.Increment(ref cycleNumber):D6}";
                activeCycleId = cycleId;
                ushort registerCycleId =
                    (ushort)(cycleNumber % (ushort.MaxValue + 1L));
                DataStore.WriteHoldingRegister(
                    ModbusRegisterMap.CycleIdRegister,
                    registerCycleId);
                DataStore.WriteHoldingRegister(
                    ModbusRegisterMap.ResultCodeRegister,
                    ModbusRegisterMap.ResultNone);
                DataStore.WriteCoils(
                    ModbusRegisterMap.StartRequestCoil,
                    new[] { true, false, false, false, false });
            }

            try
            {
                foreach (ClientSession session in activeSessions)
                {
                    await session.SendAsync(
                        $"START {cycleId}",
                        cancellationToken);
                }

                HandshakeStateChanged?.Invoke(
                    $"{cycleId}：PLC 已发送 START");
                return cycleId;
            }
            catch
            {
                lock (stateSync)
                {
                    if (activeCycleId == cycleId)
                    {
                        activeCycleId = null;
                        ResetHandshakeRegisters();
                    }
                }

                throw;
            }
        }

        public ushort TriggerModbusInspection()
        {
            ushort cycleId;

            lock (stateSync)
            {
                if (listener is null)
                {
                    throw new InvalidOperationException(
                        "模拟 PLC 尚未启动。");
                }

                bool[] handshake = DataStore.ReadCoils(
                    ModbusRegisterMap.StartRequestCoil,
                    2);
                if (activeCycleId is not null ||
                    handshake[0] ||
                    handshake[1])
                {
                    throw new InvalidOperationException(
                        "上一个检测周期尚未进入完成状态。");
                }

                cycleId = unchecked((ushort)Interlocked.Increment(
                    ref modbusCycleNumber));
                if (cycleId == 0)
                {
                    cycleId = unchecked((ushort)Interlocked.Increment(
                        ref modbusCycleNumber));
                }

                DataStore.WriteHoldingRegister(
                    ModbusRegisterMap.CycleIdRegister,
                    cycleId);
                DataStore.WriteHoldingRegister(
                    ModbusRegisterMap.ResultCodeRegister,
                    ModbusRegisterMap.ResultNone);
                DataStore.WriteCoils(
                    ModbusRegisterMap.StartRequestCoil,
                    new[] { true, false, false, false, false });
            }

            HandshakeStateChanged?.Invoke(
                $"Modbus 周期 {cycleId}：PLC 已置 START");
            return cycleId;
        }

        private async Task AcceptClientsAsync(
            TcpListener activeListener,
            CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    TcpClient client = await activeListener.AcceptTcpClientAsync();
                    client.NoDelay = true;
                    _ = HandleClientAsync(client, cancellationToken);
                }
                catch (ObjectDisposedException)
                    when (cancellationToken.IsCancellationRequested)
                {
                    return;
                }
                catch (SocketException)
                    when (cancellationToken.IsCancellationRequested)
                {
                    return;
                }
            }
        }

        private async Task HandleClientAsync(
            TcpClient client,
            CancellationToken cancellationToken)
        {
            ClientSession? session = null;

            try
            {
                session = new ClientSession(client);

                lock (stateSync)
                {
                    sessions.Add(session);
                }

                while (!cancellationToken.IsCancellationRequested)
                {
                    string? request = await session.Reader
                        .ReadLineAsync()
                        .WaitAsync(cancellationToken);

                    if (request is null)
                    {
                        return;
                    }

                    string response = ProcessRequest(request);
                    await session.SendAsync(response, cancellationToken);
                    MessageProcessed?.Invoke(request, response);
                }
            }
            catch (OperationCanceledException)
                when (cancellationToken.IsCancellationRequested)
            {
            }
            catch (ObjectDisposedException)
                when (cancellationToken.IsCancellationRequested)
            {
            }
            catch (IOException)
                when (cancellationToken.IsCancellationRequested)
            {
            }
            catch (IOException ex)
            {
                ClientFaulted?.Invoke(ex);
            }
            catch (SocketException)
                when (cancellationToken.IsCancellationRequested)
            {
            }
            catch (SocketException ex)
            {
                ClientFaulted?.Invoke(ex);
            }
            finally
            {
                if (session is not null)
                {
                    lock (stateSync)
                    {
                        sessions.Remove(session);
                    }

                    session.Dispose();
                }
                else
                {
                    client.Dispose();
                }
            }
        }

        private string ProcessRequest(string request)
        {
            if (request.Length > 256)
            {
                return "ERR MESSAGE_TOO_LONG";
            }

            string trimmed = request.Trim();

            if (string.Equals(trimmed, "PING", StringComparison.OrdinalIgnoreCase))
            {
                return "PONG";
            }

            if (string.Equals(
                    trimmed,
                    "HEARTBEAT",
                    StringComparison.OrdinalIgnoreCase))
            {
                return "ALIVE";
            }

            string[] parts = trimmed.Split(
                ' ',
                StringSplitOptions.RemoveEmptyEntries);

            if (parts.Length == 2 &&
                string.Equals(parts[0], "BUSY", StringComparison.OrdinalIgnoreCase))
            {
                return ProcessBusy(parts[1]);
            }

            if (parts.Length == 3 &&
                string.Equals(parts[0], "RESULT", StringComparison.OrdinalIgnoreCase))
            {
                return ProcessResult(parts[1], parts[2]);
            }

            return string.IsNullOrWhiteSpace(request)
                ? "ERR EMPTY_COMMAND"
                : "ERR UNKNOWN_COMMAND";
        }

        private string ProcessBusy(string cycleId)
        {
            lock (stateSync)
            {
                if (activeCycleId != cycleId)
                {
                    return "ERR CYCLE_MISMATCH";
                }
            }

            DataStore.WriteCoils(
                ModbusRegisterMap.StartRequestCoil,
                new[] { false, true });
            HandshakeStateChanged?.Invoke($"{cycleId}：上位机已返回 BUSY");
            return $"ACK BUSY {cycleId}";
        }

        private string ProcessResult(string cycleId, string result)
        {
            if (result != "OK" && result != "NG")
            {
                return "ERR INVALID_RESULT";
            }

            lock (stateSync)
            {
                if (activeCycleId != cycleId)
                {
                    return "ERR CYCLE_MISMATCH";
                }

                activeCycleId = null;
            }

            bool isOk = result == "OK";
            DataStore.WriteCoils(
                ModbusRegisterMap.StartRequestCoil,
                new[] { false, false, true, isOk, !isOk });
            DataStore.WriteHoldingRegister(
                ModbusRegisterMap.ResultCodeRegister,
                isOk ? ModbusRegisterMap.ResultOk : ModbusRegisterMap.ResultNg);

            HandshakeStateChanged?.Invoke(
                $"{cycleId}：PLC 收到 RESULT {result}");
            return $"ACK RESULT {cycleId}";
        }

        private void ResetHandshakeRegisters()
        {
            DataStore.WriteCoils(
                ModbusRegisterMap.StartRequestCoil,
                new[] { false, false, false, false, false });
            DataStore.WriteHoldingRegister(
                ModbusRegisterMap.CycleIdRegister,
                0);
            DataStore.WriteHoldingRegister(
                ModbusRegisterMap.ResultCodeRegister,
                ModbusRegisterMap.ResultNone);
        }

        public void Dispose()
        {
            Stop();
        }

        private sealed class ClientSession : IDisposable
        {
            private readonly SemaphoreSlim sendSync = new SemaphoreSlim(1, 1);

            public ClientSession(TcpClient client)
            {
                Client = client;
                Stream = client.GetStream();
                Reader = new StreamReader(
                    Stream,
                    new UTF8Encoding(false),
                    false,
                    1024,
                    true);
                Writer = new StreamWriter(
                    Stream,
                    new UTF8Encoding(false),
                    1024,
                    true)
                {
                    AutoFlush = true,
                    NewLine = "\n"
                };
            }

            public TcpClient Client { get; }

            public NetworkStream Stream { get; }

            public StreamReader Reader { get; }

            public StreamWriter Writer { get; }

            public async Task SendAsync(
                string message,
                CancellationToken cancellationToken)
            {
                await sendSync.WaitAsync(cancellationToken);

                try
                {
                    await Writer
                        .WriteLineAsync(message)
                        .WaitAsync(cancellationToken);
                }
                finally
                {
                    sendSync.Release();
                }
            }

            public void Dispose()
            {
                Client.Dispose();
            }
        }
    }
}
