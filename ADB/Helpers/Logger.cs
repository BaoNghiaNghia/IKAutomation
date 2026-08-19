using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Windows;

namespace ADB_Tool_Automation_Post_FB.Helpers
{
    public static class Logger
    {
        public static readonly string LogDirectory = "Logs";
        public static string LogFilePath => Path.Combine(LogDirectory,
            $"farm-{DateTime.Today:yyyy-MM-dd}.log");
        private static readonly object LockObject = new object();
        private static readonly StringBuilder PendingBuffer = new StringBuilder();
        private static int pendingBufferBytes;
        private static readonly Dictionary<string, InfoThrottleState> InfoThrottle =
            new Dictionary<string, InfoThrottleState>(StringComparer.OrdinalIgnoreCase);
        private static long rotationBytes = 5242880L;
        private static long maximumArchiveBytes = 104857600L;
        private static int retentionDays = 7;
        private static int maximumBufferedBytes = 65536;
        private static int verboseInfoThrottleMilliseconds = 5000;
        private static DateTime openedDate = DateTime.Today;
        private static StreamWriter logWriter = InitializeLogWriter();
        private static readonly Timer FlushTimer = new Timer(FlushTimerCallback, null,
            TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));

        private sealed class InfoThrottleState
        {
            public DateTime LastWrittenAt { get; set; }
            public int SuppressedCount { get; set; }
        }

