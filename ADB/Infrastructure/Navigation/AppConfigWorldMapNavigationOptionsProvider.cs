using ADB_Tool_Automation_Post_FB.Core.Navigation;
using System.Configuration;
using System.Globalization;

namespace ADB_Tool_Automation_Post_FB.Infrastructure.Navigation
{
    public static class AppConfigWorldMapNavigationOptionsProvider
    {
        public static WorldMapNavigationOptions Load()
        {
            return new WorldMapNavigationOptions(
                Read("WorldMapNavigation.StatePollIntervalMs", 400),
                Read("WorldMapNavigation.StateTransitionTimeoutSeconds", 8),
                Read("WorldMapNavigation.MaxOpenSearchAttempts", 2));
        }

        private static int Read(string key, int fallback)
        {
            string value = ConfigurationManager.AppSettings[key];
            if (string.IsNullOrWhiteSpace(value)) return fallback;
            if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out int parsed))
                throw new ConfigurationErrorsException($"Configuration value '{key}' must be an integer.");
            return parsed;
        }
    }
}
