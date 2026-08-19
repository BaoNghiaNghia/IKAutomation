using ADB_Tool_Automation_Post_FB.Core.Workflows;
using System;
using System.Configuration;
using System.Globalization;

namespace ADB_Tool_Automation_Post_FB.Infrastructure.Workflows
{
    public static class AppConfigReadyTeamGateOptionsProvider
    {
        public static ReadyTeamGateOptions Load()
        {
            int seconds = Int("CheckIntervalSeconds", 0);
            int checkIntervalMs = seconds > 0
                ? checked(seconds * 1000)
                : checked(Int("CheckIntervalMinutes",
                    FarmUiPreferences.DefaultReadyCheckIntervalMinutes) * 60 * 1000);
            int maxWaitHours = Int("MaxWaitHours", 12);
            int noReadyConfirmations = Int("NoReadyConfirmations", 3);
            int postDispatchRecheckDelayMs = Int("PostDispatchRecheckDelayMs", 750);
            if (seconds < 0 || seconds > 86400)
                throw new ConfigurationErrorsException(
                    "ReadyTeamGate.CheckIntervalSeconds must be between 1 and 86400 when specified.");
            if (maxWaitHours < 1 || maxWaitHours > 168)
                throw new ConfigurationErrorsException(
                    "ReadyTeamGate.MaxWaitHours must be between 1 and 168.");
            if (noReadyConfirmations < 1 || noReadyConfirmations > 10)
                throw new ConfigurationErrorsException(
                    "ReadyTeamGate.NoReadyConfirmations must be between 1 and 10.");
            if (postDispatchRecheckDelayMs < 1 || postDispatchRecheckDelayMs > 30000)
                throw new ConfigurationErrorsException(
                    "ReadyTeamGate.PostDispatchRecheckDelayMs must be between 1 and 30000.");
            return new ReadyTeamGateOptions(checkIntervalMs,
                checked(maxWaitHours * 60 * 60 * 1000), noReadyConfirmations,
                postDispatchRecheckDelayMs);
        }

        private static int Int(string name, int fallback)
        {
            string key = "ReadyTeamGate." + name;
            string value = ConfigurationManager.AppSettings[key];
            if (string.IsNullOrWhiteSpace(value)) return fallback;
            if (!int.TryParse(value, NumberStyles.Integer,
                CultureInfo.InvariantCulture, out int parsed))
                throw new ConfigurationErrorsException(
                    $"Configuration value '{key}' must be an integer.");
            return parsed;
        }
    }
}
