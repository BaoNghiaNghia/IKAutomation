using ADB_Tool_Automation_Post_FB.Core.Diagnostics;
using System;
using System.Configuration;
using System.Globalization;

namespace ADB_Tool_Automation_Post_FB.Infrastructure.Diagnostics
{
    public static class AppConfigDiagnosticOptionsProvider
    {
        public static DeviceDiagnosticOptions Load()
        {
            return new DeviceDiagnosticOptions(
                ConfigurationManager.AppSettings["InfinityKingdom.PackageName"] ?? string.Empty,
                ReadPositiveInteger("InfinityKingdom.ExpectedWidth", 1280),
                ReadPositiveInteger("InfinityKingdom.ExpectedHeight", 720),
                ConfigurationManager.AppSettings["InfinityKingdom.Language"] ?? "vi",
                ConfigurationManager.AppSettings["Diagnostics.ScreenshotDirectory"]
                    ?? "Diagnostics/Screenshots");
        }

        private static int ReadPositiveInteger(string key, int defaultValue)
        {
            string value = ConfigurationManager.AppSettings[key];
            if (string.IsNullOrWhiteSpace(value))
                return defaultValue;

            if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out int result)
                || result <= 0)
            {
                throw new ConfigurationErrorsException(
                    $"Configuration value '{key}' must be a positive integer.");
            }

            return result;
        }
    }
}
