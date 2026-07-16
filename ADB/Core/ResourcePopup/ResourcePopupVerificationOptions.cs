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
        {
            if (pollIntervalMs <= 0) throw new ArgumentOutOfRangeException(nameof(pollIntervalMs));
            if (verificationTimeoutSeconds <= 0) throw new ArgumentOutOfRangeException(nameof(verificationTimeoutSeconds));
            if (requiredConsecutiveReadyFrames < 1 || requiredConsecutiveReadyFrames > 3)
                throw new ArgumentOutOfRangeException(nameof(requiredConsecutiveReadyFrames));
            if (expectedWidth <= 0 || expectedHeight <= 0) throw new ArgumentOutOfRangeException(nameof(expectedWidth));
            if ((long)popupRegion.X + popupRegion.Width > expectedWidth
                || (long)popupRegion.Y + popupRegion.Height > expectedHeight)
                throw new ArgumentOutOfRangeException(nameof(popupRegion));
            if (string.IsNullOrWhiteSpace(failureScreenshotDirectory))
                throw new ArgumentException("Failure screenshot directory is required.", nameof(failureScreenshotDirectory));
            PollIntervalMs = pollIntervalMs;
            VerificationTimeoutSeconds = verificationTimeoutSeconds;
            RequiredConsecutiveReadyFrames = requiredConsecutiveReadyFrames;
            PopupRegion = popupRegion;
            SaveFailureScreenshots = saveFailureScreenshots;
            FailureScreenshotDirectory = failureScreenshotDirectory;
            ExpectedWidth = expectedWidth;
            ExpectedHeight = expectedHeight;
        }

        public int PollIntervalMs { get; }
        public int VerificationTimeoutSeconds { get; }
        public int RequiredConsecutiveReadyFrames { get; }
        public ImageRegion PopupRegion { get; }
        public bool SaveFailureScreenshots { get; }
        public string FailureScreenshotDirectory { get; }
        public int ExpectedWidth { get; }
        public int ExpectedHeight { get; }
    }
}
