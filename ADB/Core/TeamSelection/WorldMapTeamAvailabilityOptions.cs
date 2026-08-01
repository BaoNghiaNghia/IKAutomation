using ADB_Tool_Automation_Post_FB.Core.Vision;
using System;

namespace ADB_Tool_Automation_Post_FB.Core.TeamSelection
{
    public sealed class WorldMapTeamAvailabilityOptions
    {
        public WorldMapTeamAvailabilityOptions(ImageRegion teamRosterRegion,
            int teamRowCount = 4, int? teamRowHeight = null, int badgeTopPadding = 8,
            int rowVerticalTolerance = 4)
        {
            if (teamRosterRegion.Width < 50 || teamRosterRegion.Height < 96)
                throw new ArgumentOutOfRangeException(nameof(teamRosterRegion),
                    "Team roster region is too small for four readiness rows.");
            TeamRosterRegion = teamRosterRegion;
            if (teamRowCount <= 0)
                throw new ArgumentOutOfRangeException(nameof(teamRowCount));
            if (badgeTopPadding < 0 || badgeTopPadding >= teamRosterRegion.Height)
                throw new ArgumentOutOfRangeException(nameof(badgeTopPadding));
            if (rowVerticalTolerance < 0 || rowVerticalTolerance >= teamRosterRegion.Height / teamRowCount)
                throw new ArgumentOutOfRangeException(nameof(rowVerticalTolerance));
            int resolvedHeight = teamRowHeight ?? teamRosterRegion.Height / teamRowCount;
            if (resolvedHeight <= 0 || resolvedHeight * teamRowCount > teamRosterRegion.Height)
                throw new ArgumentOutOfRangeException(nameof(teamRowHeight),
                    "Team row height must fit inside the roster region.");
            TeamRowCount = teamRowCount;
            TeamRowHeight = resolvedHeight;
            BadgeTopPadding = badgeTopPadding;
            RowVerticalTolerance = rowVerticalTolerance;
        }

        public ImageRegion TeamRosterRegion { get; }
        public int TeamRowCount { get; }
        public int TeamRowHeight { get; }
        public int BadgeTopPadding { get; }
        public int RowVerticalTolerance { get; }
    }
}
