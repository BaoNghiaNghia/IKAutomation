using ADB_Tool_Automation_Post_FB.Core.Abstractions;
using ADB_Tool_Automation_Post_FB.Core.Workflows;
using ADB_Tool_Automation_Post_FB.Infrastructure.Diagnostics;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace ADB_Tool_Automation_Post_FB.Infrastructure.Workflows
{
    public sealed class OneShotFarmDiagnosticService : IOneShotFarmDiagnosticService
    {
        private readonly ILdPlayerClient client; private readonly string root;
        public OneShotFarmDiagnosticService(ILdPlayerClient client, string rootDirectory)
        { this.client = client ?? throw new ArgumentNullException(nameof(client)); root = Path.GetFullPath(rootDirectory); }
        public async Task<string> CaptureAsync(string deviceName, OneShotFarmStep step,
            OneShotFarmOutcome outcome, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            byte[] png = await client.CaptureScreenshotPngAsync(deviceName, cancellationToken);
            DateTimeOffset now = DateTimeOffset.Now;
            string dir = Path.Combine(root, ScreenshotPathPolicy.SanitizeDeviceName(deviceName), now.ToString("yyyy-MM-dd"));
            Directory.CreateDirectory(dir);
            string stem = ScreenshotPathPolicy.SanitizeStateName(step.ToString().ToLowerInvariant()) + "_"
                + ScreenshotPathPolicy.SanitizeStateName(outcome.ToString().ToLowerInvariant());
            for (int i = 0; i < 10; i++)
            {
                string path = Path.Combine(dir, $"{now:HH-mm-ss-fff}_{stem}{(i == 0 ? "" : "_" + i)}.png");
                try { using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true)) await stream.WriteAsync(png, 0, png.Length, cancellationToken); return path; }
                catch (IOException) when (File.Exists(path)) { }
            }
            throw new IOException("Could not create a unique one-shot diagnostic screenshot.");
        }
    }
}
