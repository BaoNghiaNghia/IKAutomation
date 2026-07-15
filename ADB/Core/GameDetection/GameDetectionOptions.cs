using System;

namespace ADB_Tool_Automation_Post_FB.Core.GameDetection
{
    public sealed class GameDetectionOptions
    {
        public GameDetectionOptions(
            int expectedWidth,
            int expectedHeight,
            bool requireExpectedResolution,
            bool saveUnknownScreenshots,
            string unknownScreenshotDirectory)
        {
            if (expectedWidth <= 0)
                throw new ArgumentOutOfRangeException(nameof(expectedWidth));
            if (expectedHeight <= 0)
                throw new ArgumentOutOfRangeException(nameof(expectedHeight));
            if (string.IsNullOrWhiteSpace(unknownScreenshotDirectory))
                throw new ArgumentException("Unknown screenshot directory is required.", nameof(unknownScreenshotDirectory));

            ExpectedWidth = expectedWidth;
            ExpectedHeight = expectedHeight;
            RequireExpectedResolution = requireExpectedResolution;
            SaveUnknownScreenshots = saveUnknownScreenshots;
            UnknownScreenshotDirectory = unknownScreenshotDirectory.Trim();
        }

        public int ExpectedWidth { get; }
        public int ExpectedHeight { get; }
        public bool RequireExpectedResolution { get; }
        public bool SaveUnknownScreenshots { get; }
        public string UnknownScreenshotDirectory { get; }
    }
}
