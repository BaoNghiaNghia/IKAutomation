using ADB_Tool_Automation_Post_FB.Core.ResourceSearch;
using ADB_Tool_Automation_Post_FB.Infrastructure.Diagnostics;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace ADB_Tool_Automation_Post_FB.Infrastructure.ResourceSearch
{
    public sealed class ResourceSearchDiagnosticStore : IResourceSearchDiagnosticStore
    {
        private readonly string resultRoot;
        private readonly string observationRoot;

        public ResourceSearchDiagnosticStore(string resultDirectory, string observationDirectory)
        {
            resultRoot = ResolveRoot(resultDirectory, nameof(resultDirectory));
            observationRoot = ResolveRoot(observationDirectory, nameof(observationDirectory));
        }

        public async Task<string> SaveResultAsync(string deviceName, ResourceSearchOutcome outcome,
            byte[] pngBytes, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!ADB_Tool_Automation_Post_FB.Core.Diagnostics.DiagnosticStorageGate.IsWriteEnabled)
                return null;
            ValidateBytes(pngBytes);
            DateTimeOffset now = DateTimeOffset.Now;
            string directory = SafeDirectory(resultRoot,
                ScreenshotPathPolicy.SanitizeDeviceName(deviceName), now.ToString("yyyy-MM-dd"));
            Directory.CreateDirectory(directory);
            string baseName = $"{now:HH-mm-ss-fff}_{outcome.ToString().ToLowerInvariant()}";
            string path = AvailablePath(directory, baseName);
            await WriteAsync(path, pngBytes, cancellationToken);
            return path;
        }

        public async Task SaveObservationAsync(string deviceName, DateTimeOffset burstTimestamp,
            int frameIndex, byte[] pngBytes, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!ADB_Tool_Automation_Post_FB.Core.Diagnostics.DiagnosticStorageGate.IsWriteEnabled)
                return;
            if (frameIndex < 1) throw new ArgumentOutOfRangeException(nameof(frameIndex));
            ValidateBytes(pngBytes);
            string directory = SafeDirectory(observationRoot,
                ScreenshotPathPolicy.SanitizeDeviceName(deviceName), burstTimestamp.ToString("yyyy-MM-dd_HH-mm-ss-fff"));
            Directory.CreateDirectory(directory);
            string path = Path.Combine(directory, $"frame_{frameIndex:000}.png");
            await WriteAsync(path, pngBytes, cancellationToken);
        }

        private static string ResolveRoot(string configured, string parameterName)
        {
            if (string.IsNullOrWhiteSpace(configured)) throw new ArgumentException("Diagnostic directory is required.", parameterName);
            return Path.IsPathRooted(configured)
                ? Path.GetFullPath(configured)
                : Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, configured));
        }

        private static string SafeDirectory(string root, params string[] segments)
        {
            string path = root;
            foreach (string segment in segments) path = Path.Combine(path, segment);
            string full = Path.GetFullPath(path);
            string prefix = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Diagnostic path escaped its configured directory.");
            return full;
        }

        private static string AvailablePath(string directory, string baseName)
        {
            for (int suffix = 0; suffix <= 999; suffix++)
            {
                string name = suffix == 0 ? baseName + ".png" : $"{baseName}_{suffix:000}.png";
                string path = Path.Combine(directory, name);
                if (!File.Exists(path)) return path;
            }
            throw new IOException("Could not allocate a unique diagnostic screenshot filename.");
        }

        private static async Task WriteAsync(string path, byte[] bytes, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write,
                FileShare.None, 4096, useAsync: true))
                await stream.WriteAsync(bytes, 0, bytes.Length, token);
        }

        private static void ValidateBytes(byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0) throw new ArgumentException("PNG data is required.", nameof(bytes));
        }
    }
}
