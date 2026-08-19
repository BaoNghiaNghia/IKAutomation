using System;
using System.Threading;

namespace ADB_Tool_Automation_Post_FB.Core.Diagnostics
{
    public static class ScreenshotCaptureContext
    {
        private static readonly AsyncLocal<string> Stage = new AsyncLocal<string>();
        public static string WorkflowStage => string.IsNullOrWhiteSpace(Stage.Value)
            ? "Unspecified" : Stage.Value;
        public static IDisposable Push(string workflowStage)
        {
            string previous = Stage.Value;
            Stage.Value = string.IsNullOrWhiteSpace(workflowStage)
                ? "Unspecified" : workflowStage.Trim();
            return new Scope(() => Stage.Value = previous);
        }
        private sealed class Scope : IDisposable
        {
            private Action restore;
            public Scope(Action restore) { this.restore = restore; }
            public void Dispose() => Interlocked.Exchange(ref restore, null)?.Invoke();
        }
    }

    /// <summary>Snapshot of low-overhead LDPlayer screenshot capture counters.</summary>
    public sealed class ScreenshotCaptureMetrics
    {
        public long FramesCaptured { get; set; }
        public long FramesEncodedToPng { get; set; }
        public long PngEncodes { get; set; }
        public long ScreenshotGateWaitMs { get; set; }
        public long ScreenShootDurationMs { get; set; }
        public long ScreenshotCaptureDurationMs { get; set; }
        public long ScreenshotTotalDurationMs { get; set; }
        public long AdbHealthChecks { get; set; }
        public long AdbHealthCacheHits { get; set; }
        public long NormalBitmapCaptures { get; set; }
        public long RecoveredFileCaptures { get; set; }
        public long RecoveryDirectoryScans { get; set; }
        public long ScreenshotRetries { get; set; }
        public long ScreenshotFailures { get; set; }
        public long ScreenshotRetryCount { get; set; }
        public long ScreenshotFailureCount { get; set; }
        public int ScreenshotQueueDepth { get; set; }
        public int ActiveScreenshotOperations { get; set; }
        public int ScreenshotGateLimit { get; set; }
        public int PeakActiveScreenshotOperations { get; set; }
        public long MaxScreenshotGateWaitMs { get; set; }
        public string LastDeviceName { get; set; }
        public int LastDeviceIndex { get; set; }
        public string LastWorkflowStage { get; set; }
        public int LastAttemptNumber { get; set; }
    }
}
