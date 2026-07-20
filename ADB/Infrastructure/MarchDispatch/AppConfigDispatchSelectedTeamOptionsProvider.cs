using ADB_Tool_Automation_Post_FB.Core.MarchDispatch;
using ADB_Tool_Automation_Post_FB.Core.TeamSelection;
using ADB_Tool_Automation_Post_FB.Core.Vision;
using ADB_Tool_Automation_Post_FB.Infrastructure.TeamSelection;
using System.Collections.Generic;
using System.Configuration;
using System.Globalization;

namespace ADB_Tool_Automation_Post_FB.Infrastructure.MarchDispatch
{
    public static class AppConfigDispatchSelectedTeamOptionsProvider
    {
        public static DispatchSelectedTeamOptions Load() => new DispatchSelectedTeamOptions(
            Int("PollIntervalMs", 250), Int("TransitionTimeoutSeconds", 10),
            Int("MaxActionTapAttempts", 2), Int("ActionTapRetryDelayMs", 800),
            Int("MaxTransientUnknownFrames", 5), Int("RequiredConsecutiveSuccessFrames", 2),
            Double("TeamRegionChangeThreshold", 0.025),
            Bool("AllowStructuralVerificationFallback", true),
            Bool("SaveFailureScreenshots", true),
            Text("FailureScreenshotDirectory", "Diagnostics/MarchDispatch"),
            Bool("EnableReadyDisappearanceVerification", true),
            Bool("EnableTimerProgressionVerification", true),
            Int("TimerSampleIntervalMs", 1000),
            Double("MinimumTimerForegroundRatio", 0.01),
            Double("MaximumTimerForegroundRatio", 0.35),
            Double("MinimumTimerDifferenceRatio", 0.003),
            Double("MaximumTimerDifferenceRatio", 0.25),
            new Dictionary<TeamNumber, ImageRegion>
            {
                { TeamNumber.Team1, TimerRegion(1, 70, 338, 70, 24) },
                { TeamNumber.Team2, TimerRegion(2, 70, 393, 70, 24) },
                { TeamNumber.Team3, TimerRegion(3, 70, 448, 70, 24) },
                { TeamNumber.Team4, TimerRegion(4, 70, 503, 70, 24) }
            },
            AppConfigWorldMapTeamAvailabilityOptionsProvider.Load().TeamRosterRegion);

        public static DispatchMarchRequest LoadRequest() => new DispatchMarchRequest
        {
            AllowStructuralVerificationFallback = Bool("AllowStructuralVerificationFallback", true)
        };

        private static string Key(string name) => "DispatchSelectedTeam." + name;
        private static ImageRegion TimerRegion(int team, int x, int y, int width, int height) =>
            new ImageRegion(Int($"TeamTimerRegions.{team}.X", x),
                Int($"TeamTimerRegions.{team}.Y", y),
                Int($"TeamTimerRegions.{team}.Width", width),
                Int($"TeamTimerRegions.{team}.Height", height));
        private static string Text(string name, string fallback) =>
            ConfigurationManager.AppSettings[Key(name)] ?? fallback;
        private static int Int(string name, int fallback)
        {
            string value = ConfigurationManager.AppSettings[Key(name)];
            if (string.IsNullOrWhiteSpace(value)) return fallback;
            if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed))
                throw new ConfigurationErrorsException($"Configuration value '{Key(name)}' must be an integer.");
            return parsed;
        }
        private static double Double(string name, double fallback)
        {
            string value = ConfigurationManager.AppSettings[Key(name)];
            if (string.IsNullOrWhiteSpace(value)) return fallback;
            if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed))
                throw new ConfigurationErrorsException($"Configuration value '{Key(name)}' must be a number.");
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
    }
}
