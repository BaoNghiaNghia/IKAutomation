using ADB_Tool_Automation_Post_FB.Core.TeamSelection;
using ADB_Tool_Automation_Post_FB.Core.Vision;
using System.Configuration;
using System.Collections.Generic;
using System.Globalization;

namespace ADB_Tool_Automation_Post_FB.Infrastructure.TeamSelection
{
    public static class AppConfigWorldMapTeamAvailabilityOptionsProvider
    {
        public static WorldMapTeamAvailabilityOptions Load() =>
            new WorldMapTeamAvailabilityOptions(new ImageRegion(
                Int("TeamRosterRegion.X", 0), Int("TeamRosterRegion.Y", 290),
                Int("TeamRosterRegion.Width", 150),
            Int("TeamRosterRegion.Height", 280)),
            Int("TeamRowCount", 4), NullableInt("TeamRowHeight"), Int("BadgeTopPadding", 8),
            Int("RowVerticalTolerance", 4), Int("ObservationFrameCount", 3),
            Int("ObservationIntervalMs", 150), KnownUnlockedTeamCounts());

        private static IReadOnlyDictionary<string, int> KnownUnlockedTeamCounts()
        {
            const string prefix = "WorldMapTeamAvailability.KnownUnlockedTeamCount.";
            var values = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (string key in ConfigurationManager.AppSettings.AllKeys)
            {
                if (string.IsNullOrWhiteSpace(key) || !key.StartsWith(prefix,
                    StringComparison.OrdinalIgnoreCase)) continue;
                string deviceName = key.Substring(prefix.Length).Trim();
                if (deviceName.Length == 0) continue;
                if (!int.TryParse(ConfigurationManager.AppSettings[key],
                    NumberStyles.Integer, CultureInfo.InvariantCulture, out int value)
                    || value < 0 || value > 4)
                    throw new ConfigurationErrorsException(
                        $"Configuration value '{key}' must be between 0 and 4.");
                if (value > 0) values[deviceName] = value;
            }
            return values;
        }

        private static string Key(string name) => "WorldMapTeamAvailability." + name;

        private static int Int(string name, int fallback)
        {
            string value = ConfigurationManager.AppSettings[Key(name)];
            if (string.IsNullOrWhiteSpace(value)) return fallback;
            if (!int.TryParse(value, NumberStyles.Integer,
                CultureInfo.InvariantCulture, out int parsed))
                throw new ConfigurationErrorsException(
                    $"Configuration value '{Key(name)}' must be an integer.");
            return parsed;
        }

        private static int? NullableInt(string name)
        {
            string value = ConfigurationManager.AppSettings[Key(name)];
            if (string.IsNullOrWhiteSpace(value)) return null;
            if (!int.TryParse(value, NumberStyles.Integer,
                CultureInfo.InvariantCulture, out int parsed))
                throw new ConfigurationErrorsException(
                    $"Configuration value '{Key(name)}' must be an integer.");
            return parsed;
        }
    }
}
