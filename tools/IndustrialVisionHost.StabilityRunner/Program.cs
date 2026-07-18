using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using IndustrialVisionHost.Camera;
using IndustrialVisionHost.Communication;
using IndustrialVisionHost.Communication.Modbus;
using IndustrialVisionHost.Models;
using IndustrialVisionHost.Services;
using IndustrialVisionHost.Vision;

namespace IndustrialVisionHost.StabilityRunner;

internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        try
        {
            StabilityRunnerOptions options = StabilityRunnerOptions.Parse(args);
            using var cancellation = new CancellationTokenSource();
            Console.CancelKeyPress += (_, eventArgs) =>
            {
                eventArgs.Cancel = true;
                cancellation.Cancel();
            };

            return await RunAsync(options, cancellation.Token);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"长稳运行器启动失败：{ex}");
            return 2;
        }
    }

    private static async Task<int> RunAsync(
        StabilityRunnerOptions options,
        CancellationToken cancellationToken)
    {
        DateTime startedAtUtc = DateTime.UtcNow;
        string runDirectory = Path.Combine(
            options.OutputDirectory,
            startedAtUtc.ToString("yyyyMMdd_HHmmss"));
        Directory.CreateDirectory(runDirectory);
        string samplesPath = Path.Combine(runDirectory, "samples.csv");
        string summaryPath = Path.Combine(runDirectory, "summary.json");
        string databasePath = Path.Combine(runDirectory, "stability-history.db");
        WriteCsvHeader(samplesPath);

        using var camera = new FakeCamera();
        using var textServer = new SimulatedPlcServer();
        using var textClient = new TcpPlcClient();
        using var modbusServer = new ModbusTcpServer(new ModbusDataStore());
        using var modbusClient = new ModbusTcpClient();
        var history = new InspectionHistoryService(databasePath);
        var counters = new Counters();
        var samples = new List<StabilitySample>();
        VisionParameters parameters = CreateParameters();

        if (!history.TryInitialize(out string? databaseError))
        {
            throw new InvalidOperationException(
                $"稳定性数据库初始化失败：{databaseError}");
        }

        if (!camera.Open())
        {
            throw new InvalidOperationException("模拟相机无法打开。");
        }

        textServer.Start(0);
        modbusServer.Start(0);
        await textClient.ConnectAsync(
            "127.0.0.1",
            textServer.ListeningPort,
            TimeSpan.FromSeconds(2),
            cancellationToken);
        await modbusClient.ConnectAsync(
            "127.0.0.1",
            modbusServer.ListeningPort,
            TimeSpan.FromSeconds(2),
            cancellationToken);

        using Process process = Process.GetCurrentProcess();
        process.Refresh();
        long initialPrivateMemory = process.PrivateMemorySize64;
        long peakPrivateMemory = initialPrivateMemory;
        var runStopwatch = Stopwatch.StartNew();
        TimeSpan nextSample = TimeSpan.Zero;
        TimeSpan requestedDuration = TimeSpan.FromSeconds(
            options.DurationSeconds);

        Console.WriteLine(
            $"长稳运行开始：{options.DurationSeconds}秒，" +
            $"采样间隔{options.SampleSeconds}秒，" +
            $"故障周期{options.FaultEveryCycles}。" );
        Console.WriteLine($"输出目录：{runDirectory}");

        while (runStopwatch.Elapsed < requestedDuration &&
               !cancellationToken.IsCancellationRequested)
        {
            counters.Cycles++;
            await ExecuteNormalCycleAsync(
                camera,
                parameters,
                textClient,
                modbusClient,
                history,
                counters,
                startedAtUtc,
                cancellationToken);

            if (options.FaultEveryCycles > 0 &&
                counters.Cycles % options.FaultEveryCycles == 0)
            {
                await InjectAndRecoverAsync(
                    camera,
                    textServer,
                    textClient,
                    modbusServer,
                    modbusClient,
                    counters,
                    cancellationToken);
            }

            if (runStopwatch.Elapsed >= nextSample)
            {
                StabilitySample sample = CaptureSample(
                    process,
                    runStopwatch.Elapsed,
                    counters);
                samples.Add(sample);
                AppendSample(samplesPath, sample);
                peakPrivateMemory = Math.Max(
                    peakPrivateMemory,
                    sample.PrivateMemoryBytes);
                PrintSample(sample);
                nextSample = runStopwatch.Elapsed +
                    TimeSpan.FromSeconds(options.SampleSeconds);
            }

            try
            {
                await Task.Delay(
                    options.CycleDelayMilliseconds,
                    cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        runStopwatch.Stop();
        StabilitySample finalSample = CaptureSample(
            process,
            runStopwatch.Elapsed,
            counters);
        if (samples.Count == 0 ||
            samples[^1].Cycles != finalSample.Cycles)
        {
            samples.Add(finalSample);
            AppendSample(samplesPath, finalSample);
        }

        peakPrivateMemory = Math.Max(
            peakPrivateMemory,
            finalSample.PrivateMemoryBytes);
        bool passed = counters.UnexpectedErrors == 0 &&
            counters.Recoveries == counters.InjectedFaults;
        var summary = new StabilitySummary
        {
            StartedAtUtc = startedAtUtc,
            FinishedAtUtc = DateTime.UtcNow,
            RequestedDurationSeconds = options.DurationSeconds,
            ActualDurationSeconds = runStopwatch.Elapsed.TotalSeconds,
            Cycles = counters.Cycles,
            VisionAverageMilliseconds = counters.VisionAverageMilliseconds,
            VisionMaximumMilliseconds = counters.VisionMaximumMilliseconds,
            InitialPrivateMemoryBytes = initialPrivateMemory,
            FinalPrivateMemoryBytes = finalSample.PrivateMemoryBytes,
            PeakPrivateMemoryBytes = peakPrivateMemory,
            TextSuccesses = counters.TextSuccesses,
            ModbusSuccesses = counters.ModbusSuccesses,
            DatabaseWrites = counters.DatabaseWrites,
            InjectedFaults = counters.InjectedFaults,
            Recoveries = counters.Recoveries,
            UnexpectedErrors = counters.UnexpectedErrors,
            Passed = passed
        };
        File.WriteAllText(
            summaryPath,
            JsonSerializer.Serialize(
                summary,
                new JsonSerializerOptions { WriteIndented = true }),
            new UTF8Encoding(false));

        Console.WriteLine(
            $"长稳运行结束：cycles={summary.Cycles}，" +
            $"visionAvg={summary.VisionAverageMilliseconds:F3}ms，" +
            $"faults={summary.InjectedFaults}，recoveries={summary.Recoveries}，" +
            $"unexpected={summary.UnexpectedErrors}，passed={summary.Passed}。" );
        Console.WriteLine($"汇总报告：{summaryPath}");
        return passed ? 0 : 1;
    }

    private static async Task ExecuteNormalCycleAsync(
        FakeCamera camera,
        VisionParameters parameters,
        TcpPlcClient textClient,
        ModbusTcpClient modbusClient,
        InspectionHistoryService history,
        Counters counters,
        DateTime startedAtUtc,
        CancellationToken cancellationToken)
    {
        try
        {
            var visionStopwatch = Stopwatch.StartNew();
            using var frame = camera.Capture();
            using VisionProcessingResult processing = VisionProcessor.Process(
                frame,
                parameters,
                VisionDebugView.Annotated);
            visionStopwatch.Stop();
            counters.AddVisionTime(visionStopwatch.Elapsed.TotalMilliseconds);

            string response = await textClient.SendRequestAsync(
                "PING",
                TimeSpan.FromSeconds(1),
                cancellationToken);
            if (response != "PONG")
            {
                throw new InvalidDataException($"PING响应异常：{response}");
            }

            counters.TextSuccesses++;
            ushort registerValue = unchecked((ushort)counters.Cycles);
            await modbusClient.WriteSingleHoldingRegisterAsync(
                10,
                registerValue,
                TimeSpan.FromSeconds(1),
                cancellationToken);
            ushort[] values = await modbusClient.ReadHoldingRegistersAsync(
                10,
                1,
                TimeSpan.FromSeconds(1),
                cancellationToken);
            if (values.Length != 1 || values[0] != registerValue)
            {
                throw new InvalidDataException("Modbus读写回读不一致。");
            }

            counters.ModbusSuccesses += 2;
            if (counters.Cycles % 10 == 0)
            {
                if (!history.TrySave(
                        CreateHistoryRecord(
                            startedAtUtc,
                            counters.Cycles,
                            processing.Inspection),
                        out string? error))
                {
                    throw new InvalidOperationException(
                        $"稳定性历史写入失败：{error}");
                }

                counters.DatabaseWrites++;
            }
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            counters.UnexpectedErrors++;
            Console.Error.WriteLine(
                $"[{DateTime.Now:HH:mm:ss}] 普通周期失败：{ex.Message}");
        }
    }

    private static async Task InjectAndRecoverAsync(
        FakeCamera camera,
        SimulatedPlcServer textServer,
        TcpPlcClient textClient,
        ModbusTcpServer modbusServer,
        ModbusTcpClient modbusClient,
        Counters counters,
        CancellationToken cancellationToken)
    {
        await InjectCameraFailureAsync(camera, counters);
        await InjectTextFailureAsync(
            textServer, textClient, counters, cancellationToken);
        await InjectModbusFailureAsync(
            modbusServer, modbusClient, counters, cancellationToken);
    }

    private static Task InjectCameraFailureAsync(
        FakeCamera camera,
        Counters counters)
    {
        counters.InjectedFaults++;
        camera.Scenario = FakeCameraScenario.CaptureFailure;
        camera.Close();
        camera.Open();
        bool failureObserved = false;
        try
        {
            for (int index = 0; index < 6; index++)
            {
                using var frame = camera.Capture();
            }
        }
        catch (InvalidOperationException)
        {
            failureObserved = true;
        }

        camera.Scenario = FakeCameraScenario.StandardSingle;
        if (failureObserved && camera.Open())
        {
            counters.Recoveries++;
        }
        else
        {
            counters.UnexpectedErrors++;
        }

        return Task.CompletedTask;
    }

    private static async Task InjectTextFailureAsync(
        SimulatedPlcServer server,
        TcpPlcClient client,
        Counters counters,
        CancellationToken cancellationToken)
    {
        counters.InjectedFaults++;
        int port = server.ListeningPort;
        server.Stop();
        await Task.Delay(20, cancellationToken);
        try
        {
            await client.SendRequestAsync(
                "PING", TimeSpan.FromMilliseconds(300), cancellationToken);
        }
        catch
        {
        }

        client.Disconnect();
        server.Start(port);
        await client.ConnectAsync(
            "127.0.0.1", port, TimeSpan.FromSeconds(1), cancellationToken);
        if (await client.SendRequestAsync(
                "PING", TimeSpan.FromSeconds(1), cancellationToken) == "PONG")
        {
            counters.Recoveries++;
            counters.TextSuccesses++;
        }
        else
        {
            counters.UnexpectedErrors++;
        }
    }

    private static async Task InjectModbusFailureAsync(
        ModbusTcpServer server,
        ModbusTcpClient client,
        Counters counters,
        CancellationToken cancellationToken)
    {
        counters.InjectedFaults++;
        int port = server.ListeningPort;
        server.Stop();
        await Task.Delay(20, cancellationToken);
        try
        {
            await client.ReadHoldingRegistersAsync(
                10, 1, TimeSpan.FromMilliseconds(300), cancellationToken);
        }
        catch
        {
        }

        client.Disconnect();
        server.Start(port);
        await client.ConnectAsync(
            "127.0.0.1", port, TimeSpan.FromSeconds(1), cancellationToken);
        await client.WriteSingleHoldingRegisterAsync(
            10, 1234, TimeSpan.FromSeconds(1), cancellationToken);
        ushort[] values = await client.ReadHoldingRegistersAsync(
            10, 1, TimeSpan.FromSeconds(1), cancellationToken);
        if (values.Length == 1 && values[0] == 1234)
        {
            counters.Recoveries++;
            counters.ModbusSuccesses += 2;
        }
        else
        {
            counters.UnexpectedErrors++;
        }
    }

    private static StabilitySample CaptureSample(
        Process process,
        TimeSpan elapsed,
        Counters counters)
    {
        process.Refresh();
        return new StabilitySample
        {
            TimestampUtc = DateTime.UtcNow,
            ElapsedSeconds = elapsed.TotalSeconds,
            Cycles = counters.Cycles,
            VisionAverageMilliseconds = counters.VisionAverageMilliseconds,
            VisionMaximumMilliseconds = counters.VisionMaximumMilliseconds,
            PrivateMemoryBytes = process.PrivateMemorySize64,
            WorkingSetBytes = process.WorkingSet64,
            ManagedHeapBytes = GC.GetTotalMemory(false),
            ThreadCount = process.Threads.Count,
            HandleCount = process.HandleCount,
            TextSuccesses = counters.TextSuccesses,
            ModbusSuccesses = counters.ModbusSuccesses,
            DatabaseWrites = counters.DatabaseWrites,
            InjectedFaults = counters.InjectedFaults,
            Recoveries = counters.Recoveries,
            UnexpectedErrors = counters.UnexpectedErrors
        };
    }

    private static void WriteCsvHeader(string path)
    {
        File.WriteAllText(
            path,
            "timestamp_utc,elapsed_seconds,cycles,vision_avg_ms,vision_max_ms," +
            "private_memory_bytes,working_set_bytes,managed_heap_bytes," +
            "thread_count,handle_count,text_successes,modbus_successes," +
            "database_writes,injected_faults,recoveries,unexpected_errors" +
            Environment.NewLine,
            new UTF8Encoding(false));
    }

    private static void AppendSample(string path, StabilitySample sample)
    {
        string line = string.Join(
            ',',
            sample.TimestampUtc.ToString("O"),
            sample.ElapsedSeconds.ToString("F3", CultureInfo.InvariantCulture),
            sample.Cycles,
            sample.VisionAverageMilliseconds.ToString("F4", CultureInfo.InvariantCulture),
            sample.VisionMaximumMilliseconds.ToString("F4", CultureInfo.InvariantCulture),
            sample.PrivateMemoryBytes,
            sample.WorkingSetBytes,
            sample.ManagedHeapBytes,
            sample.ThreadCount,
            sample.HandleCount,
            sample.TextSuccesses,
            sample.ModbusSuccesses,
            sample.DatabaseWrites,
            sample.InjectedFaults,
            sample.Recoveries,
            sample.UnexpectedErrors);
        File.AppendAllText(path, line + Environment.NewLine, new UTF8Encoding(false));
    }

    private static void PrintSample(StabilitySample sample)
    {
        Console.WriteLine(
            $"[{sample.ElapsedSeconds,7:F1}s] cycles={sample.Cycles} " +
            $"vision={sample.VisionAverageMilliseconds:F2}/{sample.VisionMaximumMilliseconds:F2}ms " +
            $"private={sample.PrivateMemoryBytes / 1024.0 / 1024.0:F1}MB " +
            $"threads={sample.ThreadCount} handles={sample.HandleCount} " +
            $"faults={sample.InjectedFaults} recovered={sample.Recoveries} " +
            $"errors={sample.UnexpectedErrors}");
    }

    private static VisionParameters CreateParameters()
    {
        return new VisionParameters(
            100, 1, 1000, 20000,
            false, 0, 0, 640, 480,
            3, 3, true, true,
            50, 0.7,
            0.05, 5, 8, 5, 8);
    }

    private static InspectionHistoryRecord CreateHistoryRecord(
        DateTime startedAtUtc,
        long cycle,
        InspectionResult result)
    {
        return new InspectionHistoryRecord
        {
            BatchNumber = "STABILITY",
            RecipeName = "长稳基线配方",
            RecipeRevision = 1,
            DetectedAtUtc = startedAtUtc.AddMilliseconds(cycle),
            TriggerSource = "StabilityRunner",
            CycleId = $"S{cycle:D8}",
            OperationMode = "Automatic",
            IsOk = result.IsOK,
            JudgementCode = result.JudgementCode.ToString(),
            JudgementReason = result.JudgementReason,
            TargetCount = result.Count,
            RawContourCount = result.RawContourCount,
            AreaPixels = result.Area,
            PhysicalAreaSquareMillimeters = result.PhysicalArea,
            CenterXPixel = result.CenterX,
            CenterYPixel = result.CenterY,
            CenterXMillimeters = result.CenterXMillimeters,
            CenterYMillimeters = result.CenterYMillimeters,
            WidthPixels = result.WidthPixels,
            HeightPixels = result.HeightPixels,
            WidthMillimeters = result.WidthMillimeters,
            HeightMillimeters = result.HeightMillimeters,
            ProcessingTimeMilliseconds = result.ProcessingTimeMs
        };
    }

    private sealed class Counters
    {
        private double visionTotalMilliseconds;
        private long visionSamples;
        public long Cycles { get; set; }
        public double VisionMaximumMilliseconds { get; private set; }
        public long TextSuccesses { get; set; }
        public long ModbusSuccesses { get; set; }
        public long DatabaseWrites { get; set; }
        public long InjectedFaults { get; set; }
        public long Recoveries { get; set; }
        public long UnexpectedErrors { get; set; }
        public double VisionAverageMilliseconds => visionSamples == 0
            ? 0
            : visionTotalMilliseconds / visionSamples;

        public void AddVisionTime(double milliseconds)
        {
            visionTotalMilliseconds += milliseconds;
            visionSamples++;
            VisionMaximumMilliseconds = Math.Max(
                VisionMaximumMilliseconds,
                milliseconds);
        }
    }
}
