using IK_Auto_ADB.Core.Vision;
using System;
using System.Collections.Generic;
using System.Linq;

namespace IK_Auto_ADB.Core.TeamSelection
{
    public sealed class FarmTeamSelectionOptions
    {
        public FarmTeamSelectionOptions(int pollIntervalMs, int selectionTimeoutSeconds,
            int maxSelectionAttemptsPerTeam, int tapRetryDelayMs,
            bool saveFailureScreenshots, string failureScreenshotDirectory,
            IReadOnlyDictionary<TeamNumber, ImageRegion> teamRegions,
            int expectedWidth = 1280, int expectedHeight = 720,
            int maxRosterScrollAttempts = 3, int rosterScrollDurationMs = 350,
            ImageRegion? teamSelectionRosterRegion = null,
            int minimumSafeTapX = 80, int maximumSafeTapX = 160,
            int maxInputFrameAgeMs = 1000, double selectedMinimumScore = .70,
            double selectedWinningMargin = .12, int selectedRequiredBorderEdges = 2,
            int selectedConsensusFrames = 3, int selectedRequiredMatchingFrames = 2,
            int selectedFrameIntervalMs = 250, int selectedDetectionTimeoutMs = 3000)
        {
            if (pollIntervalMs <= 0) throw new ArgumentOutOfRangeException(nameof(pollIntervalMs));
            if (selectionTimeoutSeconds <= 0) throw new ArgumentOutOfRangeException(nameof(selectionTimeoutSeconds));
            if (maxSelectionAttemptsPerTeam < 1 || maxSelectionAttemptsPerTeam > 3) throw new ArgumentOutOfRangeException(nameof(maxSelectionAttemptsPerTeam));
            if (tapRetryDelayMs <= 0) throw new ArgumentOutOfRangeException(nameof(tapRetryDelayMs));
            if (string.IsNullOrWhiteSpace(failureScreenshotDirectory)) throw new ArgumentException("Failure screenshot directory is required.", nameof(failureScreenshotDirectory));
            if (teamRegions == null) throw new ArgumentNullException(nameof(teamRegions));
            if (maxRosterScrollAttempts < 0 || maxRosterScrollAttempts > 5)
                throw new ArgumentOutOfRangeException(nameof(maxRosterScrollAttempts));
            if (rosterScrollDurationMs <= 0)
                throw new ArgumentOutOfRangeException(nameof(rosterScrollDurationMs));
            if (minimumSafeTapX < 80 || maximumSafeTapX < minimumSafeTapX)
                throw new ArgumentOutOfRangeException(nameof(minimumSafeTapX));
            if (maxInputFrameAgeMs <= 0 || maxInputFrameAgeMs > 5000)
                throw new ArgumentOutOfRangeException(nameof(maxInputFrameAgeMs));
            if (selectedMinimumScore < 0 || selectedMinimumScore > 1 || selectedWinningMargin < 0 || selectedWinningMargin > 1 || selectedRequiredBorderEdges < 1 || selectedRequiredBorderEdges > 4 || selectedConsensusFrames < 1 || selectedRequiredMatchingFrames < 1 || selectedRequiredMatchingFrames > selectedConsensusFrames || selectedFrameIntervalMs < 0 || selectedDetectionTimeoutMs <= 0 || (long)(selectedConsensusFrames - 1) * selectedFrameIntervalMs >= selectedDetectionTimeoutMs)
                throw new ArgumentOutOfRangeException("selected-team detector configuration");
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
            ExpectedWidth = expectedWidth;
            ExpectedHeight = expectedHeight;
            MaxRosterScrollAttempts = maxRosterScrollAttempts;
            RosterScrollDurationMs = rosterScrollDurationMs;
            MinimumSafeTapX = minimumSafeTapX;
            MaximumSafeTapX = maximumSafeTapX;
            MaxInputFrameAgeMs = maxInputFrameAgeMs;
            TeamSelectionRosterRegion = teamSelectionRosterRegion
                ?? Union(teamRegions, expectedWidth, expectedHeight);
            SelectedMinimumScore=selectedMinimumScore; SelectedWinningMargin=selectedWinningMargin; SelectedRequiredBorderEdges=selectedRequiredBorderEdges; SelectedConsensusFrames=selectedConsensusFrames; SelectedRequiredMatchingFrames=selectedRequiredMatchingFrames; SelectedFrameIntervalMs=selectedFrameIntervalMs; SelectedDetectionTimeoutMs=selectedDetectionTimeoutMs;
        }

        public int PollIntervalMs { get; }
        public int SelectionTimeoutSeconds { get; }
        public int MaxSelectionAttemptsPerTeam { get; }
        public int TapRetryDelayMs { get; }
        public bool SaveFailureScreenshots { get; }
        public string FailureScreenshotDirectory { get; }
        public IReadOnlyDictionary<TeamNumber, ImageRegion> TeamRegions { get; }
        public int ExpectedWidth { get; }
        public int ExpectedHeight { get; }
        public int MaxRosterScrollAttempts { get; }
        public int RosterScrollDurationMs { get; }
        public int MinimumSafeTapX { get; }
        public int MaximumSafeTapX { get; }
        public int MaxInputFrameAgeMs { get; }
        public ImageRegion TeamSelectionRosterRegion { get; }
        public double SelectedMinimumScore { get; } public double SelectedWinningMargin { get; }
        public int SelectedRequiredBorderEdges { get; } public int SelectedConsensusFrames { get; }
        public int SelectedRequiredMatchingFrames { get; } public int SelectedFrameIntervalMs { get; }
        public int SelectedDetectionTimeoutMs { get; }

        private static ImageRegion Union(IReadOnlyDictionary<TeamNumber, ImageRegion> regions,
            int width, int height)
        {
            int left = regions.Values.Min(item => item.X);
            int top = regions.Values.Min(item => item.Y);
            int right = Math.Min(width, regions.Values.Max(item => item.X + item.Width));
            int bottom = Math.Min(height, regions.Values.Max(item => item.Y + item.Height));
            return new ImageRegion(left, top, right - left, bottom - top);
        }
    }
}
