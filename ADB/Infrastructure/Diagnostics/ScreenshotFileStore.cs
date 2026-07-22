using ADB_Tool_Automation_Post_FB.Core.Diagnostics;
using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace ADB_Tool_Automation_Post_FB.Infrastructure.Diagnostics
{
    public sealed class ScreenshotFileStore
    {
        private static readonly string[] SensitiveNoteMarkers =
        {
            "password", "token", "secret", "credential", "cookie", "authorization"
        };

        private readonly string rootDirectory;
        private readonly Func<DateTimeOffset> clock;

        public ScreenshotFileStore(string configuredDirectory)
            : this(configuredDirectory, () => DateTimeOffset.Now)
        {
        }

        public ScreenshotFileStore(string configuredDirectory, Func<DateTimeOffset> clock)
        {
            if (string.IsNullOrWhiteSpace(configuredDirectory))
                throw new ArgumentException("Screenshot directory is required.", nameof(configuredDirectory));

            this.clock = clock ?? throw new ArgumentNullException(nameof(clock));
            rootDirectory = Path.IsPathRooted(configuredDirectory)
                ? Path.GetFullPath(configuredDirectory)
                : Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, configuredDirectory));
        }

        public async Task<ScreenshotCaptureResult> SaveAsync(
            string deviceName,
            string stateName,
            string note,
            byte[] pngBytes,
            int width,
            int height,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!DiagnosticStorageGate.IsWriteEnabled) return null;
            if (pngBytes == null || pngBytes.Length == 0)
                throw new ArgumentException("Screenshot PNG data is required.", nameof(pngBytes));

            DateTimeOffset capturedAt = clock();
            string screenshotPath = ScreenshotPathPolicy.BuildScreenshotPath(
                rootDirectory,
                deviceName,
                stateName,
                capturedAt);
            screenshotPath = FindAvailablePath(screenshotPath);
            string metadataPath = Path.ChangeExtension(screenshotPath, ".json");
            Directory.CreateDirectory(Path.GetDirectoryName(screenshotPath));

            var metadata = new ScreenshotMetadata
            {
                DeviceName = deviceName.Trim(),
                CapturedAt = capturedAt.ToString("o"),
                StateName = ScreenshotPathPolicy.SanitizeStateName(stateName),
                Note = SanitizeMetadataNote(note),
                Width = width,
                Height = height
            };

            byte[] metadataBytes = ScreenshotMetadataJson.Serialize(metadata);
            try
            {
                await WriteFileAsync(screenshotPath, pngBytes, cancellationToken);
                await WriteFileAsync(metadataPath, metadataBytes, cancellationToken);
            }
            catch
            {
                TryDelete(screenshotPath);
                TryDelete(metadataPath);
                throw;
            }

            return new ScreenshotCaptureResult
            {
                ScreenshotPath = screenshotPath,
                MetadataPath = metadataPath,
                CapturedAt = capturedAt,
                Width = width,
                Height = height
            };
        }

        private static async Task WriteFileAsync(
            string path,
            byte[] bytes,
            CancellationToken cancellationToken)
        {
            using (var stream = new FileStream(
                path,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                4096,
                useAsync: true))
            {
                await stream.WriteAsync(bytes, 0, bytes.Length, cancellationToken);
            }
        }

        private static string FindAvailablePath(string requestedPath)
        {
            if (!File.Exists(requestedPath) && !File.Exists(Path.ChangeExtension(requestedPath, ".json")))
                return requestedPath;

            string directory = Path.GetDirectoryName(requestedPath);
            string fileName = Path.GetFileNameWithoutExtension(requestedPath);
            for (int suffix = 1; suffix <= 999; suffix++)
            {
                string candidate = Path.Combine(directory, $"{fileName}_{suffix:000}.png");
                if (!File.Exists(candidate) && !File.Exists(Path.ChangeExtension(candidate, ".json")))
                    return candidate;
            }

            throw new IOException("Could not allocate a unique diagnostic screenshot filename.");
        }

        private static string SanitizeMetadataNote(string note)
        {
            if (string.IsNullOrWhiteSpace(note))
                return string.Empty;

            return SensitiveNoteMarkers.Any(marker =>
                note.IndexOf(marker, StringComparison.OrdinalIgnoreCase) >= 0)
                ? "[redacted: sensitive content omitted]"
                : note.Trim();
        }

        private static void TryDelete(string path)
        {
            try
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
            catch
            {
                // Preserve the original write exception.
            }
        }
    }
}
