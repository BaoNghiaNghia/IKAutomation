namespace ADB_Tool_Automation_Post_FB.Core.Vision
{
    public interface IFrameStabilityDetector
    {
        FrameComparisonResult Compare(
            byte[] previousPng,
            byte[] currentPng,
            ImageRegion? region = null);
    }
}
