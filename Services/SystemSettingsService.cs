using System;
using System.IO;
using System.Text;
using System.Text.Json;
using IndustrialVisionHost.Models;

namespace IndustrialVisionHost.Services
{
    public sealed class SystemSettingsService
    {
        private const int CurrentVersion = 1;
        private readonly object fileSync = new object();
        private readonly JsonSerializerOptions jsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            WriteIndented = true
        };

        public SystemSettingsService(string? settingsPath = null)
        {
            SettingsPath = settingsPath ?? Path.Combine(
                Environment.GetFolderPath(
                    Environment.SpecialFolder.LocalApplicationData),
                "IndustrialVisionHost",
                "Settings",
                "system-settings.json");
        }

        public string SettingsPath { get; }

        public bool TryLoad(
            out SystemSettings settings,
            out bool createdDefaultFile,
            out string? errorMessage)
        {
            createdDefaultFile = false;
            settings = new SystemSettings();

            try
            {
                lock (fileSync)
                {
                    if (!File.Exists(SettingsPath))
                    {
                        Validate(settings);
                        WriteFile(settings);
                        createdDefaultFile = true;
                        errorMessage = null;
                        return true;
                    }

                    string json = File.ReadAllText(SettingsPath, Encoding.UTF8);
                    SystemSettings? loaded = JsonSerializer.Deserialize<SystemSettings>(
                        json,
                        jsonOptions);
                    if (loaded is null)
                    {
                        throw new InvalidDataException("系统设置文件内容为空。");
                    }

                    Validate(loaded);
                    settings = loaded;
                }

                errorMessage = null;
                return true;
            }
            catch (Exception ex)
            {
                settings = new SystemSettings();
                errorMessage = ex.Message;
                return false;
            }
        }

        public bool TrySave(SystemSettings settings, out string? errorMessage)
        {
            if (settings is null)
            {
                throw new ArgumentNullException(nameof(settings));
            }

            try
            {
                SystemSettings normalized = settings.Copy();
                normalized.Version = CurrentVersion;
                normalized.PlcHost = normalized.PlcHost.Trim();
                Validate(normalized);

                lock (fileSync)
                {
                    WriteFile(normalized);
                }

                errorMessage = null;
                return true;
            }
            catch (Exception ex)
            {
                errorMessage = ex.Message;
                return false;
            }
        }

        public static void Validate(SystemSettings settings)
        {
            if (settings.Version < 1 || settings.Version > CurrentVersion)
            {
                throw new InvalidDataException(
                    $"不支持的系统设置版本：{settings.Version}。");
            }

            string host = (settings.PlcHost ?? string.Empty).Trim();
            if (host.Length == 0 || host.Length > 255)
            {
                throw new ArgumentException("PLC 地址长度必须为 1～255 个字符。");
            }

            foreach (char character in host)
            {
                if (char.IsControl(character) || char.IsWhiteSpace(character))
                {
                    throw new ArgumentException("PLC 地址不能包含空白或控制字符。");
                }
            }

            if (settings.PlcTextPort < 1 || settings.PlcTextPort > 65534)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(settings.PlcTextPort),
                    "文本协议端口必须是 1～65534，下一端口留给 Modbus TCP。");
            }

            if (settings.NgImageRetentionDays < 1 ||
                settings.NgImageRetentionDays > 3650)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(settings.NgImageRetentionDays),
                    "NG 图片保留天数必须是 1～3650。 ");
            }

            if (settings.NgImageMaximumMegabytes < 10 ||
                settings.NgImageMaximumMegabytes > 1024 * 1024)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(settings.NgImageMaximumMegabytes),
                    "NG 图片容量上限必须是 10～1048576 MB。");
            }

            if (!string.Equals(settings.UiTheme, "Light", StringComparison.Ordinal) &&
                !string.Equals(settings.UiTheme, "Dark", StringComparison.Ordinal))
            {
                throw new ArgumentException("界面主题只能是 Light 或 Dark。");
            }

            if (double.IsNaN(settings.MainWindowWidth) ||
                double.IsInfinity(settings.MainWindowWidth) ||
                settings.MainWindowWidth < 900 ||
                settings.MainWindowWidth > 3840)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(settings.MainWindowWidth),
                    "主窗口宽度必须是 900～3840。");
            }

            if (double.IsNaN(settings.MainWindowHeight) ||
                double.IsInfinity(settings.MainWindowHeight) ||
                settings.MainWindowHeight < 650 ||
                settings.MainWindowHeight > 2160)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(settings.MainWindowHeight),
                    "主窗口高度必须是 650～2160。");
            }
        }

        private void WriteFile(SystemSettings settings)
        {
            string? directory = Path.GetDirectoryName(SettingsPath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            string temporaryPath = SettingsPath + ".tmp";
            try
            {
                string json = JsonSerializer.Serialize(settings, jsonOptions);
                File.WriteAllText(
                    temporaryPath,
                    json,
                    new UTF8Encoding(false));
                File.Move(temporaryPath, SettingsPath, true);
            }
            finally
            {
                try
                {
                    if (File.Exists(temporaryPath))
                    {
                        File.Delete(temporaryPath);
                    }
                }
                catch
                {
                }
            }
        }
    }
}
