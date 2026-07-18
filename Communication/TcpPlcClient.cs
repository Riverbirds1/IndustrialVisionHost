using System;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace IndustrialVisionHost.Communication
{
    public sealed class TcpPlcClient : IDisposable
    {
        private readonly object stateSync = new object();
        private readonly SemaphoreSlim requestSync = new SemaphoreSlim(1, 1);
        private ConnectionContext? connection;

        public event Action<string>? UnsolicitedMessageReceived;

        public event Action<Exception>? ConnectionLost;

        public bool IsConnected
        {
            get
            {
                lock (stateSync)
                {
                    return connection is not null;
                }
            }
        }

        public async Task ConnectAsync(
            string host,
            int port,
            TimeSpan timeout,
            CancellationToken cancellationToken = default)
        {
            ValidateEndpoint(host, port, timeout);

            lock (stateSync)
            {
                if (connection is not null)
                {
                    throw new InvalidOperationException("PLC 客户端已经连接。");
                }
            }

            var tcpClient = new TcpClient { NoDelay = true };

            try
            {
                await tcpClient
                    .ConnectAsync(host.Trim(), port)
                    .WaitAsync(timeout, cancellationToken);

                NetworkStream stream = tcpClient.GetStream();
                var context = new ConnectionContext(tcpClient, stream);

                lock (stateSync)
                {
                    if (connection is not null)
                    {
                        context.Dispose();
                        throw new InvalidOperationException(
                            "连接期间已经建立了另一个 PLC 连接。");
                    }

                    connection = context;
                    context.ReceiveTask = ReceiveLoopAsync(context);
                }
            }
            catch
            {
                tcpClient.Dispose();
                throw;
            }
        }

        public async Task<string> SendRequestAsync(
            string request,
            TimeSpan timeout,
            CancellationToken cancellationToken = default)
        {
            ValidateRequest(request, timeout);
            await requestSync.WaitAsync(cancellationToken);

            ConnectionContext? context = null;
            TaskCompletionSource<string>? responseSource = null;

            try
            {
                lock (stateSync)
                {
                    context = connection
                        ?? throw new InvalidOperationException("PLC 客户端尚未连接。");
                    responseSource = new TaskCompletionSource<string>(
                        TaskCreationOptions.RunContinuationsAsynchronously);
                    context.PendingResponse = responseSource;
                }

                await context.Writer
                    .WriteLineAsync(request)
                    .WaitAsync(timeout, cancellationToken);

                return await responseSource.Task
                    .WaitAsync(timeout, cancellationToken);
            }
            catch (Exception ex)
            {
                if (context is not null)
                {
                    CloseConnection(context, ex, false);
                }

                throw;
            }
            finally
            {
                if (context is not null && responseSource is not null)
                {
                    lock (stateSync)
                    {
                        if (ReferenceEquals(
                                context.PendingResponse,
                                responseSource))
                        {
                            context.PendingResponse = null;
                        }
                    }
                }

                requestSync.Release();
            }
        }

        public void Disconnect()
        {
            ConnectionContext? context;

            lock (stateSync)
            {
                context = connection;
            }

            if (context is not null)
            {
                CloseConnection(context, null, false);
            }
        }

        private async Task ReceiveLoopAsync(ConnectionContext context)
        {
            Exception? disconnectException = null;

            try
            {
                while (!context.Cancellation.IsCancellationRequested)
                {
                    string? message = await context.Reader
                        .ReadLineAsync()
                        .WaitAsync(context.Cancellation.Token);

                    if (message is null)
                    {
                        throw new IOException("PLC 已关闭 TCP 连接。");
                    }

                    if (IsUnsolicitedMessage(message))
                    {
                        UnsolicitedMessageReceived?.Invoke(message);
                        continue;
                    }

                    TaskCompletionSource<string>? pendingResponse;

                    lock (stateSync)
                    {
                        pendingResponse = ReferenceEquals(connection, context)
                            ? context.PendingResponse
                            : null;
                    }

                    if (pendingResponse is not null)
                    {
                        pendingResponse.TrySetResult(message);
                    }
                    else
                    {
                        UnsolicitedMessageReceived?.Invoke(message);
                    }
                }
            }
            catch (OperationCanceledException)
                when (context.Cancellation.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                disconnectException = ex;
            }
            finally
            {
                if (disconnectException is not null)
                {
                    CloseConnection(context, disconnectException, true);
                }
            }
        }

        private void CloseConnection(
            ConnectionContext context,
            Exception? exception,
            bool notifyConnectionLost)
        {
            TaskCompletionSource<string>? pendingResponse;
            bool wasCurrent;

            lock (stateSync)
            {
                wasCurrent = ReferenceEquals(connection, context);
                if (wasCurrent)
                {
                    connection = null;
                }

                pendingResponse = context.PendingResponse;
                context.PendingResponse = null;
            }

            if (!wasCurrent)
            {
                return;
            }

            context.Dispose();

            if (exception is not null)
            {
                pendingResponse?.TrySetException(exception);
            }
            else
            {
                pendingResponse?.TrySetCanceled();
            }

            if (notifyConnectionLost && exception is not null)
            {
                ConnectionLost?.Invoke(exception);
            }
        }

        private static bool IsUnsolicitedMessage(string message)
        {
            return message.StartsWith("START ", StringComparison.OrdinalIgnoreCase);
        }

        private static void ValidateEndpoint(
            string host,
            int port,
            TimeSpan timeout)
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

            if (timeout <= TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(timeout),
                    "连接超时时间必须大于 0。");
            }
        }

        private static void ValidateRequest(string request, TimeSpan timeout)
        {
            if (string.IsNullOrWhiteSpace(request))
            {
                throw new ArgumentException("PLC 请求不能为空。", nameof(request));
            }

            if (request.Contains('\r') || request.Contains('\n'))
            {
                throw new ArgumentException(
                    "单条 PLC 请求不能包含换行符。",
                    nameof(request));
            }

            if (timeout <= TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(timeout),
                    "通信超时时间必须大于 0。");
            }
        }

        public void Dispose()
        {
            Disconnect();
        }

        private sealed class ConnectionContext : IDisposable
        {
            public ConnectionContext(TcpClient client, NetworkStream stream)
            {
                Client = client;
                Stream = stream;
                Cancellation = new CancellationTokenSource();
                Reader = new StreamReader(
                    stream,
                    new UTF8Encoding(false),
                    false,
                    1024,
                    true);
                Writer = new StreamWriter(
                    stream,
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

            public CancellationTokenSource Cancellation { get; }

            public Task? ReceiveTask { get; set; }

            public TaskCompletionSource<string>? PendingResponse { get; set; }

            public void Dispose()
            {
                Cancellation.Cancel();
                TryDispose(Writer);
                TryDispose(Reader);
                TryDispose(Stream);
                TryDispose(Client);
                TryDispose(Cancellation);
            }

            private static void TryDispose(IDisposable disposable)
            {
                try
                {
                    disposable.Dispose();
                }
                catch
                {
                    // 一个网络资源关闭失败时，仍要继续释放其余资源。
                }
            }
        }
    }
}
