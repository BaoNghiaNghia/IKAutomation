using System.Collections.Generic;
using IK_Auto_ADB.Core.Workflows;

namespace IK_Auto_ADB.Core.TeamSelection
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
        public TeamNumber? ExpectedTeam { get; set; }
        public FarmTeamOperationContext TeamOperation { get; set; }
        public IReadOnlyList<TeamNumber> WorldMapAvailableTeams { get; set; }
        public IReadOnlyList<TeamNumber> WorldMapReadyTeams { get; set; }
        public string WorldMapRosterStatus { get; set; }
        public string WorldMapRosterConfidence { get; set; }
        public bool AllowTeam1 { get; set; }
        public string RunId { get; set; }
    }
}
