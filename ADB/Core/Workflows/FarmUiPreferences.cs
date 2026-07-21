using ADB_Tool_Automation_Post_FB.Core.ResourceSearch;
using ADB_Tool_Automation_Post_FB.Core.TeamSelection;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace ADB_Tool_Automation_Post_FB.Core.Workflows
{
    public sealed class FarmUiPreferences
    {
        public FarmUiPreferences()
        {
            Version = 1;
            Iron = Stone = Wood = Food = true;
            LevelPriority = new[] { 7, 6, 5 };
            TeamPriority = new[] { TeamNumber.Team4, TeamNumber.Team3, TeamNumber.Team2 };
            ReadyCheckIntervalMinutes = 15;
            ReadyMaxWaitHours = 12;
            UnoccupiedOnly = true;
        }

        public int Version { get; set; }
        public bool Iron { get; set; }
        public bool Stone { get; set; }
        public bool Wood { get; set; }
        public bool Food { get; set; }
        public IReadOnlyList<int> LevelPriority { get; set; }
        public IReadOnlyList<TeamNumber> TeamPriority { get; set; }
        public bool AllowTeam1 { get; set; }
        public int ReadyCheckIntervalMinutes { get; set; }
        public int ReadyMaxWaitHours { get; set; }
        public bool UnoccupiedOnly { get; set; }
    }

    public sealed class FarmUiPreferencesValidationResult
    {
        public bool IsValid { get; set; }
        public string Message { get; set; }
    }

    public sealed class FarmUiPreferencesLoadResult
    {
        public bool Success { get; set; }
        public FarmUiPreferences Preferences { get; set; }
        public bool UsedDefaults { get; set; }
        public bool RecoveredInvalidFile { get; set; }
        public string Message { get; set; }
        public string ErrorMessage { get; set; }
    }

    public sealed class FarmUiPreferencesSaveResult
    {
        public bool Success { get; set; }
        public string Message { get; set; }
        public string ErrorMessage { get; set; }
    }

    public interface IFarmUiPreferencesStore
    {
        Task<FarmUiPreferencesLoadResult> LoadAsync(FarmUiPreferences defaults,
            CancellationToken cancellationToken);
        Task<FarmUiPreferencesSaveResult> SaveAsync(FarmUiPreferences preferences,
            CancellationToken cancellationToken);
        Task ResetAsync(CancellationToken cancellationToken);
    }

    public static class FarmUiPreferencesMapper
    {
        public static FarmUiPreferences FromDefaults(OneShotFarmRequest request,
            ReadyTeamGateOptions readyOptions)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            if (readyOptions == null) throw new ArgumentNullException(nameof(readyOptions));
            IReadOnlyList<ResourceType> resources = request.SelectedResources
                ?? request.ResourcePriority ?? new ResourceType[0];
            var candidate = new FarmUiPreferences
            {
                Iron = resources.Contains(ResourceType.Iron),
                Stone = resources.Contains(ResourceType.Stone),
                Wood = resources.Contains(ResourceType.Wood),
                Food = resources.Contains(ResourceType.Food),
                LevelPriority = (request.ResourceLevelPriority ?? new[] { request.TargetLevel }).ToArray(),
                TeamPriority = (request.TeamPriority ?? request.AllowedTeams ?? new TeamNumber[0]).ToArray(),
                AllowTeam1 = request.AllowTeam1,
                ReadyCheckIntervalMinutes = Math.Max(1, readyOptions.CheckIntervalMs / 60000),
                ReadyMaxWaitHours = Math.Max(1, readyOptions.MaxWaitMs / 3600000),
                UnoccupiedOnly = request.UnoccupiedOnly
            };
            return Validate(candidate).IsValid ? candidate : new FarmUiPreferences();
        }

        public static FarmUiPreferencesValidationResult Validate(FarmUiPreferences preferences)
        {
            if (preferences == null) return Invalid("Farm preferences are required.");
            if (preferences.Version != 1) return Invalid("Unsupported farm preference version.");
            int resources = (preferences.Iron ? 1 : 0) + (preferences.Stone ? 1 : 0)
                + (preferences.Wood ? 1 : 0) + (preferences.Food ? 1 : 0);
            if (resources < 2) return Invalid("Vui lòng chọn ít nhất 2 loại tài nguyên.");
            if (preferences.LevelPriority == null || preferences.LevelPriority.Count == 0)
                return Invalid("Level priority cannot be empty.");
            if (preferences.LevelPriority.Any(level => level < 1 || level > 30))
                return Invalid("Level priority only supports levels 1 through 30.");
            if (preferences.LevelPriority.Distinct().Count() != preferences.LevelPriority.Count)
                return Invalid("Level priority cannot contain duplicates.");
            if (preferences.TeamPriority == null || preferences.TeamPriority.Count == 0)
                return Invalid("Team priority cannot be empty.");
            if (preferences.TeamPriority.Any(team => !Enum.IsDefined(typeof(TeamNumber), team)))
                return Invalid("Team priority only supports teams 1 through 4.");
            if (preferences.TeamPriority.Distinct().Count() != preferences.TeamPriority.Count)
                return Invalid("Team priority cannot contain duplicates.");
            if (!preferences.AllowTeam1 && preferences.TeamPriority.Contains(TeamNumber.Team1))
                return Invalid("Team1 is present in priority but Allow Team1 is disabled.");
            if (preferences.ReadyCheckIntervalMinutes < 1 || preferences.ReadyCheckIntervalMinutes > 1440)
                return Invalid("Ready check interval must be between 1 and 1440 minutes.");
            if (preferences.ReadyMaxWaitHours < 1 || preferences.ReadyMaxWaitHours > 168)
                return Invalid("Maximum ready wait must be between 1 and 168 hours.");
            if ((long)preferences.ReadyMaxWaitHours * 60 < preferences.ReadyCheckIntervalMinutes)
                return Invalid("Maximum ready wait must be at least the ready check interval.");
            return new FarmUiPreferencesValidationResult { IsValid = true, Message = "Valid." };
        }

        public static OneShotFarmRequest CreateRequest(FarmUiPreferences preferences,
            OneShotFarmRequest defaults)
        {
            FarmUiPreferencesValidationResult validation = Validate(preferences);
            if (!validation.IsValid) throw new ArgumentException(validation.Message);
            if (defaults == null) throw new ArgumentNullException(nameof(defaults));
            var selection = new OneShotFarmResourceSelection
            {
                Iron = preferences.Iron, Stone = preferences.Stone,
                Wood = preferences.Wood, Food = preferences.Food
            };
            IReadOnlyList<ResourceType> resources = selection.GetSelectedResources();
            return new OneShotFarmRequest
            {
                ResourceType = resources[0], TargetLevel = preferences.LevelPriority[0],
                UnoccupiedOnly = preferences.UnoccupiedOnly,
                ResourceLevelPriority = preferences.LevelPriority.ToArray(),
                SelectedResources = resources, ResourcePriority = resources,
                ShuffleResourcePriority = true,
                StorageLimitPolicy = defaults.StorageLimitPolicy,
                AttemptsPerResourceLevel = defaults.AttemptsPerResourceLevel,
                AllowedTeams = preferences.TeamPriority.ToArray(),
                TeamPriority = preferences.TeamPriority.ToArray(),
                AllowTeam1 = preferences.AllowTeam1,
                RequireMarchVerification = defaults.RequireMarchVerification,
                RunUntilNoReadyTeams = true,
                ReadyTeamOptions = new ReadyTeamGateRunOptions(
                    checked(preferences.ReadyCheckIntervalMinutes * 60000),
                    checked(preferences.ReadyMaxWaitHours * 3600000))
            };
        }

        private static FarmUiPreferencesValidationResult Invalid(string message) =>
            new FarmUiPreferencesValidationResult { IsValid = false, Message = message };
    }
}
