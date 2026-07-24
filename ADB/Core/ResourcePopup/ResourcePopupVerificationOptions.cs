using ADB_Tool_Automation_Post_FB.Core.Vision;
using System;

namespace ADB_Tool_Automation_Post_FB.Core.ResourcePopup
{
    public sealed class ResourcePopupVerificationOptions
    {
        public ResourcePopupVerificationOptions(int pollIntervalMs,
            int verificationTimeoutSeconds, int requiredConsecutiveReadyFrames,
            ImageRegion popupRegion, bool saveFailureScreenshots,
            string failureScreenshotDirectory, int expectedWidth = 1280, int expectedHeight = 720)
            : this(pollIntervalMs, verificationTimeoutSeconds, requiredConsecutiveReadyFrames,
                popupRegion, popupRegion, saveFailureScreenshots,
                failureScreenshotDirectory, expectedWidth, expectedHeight)
        {
        }

        public ResourcePopupVerificationOptions(int pollIntervalMs,
            int verificationTimeoutSeconds, int requiredConsecutiveReadyFrames,
            ImageRegion headerRegion, ImageRegion actionRegion,
            bool saveFailureScreenshots, string failureScreenshotDirectory,
            int expectedWidth = 1280, int expectedHeight = 720)
        {
            if (pollIntervalMs <= 0) throw new ArgumentOutOfRangeException(nameof(pollIntervalMs));
            if (verificationTimeoutSeconds <= 0) throw new ArgumentOutOfRangeException(nameof(verificationTimeoutSeconds));
            if (requiredConsecutiveReadyFrames < 1 || requiredConsecutiveReadyFrames > 3)
                throw new ArgumentOutOfRangeException(nameof(requiredConsecutiveReadyFrames));
            if (expectedWidth <= 0 || expectedHeight <= 0) throw new ArgumentOutOfRangeException(nameof(expectedWidth));
            ValidateRegion(headerRegion, expectedWidth, expectedHeight, nameof(headerRegion));
            ValidateRegion(actionRegion, expectedWidth, expectedHeight, nameof(actionRegion));
            if (string.IsNullOrWhiteSpace(failureScreenshotDirectory))
                throw new ArgumentException("Failure screenshot directory is required.", nameof(failureScreenshotDirectory));
            PollIntervalMs = pollIntervalMs;
            VerificationTimeoutSeconds = verificationTimeoutSeconds;
            RequiredConsecutiveReadyFrames = requiredConsecutiveReadyFrames;
            HeaderRegion = headerRegion;
            ActionRegion = actionRegion;
            SaveFailureScreenshots = saveFailureScreenshots;
            FailureScreenshotDirectory = failureScreenshotDirectory;
            ExpectedWidth = expectedWidth;
            ExpectedHeight = expectedHeight;
        }

        public int PollIntervalMs { get; }
        public int VerificationTimeoutSeconds { get; }
        public int RequiredConsecutiveReadyFrames { get; }
        public ImageRegion PopupRegion => HeaderRegion;
        public ImageRegion HeaderRegion { get; }
        public ImageRegion ActionRegion { get; }
        public bool SaveFailureScreenshots { get; }
        public string FailureScreenshotDirectory { get; }
        public int ExpectedWidth { get; }
        public int ExpectedHeight { get; }

        private static void ValidateRegion(ImageRegion region, int width, int height, string name)
        {
            if ((long)region.X + region.Width > width
                || (long)region.Y + region.Height > height)
                throw new ArgumentOutOfRangeException(name, "ROI must be inside the expected screenshot.");
        }
    }
}
