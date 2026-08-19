using IK_Auto_ADB.Core.Vision;

namespace IK_Auto_ADB.Core.GameDetection
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
