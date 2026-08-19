using IK_Auto_ADB.Core.TeamSelection;

namespace IK_Auto_ADB.Core.ResourceSearch
{
    public sealed class ResourceSearchExecutionRequest
    {
        public string RunId { get; set; }
        public int? EffectiveLevel { get; set; }
        public bool LevelCapped { get; set; }
        public int AreaEpoch { get; set; }
        public TeamNumber? ExpectedTeam { get; set; }
        public ResourceSearchExecutionMode ExecutionMode { get; set; }
        public ResourceSearchExecutionRequest()
        {
            ConfigureBeforeSearch = true;
        }

        public ResourceSearchConfigurationRequest Configuration { get; set; }
        public bool ConfigureBeforeSearch { get; set; }
    }
}
