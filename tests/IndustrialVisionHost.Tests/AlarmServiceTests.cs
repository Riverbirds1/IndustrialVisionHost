using IndustrialVisionHost.Models;
using IndustrialVisionHost.Services;

namespace IndustrialVisionHost.Tests;

public sealed class AlarmServiceTests
{
    private static readonly AuthenticatedUser Operator = new(
        1, "operator", "操作员", UserRole.Operator, false);
    private static readonly AuthenticatedUser Engineer = new(
        2, "engineer", "工程师", UserRole.Engineer, false);

    [Fact]
    public void RepeatedActiveAlarm_IsMergedAndCountsOccurrences()
    {
        using var fixture = new AlarmFixture();

        Assert.True(fixture.Service.TryRaise(
            "CAMERA_LOST", AlarmSeverity.Error, "Camera.Primary", "第一次",
            out long firstId, out bool firstIsNew, out string? error), error);
        Assert.True(fixture.Service.TryRaise(
            "CAMERA_LOST", AlarmSeverity.Error, "Camera.Primary", "第二次",
            out long secondId, out bool secondIsNew, out error), error);

        Assert.True(firstIsNew);
        Assert.False(secondIsNew);
        Assert.Equal(firstId, secondId);
        IReadOnlyList<AlarmRecord> records = fixture.Query(activeOnly: true);
        AlarmRecord record = Assert.Single(records);
        Assert.Equal(2, record.OccurrenceCount);
        Assert.Equal("第二次", record.Message);
    }

    [Fact]
    public void OperatorCannotAcknowledge_EngineerCanAcknowledgeOnce()
    {
        using var fixture = new AlarmFixture();
        long id = fixture.Raise("PLC_LOST", "PLC.TextTcp");

        Assert.False(fixture.Service.TryAcknowledge(Operator, id, out _));
        Assert.True(fixture.Service.TryAcknowledge(
            Engineer, id, out string? error), error);
        Assert.False(fixture.Service.TryAcknowledge(Engineer, id, out _));

        AlarmRecord record = Assert.Single(fixture.Query(activeOnly: true));
        Assert.Equal("engineer", record.AcknowledgedBy);
        Assert.NotNull(record.AcknowledgedAtUtc);
    }

    [Fact]
    public void ClearClosesLifecycle_AndNextRaiseCreatesNewLifecycle()
    {
        using var fixture = new AlarmFixture();
        long oldId = fixture.Raise("MODBUS_LOST", "PLC.ModbusTcp");

        Assert.True(fixture.Service.TryClearActive(
            "MODBUS_LOST",
            "PLC.ModbusTcp",
            "重连成功",
            out int cleared,
            out string? error), error);
        Assert.Equal(1, cleared);
        Assert.Empty(fixture.Query(activeOnly: true));

        long newId = fixture.Raise("MODBUS_LOST", "PLC.ModbusTcp");
        Assert.NotEqual(oldId, newId);
        Assert.Equal(2, fixture.Query(activeOnly: false).Count);
    }

    private sealed class AlarmFixture : IDisposable
    {
        private readonly TemporaryDirectory directory = new();

        public AlarmFixture()
        {
            Service = new AlarmService(directory.File("alarms.db"));
            Assert.True(Service.TryInitialize(out string? error), error);
        }

        public AlarmService Service { get; }

        public long Raise(string code, string source)
        {
            Assert.True(Service.TryRaise(
                code,
                AlarmSeverity.Error,
                source,
                "测试报警",
                out long id,
                out _,
                out string? error), error);
            return id;
        }

        public IReadOnlyList<AlarmRecord> Query(bool activeOnly)
        {
            Assert.True(Service.TryQuery(
                Engineer,
                null,
                null,
                activeOnly,
                100,
                out IReadOnlyList<AlarmRecord> records,
                out string? error), error);
            return records;
        }

        public void Dispose()
        {
            directory.Dispose();
        }
    }
}
