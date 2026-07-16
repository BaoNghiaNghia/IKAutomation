using ADB_Tool_Automation_Post_FB.Core.TeamSelection;
using ADB_Tool_Automation_Post_FB.Infrastructure.Diagnostics;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace ADB_Tool_Automation_Post_FB.Infrastructure.TeamSelection
{
    public sealed class SelectFarmTeamDiagnosticStore : ISelectFarmTeamDiagnosticStore
    {
        private readonly string root;
        public SelectFarmTeamDiagnosticStore(string rootDirectory)
        {
            if (string.IsNullOrWhiteSpace(rootDirectory))
                throw new ArgumentException("Diagnostic root is required.", nameof(rootDirectory));
            root = Path.GetFullPath(rootDirectory);
        }

        public async Task<string> SaveAsync(string deviceName, SelectFarmTeamOutcome outcome,
            byte[] screenshotPng, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (screenshotPng == null || screenshotPng.Length == 0)
                throw new ArgumentException("Screenshot PNG is required.", nameof(screenshotPng));
            DateTimeOffset now = DateTimeOffset.Now;
            string directory = Path.Combine(root,
                ScreenshotPathPolicy.SanitizeDeviceName(deviceName), now.ToString("yyyy-MM-dd"));
            Directory.CreateDirectory(directory);
            string safeOutcome = ScreenshotPathPolicy.SanitizeStateName(
                outcome.ToString().ToLowerInvariant());
            for (int suffix = 0; suffix < 10; suffix++)
            {
                string extra = suffix == 0 ? string.Empty : "_" + suffix;
                string path = Path.Combine(directory,
                    $"{now:HH-mm-ss-fff}_{safeOutcome}{extra}.png");
                try
                {
                    using (var stream = new FileStream(path, FileMode.CreateNew,
                        FileAccess.Write, FileShare.None, 81920, true))
                        await stream.WriteAsync(screenshotPng, 0,
                            screenshotPng.Length, cancellationToken);
                    return path;
                }
                catch (IOException) when (File.Exists(path)) { }
            }
            throw new IOException("Could not create a unique Farm Team Selection diagnostic screenshot.");
        }
    }
}
