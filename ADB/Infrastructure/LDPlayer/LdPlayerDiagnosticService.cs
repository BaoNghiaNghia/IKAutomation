using ADB_Tool_Automation_Post_FB.Core.Abstractions;
using ADB_Tool_Automation_Post_FB.Core.Diagnostics;
using ADB_Tool_Automation_Post_FB.Helpers;
using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace ADB_Tool_Automation_Post_FB.Infrastructure.LDPlayer
{
    /// <summary>
    /// Explicitly invoked smoke diagnostic for the LDPlayer boundary.
    /// This service is not wired into application startup or existing UI workflows.
    /// </summary>
    public sealed class LdPlayerDiagnosticService
    {
        private readonly ILdPlayerClient ldPlayerClient;

        public LdPlayerDiagnosticService(ILdPlayerClient ldPlayerClient)
        {
            this.ldPlayerClient = ldPlayerClient ?? throw new ArgumentNullException(nameof(ldPlayerClient));
        }

        public async Task<string> CaptureRunningDeviceAsync(
            string deviceName,
            string diagnosticDirectory,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!DiagnosticStorageGate.IsWriteEnabled) return null;

            if (string.IsNullOrWhiteSpace(diagnosticDirectory))
                throw new ArgumentException("Diagnostic directory is required.", nameof(diagnosticDirectory));

            bool isRunning = await ldPlayerClient.IsRunningAsync(deviceName, cancellationToken);
            if (!isRunning)
                throw new InvalidOperationException($"LDPlayer device '{deviceName}' is not running.");

            byte[] screenshot = await ldPlayerClient.CaptureScreenshotPngAsync(deviceName, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();

            string fullDirectory = Path.GetFullPath(diagnosticDirectory);
            Directory.CreateDirectory(fullDirectory);

            string safeDeviceName = SanitizeFileName(deviceName);
            string fileName = $"{safeDeviceName}_{DateTime.Now:yyyyMMdd_HHmmss_fff}.png";
            string filePath = Path.Combine(fullDirectory, fileName);

            using (var stream = new FileStream(
                filePath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                4096,
                useAsync: true))
            {
                await stream.WriteAsync(screenshot, 0, screenshot.Length, cancellationToken);
            }

            Logger.LogInfo($"[LDPlayer Diagnostic] Screenshot saved: {filePath}");
            return filePath;
        }

        private static string SanitizeFileName(string value)
        {
            char[] invalidCharacters = Path.GetInvalidFileNameChars();
            return new string(value.Select(character =>
                invalidCharacters.Contains(character) ? '_' : character).ToArray());
        }
    }
}
