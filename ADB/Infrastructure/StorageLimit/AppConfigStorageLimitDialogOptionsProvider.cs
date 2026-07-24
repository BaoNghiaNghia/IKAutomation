using ADB_Tool_Automation_Post_FB.Core.StorageLimit;
using System;
using System.Configuration;

namespace ADB_Tool_Automation_Post_FB.Infrastructure.StorageLimit
{
    public static class AppConfigStorageLimitDialogOptionsProvider
    {
        public static StorageLimitDialogOptions Load()
        {
            var options = new StorageLimitDialogOptions
            {
                Policy = ReadEnum("StorageLimitDialog.Policy", StorageLimitPolicy.CancelAndSwitchResource),
                PollIntervalMs = ReadInt("StorageLimitDialog.PollIntervalMs", 200),
                TransitionTimeoutSeconds = ReadInt("StorageLimitDialog.TransitionTimeoutSeconds", 8),
                MaxActionAttempts = ReadInt("StorageLimitDialog.MaxCancelAttempts",
                    ReadInt("StorageLimitDialog.MaxActionAttempts", 2)),
                ActionRetryDelayMs = ReadInt("StorageLimitDialog.CancelRetryDelayMs",
                    ReadInt("StorageLimitDialog.ActionRetryDelayMs", 500)),
                MaxTransientUnknownFrames = ReadInt("StorageLimitDialog.MaxTransientUnknownFrames", 5),
                MaxBackAttempts = ReadInt("ResourceFarmFallback.MaxBackAttempts", 1)
            };
            options.Validate();
            return options;
        }

        private static int ReadInt(string key, int fallback) =>
            int.TryParse(ConfigurationManager.AppSettings[key], out int value) ? value : fallback;

        private static T ReadEnum<T>(string key, T fallback) where T : struct =>
            Enum.TryParse(ConfigurationManager.AppSettings[key], true, out T value) ? value : fallback;
    }
}