        private static StreamWriter InitializeLogWriter()
        {
            try
            {
                CleanupArchives();
                return CreateWriter();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Logger] Error initializing log writer: {ex.Message}");
                return null;
            }
        }

        public static void Configure(long maximumLogBytes, int archiveRetentionDays,
            int flushIntervalMilliseconds = 1000, int maximumBufferedLogBytes = 65536,
            int verboseThrottleMilliseconds = 5000,
            long maximumArchivedLogBytes = 104857600L)
        {
            if (maximumLogBytes < 1) throw new ArgumentOutOfRangeException(nameof(maximumLogBytes));
            if (archiveRetentionDays < 1) throw new ArgumentOutOfRangeException(nameof(archiveRetentionDays));
            if (flushIntervalMilliseconds < 100 || flushIntervalMilliseconds > 60000)
                throw new ArgumentOutOfRangeException(nameof(flushIntervalMilliseconds));
            if (maximumBufferedLogBytes < 1024)
                throw new ArgumentOutOfRangeException(nameof(maximumBufferedLogBytes));
            if (verboseThrottleMilliseconds < 0 || verboseThrottleMilliseconds > 600000)
                throw new ArgumentOutOfRangeException(nameof(verboseThrottleMilliseconds));
            if (maximumArchivedLogBytes < maximumLogBytes)
                throw new ArgumentOutOfRangeException(nameof(maximumArchivedLogBytes));

            lock (LockObject)
            {
                rotationBytes = maximumLogBytes;
                retentionDays = archiveRetentionDays;
                maximumBufferedBytes = maximumBufferedLogBytes;
                verboseInfoThrottleMilliseconds = verboseThrottleMilliseconds;
                maximumArchiveBytes = maximumArchivedLogBytes;
                FlushUnsafe();
                RotateIfRequired(0);
                CleanupArchives();
                FlushTimer.Change(flushIntervalMilliseconds, flushIntervalMilliseconds);
            }
        }

        public static void LogInfo(string message)
        {
            string normalized = message ?? string.Empty;
            int suppressed;
            if (!TryAcceptVerboseInfo(normalized, out suppressed)) return;
            if (suppressed > 0)
                normalized = $"[Logger] SuppressedVerboseInfoCount={suppressed}; {normalized}";
            WriteLog("INFO", normalized, false);
        }

        public static void LogError(string message) => WriteLog("ERROR", message, true);

        public static void LogWarning(string message) => WriteLog("WARNING", message, true);

        private static void WriteLog(string level, string message, bool flushImmediately)
        {
            string logMessage = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [{level}] {message ?? string.Empty}";
            try
            {
                lock (LockObject)
                {
                    PendingBuffer.AppendLine(logMessage);
                    pendingBufferBytes += Encoding.UTF8.GetByteCount(logMessage)
                        + Encoding.UTF8.GetByteCount(Environment.NewLine);
                    if (flushImmediately || pendingBufferBytes >= maximumBufferedBytes)
                        FlushUnsafe();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Logger] Error writing to log file: {ex.Message}");
            }
        }

        private static bool TryAcceptVerboseInfo(string message, out int suppressed)
        {
            suppressed = 0;
            if (verboseInfoThrottleMilliseconds == 0 || !IsVerboseObservation(message))
                return true;

            DateTime now = DateTime.UtcNow;
            string key = BuildThrottleKey(message);
            lock (LockObject)
            {
                InfoThrottleState state;
                if (!InfoThrottle.TryGetValue(key, out state))
                {
                    InfoThrottle[key] = new InfoThrottleState { LastWrittenAt = now };
                    return true;
                }
                if ((now - state.LastWrittenAt).TotalMilliseconds < verboseInfoThrottleMilliseconds)
                {
                    state.SuppressedCount++;
                    return false;
                }
                suppressed = state.SuppressedCount;
                state.SuppressedCount = 0;
                state.LastWrittenAt = now;
                return true;
            }
        }

        private static bool IsVerboseObservation(string message) =>
            message.IndexOf("Observation", StringComparison.OrdinalIgnoreCase) >= 0
            || message.IndexOf("FrameIndex=", StringComparison.OrdinalIgnoreCase) >= 0
            || message.IndexOf("State Poll", StringComparison.OrdinalIgnoreCase) >= 0
            || message.IndexOf("Match Probe", StringComparison.OrdinalIgnoreCase) >= 0;

        private static string BuildThrottleKey(string message)
        {
            int closingBracket = message.IndexOf(']');
            string category = closingBracket >= 0
                ? message.Substring(0, closingBracket + 1) : "[Verbose]";
            const string marker = "DeviceName='";
            int deviceStart = message.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (deviceStart < 0) return category;
            deviceStart += marker.Length;
            int deviceEnd = message.IndexOf('\'', deviceStart);
            return deviceEnd > deviceStart
                ? category + "|" + message.Substring(deviceStart, deviceEnd - deviceStart)
                : category;
        }

        private static void FlushTimerCallback(object state)
        {
            try { lock (LockObject) FlushUnsafe(); }
            catch (Exception ex)
            {
                Console.WriteLine($"[Logger] Error flushing buffered log: {ex.Message}");
            }
        }

        private static void FlushUnsafe()
        {
            if (PendingBuffer.Length == 0) return;
            RotateIfRequired(pendingBufferBytes);
            EnsureWriter();
            if (logWriter == null) return;
            logWriter.Write(PendingBuffer.ToString());
            logWriter.Flush();
            PendingBuffer.Clear();
            pendingBufferBytes = 0;
        }

        private static StreamWriter CreateWriter()
        {
            Directory.CreateDirectory(LogDirectory);
            return new StreamWriter(LogFilePath, true, Encoding.UTF8) { AutoFlush = false };
        }

        private static void EnsureWriter()
        {
            if (logWriter != null && logWriter.BaseStream != null && logWriter.BaseStream.CanWrite)
                return;
            logWriter?.Dispose();
            logWriter = CreateWriter();
        }

        private static void RotateIfRequired(int incomingBytes)
        {
            bool dayChanged = openedDate != DateTime.Today;
            long currentBytes = File.Exists(LogFilePath) ? new FileInfo(LogFilePath).Length : 0L;
            bool tooLarge = currentBytes > 0 && currentBytes + incomingBytes >= rotationBytes;
            if (!dayChanged && !tooLarge) return;

            logWriter?.Dispose();
            logWriter = null;
            if (File.Exists(LogFilePath) && currentBytes > 0)
            {
                Directory.CreateDirectory(LogDirectory);
                string archive = Path.Combine(LogDirectory,
                    $"farm-{DateTime.Now:yyyy-MM-dd-HHmmss-fff}.log");
                File.Move(LogFilePath, archive);
            }
            openedDate = DateTime.Today;
            CleanupArchives();
        }

        private static void CleanupArchives()
        {
            if (!Directory.Exists(LogDirectory)) return;
            DateTime cutoff = DateTime.Now.AddDays(-retentionDays);
            foreach (string path in Directory.EnumerateFiles(LogDirectory, "farm-*.log"))
            {
                try
                {
                    if (!string.Equals(path, LogFilePath, StringComparison.OrdinalIgnoreCase)
                        && File.GetLastWriteTime(path) < cutoff)
                        File.Delete(path);
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }

            FileInfo[] archives = Directory.EnumerateFiles(LogDirectory, "farm-????-??-??-*.log")
                .Select(path => new FileInfo(path)).OrderBy(file => file.LastWriteTimeUtc).ToArray();
            long totalBytes = archives.Sum(file => file.Length);
            foreach (FileInfo archive in archives)
            {
                if (totalBytes <= maximumArchiveBytes) break;
                long length = archive.Length;
                try { archive.Delete(); totalBytes -= length; }
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
                    PendingBuffer.Clear();
                    pendingBufferBytes = 0;
                    logWriter?.Close();
                    File.WriteAllText(LogFilePath, string.Empty);
                    openedDate = DateTime.Today;
                    logWriter = CreateWriter();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error clearing log file: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        public static void Dispose()
        {
            lock (LockObject)
            {
                FlushUnsafe();
                FlushTimer.Dispose();
                logWriter?.Dispose();
                logWriter = null;
            }
        }
    }
}
