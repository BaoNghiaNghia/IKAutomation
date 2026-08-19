using IK_Auto_ADB.Core.Vision;

namespace IK_Auto_ADB.Core.ResourceSearch
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
