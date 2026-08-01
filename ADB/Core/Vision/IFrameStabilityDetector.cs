namespace ADB_Tool_Automation_Post_FB.Core.Vision
{
    public interface IFrameStabilityDetector
    {
        FrameComparisonResult Compare(
            CapturedFrame previous,
            CapturedFrame current,
            ImageRegion? region = null);

        FrameComparisonResult Compare(
            byte[] previousPng,
            byte[] currentPng,
            ImageRegion? region = null);
    }
}
