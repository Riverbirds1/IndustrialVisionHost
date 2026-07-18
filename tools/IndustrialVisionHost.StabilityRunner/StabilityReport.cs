namespace IndustrialVisionHost.StabilityRunner;

internal sealed class StabilitySample
{
    public DateTime TimestampUtc { get; init; }
    public double ElapsedSeconds { get; init; }
    public long Cycles { get; init; }
    public double VisionAverageMilliseconds { get; init; }
    public double VisionMaximumMilliseconds { get; init; }
    public long PrivateMemoryBytes { get; init; }
    public long WorkingSetBytes { get; init; }
    public long ManagedHeapBytes { get; init; }
    public int ThreadCount { get; init; }
    public int HandleCount { get; init; }
    public long TextSuccesses { get; init; }
    public long ModbusSuccesses { get; init; }
    public long DatabaseWrites { get; init; }
    public long InjectedFaults { get; init; }
    public long Recoveries { get; init; }
    public long UnexpectedErrors { get; init; }
}

internal sealed class StabilitySummary
{
    public DateTime StartedAtUtc { get; init; }
    public DateTime FinishedAtUtc { get; init; }
    public int RequestedDurationSeconds { get; init; }
    public double ActualDurationSeconds { get; init; }
    public long Cycles { get; init; }
    public double VisionAverageMilliseconds { get; init; }
    public double VisionMaximumMilliseconds { get; init; }
    public long InitialPrivateMemoryBytes { get; init; }
    public long FinalPrivateMemoryBytes { get; init; }
    public long PeakPrivateMemoryBytes { get; init; }
    public long TextSuccesses { get; init; }
    public long ModbusSuccesses { get; init; }
    public long DatabaseWrites { get; init; }
    public long InjectedFaults { get; init; }
    public long Recoveries { get; init; }
    public long UnexpectedErrors { get; init; }
    public bool Passed { get; init; }
    public string MachineName { get; init; } = Environment.MachineName;
    public string Framework { get; init; } = Environment.Version.ToString();
    public string OperatingSystem { get; init; } = Environment.OSVersion.ToString();
}
