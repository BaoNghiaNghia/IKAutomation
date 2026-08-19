using System;
using System.Collections.Generic;

namespace ADB_Tool_Automation_Post_FB.Core.GameDetection
{
    public sealed class GameDetectionResult
    {
        public GameState State { get; set; }
        public IReadOnlyList<GameDetectionEvidence> Evidence { get; set; }
        public DateTimeOffset DetectedAt { get; set; }
        public int ScreenshotWidth { get; set; }
        public int ScreenshotHeight { get; set; }
        public string ScreenshotPath { get; set; }
        public string ErrorMessage { get; set; }
        public bool IsSuccessful { get; set; }
    }
}
