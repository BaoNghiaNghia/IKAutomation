namespace ADB_Tool_Automation_Post_FB.Core.Diagnostics
{
    /// <summary>Snapshot of low-overhead LDPlayer screenshot capture counters.</summary>
    public sealed class ScreenshotCaptureMetrics
    {
        public long FramesCaptured { get; set; }
        public long FramesEncodedToPng { get; set; }
        public long ScreenshotGateWaitMs { get; set; }
        public long ScreenShootDurationMs { get; set; }
        public long AdbHealthChecks { get; set; }
        public long AdbHealthCacheHits { get; set; }
        public long NormalBitmapCaptures { get; set; }
        public long RecoveredFileCaptures { get; set; }
        public long RecoveryDirectoryScans { get; set; }
        public long ScreenshotRetries { get; set; }
        public long ScreenshotFailures { get; set; }
    }
}
