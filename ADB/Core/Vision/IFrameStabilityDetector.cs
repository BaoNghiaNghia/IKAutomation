namespace IK_Auto_ADB.Core.Vision
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
