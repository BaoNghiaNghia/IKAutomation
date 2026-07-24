using ADB_Tool_Automation_Post_FB.Core.GameDetection;
using ADB_Tool_Automation_Post_FB.Core.Vision;
using System.Collections.Generic;

namespace ADB_Tool_Automation_Post_FB.Core.TeamSelection
{
    public sealed class WorldMapTeamAvailabilityResult
    {
        public bool Success { get; set; }
        public bool AnyReadyTeam { get; set; }
        public IReadOnlyList<TeamNumber> AvailableTeams { get; set; }
        public IReadOnlyList<TeamNumber> ReadyTeams { get; set; }
        public GameState FinalState { get; set; }
        public ImageMatchResult ReadyMatch { get; set; }
        public IReadOnlyList<ImageMatchResult> ReadyMatches { get; set; }
        public string Message { get; set; }
        public string ErrorMessage { get; set; }
    }
}
