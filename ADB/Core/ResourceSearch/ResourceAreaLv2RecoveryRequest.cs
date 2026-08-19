using IK_Auto_ADB.Core.TeamSelection;

namespace IK_Auto_ADB.Core.ResourceSearch
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
