using ADB_Tool_Automation_Post_FB.Core.ResourcePopup;
using ADB_Tool_Automation_Post_FB.Core.Vision;
using System.Configuration;
using System.Globalization;

namespace ADB_Tool_Automation_Post_FB.Infrastructure.ResourcePopup
{
    public static class AppConfigResourcePopupVerificationOptionsProvider
    {
        public static ResourcePopupVerificationOptions Load()
        {
            return new ResourcePopupVerificationOptions(
                Int("PollIntervalMs", 250), Int("VerificationTimeoutSeconds", 5),
                Int("RequiredConsecutiveReadyFrames", 1),
                new ImageRegion(Int("HeaderRegion.X", 450), Int("HeaderRegion.Y", 230),
                    Int("HeaderRegion.Width", 680), Int("HeaderRegion.Height", 310)),
                new ImageRegion(Int("ActionRegion.X", 560), Int("ActionRegion.Y", 430),
                    Int("ActionRegion.Width", 500), Int("ActionRegion.Height", 260)),
                Bool("SaveFailureScreenshots", true),
                Text("FailureScreenshotDirectory", "Diagnostics/ResourcePopup"),
                Int("ExpectedWidth", 1280), Int("ExpectedHeight", 720));
        }

        private static string Key(string name) => "ResourcePopupVerification." + name;
        private static int Int(string name, int fallback)
        {
            string value = ConfigurationManager.AppSettings[Key(name)];
            if (string.IsNullOrWhiteSpace(value)) return fallback;
            if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int result))
                throw new ConfigurationErrorsException($"Configuration value '{Key(name)}' must be an integer.");
            return result;
        }
        private static bool Bool(string name, bool fallback)
        {
            string value = ConfigurationManager.AppSettings[Key(name)];
            if (string.IsNullOrWhiteSpace(value)) return fallback;
            if (!bool.TryParse(value, out bool result))
                throw new ConfigurationErrorsException($"Configuration value '{Key(name)}' must be true or false.");
            return result;
        }
        private static string Text(string name, string fallback) =>
            ConfigurationManager.AppSettings[Key(name)] ?? fallback;
    }
}
