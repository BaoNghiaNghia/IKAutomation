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
                Read("WorldMapNavigation.MaxOpenSearchAttempts", 2),
                ReadBool("WorldMapNavigation.PreferSameTerritoryCoordinateSearch", true),
                Read("WorldMapNavigation.CoordinateTerritoryAttempts", 8),
                Read("WorldMapNavigation.MaximumCoordinateOffset", 100),
                Read("WorldMapNavigation.MinimumCoordinateOffset", 20),
                Read("WorldMapNavigation.CoordinateCandidateSettleTimeoutMs", 3000),
                Read("WorldMapNavigation.HomeTerritoryClassificationAttempts", 3),
                ReadBool("WorldMapNavigation.AllowLegacyTerritoryFallback", true),
                Read("WorldMapNavigation.HomePinAcquisitionAttempts", 3),
                ReadBool("WorldMapNavigation.RequireVerifiedSameTerritory", true),
                Read("WorldMapNavigation.MinimumWorldCoordinate", 0),
                Read("WorldMapNavigation.MaximumWorldCoordinate", 2047),
                Read("WorldMapNavigation.CoordinateInputVerificationAttempts", 2),
                Read("WorldMapNavigation.CoordinateRollbackTimeoutSeconds", 5));
        }

        private static int Read(string key, int fallback)
        {
            string value = ConfigurationManager.AppSettings[key];
            if (string.IsNullOrWhiteSpace(value)) return fallback;
            if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out int parsed))
                throw new ConfigurationErrorsException($"Configuration value '{key}' must be an integer.");
            return parsed;
        }

        private static bool ReadBool(string key, bool fallback)
        {
            string value = ConfigurationManager.AppSettings[key];
            if (string.IsNullOrWhiteSpace(value)) return fallback;
            if (!bool.TryParse(value, out bool parsed))
                throw new ConfigurationErrorsException($"Configuration value '{key}' must be a boolean.");
            return parsed;
        }
    }
}
