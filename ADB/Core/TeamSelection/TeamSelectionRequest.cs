using System.Collections.Generic;

namespace ADB_Tool_Automation_Post_FB.Core.TeamSelection
{
    public sealed class TeamSelectionRequest
    {
        public TeamSelectionRequest()
        {
            AllowedTeams = new[] { TeamNumber.Team1, TeamNumber.Team2, TeamNumber.Team3, TeamNumber.Team4 };
            Priority = new[] { TeamNumber.Team4, TeamNumber.Team3, TeamNumber.Team2, TeamNumber.Team1 };
            AllowTeam1 = true;
        }

        public IReadOnlyList<TeamNumber> AllowedTeams { get; set; }
        public IReadOnlyList<TeamNumber> Priority { get; set; }
        public bool AllowTeam1 { get; set; }
    }
}
