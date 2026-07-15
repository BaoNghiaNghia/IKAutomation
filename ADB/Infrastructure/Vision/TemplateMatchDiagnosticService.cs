using ADB_Tool_Automation_Post_FB.Core.Abstractions;
using ADB_Tool_Automation_Post_FB.Core.Vision;
using ADB_Tool_Automation_Post_FB.Helpers;
using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace ADB_Tool_Automation_Post_FB.Infrastructure.Vision
{
    public sealed class TemplateMatchDiagnosticService
    {
        private readonly ILdPlayerClient ldPlayerClient;
        private readonly ITemplateRegistry templateRegistry;
        private readonly IImageMatcher imageMatcher;

        public TemplateMatchDiagnosticService(
            ILdPlayerClient ldPlayerClient,
            ITemplateRegistry templateRegistry,
            IImageMatcher imageMatcher)
        {
            this.ldPlayerClient = ldPlayerClient ?? throw new ArgumentNullException(nameof(ldPlayerClient));
            this.templateRegistry = templateRegistry ?? throw new ArgumentNullException(nameof(templateRegistry));
            this.imageMatcher = imageMatcher ?? throw new ArgumentNullException(nameof(imageMatcher));
        }

        public Task<ImageMatchResult> MatchTemplateOnDeviceAsync(
            string deviceName,
            TemplateId templateId,
            ImageRegion? region,
            CancellationToken cancellationToken)
        {
            return MatchTemplateOnDeviceAsync(deviceName, templateId, region, null, cancellationToken);
        }

        public async Task<ImageMatchResult> MatchTemplateOnDeviceAsync(
            string deviceName,
            TemplateId templateId,
            ImageRegion? region,
            string diagnosticDirectory,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            byte[] template = templateRegistry.LoadBytes(templateId);
            byte[] screenshot = await ldPlayerClient.CaptureScreenshotPngAsync(deviceName, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();

            ImageMatchResult result = imageMatcher.Find(screenshot, template, region);

            if (result.Found)
            {
                Logger.LogInfo(
                    $"[Vision Diagnostic] Device='{deviceName}', Template='{templateId}', " +
                    $"Found=true, Bounds=({result.X},{result.Y},{result.Width},{result.Height}), Confidence=n/a");
            }
            else
            {
                Logger.LogInfo(
                    $"[Vision Diagnostic] Device='{deviceName}', Template='{templateId}', Found=false, Confidence=n/a");
            }

            if (!string.IsNullOrWhiteSpace(diagnosticDirectory))
                await SaveScreenshotAsync(deviceName, templateId, screenshot, diagnosticDirectory, cancellationToken);

            return result;
        }

        private static async Task SaveScreenshotAsync(
            string deviceName,
            TemplateId templateId,
            byte[] screenshot,
            string diagnosticDirectory,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            string fullDirectory = Path.GetFullPath(diagnosticDirectory);
            Directory.CreateDirectory(fullDirectory);

            string safeDeviceName = SanitizeFileName(deviceName);
            string fileName = $"{safeDeviceName}_{templateId}_{DateTime.Now:yyyyMMdd_HHmmss_fff}.png";
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

            Logger.LogInfo($"[Vision Diagnostic] Screenshot saved: {filePath}");
        }

        private static string SanitizeFileName(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return "unknown-device";

            char[] invalidCharacters = Path.GetInvalidFileNameChars();
            return new string(value.Select(character =>
                invalidCharacters.Contains(character) ? '_' : character).ToArray());
        }
    }
}
