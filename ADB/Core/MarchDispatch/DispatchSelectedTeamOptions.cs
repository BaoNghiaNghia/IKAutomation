using System;
using System.Collections.Generic;
using System.IO;
using ADB_Tool_Automation_Post_FB.Core.TeamSelection;
using ADB_Tool_Automation_Post_FB.Core.Vision;

namespace ADB_Tool_Automation_Post_FB.Core.MarchDispatch
{
    public sealed class DispatchSelectedTeamOptions
    {
        public DispatchSelectedTeamOptions(int pollIntervalMs, int transitionTimeoutSeconds,
            int maxActionTapAttempts, int actionTapRetryDelayMs,
            int maxTransientUnknownFrames, int requiredConsecutiveSuccessFrames,
            double teamRegionChangeThreshold, bool allowStructuralVerificationFallback,
            bool saveFailureScreenshots, string failureScreenshotDirectory,
            bool enableReadyDisappearanceVerification,
            bool enableTimerProgressionVerification, int timerSampleIntervalMs,
            double minimumTimerForegroundRatio, double maximumTimerForegroundRatio,
            double minimumTimerDifferenceRatio, double maximumTimerDifferenceRatio,
            IReadOnlyDictionary<TeamNumber, ImageRegion> teamTimerRegions,
            ImageRegion teamRosterRegion,
            int expectedWidth = 1280, int expectedHeight = 720)
        {
            if (pollIntervalMs <= 0) throw new ArgumentOutOfRangeException(nameof(pollIntervalMs));
            if (transitionTimeoutSeconds <= 0) throw new ArgumentOutOfRangeException(nameof(transitionTimeoutSeconds));
            if (maxActionTapAttempts < 1 || maxActionTapAttempts > 3) throw new ArgumentOutOfRangeException(nameof(maxActionTapAttempts));
            if (actionTapRetryDelayMs <= 0) throw new ArgumentOutOfRangeException(nameof(actionTapRetryDelayMs));
            if (maxTransientUnknownFrames < 0) throw new ArgumentOutOfRangeException(nameof(maxTransientUnknownFrames));
            if (requiredConsecutiveSuccessFrames < 1 || requiredConsecutiveSuccessFrames > 3) throw new ArgumentOutOfRangeException(nameof(requiredConsecutiveSuccessFrames));
            if (teamRegionChangeThreshold <= 0 || teamRegionChangeThreshold >= 1) throw new ArgumentOutOfRangeException(nameof(teamRegionChangeThreshold));
            if (timerSampleIntervalMs <= 0) throw new ArgumentOutOfRangeException(nameof(timerSampleIntervalMs));
            ValidateRange(minimumTimerForegroundRatio, maximumTimerForegroundRatio,
                nameof(minimumTimerForegroundRatio), nameof(maximumTimerForegroundRatio));
            ValidateRange(minimumTimerDifferenceRatio, maximumTimerDifferenceRatio,
                nameof(minimumTimerDifferenceRatio), nameof(maximumTimerDifferenceRatio));
            ValidateTimerRegions(teamTimerRegions, teamRosterRegion,
                expectedWidth, expectedHeight);
            ValidateRelativePath(failureScreenshotDirectory);
            PollIntervalMs = pollIntervalMs;
            TransitionTimeoutSeconds = transitionTimeoutSeconds;
            MaxActionTapAttempts = maxActionTapAttempts;
            ActionTapRetryDelayMs = actionTapRetryDelayMs;
            MaxTransientUnknownFrames = maxTransientUnknownFrames;
            RequiredConsecutiveSuccessFrames = requiredConsecutiveSuccessFrames;
            TeamRegionChangeThreshold = teamRegionChangeThreshold;
            AllowStructuralVerificationFallback = allowStructuralVerificationFallback;
            EnableReadyDisappearanceVerification = enableReadyDisappearanceVerification;
            EnableTimerProgressionVerification = enableTimerProgressionVerification;
            TimerSampleIntervalMs = timerSampleIntervalMs;
            MinimumTimerForegroundRatio = minimumTimerForegroundRatio;
            MaximumTimerForegroundRatio = maximumTimerForegroundRatio;
            MinimumTimerDifferenceRatio = minimumTimerDifferenceRatio;
            MaximumTimerDifferenceRatio = maximumTimerDifferenceRatio;
            TeamTimerRegions = teamTimerRegions;
            SaveFailureScreenshots = saveFailureScreenshots;
            FailureScreenshotDirectory = failureScreenshotDirectory.Trim();
        }

