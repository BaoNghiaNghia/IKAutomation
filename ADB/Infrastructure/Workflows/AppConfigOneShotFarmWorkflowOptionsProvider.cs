using IK_Auto_ADB.Core.ResourceSearch;
using IK_Auto_ADB.Core.TeamSelection;
using IK_Auto_ADB.Core.Workflows;
using System;
using System.Configuration;
using System.Globalization;
using System.Linq;

namespace IK_Auto_ADB.Infrastructure.Workflows
{
    public static class AppConfigOneShotFarmWorkflowOptionsProvider
    {
        public static OneShotFarmWorkflowOptions Load() => new OneShotFarmWorkflowOptions(
            Bool("SaveStepFailureScreenshots", true), Bool("SaveSuccessScreenshot", false),
            Text("ScreenshotDirectory", "Diagnostics/OneShotFarm"));
        public static OneShotFarmDiagnosticOptions LoadDiagnosticOptions() =>
            new OneShotFarmDiagnosticOptions(
                Bool("SaveSuccessScreenshot", false),
                Bool("EnableFailureScreenshots", true),
                Int("DiagnosticScreenshotCooldownSeconds", 30),
                Int("MaxDiagnosticScreenshotsPerDevice", 100),
                Int("DiagnosticRetentionDays", 7),
                Int("DiagnosticQueueCapacity", 16));
        public static OneShotFarmRequest LoadRequest()
        {
            ResourceFarmFallbackOptions fallback = AppConfigResourceFarmFallbackOptionsProvider.Load();
            return new OneShotFarmRequest
        {
            ResourceType = Enum.TryParse(Text("ResourceType", "Iron"), true, out ResourceType resource) ? resource : ResourceType.Iron,
            TargetLevel = Int("TargetLevel", 7), UnoccupiedOnly = Bool("UnoccupiedOnly", true),
            ResourcePriority = fallback.ResourcePriority,
            SelectedResources = fallback.ResourcePriority,
            ResourceLevelPriority = fallback.LevelPriority,
            AttemptsPerResourceLevel = fallback.AttemptsPerLevel,
            StorageLimitPolicy = fallback.StorageLimitPolicy,
            AllowedTeams = Teams("AllowedTeams", new[] { TeamNumber.Team1, TeamNumber.Team2, TeamNumber.Team3, TeamNumber.Team4 }),
            TeamPriority = Teams("TeamPriority", new[] { TeamNumber.Team4, TeamNumber.Team3, TeamNumber.Team2, TeamNumber.Team1 }),
            AllowTeam1 = Bool("AllowTeam1", true), RequireMarchVerification = Bool("RequireMarchVerification", true)
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
