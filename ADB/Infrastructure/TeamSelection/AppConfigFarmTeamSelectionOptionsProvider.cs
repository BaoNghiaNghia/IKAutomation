using IK_Auto_ADB.Core.TeamSelection;
using IK_Auto_ADB.Core.Vision;
using System.Collections.Generic;
using System.Configuration;
using System.Globalization;
using System.Linq;

namespace IK_Auto_ADB.Infrastructure.TeamSelection
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
                },
                maxRosterScrollAttempts: Int("MaxRosterScrollAttempts", 3),
                rosterScrollDurationMs: Int("RosterScrollDurationMs", 350),
                teamSelectionRosterRegion: new ImageRegion(
                    Int("RosterRegion.X", 0), Int("RosterRegion.Y", 0),
                    Int("RosterRegion.Width", 300), Int("RosterRegion.Height", 590)),
                minimumSafeTapX: Int("MinimumSafeTapX", 80),
                maximumSafeTapX: Int("MaximumSafeTapX", 160),
                maxInputFrameAgeMs: Int("MaxInputFrameAgeMs", 1000), selectedMinimumScore: Double("SelectedMinimumScore", .70), selectedWinningMargin: Double("SelectedWinningMargin", .12), selectedRequiredBorderEdges: Int("SelectedRequiredBorderEdges",2), selectedConsensusFrames: Int("SelectedConsensusFrames",3), selectedRequiredMatchingFrames: Int("SelectedRequiredMatchingFrames",2), selectedFrameIntervalMs: Int("SelectedFrameIntervalMs",250), selectedDetectionTimeoutMs: Int("SelectedDetectionTimeoutMs",3000));
        }

        public static TeamSelectionRequest LoadRequest()
        {
            return new TeamSelectionRequest
            {
                AllowedTeams = Teams("AllowedTeams", new[] { TeamNumber.Team1, TeamNumber.Team2, TeamNumber.Team3, TeamNumber.Team4 }),
                Priority = Teams("Priority", new[] { TeamNumber.Team4, TeamNumber.Team3, TeamNumber.Team2, TeamNumber.Team1 }),
                AllowTeam1 = Bool("AllowTeam1", true)
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
        private static double Double(string name,double fallback){string value=ConfigurationManager.AppSettings[Key(name)]; if(string.IsNullOrWhiteSpace(value))return fallback; if(!double.TryParse(value,NumberStyles.Float,CultureInfo.InvariantCulture,out double parsed))throw new ConfigurationErrorsException($"Configuration value '{Key(name)}' must be a number."); return parsed;}
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
