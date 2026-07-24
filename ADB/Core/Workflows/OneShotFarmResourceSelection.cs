using ADB_Tool_Automation_Post_FB.Core.ResourceSearch;
using System;
using System.Collections.Generic;

namespace ADB_Tool_Automation_Post_FB.Core.Workflows
{
    public sealed class OneShotFarmResourceSelection
    {
        public bool Iron { get; set; } = true;
        public bool Stone { get; set; } = true;
        public bool Wood { get; set; } = true;
        public bool Food { get; set; } = true;

        public IReadOnlyList<ResourceType> GetSelectedResources()
        {
            var selected = new List<ResourceType>();
            if (Iron) selected.Add(ResourceType.Iron);
            if (Stone) selected.Add(ResourceType.Stone);
            if (Wood) selected.Add(ResourceType.Wood);
            if (Food) selected.Add(ResourceType.Food);
            return selected;
        }

        public OneShotFarmRequest CreateRequest(OneShotFarmRequest defaults)
        {
            if (defaults == null) throw new ArgumentNullException(nameof(defaults));

            IReadOnlyList<ResourceType> selected = GetSelectedResources();
            if (selected.Count < 2)
                throw new ArgumentException("Vui lòng chọn ít nhất 2 loại tài nguyên.");

            return new OneShotFarmRequest
            {
                ResourceType = selected[0],
                TargetLevel = defaults.TargetLevel,
                UnoccupiedOnly = defaults.UnoccupiedOnly,
                ResourceLevelPriority = defaults.ResourceLevelPriority,
                SelectedResources = selected,
                ResourcePriority = selected,
                ShuffleResourcePriority = true,
                StorageLimitPolicy = defaults.StorageLimitPolicy,
                AttemptsPerResourceLevel = defaults.AttemptsPerResourceLevel,
                AllowedTeams = defaults.AllowedTeams,
                TeamPriority = defaults.TeamPriority,
                AllowTeam1 = defaults.AllowTeam1,
                ReadyTeamOptions = defaults.ReadyTeamOptions,
                RequireMarchVerification = defaults.RequireMarchVerification,
                RunUntilNoReadyTeams = defaults.RunUntilNoReadyTeams
            };
        }
    }
}
