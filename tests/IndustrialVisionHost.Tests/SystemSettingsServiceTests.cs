using System.Text;
using IndustrialVisionHost.Models;
using IndustrialVisionHost.Services;

namespace IndustrialVisionHost.Tests;

public sealed class SystemSettingsServiceTests
{
    [Fact]
    public void FirstLoad_CreatesValidatedDefaultFile()
    {
        using var directory = new TemporaryDirectory();
        var service = new SystemSettingsService(directory.File("settings.json"));

        Assert.True(service.TryLoad(
            out SystemSettings settings,
            out bool created,
            out string? error), error);
        Assert.True(created);
        Assert.Equal("127.0.0.1", settings.PlcHost);
        Assert.Equal(1502, settings.PlcTextPort);
        Assert.Equal("Light", settings.UiTheme);
        Assert.True(File.Exists(service.SettingsPath));
    }

    [Fact]
    public void SaveAndReload_PreservesAllSettings()
    {
        using var directory = new TemporaryDirectory();
        var service = new SystemSettingsService(directory.File("settings.json"));
        var expected = new SystemSettings
        {
            PlcHost = "192.168.10.20",
            PlcTextPort = 2502,
            NgImageRetentionDays = 60,
            NgImageMaximumMegabytes = 4096,
            UiTheme = "Dark",
            RememberWindowState = true,
            MainWindowWidth = 1400,
            MainWindowHeight = 900,
            MainWindowMaximized = true
        };

        Assert.True(service.TrySave(expected, out string? error), error);
        Assert.True(service.TryLoad(
            out SystemSettings actual,
            out bool created,
            out error), error);

        Assert.False(created);
        Assert.Equal(expected.PlcHost, actual.PlcHost);
        Assert.Equal(expected.PlcTextPort, actual.PlcTextPort);
        Assert.Equal(expected.NgImageRetentionDays, actual.NgImageRetentionDays);
        Assert.Equal(expected.NgImageMaximumMegabytes, actual.NgImageMaximumMegabytes);
        Assert.Equal(expected.UiTheme, actual.UiTheme);
        Assert.Equal(expected.MainWindowWidth, actual.MainWindowWidth);
        Assert.Equal(expected.MainWindowHeight, actual.MainWindowHeight);
        Assert.True(actual.MainWindowMaximized);
    }

    [Fact]
    public void LegacyFile_UsesNewUiDefaults()
    {
        using var directory = new TemporaryDirectory();
        string path = directory.File("settings.json");
        File.WriteAllText(
            path,
            "{\"Version\":1,\"PlcHost\":\"127.0.0.1\"," +
            "\"PlcTextPort\":1502,\"NgImageRetentionDays\":30," +
            "\"NgImageMaximumMegabytes\":1024}",
            new UTF8Encoding(false));
        var service = new SystemSettingsService(path);

        Assert.True(service.TryLoad(
            out SystemSettings settings,
            out _,
            out string? error), error);
        Assert.Equal("Light", settings.UiTheme);
        Assert.True(settings.RememberWindowState);
        Assert.Equal(1200, settings.MainWindowWidth);
        Assert.Equal(800, settings.MainWindowHeight);
    }

    [Theory]
    [InlineData(65535, 30, 1024, "Light", 1200, 800)]
    [InlineData(1502, 0, 1024, "Light", 1200, 800)]
    [InlineData(1502, 30, 9, "Light", 1200, 800)]
    [InlineData(1502, 30, 1024, "Blue", 1200, 800)]
    [InlineData(1502, 30, 1024, "Light", 500, 800)]
    [InlineData(1502, 30, 1024, "Light", 1200, 300)]
    public void InvalidSettings_AreRejected(
        int port,
        int retentionDays,
        int maximumMegabytes,
        string theme,
        double width,
        double height)
    {
        using var directory = new TemporaryDirectory();
        var service = new SystemSettingsService(directory.File("settings.json"));
        var invalid = new SystemSettings
        {
            PlcTextPort = port,
            NgImageRetentionDays = retentionDays,
            NgImageMaximumMegabytes = maximumMegabytes,
            UiTheme = theme,
            MainWindowWidth = width,
            MainWindowHeight = height
        };

        Assert.False(service.TrySave(invalid, out string? error));
        Assert.False(string.IsNullOrWhiteSpace(error));
        Assert.False(File.Exists(service.SettingsPath));
    }
}
