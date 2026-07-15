using ADB_Tool_Automation_Post_FB.Core.Abstractions;
using Auto_LDPlayer;
using Auto_LDPlayer.Enums;
using System;
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

            Auto_LDPlayer.LDPlayer.PathLD = resolvedPath;
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

        public Task<byte[]> CaptureScreenshotPngAsync(string deviceName, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ValidateDeviceName(deviceName);

            try
            {
                using (Bitmap screenshot = Auto_LDPlayer.LDPlayer.ScreenShoot(LDType.Name, deviceName))
                {
                    if (screenshot == null)
                        throw new InvalidOperationException($"Screenshot returned null for LDPlayer device '{deviceName}'.");

                    cancellationToken.ThrowIfCancellationRequested();

                    using (var stream = new MemoryStream())
                    {
                        screenshot.Save(stream, ImageFormat.Png);
                        return Task.FromResult(stream.ToArray());
                    }
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    $"Failed to capture PNG screenshot from LDPlayer device '{deviceName}'.",
                    ex);
            }
        }

        public Task TapAsync(string deviceName, int x, int y, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ValidateDeviceName(deviceName);

            Auto_LDPlayer.LDPlayer.Tap(LDType.Name, deviceName, x, y);
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
