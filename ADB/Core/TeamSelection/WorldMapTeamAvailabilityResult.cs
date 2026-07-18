using ADB_Tool_Automation_Post_FB.Core.GameDetection;
using ADB_Tool_Automation_Post_FB.Core.Vision;

namespace ADB_Tool_Automation_Post_FB.Core.TeamSelection
{
    public sealed class WorldMapTeamAvailabilityResult
    {
        public bool Success { get; set; }
        public bool AnyReadyTeam { get; set; }
        public GameState FinalState { get; set; }
        public ImageMatchResult ReadyMatch { get; set; }
        public string Message { get; set; }
        public string ErrorMessage { get; set; }
    }
}
