using IK_Auto_ADB.Core.Vision;

namespace IK_Auto_ADB.Core.MarchDispatch
{
    public interface ITeamMarchTimerDetector
    {
        TeamMarchTimerDetectionResult DetectContent(byte[] screenshotPng,
            ImageRegion timerRegion);

        TeamMarchTimerProgressionResult Compare(byte[] previousScreenshotPng,
            byte[] currentScreenshotPng, ImageRegion timerRegion);
    }
}
