using System;
using System.IO;
using System.Text;

using IndustrialVisionHost.Models;

namespace IndustrialVisionHost.Services
{
    public sealed class FileLogService
    {
        private readonly object fileSync = new object();

        public FileLogService(string? logDirectory = null)
        {
            LogDirectory = logDirectory ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "IndustrialVisionHost",
                "Logs");
        }

        public string LogDirectory { get; }

        public bool TryWrite(LogEntry entry, out string? errorMessage)
        {
            try
            {
                string filePath = Path.Combine(
                    LogDirectory,
                    $"{entry.Timestamp:yyyy-MM-dd}.log");

                string line =
                    $"{entry.Timestamp:yyyy-MM-dd HH:mm:ss.fff} " +
                    $"[{entry.LevelText}] {entry.Message}" +
                    Environment.NewLine;

                lock (fileSync)
                {
                    Directory.CreateDirectory(LogDirectory);
                    File.AppendAllText(filePath, line, new UTF8Encoding(false));
                }

                errorMessage = null;
                return true;
            }
            catch (Exception ex) when (
                ex is IOException ||
                ex is UnauthorizedAccessException ||
                ex is NotSupportedException)
            {
                errorMessage = ex.Message;
                return false;
            }
        }
    }
}
