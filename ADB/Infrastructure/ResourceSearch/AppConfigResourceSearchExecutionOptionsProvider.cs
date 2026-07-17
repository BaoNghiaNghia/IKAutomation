using ADB_Tool_Automation_Post_FB.Core.ResourceSearch;
using ADB_Tool_Automation_Post_FB.Core.Vision;
using System;
using System.Configuration;
using System.Globalization;

namespace ADB_Tool_Automation_Post_FB.Infrastructure.ResourceSearch
{
    public static class AppConfigResourceSearchExecutionOptionsProvider
    {
        public static ResourceSearchExecutionOptions Load()
        {
            return new ResourceSearchExecutionOptions(
                Int("NotFoundObservationWindowMs", 4000), Int("NotFoundFastPollIntervalMs", 120),
                Int("NormalPollIntervalMs", 300), Int("SearchResultTimeoutSeconds", 10),
                Int("MaxSearchTapAttempts", 2), Int("SearchTapVerificationTimeoutSeconds", 3),
                Region("ToastRegion", 150, 120, 980, 400), Int("MaxToastAnchorVerticalDistancePx", 140),
                Double("CameraMovementThreshold", .04), Double("CameraStableThreshold", .015),
                Int("RequiredStableFrames", 3), Int("MaxTransientUnknownFrames", 5),
                Bool("SaveResultScreenshots", true), Bool("SaveObservationBurst", false),
                Int("MaxObservationBurstFrames", 10),
                Text("ResultScreenshotDirectory", "Diagnostics/SearchResults"),
                Text("ObservationBurstDirectory", "Diagnostics/SearchObservation"),
                Int("ExpectedWidth", 1280), Int("ExpectedHeight", 720),
                Region("MapRegion", 160, 80, 960, 440));
        }

        private static string Key(string name) => "ResourceSearchExecution." + name;
        private static int Int(string name, int fallback)
        {
            string value = ConfigurationManager.AppSettings[Key(name)];
            if (string.IsNullOrWhiteSpace(value)) return fallback;
            if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed))
                throw new ConfigurationErrorsException($"Configuration value '{Key(name)}' must be an integer.");
            return parsed;
        }
        private static double Double(string name, double fallback)
        {
            string value = ConfigurationManager.AppSettings[Key(name)];
            if (string.IsNullOrWhiteSpace(value)) return fallback;
            if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed))
                throw new ConfigurationErrorsException($"Configuration value '{Key(name)}' must be a number.");
            return parsed;
        }
        private static bool Bool(string name, bool fallback)
        {
            string value = ConfigurationManager.AppSettings[Key(name)];
            if (string.IsNullOrWhiteSpace(value)) return fallback;
            if (!bool.TryParse(value, out bool parsed))
                throw new ConfigurationErrorsException($"Configuration value '{Key(name)}' must be true or false.");
            return parsed;
        }
        private static string Text(string name, string fallback) =>
            ConfigurationManager.AppSettings[Key(name)] ?? fallback;
        private static ImageRegion Region(string name, int x, int y, int width, int height) =>
            new ImageRegion(Int(name + ".X", x), Int(name + ".Y", y),
                Int(name + ".Width", width), Int(name + ".Height", height));
    }
}
