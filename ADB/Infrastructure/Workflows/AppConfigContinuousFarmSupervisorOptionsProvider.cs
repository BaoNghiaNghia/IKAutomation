using IK_Auto_ADB.Core.Workflows;
using System.Configuration;

namespace IK_Auto_ADB.Infrastructure.Workflows
{
    public static class AppConfigContinuousFarmSupervisorOptionsProvider
    {
        public static ContinuousFarmSupervisorOptions Load() =>
            new ContinuousFarmSupervisorOptions(
                heartbeatIntervalMs: ReadPositiveInt(
                    "Operations.TelegramHeartbeatIntervalMinutes", 360) * 60000,
                failureRetryDelayMs: ReadPositiveInt(
                    "Operations.FailureRetryDelaySeconds", 30) * 1000,
                backgroundRosterPriorityPollMs: ReadPositiveInt(
                    "Operations.BackgroundRosterPriorityPollMs", 500),
                backgroundRosterMaxDeferralMs: ReadNonNegativeInt(
                    "Operations.BackgroundRosterMaxDeferralMs", 10000));

        private static int ReadPositiveInt(string key, int fallback)
        {
            return int.TryParse(ConfigurationManager.AppSettings[key], out int value)
                && value > 0 && value <= int.MaxValue / 60000 ? value : fallback;
        }

        private static int ReadNonNegativeInt(string key, int fallback)
        {
            return int.TryParse(ConfigurationManager.AppSettings[key], out int value)
                && value >= 0 ? value : fallback;
        }
    }
}
