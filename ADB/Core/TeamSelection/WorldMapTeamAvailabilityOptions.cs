using ADB_Tool_Automation_Post_FB.Core.Vision;
using System;
using System.Collections.Generic;
using System.Linq;

namespace ADB_Tool_Automation_Post_FB.Core.TeamSelection
{
    public sealed class WorldMapTeamAvailabilityOptions
    {
        public WorldMapTeamAvailabilityOptions(ImageRegion teamRosterRegion,
            int teamRowCount = 4, int? teamRowHeight = null, int badgeTopPadding = 8,
            int rowVerticalTolerance = 4, int observationFrameCount = 3,
            int observationIntervalMs = 150,
            IReadOnlyDictionary<string, int> knownUnlockedTeamCounts = null,
            int expectedWidth = 1280, int expectedHeight = 720)
        {
            if (expectedWidth <= 0 || expectedHeight <= 0)
                throw new ArgumentOutOfRangeException(nameof(expectedWidth));
            if (teamRosterRegion.Width < 50 || teamRosterRegion.Height < 96)
                throw new ArgumentOutOfRangeException(nameof(teamRosterRegion),
                    "Team roster region is too small for four readiness rows.");
            TeamRosterRegion = teamRosterRegion;
            if (teamRowCount < 1 || teamRowCount > 4)
                throw new ArgumentOutOfRangeException(nameof(teamRowCount));
            if (badgeTopPadding < 0 || badgeTopPadding >= teamRosterRegion.Height)
                throw new ArgumentOutOfRangeException(nameof(badgeTopPadding));
            if (rowVerticalTolerance < 0 || rowVerticalTolerance >= teamRosterRegion.Height / teamRowCount)
                throw new ArgumentOutOfRangeException(nameof(rowVerticalTolerance));
            int resolvedHeight = teamRowHeight ?? teamRosterRegion.Height / teamRowCount;
            if (resolvedHeight <= 0 || resolvedHeight * teamRowCount > teamRosterRegion.Height)
                throw new ArgumentOutOfRangeException(nameof(teamRowHeight),
                    "Team row height must fit inside the roster region.");
            if (observationFrameCount < 1 || observationFrameCount > 3)
                throw new ArgumentOutOfRangeException(nameof(observationFrameCount));
            if (observationIntervalMs < 0 || observationIntervalMs > 1000)
                throw new ArgumentOutOfRangeException(nameof(observationIntervalMs));
            TeamRowCount = teamRowCount;
            TeamRowHeight = resolvedHeight;
            BadgeTopPadding = badgeTopPadding;
            RowVerticalTolerance = rowVerticalTolerance;
            ObservationFrameCount = observationFrameCount;
            ObservationIntervalMs = observationIntervalMs;
            ExpectedWidth = expectedWidth;
            ExpectedHeight = expectedHeight;
            KnownUnlockedTeamCounts = (knownUnlockedTeamCounts
                ?? new Dictionary<string, int>())
                .Where(item => !string.IsNullOrWhiteSpace(item.Key)
                    && item.Value >= 1 && item.Value <= 4)
                .ToDictionary(item => item.Key.Trim(), item => item.Value,
                    StringComparer.OrdinalIgnoreCase);
        }

        public ImageRegion TeamRosterRegion { get; }
        public int TeamRowCount { get; }
        public int TeamRowHeight { get; }
        public int BadgeTopPadding { get; }
        public int RowVerticalTolerance { get; }
        public int ObservationFrameCount { get; }
        public int ObservationIntervalMs { get; }
        public int ExpectedWidth { get; }
        public int ExpectedHeight { get; }
        public IReadOnlyDictionary<string, int> KnownUnlockedTeamCounts { get; }

        public int GetKnownUnlockedTeamCount(string deviceName)
        {
            return !string.IsNullOrWhiteSpace(deviceName)
                && KnownUnlockedTeamCounts.TryGetValue(deviceName.Trim(), out int value)
                ? value : 0;
        }
    }
}
