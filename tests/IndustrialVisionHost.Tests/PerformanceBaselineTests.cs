using System.Diagnostics;
using IndustrialVisionHost.Camera;
using IndustrialVisionHost.Communication;
using IndustrialVisionHost.Communication.Modbus;
using IndustrialVisionHost.Models;
using IndustrialVisionHost.Services;
using IndustrialVisionHost.Vision;
using Xunit.Abstractions;

namespace IndustrialVisionHost.Tests;

public sealed class PerformanceBaselineTests
{
    private readonly ITestOutputHelper output;

    public PerformanceBaselineTests(ITestOutputHelper output)
    {
        this.output = output;
    }

    [Fact]
    [Trait("Category", "Stability")]
    public void VisionProcessing_ThreeHundredCycles_StaysWithinBaseline()
    {
        const int warmupCycles = 20;
        const int measuredCycles = 300;
        using var camera = new FakeCamera
        {
            Scenario = FakeCameraScenario.DynamicDemo
        };
        Assert.True(camera.Open());
        VisionParameters parameters = CreatePermissiveParameters();

        for (int index = 0; index < warmupCycles; index++)
        {
            using var frame = camera.Capture();
            using VisionProcessingResult result = VisionProcessor.Process(
                frame,
                parameters,
                VisionDebugView.Binary);
        }

        ForceGarbageCollection();
        using Process process = Process.GetCurrentProcess();
        process.Refresh();
        long memoryBefore = process.PrivateMemorySize64;
        var stopwatch = Stopwatch.StartNew();

        for (int index = 0; index < measuredCycles; index++)
        {
            using var frame = camera.Capture();
            using VisionProcessingResult result = VisionProcessor.Process(
                frame,
                parameters,
                index % 2 == 0
                    ? VisionDebugView.Annotated
                    : VisionDebugView.Morphology);
            Assert.False(result.AnnotatedImage.Empty());
        }

        stopwatch.Stop();
        ForceGarbageCollection();
        process.Refresh();
        long memoryAfter = process.PrivateMemorySize64;
        long memoryGrowth = Math.Max(0, memoryAfter - memoryBefore);
        double averageMilliseconds =
            stopwatch.Elapsed.TotalMilliseconds / measuredCycles;

        output.WriteLine(
            $"VISION cycles={measuredCycles} totalMs={stopwatch.Elapsed.TotalMilliseconds:F2} " +
            $"avgMs={averageMilliseconds:F3} privateMemoryGrowthMB=" +
            $"{memoryGrowth / 1024.0 / 1024.0:F2}");
        Assert.True(
            averageMilliseconds < 25,
            $"视觉平均耗时 {averageMilliseconds:F3} ms 超过25 ms基线。");
        Assert.True(
            memoryGrowth < 128L * 1024 * 1024,
            $"私有内存增长 {memoryGrowth / 1024.0 / 1024.0:F2} MB 超过128 MB基线。");
    }

    [Fact]
    [Trait("Category", "Stability")]
    public async Task TcpAndModbus_FourHundredRequests_StayWithinBaseline()
    {
        const int textRequests = 200;
        const int modbusCycles = 100;
        using var textServer = new SimulatedPlcServer();
        using var textClient = new TcpPlcClient();
        using var modbusServer = new ModbusTcpServer(new ModbusDataStore());
        using var modbusClient = new ModbusTcpClient();
        textServer.Start(0);
        modbusServer.Start(0);
        await textClient.ConnectAsync(
            "127.0.0.1", textServer.ListeningPort, TimeSpan.FromSeconds(2));
        await modbusClient.ConnectAsync(
            "127.0.0.1", modbusServer.ListeningPort, TimeSpan.FromSeconds(2));

        var stopwatch = Stopwatch.StartNew();
        for (int index = 0; index < textRequests; index++)
        {
            Assert.Equal("PONG", await textClient.SendRequestAsync(
                "PING", TimeSpan.FromSeconds(2)));
        }

        for (int index = 0; index < modbusCycles; index++)
        {
            ushort value = (ushort)(1000 + index);
            await modbusClient.WriteSingleHoldingRegisterAsync(
                10, value, TimeSpan.FromSeconds(2));
            ushort[] values = await modbusClient.ReadHoldingRegistersAsync(
                10, 1, TimeSpan.FromSeconds(2));
            Assert.Equal(value, Assert.Single(values));
        }

        stopwatch.Stop();
        int totalRequests = textRequests + modbusCycles * 2;
        double averageMilliseconds =
            stopwatch.Elapsed.TotalMilliseconds / totalRequests;
        output.WriteLine(
            $"COMM requests={totalRequests} totalMs={stopwatch.Elapsed.TotalMilliseconds:F2} " +
            $"avgMs={averageMilliseconds:F3}");
        Assert.True(
            averageMilliseconds < 20,
            $"本机通信平均耗时 {averageMilliseconds:F3} ms 超过20 ms基线。");
    }

