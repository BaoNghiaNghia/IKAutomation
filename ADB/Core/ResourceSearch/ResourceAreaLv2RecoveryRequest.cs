using ADB_Tool_Automation_Post_FB.Core.TeamSelection;

namespace ADB_Tool_Automation_Post_FB.Core.ResourceSearch
{
    public sealed class ResourceAreaLv2RecoveryRequest
    {
        public string RunId { get; set; }
        public string DeviceName { get; set; }
        public ResourceType Resource { get; set; }
        public int Level { get; set; }
        public bool UnoccupiedOnly { get; set; }
        public int AreaEpoch { get; set; }
        public TeamNumber? ExpectedTeam { get; set; }
    }
}
