using ADB_Tool_Automation_Post_FB.Core.ResourceSearch;
using ADB_Tool_Automation_Post_FB.Core.StorageLimit;
using ADB_Tool_Automation_Post_FB.Core.Workflows;
using System;
using System.Configuration;
using System.Linq;

namespace ADB_Tool_Automation_Post_FB.Infrastructure.Workflows
{
    public static class AppConfigResourceFarmFallbackOptionsProvider
    {
        public static ResourceFarmFallbackOptions Load()
        {
            var options = new ResourceFarmFallbackOptions
            {
                ResourcePriority = ReadList("ResourceFarmFallback.ResourcePriority", "Iron,Stone,Wood,Food", ParseResource),
                LevelPriority = ReadList("ResourceFarmFallback.LevelPriority", "7,6,5", int.Parse),
                AttemptsPerLevel = ReadInt("ResourceFarmFallback.AttemptsPerLevel", 1),
                StorageLimitPolicy = ReadEnum("ResourceFarmFallback.StorageLimitPolicy", StorageLimitPolicy.CancelAndSwitchResource),
                SwitchOnStorageLimit = ReadBool("ResourceFarmFallback.SwitchOnStorageLimit", true),
                SwitchWhenLevelsExhausted = ReadBool("ResourceFarmFallback.SwitchWhenLevelsExhausted", true),
                MaxRecoveryTransitions = ReadInt("ResourceFarmFallback.MaxRecoveryTransitions", 3),
                RecoveryPollIntervalMs = ReadInt("ResourceFarmFallback.RecoveryPollIntervalMs", 250),
                RecoveryTimeoutSeconds = ReadInt("ResourceFarmFallback.RecoveryTimeoutSeconds", 8),
                MaxSearchAreaRecoveryAttempts = ReadInt("ResourceFarmFallback.MaxSearchAreaRecoveryAttempts", 1),
                StopOnFirstMarchStarted = ReadBool("ResourceFarmFallback.StopOnFirstMarchStarted", true),
                SaveAttemptScreenshots = ReadBool("ResourceFarmFallback.SaveAttemptScreenshots", true),
                ScreenshotDirectory = ConfigurationManager.AppSettings["ResourceFarmFallback.ScreenshotDirectory"]
                    ?? "Diagnostics/ResourceFarmFallback"
            };
            options.Validate(); return options;
        }
        private static T[] ReadList<T>(string key, string fallback, Func<string,T> parser) =>
            (ConfigurationManager.AppSettings[key] ?? fallback).Split(',').Select(x => parser(x.Trim())).ToArray();
        private static ResourceType ParseResource(string value) => (ResourceType)Enum.Parse(typeof(ResourceType), value, true);
        private static int ReadInt(string key,int fallback) => int.TryParse(ConfigurationManager.AppSettings[key],out int value)?value:fallback;
        private static bool ReadBool(string key,bool fallback) => bool.TryParse(ConfigurationManager.AppSettings[key],out bool value)?value:fallback;
        private static T ReadEnum<T>(string key,T fallback) where T:struct => Enum.TryParse(ConfigurationManager.AppSettings[key],true,out T value)?value:fallback;
    }
}
