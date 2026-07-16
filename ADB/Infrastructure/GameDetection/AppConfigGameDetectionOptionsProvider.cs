using ADB_Tool_Automation_Post_FB.Core.GameDetection;
using ADB_Tool_Automation_Post_FB.Core.Vision;
using System;
using System.Configuration;
using System.Globalization;

namespace ADB_Tool_Automation_Post_FB.Infrastructure.GameDetection
{
    public static class AppConfigGameDetectionOptionsProvider
    {
        public static GameDetectionOptions Load()
        {
            return new GameDetectionOptions(
                ReadPositiveInteger("GameDetection.ExpectedWidth", 1280),
                ReadPositiveInteger("GameDetection.ExpectedHeight", 720),
                ReadBoolean("GameDetection.RequireExpectedResolution", true),
                ReadBoolean("GameDetection.SaveUnknownScreenshots", true),
                ConfigurationManager.AppSettings["GameDetection.UnknownScreenshotDirectory"]
                    ?? "Diagnostics/UnknownStates",
                new ImageRegion(
                    ReadPositiveInteger("ResourcePopupVerification.PopupRegion.X", 540, allowZero: true),
                    ReadPositiveInteger("ResourcePopupVerification.PopupRegion.Y", 400, allowZero: true),
                    ReadPositiveInteger("ResourcePopupVerification.PopupRegion.Width", 470),
                    ReadPositiveInteger("ResourcePopupVerification.PopupRegion.Height", 320)));
        }

        private static int ReadPositiveInteger(string key, int defaultValue, bool allowZero = false)
        {
            string value = ConfigurationManager.AppSettings[key];
            if (string.IsNullOrWhiteSpace(value))
                return defaultValue;

            if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out int result)
                || (allowZero ? result < 0 : result <= 0))
            {
                throw new ConfigurationErrorsException(
                    $"Configuration value '{key}' must be a positive integer.");
            }

            return result;
        }

        private static bool ReadBoolean(string key, bool defaultValue)
        {
            string value = ConfigurationManager.AppSettings[key];
            if (string.IsNullOrWhiteSpace(value))
                return defaultValue;

            if (!bool.TryParse(value, out bool result))
                throw new ConfigurationErrorsException($"Configuration value '{key}' must be true or false.");

            return result;
        }
    }
}
