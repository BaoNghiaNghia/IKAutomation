using ADB_Tool_Automation_Post_FB.Core.ResourceSearch;
using System.Configuration;
using System.Globalization;

namespace ADB_Tool_Automation_Post_FB.Infrastructure.ResourceSearch
{
    public static class AppConfigResourceSearchConfigurationOptionsProvider
    {
        public static ResourceSearchConfigurationOptions Load()
        {
            return new ResourceSearchConfigurationOptions(
                Read("ResourceSearchConfiguration.StatePollIntervalMs", 300),
                Read("ResourceSearchConfiguration.ActionVerificationTimeoutSeconds", 5),
                Read("ResourceSearchConfiguration.MaxSelectionAttempts", 2),
                Read("ResourceSearchConfiguration.MinimumLevel", 1),
                Read("ResourceSearchConfiguration.MaximumLevel", 30),
                Read("ResourceSearchConfiguration.ResetMinusTapCount", 30),
                Read("ResourceSearchConfiguration.TapIntervalMs", 150));
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
