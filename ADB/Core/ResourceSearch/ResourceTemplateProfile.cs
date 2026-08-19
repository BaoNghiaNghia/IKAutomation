using ADB_Tool_Automation_Post_FB.Core.Vision;

namespace ADB_Tool_Automation_Post_FB.Core.ResourceSearch
{
    public sealed class ResourceTemplateProfile
    {
        public ResourceType ResourceType { get; set; }
        public TemplateId SelectedTemplate { get; set; }
        public TemplateId UnselectedTemplate { get; set; }
        public TemplateId PopupTitleTemplate { get; set; }
        public string DisplayName { get; set; }
    }
}
