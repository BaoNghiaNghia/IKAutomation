using ADB_Tool_Automation_Post_FB.Core.GameDetection;
using ADB_Tool_Automation_Post_FB.Infrastructure.Diagnostics;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace ADB_Tool_Automation_Post_FB.Infrastructure.GameDetection
{
    public sealed class UnknownScreenshotStore : IUnknownScreenshotStore
    {
        private readonly string rootDirectory;
        private readonly Func<DateTimeOffset> clock;

        public UnknownScreenshotStore(string configuredDirectory)
            : this(configuredDirectory, () => DateTimeOffset.Now)
        {
        }

        public UnknownScreenshotStore(string configuredDirectory, Func<DateTimeOffset> clock)
        {
            if (string.IsNullOrWhiteSpace(configuredDirectory))
                throw new ArgumentException("Unknown screenshot directory is required.", nameof(configuredDirectory));

            this.clock = clock ?? throw new ArgumentNullException(nameof(clock));
            rootDirectory = Path.IsPathRooted(configuredDirectory)
                ? Path.GetFullPath(configuredDirectory)
                : Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, configuredDirectory));
        }

        public async Task<string> SaveAsync(
            string deviceName,
            byte[] screenshotPng,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!ADB_Tool_Automation_Post_FB.Core.Diagnostics.DiagnosticStorageGate.IsWriteEnabled)
                return null;
            if (screenshotPng == null || screenshotPng.Length == 0)
                throw new ArgumentException("Unknown-state screenshot PNG is required.", nameof(screenshotPng));

            string safeDeviceName = ScreenshotPathPolicy.SanitizeDeviceName(deviceName);
            DateTime localTime = clock().LocalDateTime;
            string directory = Path.GetFullPath(Path.Combine(
                rootDirectory,
                safeDeviceName,
                localTime.ToString("yyyy-MM-dd")));
            EnsureInsideRoot(directory);
            Directory.CreateDirectory(directory);

            string screenshotPath = FindAvailablePath(directory, localTime);
            using (var stream = new FileStream(
                screenshotPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                4096,
                useAsync: true))
            {
                await stream.WriteAsync(screenshotPng, 0, screenshotPng.Length, cancellationToken);
            }

            return screenshotPath;
        }

        private string FindAvailablePath(string directory, DateTime capturedAt)
        {
            for (int millisecondOffset = 0; millisecondOffset < 1000; millisecondOffset++)
            {
                DateTime candidateTime = capturedAt.AddMilliseconds(millisecondOffset);
                string candidate = Path.Combine(
                    directory,
                    $"{candidateTime:HH-mm-ss-fff}_unknown.png");
                if (!File.Exists(candidate))
                    return candidate;
            }

            throw new IOException("Could not allocate a unique unknown-state screenshot filename.");
        }

        private void EnsureInsideRoot(string path)
        {
            string expectedPrefix = rootDirectory
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                + Path.DirectorySeparatorChar;
            if (!path.StartsWith(expectedPrefix, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Unknown screenshot path escaped its configured directory.");
        }
    }
}
