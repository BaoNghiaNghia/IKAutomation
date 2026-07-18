using ADB_Tool_Automation_Post_FB.Core.Vision;
using System;

namespace ADB_Tool_Automation_Post_FB.Core.TeamSelection
{
    public sealed class WorldMapTeamAvailabilityOptions
    {
        public WorldMapTeamAvailabilityOptions(ImageRegion teamRosterRegion)
        {
            if (teamRosterRegion.Width < 50 || teamRosterRegion.Height < 96)
                throw new ArgumentOutOfRangeException(nameof(teamRosterRegion),
                    "Team roster region is too small for four readiness rows.");
            TeamRosterRegion = teamRosterRegion;
        }

        public ImageRegion TeamRosterRegion { get; }
    }
}
