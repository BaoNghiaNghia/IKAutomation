using ADB_Tool_Automation_Post_FB.Core.TeamSelection;
using ADB_Tool_Automation_Post_FB.Core.Vision;
using System.Collections.Generic;
using System.Configuration;
using System.Globalization;
using System.Linq;

namespace ADB_Tool_Automation_Post_FB.Infrastructure.TeamSelection
{
    public static class AppConfigFarmTeamSelectionOptionsProvider
    {
        public static FarmTeamSelectionOptions Load()
        {
            return new FarmTeamSelectionOptions(
                Int("PollIntervalMs", 250), Int("SelectionTimeoutSeconds", 6),
                Int("MaxSelectionAttemptsPerTeam", 2), Int("TapRetryDelayMs", 500),
                Bool("SaveFailureScreenshots", true),
                Text("FailureScreenshotDirectory", "Diagnostics/FarmTeamSelection"),
                new Dictionary<TeamNumber, ImageRegion>
                {
                    { TeamNumber.Team1, Region(1, 0, 0, 235, 150) },
                    { TeamNumber.Team2, Region(2, 0, 145, 235, 145) },
                    { TeamNumber.Team3, Region(3, 0, 290, 235, 145) },
                    { TeamNumber.Team4, Region(4, 0, 435, 235, 155) }
                });
        }

        public static TeamSelectionRequest LoadRequest()
        {
            return new TeamSelectionRequest
            {
                AllowedTeams = Teams("AllowedTeams", new[] { TeamNumber.Team2, TeamNumber.Team3, TeamNumber.Team4 }),
                Priority = Teams("Priority", new[] { TeamNumber.Team4, TeamNumber.Team3, TeamNumber.Team2 }),
                AllowTeam1 = Bool("AllowTeam1", false)
            };
        }

        private static ImageRegion Region(int team, int x, int y, int width, int height) =>
            new ImageRegion(Int($"TeamRegions.{team}.X", x),
                Int($"TeamRegions.{team}.Y", y),
                Int($"TeamRegions.{team}.Width", width),
                Int($"TeamRegions.{team}.Height", height));
        private static string Key(string name) => "FarmTeamSelection." + name;
        private static int Int(string name, int fallback)
        {
            string value = ConfigurationManager.AppSettings[Key(name)];
            if (string.IsNullOrWhiteSpace(value)) return fallback;
            if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed))
                throw new ConfigurationErrorsException($"Configuration value '{Key(name)}' must be an integer.");
            return parsed;
        }
        private static bool Bool(string name, bool fallback)
        {
            string value = ConfigurationManager.AppSettings[Key(name)];
            if (string.IsNullOrWhiteSpace(value)) return fallback;
            if (!bool.TryParse(value, out bool parsed))
                throw new ConfigurationErrorsException($"Configuration value '{Key(name)}' must be true or false.");
            return parsed;
        }
        private static string Text(string name, string fallback) =>
            ConfigurationManager.AppSettings[Key(name)] ?? fallback;
        private static IReadOnlyList<TeamNumber> Teams(string name, TeamNumber[] fallback)
        {
            string value = ConfigurationManager.AppSettings[Key(name)];
            if (string.IsNullOrWhiteSpace(value)) return fallback;
            try
            {
                return value.Split(',').Select(item =>
                    (TeamNumber)int.Parse(item.Trim(), CultureInfo.InvariantCulture)).ToArray();
            }
            catch
            {
                throw new ConfigurationErrorsException(
                    $"Configuration value '{Key(name)}' must be a comma-separated team list.");
            }
        }
    }
}
