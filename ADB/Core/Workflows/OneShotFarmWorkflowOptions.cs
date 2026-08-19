using System;
using System.IO;

namespace ADB_Tool_Automation_Post_FB.Core.Workflows
{
    public sealed class OneShotFarmWorkflowOptions
    {
        public OneShotFarmWorkflowOptions(bool saveStepFailureScreenshots,
            bool saveSuccessScreenshot, string screenshotDirectory)
        {
            if (string.IsNullOrWhiteSpace(screenshotDirectory) || Path.IsPathRooted(screenshotDirectory))
                throw new ArgumentException("Screenshot directory must be relative.", nameof(screenshotDirectory));
            foreach (string part in screenshotDirectory.Replace('\\', '/').Split('/'))
                if (part == "..") throw new ArgumentException("Screenshot directory cannot contain path traversal.", nameof(screenshotDirectory));
            SaveStepFailureScreenshots = saveStepFailureScreenshots;
            SaveSuccessScreenshot = saveSuccessScreenshot;
            ScreenshotDirectory = screenshotDirectory.Trim();
        }
        public bool SaveStepFailureScreenshots { get; }
        public bool SaveSuccessScreenshot { get; }
        public string ScreenshotDirectory { get; }
    }
}
