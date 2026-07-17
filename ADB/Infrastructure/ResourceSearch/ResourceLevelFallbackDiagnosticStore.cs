using ADB_Tool_Automation_Post_FB.Core.ResourceSearch;
using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace ADB_Tool_Automation_Post_FB.Infrastructure.ResourceSearch
{
    public sealed class ResourceLevelFallbackDiagnosticStore : IResourceLevelFallbackDiagnosticStore
    {
        private readonly string rootDirectory;
        public ResourceLevelFallbackDiagnosticStore(string relativeDirectory)
        {
            if (string.IsNullOrWhiteSpace(relativeDirectory)) throw new ArgumentException(nameof(relativeDirectory));
            rootDirectory = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
                relativeDirectory.Replace('/', Path.DirectorySeparatorChar)));
        }

        public Task<string> SaveAsync(string deviceName, string fileSuffix,
            byte[] pngBytes, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (pngBytes == null || pngBytes.Length == 0) throw new ArgumentException("PNG bytes are required.", nameof(pngBytes));
            DateTime now = DateTime.Now;
            string directory = Path.Combine(rootDirectory, Sanitize(deviceName), now.ToString("yyyy-MM-dd"));
            Directory.CreateDirectory(directory);
            string baseName = $"{now:HH-mm-ss-fff}_{Sanitize(fileSuffix)}";
            string path = Path.Combine(directory, baseName + ".png");
            int suffix = 1;
            while (File.Exists(path)) path = Path.Combine(directory, $"{baseName}_{suffix++}.png");
            File.WriteAllBytes(path, pngBytes);
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(path);
        }

        private static string Sanitize(string value)
        {
            string safe = string.IsNullOrWhiteSpace(value) ? "unknown" : value.Trim();
            foreach (char invalid in Path.GetInvalidFileNameChars()) safe = safe.Replace(invalid, '_');
            safe = new string(safe.Where(ch => ch != '/' && ch != '\\').ToArray());
            if (safe == "." || safe == "..") safe = "unknown";
            return string.IsNullOrWhiteSpace(safe) ? "unknown" : safe;
        }
    }
}
