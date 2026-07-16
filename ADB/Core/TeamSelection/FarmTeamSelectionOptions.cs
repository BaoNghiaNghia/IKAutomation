using ADB_Tool_Automation_Post_FB.Core.Vision;
using System;
using System.Collections.Generic;

namespace ADB_Tool_Automation_Post_FB.Core.TeamSelection
{
    public sealed class FarmTeamSelectionOptions
    {
        public FarmTeamSelectionOptions(int pollIntervalMs, int selectionTimeoutSeconds,
            int maxSelectionAttemptsPerTeam, int tapRetryDelayMs,
            bool saveFailureScreenshots, string failureScreenshotDirectory,
            IReadOnlyDictionary<TeamNumber, ImageRegion> teamRegions,
            int expectedWidth = 1280, int expectedHeight = 720)
        {
            if (pollIntervalMs <= 0) throw new ArgumentOutOfRangeException(nameof(pollIntervalMs));
            if (selectionTimeoutSeconds <= 0) throw new ArgumentOutOfRangeException(nameof(selectionTimeoutSeconds));
            if (maxSelectionAttemptsPerTeam < 1 || maxSelectionAttemptsPerTeam > 3) throw new ArgumentOutOfRangeException(nameof(maxSelectionAttemptsPerTeam));
            if (tapRetryDelayMs <= 0) throw new ArgumentOutOfRangeException(nameof(tapRetryDelayMs));
            if (string.IsNullOrWhiteSpace(failureScreenshotDirectory)) throw new ArgumentException("Failure screenshot directory is required.", nameof(failureScreenshotDirectory));
            if (teamRegions == null) throw new ArgumentNullException(nameof(teamRegions));
            foreach (TeamNumber team in new[] { TeamNumber.Team1, TeamNumber.Team2, TeamNumber.Team3, TeamNumber.Team4 })
            {
                if (!teamRegions.TryGetValue(team, out ImageRegion region))
                    throw new ArgumentException($"ROI for '{team}' is required.", nameof(teamRegions));
                if (region.X < 0 || region.Y < 0 || region.Width <= 0 || region.Height <= 0
                    || (long)region.X + region.Width > expectedWidth
                    || (long)region.Y + region.Height > expectedHeight)
                    throw new ArgumentOutOfRangeException(nameof(teamRegions), $"ROI for '{team}' must be inside {expectedWidth}x{expectedHeight}.");
            }
            PollIntervalMs = pollIntervalMs;
            SelectionTimeoutSeconds = selectionTimeoutSeconds;
            MaxSelectionAttemptsPerTeam = maxSelectionAttemptsPerTeam;
            TapRetryDelayMs = tapRetryDelayMs;
            SaveFailureScreenshots = saveFailureScreenshots;
            FailureScreenshotDirectory = failureScreenshotDirectory.Trim();
            TeamRegions = teamRegions;
        }

        public int PollIntervalMs { get; }
        public int SelectionTimeoutSeconds { get; }
        public int MaxSelectionAttemptsPerTeam { get; }
        public int TapRetryDelayMs { get; }
        public bool SaveFailureScreenshots { get; }
        public string FailureScreenshotDirectory { get; }
        public IReadOnlyDictionary<TeamNumber, ImageRegion> TeamRegions { get; }
    }
}
