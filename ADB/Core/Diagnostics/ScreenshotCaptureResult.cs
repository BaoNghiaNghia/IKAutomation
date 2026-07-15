using System;

namespace ADB_Tool_Automation_Post_FB.Core.Diagnostics
{
    public sealed class ScreenshotCaptureResult
    {
        public string ScreenshotPath { get; set; }
        public string MetadataPath { get; set; }
        public DateTimeOffset CapturedAt { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
    }
}
