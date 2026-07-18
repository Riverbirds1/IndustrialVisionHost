namespace IndustrialVisionHost.Models
{
    public sealed class SystemSettings
    {
        public int Version { get; set; } = 1;

        public string PlcHost { get; set; } = "127.0.0.1";

        public int PlcTextPort { get; set; } = 1502;

        public int NgImageRetentionDays { get; set; } = 30;

        public int NgImageMaximumMegabytes { get; set; } = 1024;

        public string UiTheme { get; set; } = "Light";

        public bool RememberWindowState { get; set; } = true;

        public double MainWindowWidth { get; set; } = 1200;

        public double MainWindowHeight { get; set; } = 800;

        public bool MainWindowMaximized { get; set; }

        public SystemSettings Copy()
        {
            return new SystemSettings
            {
                Version = Version,
                PlcHost = PlcHost,
                PlcTextPort = PlcTextPort,
                NgImageRetentionDays = NgImageRetentionDays,
                NgImageMaximumMegabytes = NgImageMaximumMegabytes,
                UiTheme = UiTheme,
                RememberWindowState = RememberWindowState,
                MainWindowWidth = MainWindowWidth,
                MainWindowHeight = MainWindowHeight,
                MainWindowMaximized = MainWindowMaximized
            };
        }
    }
}
