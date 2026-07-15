using ADB_Tool_Automation_Post_FB.Core.Abstractions;
using ADB_Tool_Automation_Post_FB.Core.Diagnostics;
using ADB_Tool_Automation_Post_FB.Infrastructure.Diagnostics;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace IKAutomation.DeviceDiagnostics.Tests
{
    internal static class Program
    {
        private static readonly CancellationToken TestToken = new CancellationToken(false);
        private static int passed;
        private static int failed;

        private static int Main()
        {
            Run("Sanitize filename", TestSanitizeFilename);
            Run("Create screenshot path", TestScreenshotPath);
            Run("Serialize metadata JSON", TestMetadataJson);
            Run("Validate screenshot resolution", TestResolutionValidation);
            Run("Reject percent outside zero to one hundred", TestRejectInvalidPercent);
            Run("Reject dangerous state name", TestRejectDangerousStateName);
            Run("Handle null and failed screenshot capture", TestScreenshotCaptureFailures);
            Run("Pass cancellation token to LDPlayer client", TestCancellationTokenPropagation);
            Console.WriteLine($"Device diagnostic tests: {passed} passed, {failed} failed.");
            return failed == 0 ? 0 : 1;
        }

        private static void Run(string name, Action test)
        {
            try { test(); passed++; Console.WriteLine($"PASS: {name}"); }
            catch (Exception exception) { failed++; Console.WriteLine($"FAIL: {name} - {exception.Message}"); }
        }

        private static void TestSanitizeFilename()
        {
            string value = ScreenshotPathPolicy.SanitizeDeviceName(" LD:Player/1 ");
            Equal("LD_Player_1", value, "Device filename segment was not sanitized.");
            Assert(!value.Contains("/"), "Sanitized filename still contains a separator.");
        }

        private static void TestScreenshotPath()
        {
            string root = CreateTemporaryDirectory();
            try
            {
                var capturedAt = new DateTimeOffset(2026, 7, 15, 9, 8, 7, TimeSpan.FromHours(7));
                string path = ScreenshotPathPolicy.BuildScreenshotPath(root, "Device 1", "world map", capturedAt);
                string expected = Path.Combine(root, "Device_1", "2026-07-15", "09-08-07_world_map.png");
                Equal(Path.GetFullPath(expected), path, "Screenshot path did not match the required layout.");
            }
            finally { Directory.Delete(root, true); }
        }

        private static void TestMetadataJson()
        {
            string root = CreateTemporaryDirectory();
            try
            {
                var capturedAt = new DateTimeOffset(2026, 7, 15, 9, 8, 7, TimeSpan.FromHours(7));
                var store = new ScreenshotFileStore(root, () => capturedAt);
                ScreenshotCaptureResult result = store.SaveAsync(
                    "IK-1", "world_map", "manual capture", CreatePng(1280, 720), 1280, 720, TestToken)
                    .GetAwaiter().GetResult();
                Assert(File.Exists(result.ScreenshotPath), "Screenshot file was not created.");
                Assert(File.Exists(result.MetadataPath), "Metadata file was not created.");
                string json = File.ReadAllText(result.MetadataPath, Encoding.UTF8);
                Contains(json, "\"deviceName\":\"IK-1\"", "Metadata deviceName is missing.");
                Contains(json, "\"stateName\":\"world_map\"", "Metadata stateName is missing.");
                Contains(json, "\"width\":1280", "Metadata width is missing.");
                Contains(json, "\"height\":720", "Metadata height is missing.");
            }
            finally { Directory.Delete(root, true); }
        }

        private static void TestResolutionValidation()
        {
            var fake = new FakeLdPlayerClient { Screenshot = CreatePng(1280, 720) };
            DeviceDiagnosticResult result = CreateService(fake).CheckDeviceAsync("IK-1", TestToken).GetAwaiter().GetResult();
            Assert(result.ScreenshotSucceeded, "Screenshot should have succeeded.");
            Equal(1280, result.ScreenshotWidth.Value, "Unexpected screenshot width.");
            Equal(720, result.ScreenshotHeight.Value, "Unexpected screenshot height.");
            Assert(result.MatchesExpectedResolution, "Expected resolution should match.");

            var mismatchFake = new FakeLdPlayerClient { Screenshot = CreatePng(960, 540) };
            DeviceDiagnosticResult mismatch = CreateService(mismatchFake)
                .CheckDeviceAsync("IK-2", TestToken).GetAwaiter().GetResult();
            Assert(!mismatch.MatchesExpectedResolution, "Unexpected resolution should not match.");
        }

        private static void TestRejectInvalidPercent()
        {
            var fake = new FakeLdPlayerClient();
            Throws<ArgumentOutOfRangeException>(() =>
                CreateService(fake).TapByPercentAsync("IK-1", 101, 50, TestToken).GetAwaiter().GetResult());
            Equal(0, fake.TapByPercentCallCount, "Invalid input reached the LDPlayer client.");
        }

        private static void TestRejectDangerousStateName()
        {
            Throws<ArgumentException>(() => ScreenshotPathPolicy.SanitizeStateName("../../test"));
            var fake = new FakeLdPlayerClient();
            Throws<ArgumentException>(() => CreateService(fake)
                .CaptureScreenshotAsync("IK-1", "../../test", string.Empty, TestToken).GetAwaiter().GetResult());
            Equal(0, fake.CaptureCallCount, "Dangerous state name reached screenshot capture.");
        }

        private static void TestScreenshotCaptureFailures()
        {
            var nullFake = new FakeLdPlayerClient { Screenshot = null };
            DeviceDiagnosticResult nullResult = CreateService(nullFake)
                .CheckDeviceAsync("IK-1", TestToken).GetAwaiter().GetResult();
            Assert(!nullResult.ScreenshotSucceeded, "Null screenshot should not succeed.");
            Contains(nullResult.ErrorMessage, "no PNG data", "Null screenshot error was unclear.");

            var failingFake = new FakeLdPlayerClient { CaptureException = new InvalidOperationException("capture unavailable") };
            DeviceDiagnosticResult failedResult = CreateService(failingFake)
                .CheckDeviceAsync("IK-2", TestToken).GetAwaiter().GetResult();
            Assert(!failedResult.ScreenshotSucceeded, "Failed screenshot should not succeed.");
            Contains(failedResult.ErrorMessage, "capture unavailable", "Capture error was not returned.");
        }

        private static void TestCancellationTokenPropagation()
        {
            var fake = new FakeLdPlayerClient { Screenshot = CreatePng(1280, 720) };
            using (var source = new CancellationTokenSource())
            {
                CreateService(fake).CheckDeviceAsync("IK-1", source.Token).GetAwaiter().GetResult();
                Equal(source.Token, fake.IsRunningToken, "IsRunning received a different token.");
                Equal(source.Token, fake.CaptureToken, "Capture received a different token.");
            }
        }

        private static DeviceDiagnosticService CreateService(FakeLdPlayerClient fake)
        {
            string directory = CreateTemporaryDirectory();
            var options = new DeviceDiagnosticOptions("com.example.ik", 1280, 720, "vi", directory);
            return new DeviceDiagnosticService(fake, options, new ScreenshotFileStore(directory), new FakeLogger());
        }

        private static byte[] CreatePng(int width, int height)
        {
            using (var bitmap = new Bitmap(width, height))
            using (var stream = new MemoryStream())
            { bitmap.Save(stream, ImageFormat.Png); return stream.ToArray(); }
        }

        private static string CreateTemporaryDirectory()
        {
            string path = Path.Combine(Path.GetTempPath(), "IKAutomationTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            return path;
        }

        private static void Assert(bool condition, string message)
        { if (!condition) throw new InvalidOperationException(message); }

        private static void Equal<T>(T expected, T actual, string message)
        {
            if (!EqualityComparer<T>.Default.Equals(expected, actual))
                throw new InvalidOperationException($"{message} Expected='{expected}', Actual='{actual}'.");
        }

        private static void Contains(string actual, string expected, string message)
        {
            if (actual == null || actual.IndexOf(expected, StringComparison.OrdinalIgnoreCase) < 0)
                throw new InvalidOperationException($"{message} Actual='{actual}'.");
        }

        private static void Throws<TException>(Action action) where TException : Exception
        {
            try { action(); }
            catch (TException) { return; }
            throw new InvalidOperationException($"Expected exception {typeof(TException).Name}.");
        }

        private sealed class FakeLogger : IDiagnosticLogger
        {
            public void Info(string message) { }
            public void Error(string message, Exception exception) { }
        }

        private sealed class FakeLdPlayerClient : ILdPlayerClient
        {
            public bool IsRunning { get; set; } = true;
            public byte[] Screenshot { get; set; } = CreatePng(10, 10);
            public Exception CaptureException { get; set; }
            public int CaptureCallCount { get; private set; }
            public int TapByPercentCallCount { get; private set; }
            public CancellationToken IsRunningToken { get; private set; }
            public CancellationToken CaptureToken { get; private set; }

            public Task<bool> IsRunningAsync(string deviceName, CancellationToken token)
            { IsRunningToken = token; return Task.FromResult(IsRunning); }
            public Task<byte[]> CaptureScreenshotPngAsync(string deviceName, CancellationToken token)
            {
                CaptureCallCount++; CaptureToken = token;
                if (CaptureException != null) throw CaptureException;
                return Task.FromResult(Screenshot);
            }
            public Task TapByPercentAsync(string deviceName, double x, double y, CancellationToken token)
            { TapByPercentCallCount++; return Task.CompletedTask; }
            public Task RunAppAsync(string d, string p, CancellationToken t) => Task.CompletedTask;
            public Task TapAsync(string d, int x, int y, CancellationToken t) => Task.CompletedTask;
            public Task SwipeByPercentAsync(string d, double sx, double sy, double ex, double ey, int ms, CancellationToken t) => Task.CompletedTask;
            public Task BackAsync(string d, CancellationToken t) => Task.CompletedTask;
            public Task OpenAsync(string d, CancellationToken t) => Task.CompletedTask;
            public Task CloseAsync(string d, CancellationToken t) => Task.CompletedTask;
            public Task LongPressAsync(string d, int x, int y, int ms, CancellationToken t) => Task.CompletedTask;
            public Task InputTextAsync(string d, string value, CancellationToken t) => Task.CompletedTask;
            public Task PressKeyAsync(string d, AndroidKeyCode key, CancellationToken t) => Task.CompletedTask;
        }
    }
}
