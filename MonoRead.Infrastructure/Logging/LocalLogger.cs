using System;
using System.IO;

namespace MonoRead.Infrastructure.Logging
{
    public static class LocalLogger
    {
        private static readonly object _lock = new object();

        // 【修复 1】：直接初始化为 null，或者去掉默认赋值
        private static string? _logDirectory;

        // 获取日志存放目录（放在沙盒下的 Logs 文件夹）
        public static string LogDirectory
        {
            get
            {
                // 【修复 2】：使用 string.IsNullOrEmpty 判断更加严谨
                if (string.IsNullOrEmpty(_logDirectory))
                {
                    // MAUI 跨平台获取沙盒路径
                    string appData = Microsoft.Maui.Storage.FileSystem.AppDataDirectory;
                    _logDirectory = Path.Combine(appData, "Logs");

                    if (!Directory.Exists(_logDirectory))
                    {
                        Directory.CreateDirectory(_logDirectory);
                    }
                }
                return _logDirectory;
            }
        }

        // 【新增】：暴露当前日志文件路径，给 SettingsViewModel 导出日志用！
        public static string GetCurrentLogFilePath()
        {
            string fileName = $"monoread_{DateTime.Now:yyyyMMdd}.log";
            return Path.Combine(LogDirectory, fileName);
        }

        public static void LogError(string message, Exception? ex = null)
        {
            string logMessage = $"[ERROR] {DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} - {message}";
            if (ex != null)
            {
                logMessage += $"\nException: {ex.GetType().Name} - {ex.Message}\nStackTrace: {ex.StackTrace}";
            }
            WriteToFile(logMessage);
        }

        public static void LogInfo(string message)
        {
            string logMessage = $"[INFO] {DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} - {message}";
            WriteToFile(logMessage);
        }

        private static void WriteToFile(string content)
        {
            string filePath = GetCurrentLogFilePath();

            lock (_lock)
            {
                try
                {
                    // 【优化】：using 会自动 Flush 和 Close
                    using (StreamWriter writer = new StreamWriter(filePath, true))
                    {
                        writer.WriteLine(content);
                        writer.WriteLine(new string('-', 50));
                    }
                }
                catch (Exception writeEx)
                {
                    // 开发阶段可以打印到控制台看看是不是没权限，发版时保留生吞即可
                    System.Diagnostics.Debug.WriteLine($"写入日志失败: {writeEx.Message}");
                }
            }
        }
    }
}