using ADB_Tool_Automation_Post_FB.Core.Vision;

namespace ADB_Tool_Automation_Post_FB.Core.ResourceSearch
{
    public sealed class ConfigurationTemplateEvidence
    {
        public TemplateId TemplateId { get; set; }
        public bool Found { get; set; }
        public int X { get; set; }
        public int Y { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
        public double? Confidence { get; set; }
        public string Message { get; set; }
    }
}
