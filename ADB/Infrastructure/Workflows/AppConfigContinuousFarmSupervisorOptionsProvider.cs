using ADB_Tool_Automation_Post_FB.Core.Workflows;
using System.Configuration;

namespace ADB_Tool_Automation_Post_FB.Infrastructure.Workflows
{
    public static class AppConfigContinuousFarmSupervisorOptionsProvider
    {
        public static ContinuousFarmSupervisorOptions Load() =>
            new ContinuousFarmSupervisorOptions(
                heartbeatIntervalMs: ReadPositiveInt(
                    "Operations.TelegramHeartbeatIntervalMinutes", 360) * 60000);

        private static int ReadPositiveInt(string key, int fallback)
        {
            return int.TryParse(ConfigurationManager.AppSettings[key], out int value)
                && value > 0 && value <= int.MaxValue / 60000 ? value : fallback;
        }
    }
}
