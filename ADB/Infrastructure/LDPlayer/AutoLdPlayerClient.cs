using ADB_Tool_Automation_Post_FB.Core.Abstractions;
using ADB_Tool_Automation_Post_FB.Core.Diagnostics;
using ADB_Tool_Automation_Post_FB.Core.Vision;
using Auto_LDPlayer;
using Auto_LDPlayer.Enums;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Configuration;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace ADB_Tool_Automation_Post_FB.Infrastructure.LDPlayer
{
    /// <summary>
    /// Auto_LDPlayer-backed implementation of the LDPlayer automation boundary.
    /// New automation code must depend on ILdPlayerClient instead of calling
    /// Auto_LDPlayer.LDPlayer directly.
    /// </summary>
    public sealed class AutoLdPlayerClient : ILdPlayerClient, IFocusedInputValueReader,
        IFrameCapturingLdPlayerClient
    {
        private const int InputCommandTimeoutMilliseconds = 3000;
        private const int FocusedInputReadAttempts = 3;
        private const int FocusedInputRetryDelayMilliseconds = 150;

        private const int ScreenshotReadyAttempts = 3;
        private const int ScreenshotCaptureAttempts = 4;
        private const int ScreenshotCaptureRetryDelayMilliseconds = 500;
        private static readonly ConcurrentDictionary<string, SemaphoreSlim> ScreenshotLocks =
            new ConcurrentDictionary<string, SemaphoreSlim>(StringComparer.OrdinalIgnoreCase);
        private static readonly ConcurrentDictionary<string, DateTimeOffset> HealthyDevices =
            new ConcurrentDictionary<string, DateTimeOffset>(StringComparer.OrdinalIgnoreCase);
        private static readonly SemaphoreSlim ScreenshotGate = new SemaphoreSlim(
            ReadPositiveSetting("Operations.MaxConcurrentScreenshots", 4),
            ReadPositiveSetting("Operations.MaxConcurrentScreenshots", 4));
        private static readonly int AdbHealthTtlMilliseconds =
            ReadPositiveSetting("Operations.AdbHealthTtlMs", 3000);
        private static long framesCaptured;
        private static long framesEncodedToPng;
        private static long screenshotGateWaitMs;
        private static long screenShootDurationMs;
        private static long adbHealthChecks;
        private static long adbHealthCacheHits;
        private static long normalBitmapCaptures;
        private static long recoveredFileCaptures;
        private static long recoveryDirectoryScans;
        private static long screenshotRetries;
        private static long screenshotFailures;
        private static long screenshotTotalDurationMs;
        private static int screenshotQueueDepth;
        private static int activeScreenshotOperations;
        private static string lastScreenshotDeviceName;
        private static string lastScreenshotWorkflowStage;
        private static int lastScreenshotDeviceIndex = -1;
        private static int lastScreenshotAttemptNumber;

        public static ScreenshotCaptureMetrics GetScreenshotCaptureMetrics()
        {
            return new ScreenshotCaptureMetrics
            {
                FramesCaptured = Interlocked.Read(ref framesCaptured),
                FramesEncodedToPng = Interlocked.Read(ref framesEncodedToPng),
                PngEncodes = Interlocked.Read(ref framesEncodedToPng),
                ScreenshotGateWaitMs = Interlocked.Read(ref screenshotGateWaitMs),
                ScreenShootDurationMs = Interlocked.Read(ref screenShootDurationMs),
                ScreenshotCaptureDurationMs = Interlocked.Read(ref screenShootDurationMs),
                ScreenshotTotalDurationMs = Interlocked.Read(ref screenshotTotalDurationMs),
                AdbHealthChecks = Interlocked.Read(ref adbHealthChecks),
                AdbHealthCacheHits = Interlocked.Read(ref adbHealthCacheHits),
                NormalBitmapCaptures = Interlocked.Read(ref normalBitmapCaptures),
                RecoveredFileCaptures = Interlocked.Read(ref recoveredFileCaptures),
                RecoveryDirectoryScans = Interlocked.Read(ref recoveryDirectoryScans),
                ScreenshotRetries = Interlocked.Read(ref screenshotRetries),
                ScreenshotFailures = Interlocked.Read(ref screenshotFailures),
                ScreenshotRetryCount = Interlocked.Read(ref screenshotRetries),
                ScreenshotFailureCount = Interlocked.Read(ref screenshotFailures),
                ScreenshotQueueDepth = Volatile.Read(ref screenshotQueueDepth),
                ActiveScreenshotOperations = Volatile.Read(ref activeScreenshotOperations),
                LastDeviceName = Volatile.Read(ref lastScreenshotDeviceName),
                LastDeviceIndex = Volatile.Read(ref lastScreenshotDeviceIndex),
                LastWorkflowStage = Volatile.Read(ref lastScreenshotWorkflowStage)
                    ?? "Unspecified",
                LastAttemptNumber = Volatile.Read(ref lastScreenshotAttemptNumber)
            };
        }

        private static int ReadPositiveSetting(string key, int fallback)
        {
            int value;
            return int.TryParse(ConfigurationManager.AppSettings[key], out value) && value > 0
                ? value : fallback;
        }

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
            using (CapturedFrame frame = await CaptureFrameAsync(deviceName, cancellationToken))
                return frame.GetPngBytes();
        }

        public async Task<CapturedFrame> CaptureFrameAsync(string deviceName, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ValidateDeviceName(deviceName);

            string normalizedDeviceName = deviceName.Trim();
            var totalWatch = Stopwatch.StartNew();
            Volatile.Write(ref lastScreenshotDeviceName, normalizedDeviceName);
            Volatile.Write(ref lastScreenshotDeviceIndex, ParseDeviceIndex(normalizedDeviceName));
            Volatile.Write(ref lastScreenshotWorkflowStage,
                ScreenshotCaptureContext.WorkflowStage);
            SemaphoreSlim screenshotLock = ScreenshotLocks.GetOrAdd(
                normalizedDeviceName,
                _ => new SemaphoreSlim(1, 1));

            try
            {
                await screenshotLock.WaitAsync(cancellationToken);
                try
                {
                string adbState = "device";
                if (!IsRecentlyHealthy(normalizedDeviceName))
                {
                    adbState = null;
                    for (int attempt = 1; attempt <= ScreenshotReadyAttempts; attempt++)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        Interlocked.Increment(ref adbHealthChecks);
                        adbState = Auto_LDPlayer.LDPlayer.Adb(
                            LDType.Name, normalizedDeviceName, "get-state", 3000, 1);

                        if (string.Equals(adbState?.Trim(), "device", StringComparison.OrdinalIgnoreCase))
                            break;

                        if (attempt < ScreenshotReadyAttempts)
                            await Task.Delay(300, cancellationToken);
                    }
                }
                else
                    Interlocked.Increment(ref adbHealthCacheHits);

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
                HealthyDevices[normalizedDeviceName] = DateTimeOffset.UtcNow;

                for (int attempt = 1; attempt <= ScreenshotCaptureAttempts; attempt++)
                {
                    Volatile.Write(ref lastScreenshotAttemptNumber, attempt);
                    cancellationToken.ThrowIfCancellationRequested();
                    if (attempt > 1)
                    {
                        Interlocked.Increment(ref screenshotRetries);
                        Interlocked.Increment(ref adbHealthChecks);
                        string retryAdbState = Auto_LDPlayer.LDPlayer.Adb(
                            LDType.Name,
                            normalizedDeviceName,
                            "get-state",
                            InputCommandTimeoutMilliseconds,
                            1);
                        if (!string.Equals(retryAdbState?.Trim(), "device",
                            StringComparison.OrdinalIgnoreCase))
                        {
                            string response = string.IsNullOrWhiteSpace(retryAdbState)
                                ? "no response"
                                : retryAdbState.Trim();
                            throw new InvalidOperationException(
                                $"LDPlayer device '{normalizedDeviceName}' is not available through ADB "
                                + $"after screenshot attempt {attempt - 1}. ADB response: {response}");
                        }
                        HealthyDevices[normalizedDeviceName] = DateTimeOffset.UtcNow;
                    }

                    string screenshotFileName = $"ikautomation_{Guid.NewGuid():N}.png";
                    string generatedFilePrefix = Path.GetFileNameWithoutExtension(
                        screenshotFileName);
                    Bitmap screenshot = null;
                    var gateWait = Stopwatch.StartNew();
                    Interlocked.Increment(ref screenshotQueueDepth);
                    try { await ScreenshotGate.WaitAsync(cancellationToken); }
                    finally { Interlocked.Decrement(ref screenshotQueueDepth); }
                    gateWait.Stop();
                    Interlocked.Increment(ref activeScreenshotOperations);
                    Interlocked.Add(ref screenshotGateWaitMs, gateWait.ElapsedMilliseconds);
                    try
                    {
                        var screenShootWatch = Stopwatch.StartNew();
                        try
                        {
                            screenshot = Auto_LDPlayer.LDPlayer.ScreenShoot(
                                LDType.Name, normalizedDeviceName, true, screenshotFileName);
                        }
                        finally
                        {
                            screenShootWatch.Stop();
                            Interlocked.Add(ref screenShootDurationMs,
                                screenShootWatch.ElapsedMilliseconds);
                        }
                    }
                    finally
                    {
                        Interlocked.Decrement(ref activeScreenshotOperations);
                        ScreenshotGate.Release();
                        RuntimePressureMetrics.ReportScreenshot(gateWait.ElapsedMilliseconds,
                            screenshot == null, Volatile.Read(ref screenshotQueueDepth),
                            Volatile.Read(ref activeScreenshotOperations));
                    }
                    if (screenshot != null)
                    {
                        Interlocked.Increment(ref framesCaptured);
                        Interlocked.Increment(ref normalBitmapCaptures);
                        return new CapturedFrame(screenshot, DateTimeOffset.UtcNow,
                            OnFrameEncodedToPng);
                    }

                    // Auto_LDPlayer can pull a valid artifact but return null when an
                    // instance name contains spaces. Recovery is deliberately outside
                    // ScreenshotGate and is never attempted after a normal Bitmap capture.
                    CapturedFrame recovered = TryRecoverGeneratedScreenshot(
                        generatedFilePrefix, cancellationToken);
                    if (recovered != null)
                    {
                        Interlocked.Increment(ref framesCaptured);
                        Interlocked.Increment(ref recoveredFileCaptures);
                        return recovered;
                    }

                    if (attempt < ScreenshotCaptureAttempts)
                    {
                        HealthyDevices.TryRemove(normalizedDeviceName, out DateTimeOffset ignoredHealth);
                        await Task.Delay(ScreenshotCaptureRetryDelayMilliseconds,
                            cancellationToken);
                    }
                }

                throw new InvalidOperationException(
                    $"Auto_LDPlayer returned no screenshot for LDPlayer device '{normalizedDeviceName}' "
                    + $"after ADB reported ready and {ScreenshotCaptureAttempts} capture attempts.");
                }
                finally
                {
                    screenshotLock.Release();
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                Interlocked.Increment(ref screenshotFailures);
                RuntimePressureMetrics.ReportScreenshot(0, true,
                    Volatile.Read(ref screenshotQueueDepth),
                    Volatile.Read(ref activeScreenshotOperations));
                HealthyDevices.TryRemove(normalizedDeviceName, out DateTimeOffset ignoredHealth);
                throw new InvalidOperationException(
                    $"Failed to capture PNG screenshot from LDPlayer device '{deviceName}': {ex.Message}",
                    ex);
            }
            finally
            {
                totalWatch.Stop();
                Interlocked.Add(ref screenshotTotalDurationMs, totalWatch.ElapsedMilliseconds);
            }
        }

        private static int ParseDeviceIndex(string deviceName)
        {
            int start = deviceName?.Length ?? 0;
            while (start > 0 && char.IsDigit(deviceName[start - 1])) start--;
            return start < (deviceName?.Length ?? 0)
                && int.TryParse(deviceName.Substring(start), out int value) ? value : -1;
        }

        private static bool IsRecentlyHealthy(string deviceName)
        {
            DateTimeOffset confirmedAt;
            return HealthyDevices.TryGetValue(deviceName, out confirmedAt)
                && DateTimeOffset.UtcNow - confirmedAt
                    < TimeSpan.FromMilliseconds(AdbHealthTtlMilliseconds);
        }

        private static void OnFrameEncodedToPng()
        {
            Interlocked.Increment(ref framesEncodedToPng);
        }

        private static CapturedFrame TryRecoverGeneratedScreenshot(string filePrefix,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string currentDirectory = Environment.CurrentDirectory;
            string[] matches;
            try
            {
                Interlocked.Increment(ref recoveryDirectoryScans);
                matches = Directory.GetFiles(currentDirectory, filePrefix + "*");
            }
            catch (IOException) { return null; }
            catch (UnauthorizedAccessException) { return null; }

            try
            {
                foreach (string path in matches.OrderByDescending(File.GetLastWriteTimeUtc))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    try
                    {
                        using (var screenshot = new Bitmap(path))
                            return new CapturedFrame(new Bitmap(screenshot),
                                DateTimeOffset.UtcNow, OnFrameEncodedToPng);
                    }
                    catch (ArgumentException) { }
                    catch (IOException) { }
                    catch (UnauthorizedAccessException) { }
                }
            }
            finally { DeleteGeneratedScreenshotArtifacts(matches); }
            return null;
        }

        private static void DeleteGeneratedScreenshotArtifacts(IEnumerable<string> artifacts)
        {
            foreach (string path in artifacts ?? Enumerable.Empty<string>())
            {
                try { File.Delete(path); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
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

        public async Task<int> ReadFocusedIntegerAsync(
            string deviceName,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ValidateDeviceName(deviceName);

            for (int attempt = 1; attempt <= FocusedInputReadAttempts; attempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string hierarchy = Auto_LDPlayer.LDPlayer.Adb(
                    LDType.Name,
                    deviceName,
                    "shell uiautomator dump /dev/tty",
                    InputCommandTimeoutMilliseconds,
                    0);
                cancellationToken.ThrowIfCancellationRequested();

                int value;
                if (TryReadFocusedInteger(hierarchy, out value))
                    return value;

                cancellationToken.ThrowIfCancellationRequested();
                Auto_LDPlayer.LDPlayer.Adb(
                    LDType.Name,
                    deviceName,
                    "shell uiautomator dump /sdcard/ikautomation_focused_input.xml",
                    InputCommandTimeoutMilliseconds,
                    0);
                cancellationToken.ThrowIfCancellationRequested();
                hierarchy = Auto_LDPlayer.LDPlayer.Adb(
                    LDType.Name,
                    deviceName,
                    "shell cat /sdcard/ikautomation_focused_input.xml",
                    InputCommandTimeoutMilliseconds,
                    0);
                cancellationToken.ThrowIfCancellationRequested();
                if (TryReadFocusedInteger(hierarchy, out value))
                    return value;

                if (attempt < FocusedInputReadAttempts)
                    await Task.Delay(
                        FocusedInputRetryDelayMilliseconds,
                        cancellationToken);
            }

            throw new InvalidOperationException(
                $"Focused numeric coordinate input could not be read from "
                + $"LDPlayer device '{deviceName}'.");
        }

        private static bool TryReadFocusedInteger(string hierarchy, out int value)
        {
            value = 0;
            if (string.IsNullOrWhiteSpace(hierarchy))
                return false;

            foreach (Match nodeMatch in Regex.Matches(
                hierarchy,
                @"<node\b[^>]*>",
                RegexOptions.IgnoreCase))
            {
                string node = nodeMatch.Value;
                if (!Regex.IsMatch(
                        node,
                        @"\bclass=""android\.widget\.EditText""",
                        RegexOptions.IgnoreCase)
                    || !Regex.IsMatch(
                        node,
                        @"\bfocused=""true""",
                        RegexOptions.IgnoreCase))
                    continue;

                Match textMatch = Regex.Match(
                    node,
                    @"\btext=""([^""]*)""",
                    RegexOptions.IgnoreCase);
                if (!textMatch.Success)
                    continue;

                string text = WebUtility.HtmlDecode(textMatch.Groups[1].Value);
                if (int.TryParse(
                    text,
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out value))
                    return true;
            }

            return false;
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
