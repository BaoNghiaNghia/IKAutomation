using ADB_Tool_Automation_Post_FB.Core.Vision;

namespace ADB_Tool_Automation_Post_FB.Core.MarchDispatch
{
    public interface ITeamMarchTimerDetector
    {
        TeamMarchTimerDetectionResult DetectContent(byte[] screenshotPng,
            ImageRegion timerRegion);

        TeamMarchTimerProgressionResult Compare(byte[] previousScreenshotPng,
            byte[] currentScreenshotPng, ImageRegion timerRegion);
    }
}
