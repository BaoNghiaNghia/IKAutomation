using System;

namespace ADB_Tool_Automation_Post_FB.Core.Diagnostics
{
    public sealed class DeviceDiagnosticOptions
    {
        public DeviceDiagnosticOptions(
            string packageName,
            int expectedWidth,
            int expectedHeight,
            string language,
            string screenshotDirectory)
        {
            if (expectedWidth <= 0)
                throw new ArgumentOutOfRangeException(nameof(expectedWidth), "Expected width must be greater than zero.");
            if (expectedHeight <= 0)
                throw new ArgumentOutOfRangeException(nameof(expectedHeight), "Expected height must be greater than zero.");
            if (string.IsNullOrWhiteSpace(language))
                throw new ArgumentException("Language is required.", nameof(language));
            if (string.IsNullOrWhiteSpace(screenshotDirectory))
                throw new ArgumentException("Screenshot directory is required.", nameof(screenshotDirectory));

            PackageName = packageName?.Trim() ?? string.Empty;
            ExpectedWidth = expectedWidth;
            ExpectedHeight = expectedHeight;
            Language = language.Trim();
            ScreenshotDirectory = screenshotDirectory.Trim();
        }

        public string PackageName { get; }
        public int ExpectedWidth { get; }
        public int ExpectedHeight { get; }
        public string Language { get; }
        public string ScreenshotDirectory { get; }
    }
}
