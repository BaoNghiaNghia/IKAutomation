using ADB_Tool_Automation_Post_FB.Core.MarchDispatch;
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
            Text("FailureScreenshotDirectory", "Diagnostics/MarchDispatch"));

        public static DispatchMarchRequest LoadRequest() => new DispatchMarchRequest
        {
            AllowStructuralVerificationFallback = Bool("AllowStructuralVerificationFallback", true)
        };

        private static string Key(string name) => "DispatchSelectedTeam." + name;
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
