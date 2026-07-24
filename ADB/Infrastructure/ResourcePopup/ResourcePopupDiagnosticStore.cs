using ADB_Tool_Automation_Post_FB.Core.ResourcePopup;
using ADB_Tool_Automation_Post_FB.Infrastructure.Diagnostics;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace ADB_Tool_Automation_Post_FB.Infrastructure.ResourcePopup
{
    public sealed class ResourcePopupDiagnosticStore : IResourcePopupDiagnosticStore
    {
        private readonly string root;
        public ResourcePopupDiagnosticStore(string directory)
        {
            if (string.IsNullOrWhiteSpace(directory)) throw new ArgumentException("Directory is required.", nameof(directory));
            root = Path.GetFullPath(Path.IsPathRooted(directory)
                ? directory : Path.Combine(AppContext.BaseDirectory, directory));
        }

        public async Task<string> SaveAsync(string deviceName, ResourcePopupOutcome outcome,
            byte[] pngBytes, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!ADB_Tool_Automation_Post_FB.Core.Diagnostics.DiagnosticStorageGate.IsWriteEnabled)
                return null;
            if (pngBytes == null || pngBytes.Length == 0) throw new ArgumentException("PNG data is required.", nameof(pngBytes));
            DateTimeOffset now = DateTimeOffset.Now;
            string directory = SafeDirectory(root,
                ScreenshotPathPolicy.SanitizeDeviceName(deviceName), now.ToString("yyyy-MM-dd"));
            Directory.CreateDirectory(directory);
            string baseName = $"{now:HH-mm-ss-fff}_{outcome.ToString().ToLowerInvariant()}";
            string path = AvailablePath(directory, baseName);
            using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write,
                FileShare.None, 4096, useAsync: true))
                await stream.WriteAsync(pngBytes, 0, pngBytes.Length, cancellationToken);
            return path;
        }

        private static string SafeDirectory(string rootDirectory, params string[] segments)
        {
            string path = rootDirectory;
            foreach (string segment in segments) path = Path.Combine(path, segment);
            string full = Path.GetFullPath(path);
            string prefix = rootDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                + Path.DirectorySeparatorChar;
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
            throw new IOException("Could not allocate a unique popup diagnostic path.");
        }
    }
}
