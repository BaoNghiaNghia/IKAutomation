using ADB_Tool_Automation_Post_FB.Core.Abstractions;
using Auto_LDPlayer;
using Auto_LDPlayer.Enums;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace ADB_Tool_Automation_Post_FB.Infrastructure.LDPlayer
{
    /// <summary>
    /// Auto_LDPlayer-backed implementation of the LDPlayer automation boundary.
    /// New automation code must depend on ILdPlayerClient instead of calling
    /// Auto_LDPlayer.LDPlayer directly.
    /// </summary>
    public sealed class AutoLdPlayerClient : ILdPlayerClient
    {
        private const int InputCommandTimeoutMilliseconds = 3000;

        private const int ScreenshotReadyAttempts = 3;
        private const int ScreenshotCaptureAttempts = 4;
        private const int ScreenshotCaptureRetryDelayMilliseconds = 500;
        private static readonly ConcurrentDictionary<string, SemaphoreSlim> ScreenshotLocks =
            new ConcurrentDictionary<string, SemaphoreSlim>(StringComparer.OrdinalIgnoreCase);

        public static string ConfigureLdConsolePath(string configuredPath)
        {
            string expandedPath = string.IsNullOrWhiteSpace(configuredPath)
                ? null
                : Environment.ExpandEnvironmentVariables(configuredPath.Trim());
            string[] candidates =
            {
                expandedPath,
                @"C:\LDPlayer\LDPlayer9\ldconsole.exe",
                @"D:\LDPlayer\LDPlayer9\ldconsole.exe"
            };

            string resolvedPath = candidates.FirstOrDefault(path =>
                !string.IsNullOrWhiteSpace(path) && File.Exists(path));
            if (resolvedPath == null)
            {
                throw new FileNotFoundException(
                    "LDPlayer console was not found. Configure 'LDCONSOLE_PATH' in App.config.",
                    expandedPath);
            }

            string ldPlayerDirectory = Path.GetDirectoryName(resolvedPath);
            string adbPath = Path.Combine(ldPlayerDirectory, "adb.exe");
            if (!File.Exists(adbPath))
            {
                throw new FileNotFoundException(
                    $"LDPlayer ADB was not found beside '{resolvedPath}'.",
                    adbPath);
            }

            Auto_LDPlayer.LDPlayer.PathLD = resolvedPath;
            KAutoHelper.ADBHelper.SetADBFolderPath(ldPlayerDirectory);
            return resolvedPath;
        }

        public Task<IReadOnlyList<string>> GetDeviceNamesAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var runningDevices = Auto_LDPlayer.LDPlayer.GetDevicesRunning()
                ?? new List<string>();
            var allDevices = Auto_LDPlayer.LDPlayer.GetDevices()
                ?? new List<string>();

            cancellationToken.ThrowIfCancellationRequested();
            IReadOnlyList<string> deviceNames = runningDevices
                .Concat(allDevices)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Select(name => name.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            return Task.FromResult(deviceNames);
        }

        public Task<bool> IsRunningAsync(string deviceName, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ValidateDeviceName(deviceName);

            bool isRunning = Auto_LDPlayer.LDPlayer.IsDeviceRunning(LDType.Name, deviceName);
            return Task.FromResult(isRunning);
        }

        public Task OpenAsync(string deviceName, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ValidateDeviceName(deviceName);

            Auto_LDPlayer.LDPlayer.Open(LDType.Name, deviceName);
            return Task.CompletedTask;
        }

        public Task CloseAsync(string deviceName, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ValidateDeviceName(deviceName);

            Auto_LDPlayer.LDPlayer.Close(LDType.Name, deviceName);
            return Task.CompletedTask;
        }

        public Task RunAppAsync(string deviceName, string packageName, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ValidateDeviceName(deviceName);

            if (string.IsNullOrWhiteSpace(packageName))
                throw new ArgumentException("Android package name is required.", nameof(packageName));

            Auto_LDPlayer.LDPlayer.RunApp(LDType.Name, deviceName, packageName);
            return Task.CompletedTask;
        }

        public async Task<byte[]> CaptureScreenshotPngAsync(string deviceName, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ValidateDeviceName(deviceName);

            string normalizedDeviceName = deviceName.Trim();
            SemaphoreSlim screenshotLock = ScreenshotLocks.GetOrAdd(
                normalizedDeviceName,
                _ => new SemaphoreSlim(1, 1));

            await screenshotLock.WaitAsync(cancellationToken);
            try
            {
                string adbState = null;
                for (int attempt = 1; attempt <= ScreenshotReadyAttempts; attempt++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    adbState = Auto_LDPlayer.LDPlayer.Adb(
                        LDType.Name,
                        normalizedDeviceName,
                        "get-state",
                        3000,
                        1);

                    if (string.Equals(adbState?.Trim(), "device", StringComparison.OrdinalIgnoreCase))
                        break;

                    if (attempt < ScreenshotReadyAttempts)
                        await Task.Delay(300, cancellationToken);
                }

                if (!string.Equals(adbState?.Trim(), "device", StringComparison.OrdinalIgnoreCase))
                {
                    string response = string.IsNullOrWhiteSpace(adbState)
                        ? "no response"
                        : adbState.Trim();
                    throw new InvalidOperationException(
                        $"LDPlayer device '{normalizedDeviceName}' is not available through ADB. "
                        + "In LDPlayer, open Settings > Other settings, set ADB debugging to "
                        + $"Open local connection, save, and restart the emulator. ADB response: {response}");
                }

                for (int attempt = 1; attempt <= ScreenshotCaptureAttempts; attempt++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    string screenshotFileName = $"ikautomation_{Guid.NewGuid():N}.png";
                    using (Bitmap screenshot = Auto_LDPlayer.LDPlayer.ScreenShoot(
                        LDType.Name,
                        normalizedDeviceName,
                        true,
                        screenshotFileName))
                    {
                        if (screenshot != null)
                        {
                            cancellationToken.ThrowIfCancellationRequested();

                            using (var stream = new MemoryStream())
                            {
                                screenshot.Save(stream, ImageFormat.Png);
                                return stream.ToArray();
                            }
                        }
                    }

                    if (attempt < ScreenshotCaptureAttempts)
                        await Task.Delay(ScreenshotCaptureRetryDelayMilliseconds,
                            cancellationToken);
                }

                throw new InvalidOperationException(
                    $"Auto_LDPlayer returned no screenshot for LDPlayer device '{normalizedDeviceName}' "
                    + $"after ADB reported ready and {ScreenshotCaptureAttempts} capture attempts.");
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    $"Failed to capture PNG screenshot from LDPlayer device '{deviceName}': {ex.Message}",
                    ex);
            }
            finally
            {
                screenshotLock.Release();
            }
        }

        public Task TapAsync(string deviceName, int x, int y, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ValidateDeviceName(deviceName);

            // LDPlayer.Tap uses a 200 ms process timeout and retries once. The first
            // command can reach Android even when ldnconsole has not exited yet, so
            // its retry may become a delayed second tap after the current panel has
            // closed. Input commands have side effects and must never be retried.
            string output = Auto_LDPlayer.LDPlayer.Adb(
                LDType.Name,
                deviceName,
                $"shell input tap {x} {y}",
                InputCommandTimeoutMilliseconds,
                0);
            if (output == null)
                throw new InvalidOperationException(
                    $"Failed to send a single tap to LDPlayer device '{deviceName}' at ({x}, {y}).");

            return Task.CompletedTask;
        }

        public Task TapByPercentAsync(string deviceName, double xPercent, double yPercent, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ValidateDeviceName(deviceName);

            Auto_LDPlayer.LDPlayer.TapByPercent(LDType.Name, deviceName, xPercent, yPercent);
            return Task.CompletedTask;
        }

        public Task LongPressAsync(
            string deviceName,
            int x,
            int y,
            int durationMilliseconds,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ValidateDeviceName(deviceName);

            Auto_LDPlayer.LDPlayer.LongPress(LDType.Name, deviceName, x, y, durationMilliseconds);
            return Task.CompletedTask;
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
            cancellationToken.ThrowIfCancellationRequested();
            ValidateDeviceName(deviceName);

            Auto_LDPlayer.LDPlayer.SwipeByPercent(
                LDType.Name,
                deviceName,
                startXPercent,
                startYPercent,
                endXPercent,
                endYPercent,
                durationMilliseconds);

            return Task.CompletedTask;
        }

        public Task BackAsync(string deviceName, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ValidateDeviceName(deviceName);

            Auto_LDPlayer.LDPlayer.Back(LDType.Name, deviceName);
            return Task.CompletedTask;
        }

        public Task InputTextAsync(string deviceName, string text, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ValidateDeviceName(deviceName);

            if (text == null)
                throw new ArgumentNullException(nameof(text));

            Auto_LDPlayer.LDPlayer.InputText(LDType.Name, deviceName, text);
            return Task.CompletedTask;
        }

        public Task PressKeyAsync(
            string deviceName,
            AndroidKeyCode keyCode,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ValidateDeviceName(deviceName);

            Auto_LDPlayer.LDPlayer.PressKey(LDType.Name, deviceName, (LDKeyEvent)(int)keyCode);
            return Task.CompletedTask;
        }

        private static void ValidateDeviceName(string deviceName)
        {
            if (string.IsNullOrWhiteSpace(deviceName))
                throw new ArgumentException("LDPlayer device name is required.", nameof(deviceName));
        }
    }
}
