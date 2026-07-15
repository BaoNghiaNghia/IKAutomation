namespace ADB_Tool_Automation_Post_FB.Core.Vision
{
    public interface IImageMatcher
    {
        ImageMatchResult Find(byte[] screenshotPng, byte[] templatePng, ImageRegion? searchRegion = null);
    }
}
