using ADB_Tool_Automation_Post_FB.Core.ResourceSearch;
using ADB_Tool_Automation_Post_FB.Core.Vision;

namespace ADB_Tool_Automation_Post_FB.Core.Workflows
{
    public sealed class MissingRuntimeTemplate
    {
        public ResourceType ResourceType { get; set; }
        public TemplateId TemplateId { get; set; }
        public string ExpectedPath { get; set; }
    }
}
