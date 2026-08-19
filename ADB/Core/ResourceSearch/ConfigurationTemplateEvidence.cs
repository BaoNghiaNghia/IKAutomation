using IK_Auto_ADB.Core.Vision;

namespace IK_Auto_ADB.Core.ResourceSearch
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
