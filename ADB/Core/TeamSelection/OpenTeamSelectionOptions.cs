using ADB_Tool_Automation_Post_FB.Core.Vision;
using System;

namespace ADB_Tool_Automation_Post_FB.Core.TeamSelection
{
    public sealed class OpenTeamSelectionOptions
    {
        public OpenTeamSelectionOptions(int pollIntervalMs, int transitionTimeoutSeconds,
            int maxGatherTapAttempts, int gatherTapRetryDelayMs, int maxTransientUnknownFrames,
            int requiredTeamSelectionSignals, bool requireReadyForSuccess,
            bool saveFailureScreenshots, string failureScreenshotDirectory,
            ImageRegion teamSelectionRegion, ImageRegion resourcePopupRegion,
            int expectedWidth = 1280, int expectedHeight = 720)
        {
            if (pollIntervalMs <= 0) throw new ArgumentOutOfRangeException(nameof(pollIntervalMs));
            if (transitionTimeoutSeconds <= 0) throw new ArgumentOutOfRangeException(nameof(transitionTimeoutSeconds));
            if (maxGatherTapAttempts < 1 || maxGatherTapAttempts > 3) throw new ArgumentOutOfRangeException(nameof(maxGatherTapAttempts));
            if (gatherTapRetryDelayMs <= 0) throw new ArgumentOutOfRangeException(nameof(gatherTapRetryDelayMs));
            if (maxTransientUnknownFrames < 0) throw new ArgumentOutOfRangeException(nameof(maxTransientUnknownFrames));
            if (requiredTeamSelectionSignals != 2 && requiredTeamSelectionSignals != 3) throw new ArgumentOutOfRangeException(nameof(requiredTeamSelectionSignals));
            if (string.IsNullOrWhiteSpace(failureScreenshotDirectory)) throw new ArgumentException("Failure screenshot directory is required.", nameof(failureScreenshotDirectory));
            ValidateRegion(teamSelectionRegion, expectedWidth, expectedHeight, nameof(teamSelectionRegion));
            ValidateRegion(resourcePopupRegion, expectedWidth, expectedHeight, nameof(resourcePopupRegion));
            PollIntervalMs = pollIntervalMs;
            TransitionTimeoutSeconds = transitionTimeoutSeconds;
            MaxGatherTapAttempts = maxGatherTapAttempts;
            GatherTapRetryDelayMs = gatherTapRetryDelayMs;
            MaxTransientUnknownFrames = maxTransientUnknownFrames;
            RequiredTeamSelectionSignals = requiredTeamSelectionSignals;
            RequireReadyForSuccess = requireReadyForSuccess;
            SaveFailureScreenshots = saveFailureScreenshots;
            FailureScreenshotDirectory = failureScreenshotDirectory;
            TeamSelectionRegion = teamSelectionRegion;
            ResourcePopupRegion = resourcePopupRegion;
        }

        public int PollIntervalMs { get; }
        public int TransitionTimeoutSeconds { get; }
        public int MaxGatherTapAttempts { get; }
        public int GatherTapRetryDelayMs { get; }
        public int MaxTransientUnknownFrames { get; }
        public int RequiredTeamSelectionSignals { get; }
        public bool RequireReadyForSuccess { get; }
        public bool SaveFailureScreenshots { get; }
        public string FailureScreenshotDirectory { get; }
        public ImageRegion TeamSelectionRegion { get; }
        public ImageRegion ResourcePopupRegion { get; }

        private static void ValidateRegion(ImageRegion region, int width, int height, string name)
        {
            if (region.Width <= 0 || region.Height <= 0 || region.X < 0 || region.Y < 0
                || (long)region.X + region.Width > width || (long)region.Y + region.Height > height)
                throw new ArgumentOutOfRangeException(name);
        }
    }
}
