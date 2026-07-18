using System;

namespace IndustrialVisionHost.Models
{
    public enum LogLevel
    {
        Info,
        Success,
        Warning,
        Error
    }

    public sealed class LogEntry
    {
        public LogEntry(DateTime timestamp, LogLevel level, string message)
        {
            Timestamp = timestamp;
            Level = level;
            Message = message;
        }

        public DateTime Timestamp { get; }

        public LogLevel Level { get; }

        public string Message { get; }

        public string LevelText => Level switch
        {
            LogLevel.Info => "信息",
            LogLevel.Success => "成功",
            LogLevel.Warning => "警告",
            LogLevel.Error => "错误",
            _ => "未知"
        };

        public string DisplayText =>
            $"{Timestamp:HH:mm:ss} [{LevelText}] {Message}";
    }
}
