using System;
using System.IO;
using System.Text;
using System.Windows;

namespace ADB_Tool_Automation_Post_FB.Helpers
{
    public static class Logger
    {
        public static readonly string LogFilePath = "log.txt";
        private static readonly object LockObject = new object();
        private static readonly string ArchiveDirectory = "Logs";
        private static long rotationBytes = 20971520L;
        private static int retentionDays = 30;
        private static DateTime openedDate = DateTime.Today;
        private static StreamWriter logWriter = InitializeLogWriter();

        // Inline initialization method
        private static StreamWriter InitializeLogWriter()
        {
            try
            {
                CleanupArchives();
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

        public static void Configure(long maximumLogBytes, int archiveRetentionDays)
        {
            if (maximumLogBytes < 1)
                throw new ArgumentOutOfRangeException(nameof(maximumLogBytes));
            if (archiveRetentionDays < 1)
                throw new ArgumentOutOfRangeException(nameof(archiveRetentionDays));
            lock (LockObject)
            {
                rotationBytes = maximumLogBytes;
                retentionDays = archiveRetentionDays;
                RotateIfRequired();
                CleanupArchives();
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
                    RotateIfRequired();
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

        private static void RotateIfRequired()
        {
            bool dayChanged = openedDate != DateTime.Today;
            bool tooLarge = File.Exists(LogFilePath)
                && new FileInfo(LogFilePath).Length >= rotationBytes;
            if (!dayChanged && !tooLarge) return;

            logWriter?.Dispose();
            logWriter = null;
            if (File.Exists(LogFilePath) && new FileInfo(LogFilePath).Length > 0)
            {
                Directory.CreateDirectory(ArchiveDirectory);
                string archive = Path.Combine(ArchiveDirectory,
                    $"log-{DateTime.Now:yyyyMMdd-HHmmss-fff}.txt");
                File.Move(LogFilePath, archive);
            }
            openedDate = DateTime.Today;
            CleanupArchives();
        }

        private static void CleanupArchives()
        {
            if (!Directory.Exists(ArchiveDirectory)) return;
            DateTime cutoff = DateTime.Now.AddDays(-retentionDays);
            foreach (string path in Directory.EnumerateFiles(ArchiveDirectory, "log-*.txt"))
            {
                try
                {
                    if (File.GetLastWriteTime(path) < cutoff) File.Delete(path);
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }

        public static void ClearLog()
        {
            try
            {
                lock (LockObject)
                {
                    logWriter?.Close();
                    File.WriteAllText(LogFilePath, string.Empty);
                    openedDate = DateTime.Today;
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
