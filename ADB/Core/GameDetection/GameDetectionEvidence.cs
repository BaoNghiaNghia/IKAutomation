using ADB_Tool_Automation_Post_FB.Core.Vision;

namespace ADB_Tool_Automation_Post_FB.Core.GameDetection
{
    public sealed class GameDetectionEvidence
    {
        public TemplateId TemplateId { get; set; }
        public bool TemplateExists { get; set; }
        public bool Found { get; set; }
        public ImageMatchResult MatchResult { get; set; }
        public double? Confidence { get; set; }
        public ImageRegion? SearchRegion { get; set; }
        public string Message { get; set; }
    }
}
