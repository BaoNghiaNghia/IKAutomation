using ADB_Tool_Automation_Post_FB.Core.TeamSelection;
using ADB_Tool_Automation_Post_FB.Core.Vision;
using System;
using System.Collections.Generic;
using System.Linq;

namespace ADB_Tool_Automation_Post_FB.Infrastructure.TeamSelection
{
    public sealed class WorldMapTeamRosterLayout
    {
        public WorldMapTeamRosterLayout(IReadOnlyList<ImageRegion> rows,
            IReadOnlyList<ImageRegion> searchRows)
        {
            Rows = rows;
            SearchRows = searchRows;
        }
        public IReadOnlyList<ImageRegion> Rows { get; }
        public IReadOnlyList<ImageRegion> SearchRows { get; }
    }

    public static class WorldMapTeamRosterLayoutResolver
    {
        public static WorldMapTeamRosterLayout Resolve(int frameWidth, int frameHeight,
            WorldMapTeamAvailabilityOptions options)
        {
            if (options == null || frameWidth <= 0 || frameHeight <= 0) return null;
            double scaleX = frameWidth / (double)options.ExpectedWidth;
            double scaleY = frameHeight / (double)options.ExpectedHeight;
            ImageRegion configured = options.TeamRosterRegion;
            var roster = new ImageRegion(
                (int)Math.Round(configured.X * scaleX),
                (int)Math.Round(configured.Y * scaleY),
                Math.Max(1, (int)Math.Round(configured.Width * scaleX)),
                Math.Max(options.TeamRowCount,
                    (int)Math.Round(configured.Height * scaleY)));
            if (roster.X < 0 || roster.Y < 0 || roster.X >= frameWidth || roster.Y >= frameHeight)
                return null;
            int width = Math.Min(roster.Width, frameWidth - roster.X);
            int rowHeight = Math.Max(1,
                (int)Math.Round(options.TeamRowHeight * scaleY));
            int lastBottom = roster.Y + options.TeamRowCount * rowHeight;
            if (width <= 0 || lastBottom > roster.Y + roster.Height || lastBottom > frameHeight)
                return null;
            var rows = new List<ImageRegion>(options.TeamRowCount);
            var searchRows = new List<ImageRegion>(options.TeamRowCount);
            int verticalTolerance = Math.Max(0,
                (int)Math.Round(options.RowVerticalTolerance * scaleY));
            for (int index = 0; index < options.TeamRowCount; index++)
            {
                var row = new ImageRegion(roster.X, roster.Y + index * rowHeight,
                    width, rowHeight);
                rows.Add(row);
                // Status and badge templates can straddle the upper boundary of a
                // visual row. Extend only upward: the lower boundary remains the
                // canonical row boundary, so a label from the next row cannot be
                // fully matched by the preceding row.
                int searchTop = Math.Max(roster.Y, row.Y - verticalTolerance);
                searchRows.Add(new ImageRegion(row.X, searchTop, row.Width,
                    row.Y + row.Height - searchTop));
            }
            return new WorldMapTeamRosterLayout(rows.AsReadOnly(),
                searchRows.AsReadOnly());
        }
    }

    public sealed class TeamSelectionRosterLayout
    {
        public TeamSelectionRosterLayout(
            IReadOnlyDictionary<TeamNumber, ImageMatchResult> badges,
            IReadOnlyDictionary<TeamNumber, ImageRegion> rows)
        { BadgeMatches = badges; Rows = rows; }

        public IReadOnlyDictionary<TeamNumber, ImageMatchResult> BadgeMatches { get; }
        public IReadOnlyDictionary<TeamNumber, ImageRegion> Rows { get; }
        public IReadOnlyList<TeamNumber> VisibleTeams => Rows.Keys.OrderBy(item => (int)item).ToArray();
    }

    public static class TeamSelectionRosterLayoutResolver
    {
        public static TeamSelectionRosterLayout Resolve(
            IReadOnlyDictionary<TeamNumber, ImageMatchResult> badgeMatches,
            ImageRegion rosterRegion, int frameWidth, int frameHeight)
        {
            // Badge templates identify a team directly.  Ordering the matches by Y
            // and assigning compact row indexes made a missing Team2 shift Team3
            // into Team2.  Preserve the template's TeamNumber instead.
            var valid = (badgeMatches ?? new Dictionary<TeamNumber, ImageMatchResult>())
                .Where(item => HasBounds(item.Value))
                .OrderBy(item => (int)item.Key).ToArray();
            var badges = valid.ToDictionary(item => item.Key, item => item.Value);
            var rows = new Dictionary<TeamNumber, ImageRegion>();
            int left = Math.Max(0, rosterRegion.X);
            int right = Math.Min(frameWidth, rosterRegion.X + rosterRegion.Width);
            int topLimit = Math.Max(0, rosterRegion.Y);
            int bottomLimit = Math.Min(frameHeight, rosterRegion.Y + rosterRegion.Height);
            // The geometry may move with the panel.  Neighbouring bounds are used
            // only to stop ROIs overlapping; the dictionary key remains the badge
            // template's TeamNumber, so this is not compact-index assignment.
            var byVerticalPosition = valid.OrderBy(item => item.Value.CenterY).ToArray();
            for (int index = 0; index < byVerticalPosition.Length; index++)
            {
                KeyValuePair<TeamNumber, ImageMatchResult> item = byVerticalPosition[index];
                ImageMatchResult badge = item.Value;
                int top = index == 0
                    ? Math.Max(topLimit, badge.Y - badge.Height * 2)
                    : Math.Max(topLimit, (byVerticalPosition[index - 1].Value.CenterY
                        + badge.CenterY) / 2);
                int bottom = index == byVerticalPosition.Length - 1
                    ? Math.Min(bottomLimit, badge.Y + badge.Height * 3)
                    : Math.Min(bottomLimit, (badge.CenterY
                        + byVerticalPosition[index + 1].Value.CenterY) / 2);
                if (bottom <= top) continue;
                rows[item.Key] = new ImageRegion(left, top,
                    Math.Max(1, right - left), bottom - top);
            }
            return new TeamSelectionRosterLayout(badges, rows);
        }

        private static bool HasBounds(ImageMatchResult match) => match != null
            && match.Found && match.Width > 0 && match.Height > 0;
    }
}
