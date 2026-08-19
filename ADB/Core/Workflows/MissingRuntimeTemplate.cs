using IK_Auto_ADB.Core.ResourceSearch;
using IK_Auto_ADB.Core.Vision;

namespace IK_Auto_ADB.Core.Workflows
{
    public sealed class MissingRuntimeTemplate
    {
        public ResourceType ResourceType { get; set; }
        public TemplateId TemplateId { get; set; }
        public string ExpectedPath { get; set; }
    }
}
