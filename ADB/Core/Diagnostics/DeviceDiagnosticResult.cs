namespace ADB_Tool_Automation_Post_FB.Core.Diagnostics
{
    public sealed class DeviceDiagnosticResult
    {
        public string DeviceName { get; set; }
        public bool IsRunning { get; set; }
        public bool ScreenshotSucceeded { get; set; }
        public int? ScreenshotWidth { get; set; }
        public int? ScreenshotHeight { get; set; }
        public bool MatchesExpectedResolution { get; set; }
        public string ErrorMessage { get; set; }
        public string ExpectedPackage { get; set; }
        public string CurrentForegroundPackage { get; set; }
        public bool? PackageMatches { get; set; }
    }
}
