using ADB_Tool_Automation_Post_FB.Core.TeamSelection;

namespace ADB_Tool_Automation_Post_FB.Core.MarchDispatch
{
    public sealed class DispatchMarchRequest
    {
        public TeamNumber ExpectedTeam { get; set; } = TeamNumber.Team4;
        public bool RequireExpectedTeamSelected { get; set; } = true;
        public bool AllowStructuralVerificationFallback { get; set; } = true;
    }
}
