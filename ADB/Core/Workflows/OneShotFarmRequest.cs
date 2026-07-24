using ADB_Tool_Automation_Post_FB.Core.ResourceSearch;
using ADB_Tool_Automation_Post_FB.Core.TeamSelection;
using System.Collections.Generic;
using ADB_Tool_Automation_Post_FB.Core.StorageLimit;

namespace ADB_Tool_Automation_Post_FB.Core.Workflows
{
    public sealed class OneShotFarmRequest
    {
        public OneShotFarmRequest()
        {
            ResourceType = ResourceType.Iron; TargetLevel = 7; UnoccupiedOnly = true;
            ResourceLevelPriority = new[] { 7, 6, 5 };
            ResourcePriority = new[] { ResourceType.Iron, ResourceType.Stone, ResourceType.Wood, ResourceType.Food };
            SelectedResources = ResourcePriority;
            StorageLimitPolicy = StorageLimitPolicy.CancelAndSwitchResource;
            AttemptsPerResourceLevel = 1;
            AllowedTeams = new[] { TeamNumber.Team1, TeamNumber.Team2, TeamNumber.Team3, TeamNumber.Team4 };
            TeamPriority = new[] { TeamNumber.Team4, TeamNumber.Team3, TeamNumber.Team2, TeamNumber.Team1 };
            AllowTeam1 = true;
            RequireMarchVerification = true;
        }
        public ResourceType ResourceType { get; set; }
        public int TargetLevel { get; set; }
        public bool UnoccupiedOnly { get; set; }
        public IReadOnlyList<int> ResourceLevelPriority { get; set; }
        public IReadOnlyList<ResourceType> ResourcePriority { get; set; }
        public IReadOnlyList<ResourceType> SelectedResources { get; set; }
        public bool ShuffleResourcePriority { get; set; }
        public StorageLimitPolicy StorageLimitPolicy { get; set; }
        public int AttemptsPerResourceLevel { get; set; }
        public IReadOnlyList<TeamNumber> AllowedTeams { get; set; }
        public IReadOnlyList<TeamNumber> TeamPriority { get; set; }
        public bool AllowTeam1 { get; set; }
        public bool RequireMarchVerification { get; set; }
        public bool RunUntilNoReadyTeams { get; set; }
        public ReadyTeamGateRunOptions ReadyTeamOptions { get; set; }
        public WorldMapTeamAvailabilityResult InitialTeamAvailability { get; set; }
        public string RunId { get; set; }
    }
}
