using ADB_Tool_Automation_Post_FB.Core.ResourceSearch;
using ADB_Tool_Automation_Post_FB.Core.TeamSelection;
using System.Collections.Generic;
using ADB_Tool_Automation_Post_FB.Core.StorageLimit;

namespace ADB_Tool_Automation_Post_FB.Core.Workflows
{
    public enum ReadyTeamWaitMode
    {
        InlineWait,
        YieldToSupervisor
    }

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
            ReadyTeamWaitMode = ReadyTeamWaitMode.InlineWait;
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
        public ReadyTeamWaitMode ReadyTeamWaitMode { get; set; }
        // Compatibility for callers persisted before the explicit wait mode.
        public bool YieldWhenNoReadyTeam
        {
            get { return ReadyTeamWaitMode == ReadyTeamWaitMode.YieldToSupervisor; }
            set { ReadyTeamWaitMode = value
                ? ReadyTeamWaitMode.YieldToSupervisor : ReadyTeamWaitMode.InlineWait; }
        }
        public ReadyTeamGateRunOptions ReadyTeamOptions { get; set; }
        public WorldMapTeamAvailabilityResult InitialTeamAvailability { get; set; }
        // Per-cycle evidence is deliberately kept separate from the user's policy.
        public TeamNumber? ExpectedTeam { get; set; }
        public IReadOnlyList<TeamNumber> WorldMapAvailableTeams { get; set; }
        public IReadOnlyList<TeamNumber> WorldMapReadyTeams { get; set; }
        public string WorldMapRosterStatus { get; set; }
        public string WorldMapRosterConfidence { get; set; }
        public string RunId { get; set; }
        // Set by the multi-device runner so one admission lease dispatches at most one team.
        public bool CooperativeDispatch { get; set; }
        public IReadOnlyList<TeamNumber> CycleDispatchedTeams { get; set; }
        public IReadOnlyList<ResourceType> CycleDispatchedResources { get; set; }
    }
}
