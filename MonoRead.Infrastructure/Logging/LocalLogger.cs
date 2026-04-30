using System;
using System.Collections.Generic;
using System.Text;

namespace MonoRead.Infrastructure.Logging
{
    public static class LocalLogger
    {
        private static readonly object _lock = new object();
        private static string _logDirectory;

        // 获取日志存放目录（放在沙盒下的 Logs 文件夹）
        public static string LogDirectory
        {
            get
            {
                if (_logDirectory == null)
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

        public static void LogError(string message, Exception ex = null)
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
            // 每天生成一个日志文件，例如：monoread_20260430.log
            string fileName = $"monoread_{DateTime.Now:yyyyMMdd}.log";
            string filePath = Path.Combine(LogDirectory, fileName);

            // 异步任务中极其容易发生文件占用并发，必须加锁保证线程安全
            lock (_lock)
            {
                try
                {
                    using (StreamWriter writer = new StreamWriter(filePath, true)) // true 代表追加模式
                    {
                        writer.WriteLine(content);
                        writer.WriteLine(new string('-', 50)); // 分隔线
                    }
                }
                catch (Exception)
                {
                    // 日志写入本身的异常，此时由于没有别的日志系统，只能生吞，防止带崩 App
                }
            }
        }
    }
}
