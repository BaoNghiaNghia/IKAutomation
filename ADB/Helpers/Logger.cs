using System;
using System.IO;
using System.Text;
using System.Windows;

namespace ADB_Tool_Automation_Post_FB.Helpers
{
    public static class Logger
    {
        // Log file path
        public static readonly string LogFilePath = "log.txt";

        // Lock for thread safety
        private static readonly object LockObject = new object();

        // Inline initialization for logWriter (after clearing file)
        private static StreamWriter logWriter = InitializeLogWriter();

        // Inline initialization method
        private static StreamWriter InitializeLogWriter()
        {
            try
            {
                // Xóa toàn bộ log khi khởi động
                File.WriteAllText(LogFilePath, string.Empty);

                // Tạo StreamWriter mới
                return new StreamWriter(LogFilePath, true, Encoding.UTF8)
                {
                    AutoFlush = true
                };
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Logger] Error initializing log writer: {ex.Message}");
                return null;
            }
        }

        public static void LogInfo(string message)
        {
            string logMessage = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [INFO] {message}";
            Console.WriteLine(logMessage);
            WriteLog(logMessage);
        }

        public static void LogError(string message)
        {
            string logMessage = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [ERROR] {message}";
            Console.WriteLine(logMessage);
            WriteLog(logMessage);
        }

        public static void LogWarning(string message)
        {
            string logMessage = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [WARNING] {message}";
            Console.WriteLine(logMessage);
            WriteLog(logMessage);
        }

        private static void WriteLog(string logMessage)
        {
            try
            {
                lock (LockObject)
                {
                    if (logWriter == null || logWriter.BaseStream == null || !logWriter.BaseStream.CanWrite)
                    {
                        logWriter?.Dispose();
                        logWriter = new StreamWriter(LogFilePath, true, Encoding.UTF8)
                        {
                            AutoFlush = true
                        };
                    }

                    logWriter.WriteLine(logMessage);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Logger] Error writing to log file: {ex.Message}");
            }
        }

        public static void ClearLog()
        {
            try
            {
                lock (LockObject)
                {
                    logWriter?.Close(); // close first
                    File.WriteAllText(LogFilePath, string.Empty);
                    logWriter = new StreamWriter(LogFilePath, true, Encoding.UTF8)
                    {
                        AutoFlush = true
                    };
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error clearing log file: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        public static void Dispose()
        {
            lock (LockObject)
            {
                logWriter?.Dispose();
            }
        }
    }
}
