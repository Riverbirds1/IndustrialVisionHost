using IndustrialVisionHost.Communication;

namespace IndustrialVisionHost.Tests;

public sealed class TcpPlcIntegrationTests
{
    [Fact]
    public async Task ClientAndServer_ExchangeRequestsAndCompleteHandshake()
    {
        using var server = new SimulatedPlcServer();
        using var client = new TcpPlcClient();
        server.Start(0);
        await client.ConnectAsync(
            "127.0.0.1",
            server.ListeningPort,
            TimeSpan.FromSeconds(2));

        Assert.Equal("PONG", await client.SendRequestAsync(
            "PING", TimeSpan.FromSeconds(2)));
        Assert.Equal("ERR UNKNOWN_COMMAND", await client.SendRequestAsync(
            "UNKNOWN", TimeSpan.FromSeconds(2)));

        var startReceived = NewCompletion<string>();
        client.UnsolicitedMessageReceived += message =>
        {
            if (message.StartsWith("START ", StringComparison.Ordinal))
            {
                startReceived.TrySetResult(message);
            }
        };

        string cycleId = await server.TriggerInspectionAsync();
        Assert.Equal(
            $"START {cycleId}",
            await startReceived.Task.WaitAsync(TimeSpan.FromSeconds(2)));
        Assert.Equal(
            $"ACK BUSY {cycleId}",
            await client.SendRequestAsync(
                $"BUSY {cycleId}",
                TimeSpan.FromSeconds(2)));
        Assert.Equal(
            $"ACK RESULT {cycleId}",
            await client.SendRequestAsync(
                $"RESULT {cycleId} OK",
                TimeSpan.FromSeconds(2)));

        bool[] coils = server.DataStore.ReadCoils(0, 5);
        Assert.Equal(new[] { false, false, true, true, false }, coils);
    }

    [Fact]
    public async Task Heartbeat_DetectsHealthyLinkAndServerShutdown()
    {
        using var server = new SimulatedPlcServer();
        using var client = new TcpPlcClient();
        using var monitor = new PlcHeartbeatMonitor(
            client,
            TimeSpan.FromMilliseconds(50),
            TimeSpan.FromMilliseconds(500));
        server.Start(0);
        await client.ConnectAsync(
            "127.0.0.1",
            server.ListeningPort,
            TimeSpan.FromSeconds(2));

        var succeeded = NewCompletion<TimeSpan>();
        var failed = NewCompletion<Exception>();
        monitor.HeartbeatSucceeded += elapsed => succeeded.TrySetResult(elapsed);
        monitor.HeartbeatFailed += error => failed.TrySetResult(error);
        monitor.Start();

        TimeSpan elapsed = await succeeded.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.True(elapsed >= TimeSpan.Zero);
        Assert.True(client.IsConnected);

        server.Stop();
        Exception exception = await failed.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.NotNull(exception);
        Assert.False(client.IsConnected);
    }

    [Fact]
    public async Task AutoReconnect_RecoversAfterServerComesOnline()
    {
        int port = NetworkTestHelper.GetFreeTcpPort();
        using var client = new TcpPlcClient();
        using var reconnect = new PlcAutoReconnectService(
            client,
            TimeSpan.FromMilliseconds(50),
            TimeSpan.FromMilliseconds(100),
            TimeSpan.FromMilliseconds(200));
        using var server = new SimulatedPlcServer();
        var firstFailure = NewCompletion<int>();
        var reconnected = NewCompletion<int>();
        reconnect.ReconnectAttemptFailed +=
            (attempt, _, _) => firstFailure.TrySetResult(attempt);
        reconnect.Reconnected += attempt => reconnected.TrySetResult(attempt);

        reconnect.Start("127.0.0.1", port);
        Assert.Equal(
            1,
            await firstFailure.Task.WaitAsync(TimeSpan.FromSeconds(2)));
        server.Start(port);

        int successfulAttempt =
            await reconnected.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.True(successfulAttempt >= 2);
        Assert.True(client.IsConnected);
        Assert.Equal("PONG", await client.SendRequestAsync(
            "PING", TimeSpan.FromSeconds(2)));
    }

    private static TaskCompletionSource<T> NewCompletion<T>()
    {
        return new TaskCompletionSource<T>(
            TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