    [Fact]
    [Trait("Category", "Stability")]
    public void Sqlite_FiveHundredWritesAndQuery_StayWithinBaseline()
    {
        const int recordCount = 500;
        using var directory = new TemporaryDirectory();
        var service = new InspectionHistoryService(directory.File("stress.db"));
        Assert.True(service.TryInitialize(out string? error), error);
        DateTime start = new DateTime(2026, 7, 18, 0, 0, 0, DateTimeKind.Utc);
        var stopwatch = Stopwatch.StartNew();

        for (int index = 0; index < recordCount; index++)
        {
            Assert.True(service.TrySave(
                CreateHistoryRecord(start.AddMilliseconds(index), index),
                out error), error);
        }

        Assert.True(service.TryQueryPage(
            start,
            start.AddDays(1),
            InspectionResultFilter.All,
            string.Empty,
            1,
            50,
            out IReadOnlyList<InspectionHistoryRecord> records,
            out long total,
            out error), error);
        Assert.True(service.TryGetSummary(
            start,
            start.AddDays(1),
            string.Empty,
            out InspectionHistorySummary summary,
            out error), error);
        stopwatch.Stop();

        Assert.Equal(recordCount, total);
        Assert.Equal(50, records.Count);
        Assert.Equal(recordCount, summary.TotalCount);
        Assert.Equal(250, summary.OkCount);
        Assert.Equal(250, summary.NgCount);
        output.WriteLine(
            $"SQLITE writes={recordCount} totalMs={stopwatch.Elapsed.TotalMilliseconds:F2} " +
            $"avgWriteAndQueryMs={stopwatch.Elapsed.TotalMilliseconds / recordCount:F3}");
        Assert.True(
            stopwatch.Elapsed < TimeSpan.FromSeconds(10),
            $"500条SQLite写入与查询耗时 {stopwatch.Elapsed.TotalSeconds:F2} 秒超过10秒基线。");
    }

    private static VisionParameters CreatePermissiveParameters()
    {
        return new VisionParameters(
            100, 1, 100, 100000,
            false, 0, 0, 640, 480,
            3, 3, true, true,
            20, 0.5,
            0.05, 0, 100, 0, 100);
    }

    private static InspectionHistoryRecord CreateHistoryRecord(
        DateTime detectedAtUtc,
        int index)
    {
        bool isOk = index % 2 == 0;
        return new InspectionHistoryRecord
        {
            BatchNumber = $"BATCH-{index % 5}",
            RecipeName = "压力测试配方",
            RecipeRevision = 1,
            DetectedAtUtc = detectedAtUtc,
            TriggerSource = "Modbus",
            CycleId = $"S{index:D6}",
            OperationMode = "Automatic",
            IsOk = isOk,
            JudgementCode = isOk ? "OK" : "AREA_LOW",
            JudgementReason = isOk ? "合格" : "面积过小",
            TargetCount = 1,
            RawContourCount = 1,
            AreaPixels = 13000,
            PhysicalAreaSquareMillimeters = 32.5,
            CenterXPixel = 320,
            CenterYPixel = 240,
            CenterXMillimeters = 16,
            CenterYMillimeters = 12,
            WidthPixels = 130,
            HeightPixels = 130,
            WidthMillimeters = 6.5,
            HeightMillimeters = 6.5,
            ProcessingTimeMilliseconds = 1.5,
            NgImagePath = isOk ? null : $"ng-{index}.jpg"
        };
    }

    private static void ForceGarbageCollection()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }
}
