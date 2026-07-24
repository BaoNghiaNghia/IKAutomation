using ADB_Tool_Automation_Post_FB.Core.ResourceSearch;
using ADB_Tool_Automation_Post_FB.Core.Vision;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Globalization;
using System.Linq;

namespace ADB_Tool_Automation_Post_FB.Infrastructure.ResourceSearch
{
    public static class AppConfigResourceLevelFallbackOptionsProvider
    {
        public static ResourceLevelFallbackOptions Load()
        {
            return new ResourceLevelFallbackOptions(
                ReadLevels("ResourceLevelFallback.Levels", new[] { 7, 6, 5 }),
                ReadInt("ResourceLevelFallback.AttemptsPerLevel", 1),
                ReadInt("ResourceLevelFallback.RequiredConsecutiveToastClearFrames", 2),
                ReadInt("ResourceLevelFallback.ToastClearPollIntervalMs", 150),
                ReadInt("ResourceLevelFallback.ToastClearTimeoutSeconds", 5),
                ReadBool("ResourceLevelFallback.StopOnFirstLocated", true),
                ReadBool("ResourceLevelFallback.WaitForToastClearBetweenAttempts", true),
                ReadBool("ResourceLevelFallback.SaveExhaustedScreenshot", true),
                ReadString("ResourceLevelFallback.ScreenshotDirectory", "Diagnostics/ResourceLevelFallback"),
                new ImageRegion(
                    ReadInt("ResourceSearchExecution.ToastRegion.X", 150),
                    ReadInt("ResourceSearchExecution.ToastRegion.Y", 120),
                    ReadInt("ResourceSearchExecution.ToastRegion.Width", 980),
                    ReadInt("ResourceSearchExecution.ToastRegion.Height", 400)),
                ReadInt("ResourceSearchExecution.MaxToastAnchorVerticalDistancePx", 140));
        }

        private static IReadOnlyList<int> ReadLevels(string key, IReadOnlyList<int> fallback)
        {
            string value = ConfigurationManager.AppSettings[key];
            if (string.IsNullOrWhiteSpace(value)) return fallback;
            return value.Split(',').Select(item => int.Parse(item.Trim(), CultureInfo.InvariantCulture)).ToArray();
        }
        private static int ReadInt(string key, int fallback) => int.TryParse(
            ConfigurationManager.AppSettings[key], NumberStyles.Integer,
            CultureInfo.InvariantCulture, out int value) ? value : fallback;
        private static bool ReadBool(string key, bool fallback) => bool.TryParse(
            ConfigurationManager.AppSettings[key], out bool value) ? value : fallback;
        private static string ReadString(string key, string fallback)
        {
            string value = ConfigurationManager.AppSettings[key];
            return string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
        }
    }
}
