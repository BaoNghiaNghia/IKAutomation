namespace ADB_Tool_Automation_Post_FB.Core.Vision
{
    public sealed class ImageMatchRequest
    {
        public ImageMatchRequest(byte[] templatePng, ImageRegion? searchRegion = null)
        {
            TemplatePng = templatePng ?? throw new System.ArgumentNullException(nameof(templatePng));
            SearchRegion = searchRegion;
        }

        public byte[] TemplatePng { get; }
        public ImageRegion? SearchRegion { get; }
    }

    public interface IBatchImageMatcher
    {
        System.Collections.Generic.IReadOnlyList<ImageMatchResult> FindMany(
            byte[] screenshotPng,
            System.Collections.Generic.IReadOnlyList<ImageMatchRequest> requests);
    }

    /// <summary>
    /// Optional optimized matching capability for a screenshot that is already decoded.
    /// The frame remains owned by the caller and must stay alive for the entire call.
    /// </summary>
    public interface IFrameImageMatcher
    {
        ImageMatchResult Find(CapturedFrame frame, byte[] templatePng,
            ImageRegion? searchRegion = null);

        System.Collections.Generic.IReadOnlyList<ImageMatchResult> FindMany(
            CapturedFrame frame,
            System.Collections.Generic.IReadOnlyList<ImageMatchRequest> requests);
    }

    public interface IImageMatcher
    {
        ImageMatchResult Find(byte[] screenshotPng, byte[] templatePng, ImageRegion? searchRegion = null);
    }
}
