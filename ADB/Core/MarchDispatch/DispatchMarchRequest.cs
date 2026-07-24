using ADB_Tool_Automation_Post_FB.Core.TeamSelection;
using ADB_Tool_Automation_Post_FB.Core.ResourceSearch;

namespace ADB_Tool_Automation_Post_FB.Core.MarchDispatch
{
    public sealed class DispatchMarchRequest
    {
        public TeamNumber ExpectedTeam { get; set; } = TeamNumber.Team4;
        public bool RequireExpectedTeamSelected { get; set; } = true;
        public bool AllowStructuralVerificationFallback { get; set; } = true;
        public ResourceType CurrentResource { get; set; } = ResourceType.Iron;
    }
}
