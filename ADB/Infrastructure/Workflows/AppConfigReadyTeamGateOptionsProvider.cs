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
            int minutes = Int("CheckIntervalMinutes", 15);
            int maxWaitHours = Int("MaxWaitHours", 12);
            if (minutes < 1 || minutes > 1440)
                throw new ConfigurationErrorsException(
                    "ReadyTeamGate.CheckIntervalMinutes must be between 1 and 1440.");
            if (maxWaitHours < 1 || maxWaitHours > 168)
                throw new ConfigurationErrorsException(
                    "ReadyTeamGate.MaxWaitHours must be between 1 and 168.");
            return new ReadyTeamGateOptions(checked(minutes * 60 * 1000),
                checked(maxWaitHours * 60 * 60 * 1000));
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
