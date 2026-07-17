using ADB_Tool_Automation_Post_FB.Core.Vision;
using System;

namespace ADB_Tool_Automation_Post_FB.Core.ResourceSearch
{
    public static class ResourceTemplateMap
    {
        public static TemplateId Selected(ResourceType resource) => resource == ResourceType.Iron
            ? TemplateId.ResourceIronSelected
            : resource == ResourceType.Stone ? TemplateId.ResourceStoneSelected
            : throw new ArgumentOutOfRangeException(nameof(resource));
        public static TemplateId Unselected(ResourceType resource) => resource == ResourceType.Iron
            ? TemplateId.ResourceIronUnselected
            : resource == ResourceType.Stone ? TemplateId.ResourceStoneUnselected
            : throw new ArgumentOutOfRangeException(nameof(resource));
        public static TemplateId PopupTitle(ResourceType resource) => resource == ResourceType.Iron
            ? TemplateId.ResourcePopupIronTitle
            : resource == ResourceType.Stone ? TemplateId.ResourcePopupStoneTitle
            : throw new ArgumentOutOfRangeException(nameof(resource));
    }
}
