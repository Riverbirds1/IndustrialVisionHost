namespace IndustrialVisionHost.StabilityRunner;

internal sealed class StabilityRunnerOptions
{
    public int DurationSeconds { get; private set; } = 600;
    public int SampleSeconds { get; private set; } = 5;
    public int CycleDelayMilliseconds { get; private set; } = 20;
    public int FaultEveryCycles { get; private set; } = 500;
    public string OutputDirectory { get; private set; } = Path.Combine(
        Environment.CurrentDirectory,
        "artifacts",
        "stability");

    public static StabilityRunnerOptions Parse(string[] args)
    {
        var options = new StabilityRunnerOptions();
        for (int index = 0; index < args.Length; index++)
        {
            string name = args[index];
            if (name == "--no-faults")
            {
                options.FaultEveryCycles = 0;
                continue;
            }

            if (index + 1 >= args.Length)
            {
                throw new ArgumentException($"参数 {name} 缺少值。");
            }

            string value = args[++index];
            switch (name)
            {
                case "--duration-seconds":
                    options.DurationSeconds = ParseInt(value, name, 1, 86400);
                    break;
                case "--sample-seconds":
                    options.SampleSeconds = ParseInt(value, name, 1, 3600);
                    break;
                case "--cycle-delay-ms":
                    options.CycleDelayMilliseconds = ParseInt(value, name, 1, 10000);
                    break;
                case "--fault-every-cycles":
                    options.FaultEveryCycles = ParseInt(value, name, 1, int.MaxValue);
                    break;
                case "--output":
                    options.OutputDirectory = Path.GetFullPath(value);
                    break;
                default:
                    throw new ArgumentException($"未知参数：{name}。");
            }
        }

        if (options.SampleSeconds > options.DurationSeconds)
        {
            options.SampleSeconds = options.DurationSeconds;
        }

        return options;
    }

    private static int ParseInt(string value, string name, int minimum, int maximum)
    {
        if (!int.TryParse(value, out int result) ||
            result < minimum || result > maximum)
        {
            throw new ArgumentOutOfRangeException(
                name,
                $"必须是 {minimum}～{maximum} 的整数。");
        }

        return result;
    }
}
