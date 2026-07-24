using ADB_Tool_Automation_Post_FB.Core.Abstractions;
using ADB_Tool_Automation_Post_FB.Core.Diagnostics;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace ADB_Tool_Automation_Post_FB.Infrastructure.Diagnostics
{
    public sealed class DeviceDiagnosticService : IDeviceDiagnosticService
    {
        private readonly ILdPlayerClient ldPlayerClient;
        private readonly ScreenshotFileStore screenshotFileStore;
        private readonly IDiagnosticLogger logger;

        public DeviceDiagnosticService(
            ILdPlayerClient ldPlayerClient,
            DeviceDiagnosticOptions configuration,
            ScreenshotFileStore screenshotFileStore,
            IDiagnosticLogger logger)
        {
            this.ldPlayerClient = ldPlayerClient ?? throw new ArgumentNullException(nameof(ldPlayerClient));
            Configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
            this.screenshotFileStore = screenshotFileStore ?? throw new ArgumentNullException(nameof(screenshotFileStore));
            this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public DeviceDiagnosticOptions Configuration { get; }

        public Task<IReadOnlyList<string>> GetDeviceNamesAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ldPlayerClient.GetDeviceNamesAsync(cancellationToken);
        }

        public async Task<DeviceDiagnosticResult> CheckDeviceAsync(
            string deviceName,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            DateTimeOffset startedAt = DateTimeOffset.Now;
            var stopwatch = Stopwatch.StartNew();
            var result = new DeviceDiagnosticResult
            {
                DeviceName = deviceName?.Trim(),
                ExpectedPackage = Configuration.PackageName,
                CurrentForegroundPackage = null,
                PackageMatches = null
            };

            LogStart(deviceName, "CheckDevice", startedAt);
            try
            {
                ValidateDeviceName(deviceName);
                result.IsRunning = await ldPlayerClient.IsRunningAsync(deviceName, cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();

                if (!result.IsRunning)
                {
                    result.ErrorMessage = $"LDPlayer device '{deviceName}' is not running.";
                    LogResult(deviceName, "CheckDevice", "NotRunning", stopwatch.Elapsed, null);
                    return result;
                }

                byte[] screenshot = await ldPlayerClient.CaptureScreenshotPngAsync(deviceName, cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                ReadPngDimensions(screenshot, deviceName, out int width, out int height);

                result.ScreenshotSucceeded = true;
                result.ScreenshotWidth = width;
                result.ScreenshotHeight = height;
                result.MatchesExpectedResolution = width == Configuration.ExpectedWidth
                    && height == Configuration.ExpectedHeight;

                string resolutionResult = result.MatchesExpectedResolution
                    ? $"Success; Resolution={width}x{height}; PackageCheck=Unavailable"
                    : $"ResolutionMismatch; Actual={width}x{height}; Expected={Configuration.ExpectedWidth}x{Configuration.ExpectedHeight}; PackageCheck=Unavailable";
                LogResult(deviceName, "CheckDevice", resolutionResult, stopwatch.Elapsed, null);
                return result;
            }
            catch (OperationCanceledException)
            {
                LogResult(deviceName, "CheckDevice", "Canceled", stopwatch.Elapsed, null);
                throw;
            }
            catch (Exception exception)
            {
                result.ErrorMessage = exception.Message;
                logger.Error(FormatLog(deviceName, "CheckDevice", "Failed", stopwatch.Elapsed, null, exception.Message), exception);
                return result;
            }
        }

        public Task LaunchGameAsync(string deviceName, CancellationToken cancellationToken)
        {
            return ExecuteLoggedAsync(deviceName, "LaunchGame", async () =>
            {
                ValidateDeviceName(deviceName);
                if (string.IsNullOrWhiteSpace(Configuration.PackageName))
                {
                    throw new InvalidOperationException(
                        "Infinity Kingdom package name is not configured. Set 'InfinityKingdom.PackageName' in App.config.");
                }

                await EnsureDeviceRunningAsync(deviceName, cancellationToken);
                await ldPlayerClient.RunAppAsync(deviceName, Configuration.PackageName, cancellationToken);
            }, cancellationToken);
        }

        public async Task<ScreenshotCaptureResult> CaptureScreenshotAsync(
            string deviceName,
            string stateName,
            string note,
            CancellationToken cancellationToken)
        {
            ScreenshotCaptureResult captureResult = null;
            await ExecuteLoggedAsync(deviceName, "CaptureScreenshot", async () =>
            {
                ValidateDeviceName(deviceName);
                ScreenshotPathPolicy.SanitizeStateName(stateName);
                await EnsureDeviceRunningAsync(deviceName, cancellationToken);

                byte[] screenshot = await ldPlayerClient.CaptureScreenshotPngAsync(deviceName, cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                ReadPngDimensions(screenshot, deviceName, out int width, out int height);
                captureResult = await screenshotFileStore.SaveAsync(
                    deviceName,
                    stateName,
                    note,
                    screenshot,
                    width,
                    height,
                    cancellationToken);
            }, cancellationToken, () => captureResult?.ScreenshotPath);

            return captureResult;
        }

        public Task TapAsync(string deviceName, int x, int y, CancellationToken cancellationToken)
        {
            return ExecuteLoggedAsync(deviceName, "TapPixel", async () =>
            {
                ValidateDeviceName(deviceName);
                if (x < 0)
                    throw new ArgumentOutOfRangeException(nameof(x), "Pixel X must not be negative.");
                if (y < 0)
                    throw new ArgumentOutOfRangeException(nameof(y), "Pixel Y must not be negative.");

                await EnsureDeviceRunningAsync(deviceName, cancellationToken);
                await ldPlayerClient.TapAsync(deviceName, x, y, cancellationToken);
            }, cancellationToken);
        }

        public Task TapByPercentAsync(
            string deviceName,
            double xPercent,
            double yPercent,
            CancellationToken cancellationToken)
        {
            return ExecuteLoggedAsync(deviceName, "TapPercent", async () =>
            {
                ValidateDeviceName(deviceName);
                ValidatePercent(xPercent, nameof(xPercent));
                ValidatePercent(yPercent, nameof(yPercent));
                await EnsureDeviceRunningAsync(deviceName, cancellationToken);
                await ldPlayerClient.TapByPercentAsync(deviceName, xPercent, yPercent, cancellationToken);
            }, cancellationToken);
        }

        public Task SwipeByPercentAsync(
            string deviceName,
            double startXPercent,
            double startYPercent,
            double endXPercent,
            double endYPercent,
            int durationMilliseconds,
            CancellationToken cancellationToken)
        {
            return ExecuteLoggedAsync(deviceName, "SwipePercent", async () =>
            {
                ValidateDeviceName(deviceName);
                ValidatePercent(startXPercent, nameof(startXPercent));
                ValidatePercent(startYPercent, nameof(startYPercent));
                ValidatePercent(endXPercent, nameof(endXPercent));
                ValidatePercent(endYPercent, nameof(endYPercent));
                if (durationMilliseconds <= 0)
                    throw new ArgumentOutOfRangeException(
                        nameof(durationMilliseconds),
                        "Swipe duration must be greater than zero.");

                await EnsureDeviceRunningAsync(deviceName, cancellationToken);
                await ldPlayerClient.SwipeByPercentAsync(
                    deviceName,
                    startXPercent,
                    startYPercent,
                    endXPercent,
                    endYPercent,
                    durationMilliseconds,
                    cancellationToken);
            }, cancellationToken);
        }

        public Task BackAsync(string deviceName, CancellationToken cancellationToken)
        {
            return ExecuteLoggedAsync(deviceName, "Back", async () =>
            {
                ValidateDeviceName(deviceName);
                await EnsureDeviceRunningAsync(deviceName, cancellationToken);
                await ldPlayerClient.BackAsync(deviceName, cancellationToken);
            }, cancellationToken);
        }

        private async Task EnsureDeviceRunningAsync(string deviceName, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            bool isRunning = await ldPlayerClient.IsRunningAsync(deviceName, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (!isRunning)
                throw new InvalidOperationException($"LDPlayer device '{deviceName}' is not running.");
        }

        private async Task ExecuteLoggedAsync(
            string deviceName,
            string operation,
            Func<Task> action,
            CancellationToken cancellationToken,
            Func<string> screenshotPath = null)
        {
            cancellationToken.ThrowIfCancellationRequested();
            DateTimeOffset startedAt = DateTimeOffset.Now;
            var stopwatch = Stopwatch.StartNew();
            LogStart(deviceName, operation, startedAt);
            try
            {
                await action();
                cancellationToken.ThrowIfCancellationRequested();
                LogResult(deviceName, operation, "Success", stopwatch.Elapsed, screenshotPath?.Invoke());
            }
            catch (OperationCanceledException)
            {
                LogResult(deviceName, operation, "Canceled", stopwatch.Elapsed, screenshotPath?.Invoke());
                throw;
            }
            catch (Exception exception)
            {
                logger.Error(
                    FormatLog(deviceName, operation, "Failed", stopwatch.Elapsed, screenshotPath?.Invoke(), exception.Message),
                    exception);
                throw;
            }
        }

        private void LogStart(string deviceName, string operation, DateTimeOffset startedAt)
        {
            logger.Info(
                $"[Device Diagnostic] Device='{deviceName}', Operation='{operation}', StartTime='{startedAt:o}', Result='Started'");
        }

        private void LogResult(
            string deviceName,
            string operation,
            string result,
            TimeSpan duration,
            string screenshotPath)
        {
            logger.Info(FormatLog(deviceName, operation, result, duration, screenshotPath, null));
        }

        private static string FormatLog(
            string deviceName,
            string operation,
            string result,
            TimeSpan duration,
            string screenshotPath,
            string error)
        {
            return $"[Device Diagnostic] Device='{deviceName}', Operation='{operation}', Result='{result}', "
                + $"DurationMs={duration.TotalMilliseconds:F0}, ScreenshotPath='{screenshotPath ?? string.Empty}', "
                + $"Error='{error ?? string.Empty}'";
        }

        private static void ReadPngDimensions(
            byte[] screenshot,
            string deviceName,
            out int width,
            out int height)
        {
            if (screenshot == null || screenshot.Length == 0)
            {
                throw new InvalidOperationException(
                    $"Screenshot capture returned no PNG data for LDPlayer device '{deviceName}'.");
            }

            try
            {
                using (var stream = new MemoryStream(screenshot, writable: false))
                using (Image image = Image.FromStream(stream, useEmbeddedColorManagement: false, validateImageData: true))
                {
                    width = image.Width;
                    height = image.Height;
                }
            }
            catch (Exception exception)
            {
                throw new InvalidOperationException(
                    $"Screenshot from LDPlayer device '{deviceName}' is not a valid PNG image.",
                    exception);
            }
        }

        private static void ValidateDeviceName(string deviceName)
        {
            if (string.IsNullOrWhiteSpace(deviceName))
                throw new ArgumentException("LDPlayer device name is required.", nameof(deviceName));
        }

        private static void ValidatePercent(double value, string parameterName)
        {
            if (double.IsNaN(value) || double.IsInfinity(value) || value < 0 || value > 100)
                throw new ArgumentOutOfRangeException(parameterName, "Percent must be between 0 and 100.");
        }
    }
}