        public int PollIntervalMs { get; }
        public int TransitionTimeoutSeconds { get; }
        public int MaxActionTapAttempts { get; }
        public int ActionTapRetryDelayMs { get; }
        public int MaxTransientUnknownFrames { get; }
        public int RequiredConsecutiveSuccessFrames { get; }
        public double TeamRegionChangeThreshold { get; }
        public bool AllowStructuralVerificationFallback { get; }
        public bool EnableReadyDisappearanceVerification { get; }
        public bool EnableTimerProgressionVerification { get; }
        public int TimerSampleIntervalMs { get; }
        public double MinimumTimerForegroundRatio { get; }
        public double MaximumTimerForegroundRatio { get; }
        public double MinimumTimerDifferenceRatio { get; }
        public double MaximumTimerDifferenceRatio { get; }
        public IReadOnlyDictionary<TeamNumber, ImageRegion> TeamTimerRegions { get; }
        public bool SaveFailureScreenshots { get; }
        public string FailureScreenshotDirectory { get; }

        private static void ValidateRange(double minimum, double maximum,
            string minimumName, string maximumName)
        {
            if (minimum < 0d || minimum > 1d)
                throw new ArgumentOutOfRangeException(minimumName);
            if (maximum < 0d || maximum > 1d)
                throw new ArgumentOutOfRangeException(maximumName);
            if (minimum >= maximum)
                throw new ArgumentException($"{minimumName} must be less than {maximumName}.");
        }

        private static void ValidateTimerRegions(
            IReadOnlyDictionary<TeamNumber, ImageRegion> regions,
            ImageRegion rosterRegion,
            int expectedWidth, int expectedHeight)
        {
            if (regions == null) throw new ArgumentNullException(nameof(regions));
            if ((long)rosterRegion.X + rosterRegion.Width > expectedWidth
                || (long)rosterRegion.Y + rosterRegion.Height > expectedHeight)
                throw new ArgumentOutOfRangeException(nameof(rosterRegion));
            int rowHeight = rosterRegion.Height / 4;
            foreach (TeamNumber team in new[]
            {
                TeamNumber.Team1, TeamNumber.Team2, TeamNumber.Team3, TeamNumber.Team4
            })
            {
                if (!regions.TryGetValue(team, out ImageRegion region))
                    throw new ArgumentException($"Timer ROI for '{team}' is required.", nameof(regions));
                if ((long)region.X + region.Width > expectedWidth
                    || (long)region.Y + region.Height > expectedHeight)
                    throw new ArgumentOutOfRangeException(nameof(regions),
                        $"Timer ROI for '{team}' must be inside {expectedWidth}x{expectedHeight}.");
                int index = (int)team - 1;
                int rowTop = rosterRegion.Y + (index * rowHeight);
                int rowBottom = index == 3
                    ? rosterRegion.Y + rosterRegion.Height : rowTop + rowHeight;
                if (region.X < rosterRegion.X
                    || region.X + region.Width > rosterRegion.X + rosterRegion.Width
                    || region.Y < rowTop || region.Y + region.Height > rowBottom)
                    throw new ArgumentOutOfRangeException(nameof(regions),
                        $"Timer ROI for '{team}' must stay inside its WorldMap team row.");
            }
        }

        private static void ValidateRelativePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || Path.IsPathRooted(path))
                throw new ArgumentException("Diagnostic directory must be a relative path.", nameof(path));
            string[] parts = path.Replace('\\', '/').Split('/');
            foreach (string part in parts)
                if (part == "..") throw new ArgumentException("Diagnostic directory cannot contain path traversal.", nameof(path));
        }
    }
}
