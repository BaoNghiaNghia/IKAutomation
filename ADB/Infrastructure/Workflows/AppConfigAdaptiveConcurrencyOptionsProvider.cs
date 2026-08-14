using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Configuration;
using System.Globalization;

namespace ADB_Tool_Automation_Post_FB.Infrastructure.Workflows
{
    public sealed class AdaptiveConcurrencyConfigurationResult
    {
        public AdaptiveConcurrencyOptions Options { get; set; }
        public int ScreenshotConcurrency { get; set; }
        public int VisionConcurrency { get; set; }
        public string Source { get; set; }
        public IReadOnlyList<string> Warnings { get; set; }

        public string BuildSummary()
        {
            AdaptiveConcurrencyOptions value = Options ?? new AdaptiveConcurrencyOptions();
            return $"[Adaptive Concurrency Config] Source='{Source}', "
                + $"Min={value.MinimumConcurrency}, Initial={value.InitialConcurrency}, "
                + $"Max={value.MaximumConcurrency}, ScreenshotGate={ScreenshotConcurrency}, "
                + $"VisionGate={VisionConcurrency}, AutomationStaggerMinMs={value.AutomationStaggerMinMs}, "
                + $"AutomationStaggerMaxMs={value.AutomationStaggerMaxMs}, "
                + $"RecoveryStaggerMinMs={value.RecoveryStaggerMinMs}, "
                + $"RecoveryStaggerMaxMs={value.RecoveryStaggerMaxMs}, "
                + $"ScreenshotWaitP95Ms={value.HighScreenshotGateWaitMs}, "
                + $"VisionWaitP95Ms={value.HighVisionGateWaitMs}, "
                + $"QueuePressureWindows={value.QueuePressureWindows}";
        }
    }

    public static class AppConfigAdaptiveConcurrencyOptionsProvider
    {
        public static AdaptiveConcurrencyOptions Load() => LoadConfiguration().Options;

        public static AdaptiveConcurrencyConfigurationResult LoadConfiguration(
            NameValueCollection settings = null)
        {
            NameValueCollection values = settings ?? ConfigurationManager.AppSettings;
            var warnings = new List<string>();
            AdaptiveConcurrencyOptions resolved;
            string source = "App.config";
            try
            {
                resolved = new AdaptiveConcurrencyOptions(
                    ReadInt(values, warnings, "Operations.AdaptiveMinimumConcurrency",
                        AdaptiveConcurrencyOptions.DefaultMinimumConcurrency),
                    ReadInt(values, warnings, "Operations.AdaptiveInitialConcurrency",
                        AdaptiveConcurrencyOptions.DefaultInitialConcurrency),
                    ReadInt(values, warnings, "Operations.AdaptiveMaximumConcurrency",
                        AdaptiveConcurrencyOptions.DefaultMaximumConcurrency),
                    ReadInt(values, warnings, "Operations.AdaptiveSampleIntervalMs", 5000),
                    ReadInt(values, warnings, "Operations.AdaptiveHealthySamplesToIncrease", 3),
                    ReadDouble(values, warnings, "Operations.AdaptiveHighCpuPercent", 88d),
                    ReadLong(values, warnings, "Operations.AdaptiveLowAvailableMemoryBytes", 2147483648L),
                    ReadDouble(values, warnings, "Operations.AdaptiveHighTechnicalFailureRate", 0.25d),
                    ReadInt(values, warnings, "Operations.AdaptiveObservationWindowSize", 20),
                    ReadInt(values, warnings, "Operations.AdaptiveHighPreflightLatencyMs", 30000),
                    ReadInt(values, warnings, "Operations.AutomationStaggerMinMs", 400),
                    ReadInt(values, warnings, "Operations.AutomationStaggerMaxMs", 1200),
                    ReadInt(values, warnings, "Operations.RecoveryStaggerMinMs", 30000),
                    ReadInt(values, warnings, "Operations.RecoveryStaggerMaxMs", 60000),
                    ReadInt(values, warnings, "Operations.AdaptiveHighScreenshotGateWaitMs", 1500),
                    ReadInt(values, warnings, "Operations.AdaptiveHighVisionGateWaitMs", 1000),
                    ReadDouble(values, warnings, "Operations.AdaptiveHighIoFailureRate", 0.15d),
                    ReadInt(values, warnings, "Operations.AdaptiveQueuePressureWindows", 3),
                    ReadInt(values, warnings, "Operations.AdaptiveAdjustmentCooldownMs", 10000),
                    ReadInt(values, warnings, "Operations.AdaptiveHighGameplayLeaseWaitMs", 3000));
            }
            catch (ArgumentOutOfRangeException exception)
            {
                source = "FallbackDefaults";
                warnings.Add("Invalid resolved adaptive concurrency configuration; "
                    + "Fallback=conservative defaults; Error='" + exception.Message + "'");
                resolved = new AdaptiveConcurrencyOptions();
            }
            int screenshotConcurrency = ReadInt(values, warnings,
                "Operations.MaxConcurrentScreenshots", 5);
            int visionConcurrency = ReadInt(values, warnings,
                "Operations.MaxConcurrentVisionOperations", 8);
            if (warnings.Count > 0 && source == "App.config") source = "App.config+Fallbacks";
            return new AdaptiveConcurrencyConfigurationResult
            {
                Options = resolved,
                ScreenshotConcurrency = screenshotConcurrency,
                VisionConcurrency = visionConcurrency,
                Source = source,
                Warnings = warnings.ToArray()
            };
        }

        private static int ReadInt(NameValueCollection settings, ICollection<string> warnings,
            string key, int fallback)
        {
            string raw = settings[key];
            if (int.TryParse(raw, out int value)) return value;
            Warn(warnings, key, raw, fallback.ToString(CultureInfo.InvariantCulture));
            return fallback;
        }

        private static long ReadLong(NameValueCollection settings, ICollection<string> warnings,
            string key, long fallback)
        {
            string raw = settings[key];
            if (long.TryParse(raw, out long value)) return value;
            Warn(warnings, key, raw, fallback.ToString(CultureInfo.InvariantCulture));
            return fallback;
        }

        private static double ReadDouble(NameValueCollection settings, ICollection<string> warnings,
            string key, double fallback)
        {
            string raw = settings[key];
            if (double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture,
                out double value)) return value;
            Warn(warnings, key, raw, fallback.ToString(CultureInfo.InvariantCulture));
            return fallback;
        }

        private static void Warn(ICollection<string> warnings, string key, string raw,
            string fallback)
        {
            warnings.Add($"Key='{key}', Value='{raw ?? "<missing>"}', Fallback={fallback}");
        }
    }
}
