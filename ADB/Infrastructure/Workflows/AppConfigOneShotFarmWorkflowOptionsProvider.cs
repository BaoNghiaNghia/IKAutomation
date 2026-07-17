using ADB_Tool_Automation_Post_FB.Core.ResourceSearch;
using ADB_Tool_Automation_Post_FB.Core.TeamSelection;
using ADB_Tool_Automation_Post_FB.Core.Workflows;
using System;
using System.Configuration;
using System.Globalization;
using System.Linq;

namespace ADB_Tool_Automation_Post_FB.Infrastructure.Workflows
{
    public static class AppConfigOneShotFarmWorkflowOptionsProvider
    {
        public static OneShotFarmWorkflowOptions Load() => new OneShotFarmWorkflowOptions(
            Bool("SaveStepFailureScreenshots", true), Bool("SaveSuccessScreenshot", true),
            Text("ScreenshotDirectory", "Diagnostics/OneShotFarm"));
        public static OneShotFarmRequest LoadRequest()
        {
            ResourceFarmFallbackOptions fallback = AppConfigResourceFarmFallbackOptionsProvider.Load();
            return new OneShotFarmRequest
        {
            ResourceType = Enum.TryParse(Text("ResourceType", "Iron"), true, out ResourceType resource) ? resource : ResourceType.Iron,
            TargetLevel = Int("TargetLevel", 7), UnoccupiedOnly = Bool("UnoccupiedOnly", true),
            ResourcePriority = fallback.ResourcePriority,
            ResourceLevelPriority = fallback.LevelPriority,
            AttemptsPerResourceLevel = fallback.AttemptsPerLevel,
            StorageLimitPolicy = fallback.StorageLimitPolicy,
            AllowedTeams = Teams("AllowedTeams", new[] { TeamNumber.Team2, TeamNumber.Team3, TeamNumber.Team4 }),
            TeamPriority = Teams("TeamPriority", new[] { TeamNumber.Team4, TeamNumber.Team3, TeamNumber.Team2 }),
            AllowTeam1 = Bool("AllowTeam1", false), RequireMarchVerification = Bool("RequireMarchVerification", true)
        };
        }
        private static string Key(string n) => "OneShotFarmWorkflow." + n;
        private static string Text(string n, string f) => ConfigurationManager.AppSettings[Key(n)] ?? f;
        private static bool Bool(string n, bool f) { string v = ConfigurationManager.AppSettings[Key(n)]; return string.IsNullOrWhiteSpace(v) ? f : bool.Parse(v); }
        private static int Int(string n, int f) { string v = ConfigurationManager.AppSettings[Key(n)]; return string.IsNullOrWhiteSpace(v) ? f : int.Parse(v, CultureInfo.InvariantCulture); }
        private static TeamNumber[] Teams(string n, TeamNumber[] f) { string v = ConfigurationManager.AppSettings[Key(n)]; return string.IsNullOrWhiteSpace(v) ? f : v.Split(',').Select(x => (TeamNumber)int.Parse(x.Trim(), CultureInfo.InvariantCulture)).ToArray(); }
        private static int[] Levels(string n, int[] f) { string v = ConfigurationManager.AppSettings[Key(n)]; return string.IsNullOrWhiteSpace(v) ? f : v.Split(',').Select(x => int.Parse(x.Trim(), CultureInfo.InvariantCulture)).ToArray(); }
    }
}
