using System.Configuration;
using System.Globalization;

namespace ADB_Tool_Automation_Post_FB.Infrastructure.Workflows
{
    public static class AppConfigAdaptiveConcurrencyOptionsProvider
    {
        public static AdaptiveConcurrencyOptions Load() => new AdaptiveConcurrencyOptions(
            ReadInt("Operations.AdaptiveMinimumConcurrency", 4),
            ReadInt("Operations.AdaptiveInitialConcurrency", 6),
            ReadInt("Operations.AdaptiveMaximumConcurrency", 20),
            ReadInt("Operations.AdaptiveSampleIntervalMs", 5000),
            ReadInt("Operations.AdaptiveHealthySamplesToIncrease", 3),
            ReadDouble("Operations.AdaptiveHighCpuPercent", 88d),
            ReadLong("Operations.AdaptiveLowAvailableMemoryBytes", 2147483648L),
            ReadDouble("Operations.AdaptiveHighTechnicalFailureRate", 0.25d),
            ReadInt("Operations.AdaptiveObservationWindowSize", 20),
            ReadInt("Operations.AdaptiveHighPreflightLatencyMs", 30000),
            ReadInt("Operations.AutomationStaggerMinMs", 2000),
            ReadInt("Operations.AutomationStaggerMaxMs", 10000),
            ReadInt("Operations.RecoveryStaggerMinMs", 30000),
            ReadInt("Operations.RecoveryStaggerMaxMs", 60000));

        private static int ReadInt(string key, int fallback) =>
            int.TryParse(ConfigurationManager.AppSettings[key], out int value) ? value : fallback;

        private static long ReadLong(string key, long fallback) =>
            long.TryParse(ConfigurationManager.AppSettings[key], out long value) ? value : fallback;

        private static double ReadDouble(string key, double fallback) =>
            double.TryParse(ConfigurationManager.AppSettings[key], NumberStyles.Float,
                CultureInfo.InvariantCulture, out double value) ? value : fallback;
    }
}
