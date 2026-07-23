using ADB_Tool_Automation_Post_FB.Core.TeamSelection;
using ADB_Tool_Automation_Post_FB.Core.Vision;
using System.Configuration;
using System.Globalization;

namespace ADB_Tool_Automation_Post_FB.Infrastructure.TeamSelection
{
    public static class AppConfigWorldMapTeamAvailabilityOptionsProvider
    {
        public static WorldMapTeamAvailabilityOptions Load() =>
            new WorldMapTeamAvailabilityOptions(new ImageRegion(
                Int("TeamRosterRegion.X", 0), Int("TeamRosterRegion.Y", 290),
                Int("TeamRosterRegion.Width", 150),
                Int("TeamRosterRegion.Height", 240)));

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
    }
}
