using IK_Auto_ADB.Core.TeamSelection;
using IK_Auto_ADB.Core.ResourceSearch;

namespace IK_Auto_ADB.Core.MarchDispatch
{
    public sealed class DispatchMarchRequest
    {
        public TeamNumber ExpectedTeam { get; set; } = TeamNumber.Team4;
        public bool RequireExpectedTeamSelected { get; set; } = true;
        public bool AllowStructuralVerificationFallback { get; set; } = true;
        public ResourceType CurrentResource { get; set; } = ResourceType.Iron;
        public string RunId { get; set; }
    }
}
