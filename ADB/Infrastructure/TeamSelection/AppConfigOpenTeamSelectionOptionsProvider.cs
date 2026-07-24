using ADB_Tool_Automation_Post_FB.Core.TeamSelection;
using ADB_Tool_Automation_Post_FB.Core.Vision;
using System.Configuration;
using System.Globalization;

namespace ADB_Tool_Automation_Post_FB.Infrastructure.TeamSelection
{
    public static class AppConfigOpenTeamSelectionOptionsProvider
    {
        public static OpenTeamSelectionOptions Load(ImageRegion resourcePopupRegion)
        {
            return new OpenTeamSelectionOptions(
                Int("PollIntervalMs", 250), Int("TransitionTimeoutSeconds", 8),
                Int("MaxGatherTapAttempts", 2), Int("GatherTapRetryDelayMs", 750),
                Int("MaxTransientUnknownFrames", 5), Int("RequiredTeamSelectionSignals", 2),
                Bool("RequireReadyForSuccess", true), Bool("SaveFailureScreenshots", true),
                Text("FailureScreenshotDirectory", "Diagnostics/TeamSelection"),
                new ImageRegion(Int("TeamSelectionRegion.X", 0), Int("TeamSelectionRegion.Y", 0),
                    Int("TeamSelectionRegion.Width", 780), Int("TeamSelectionRegion.Height", 720)),
                resourcePopupRegion);
        }

        private static string Key(string name) => "OpenTeamSelection." + name;
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
        private static string Text(string name, string fallback) => ConfigurationManager.AppSettings[Key(name)] ?? fallback;
    }
}
