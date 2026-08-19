using IK_Auto_ADB.Core.Vision;
using System;

namespace IK_Auto_ADB.Core.ResourceSearch
{
    public static class ResourceTemplateMap
    {
        public static TemplateId Selected(ResourceType resource) => resource == ResourceType.Iron
            ? TemplateId.ResourceIronSelected
            : resource == ResourceType.Stone ? TemplateId.ResourceStoneSelected
            : resource == ResourceType.Wood ? TemplateId.ResourceWoodSelected
            : resource == ResourceType.Food ? TemplateId.ResourceFoodSelected
            : throw new ArgumentOutOfRangeException(nameof(resource));
        public static TemplateId Unselected(ResourceType resource) => resource == ResourceType.Iron
            ? TemplateId.ResourceIronUnselected
            : resource == ResourceType.Stone ? TemplateId.ResourceStoneUnselected
            : resource == ResourceType.Wood ? TemplateId.ResourceWoodUnselected
            : resource == ResourceType.Food ? TemplateId.ResourceFoodUnselected
            : throw new ArgumentOutOfRangeException(nameof(resource));
        public static TemplateId PopupTitle(ResourceType resource) => resource == ResourceType.Iron
            ? TemplateId.ResourcePopupIronTitle
            : resource == ResourceType.Stone ? TemplateId.ResourcePopupStoneTitle
            : resource == ResourceType.Wood ? TemplateId.ResourcePopupWoodTitle
            : resource == ResourceType.Food ? TemplateId.ResourcePopupFoodTitle
            : throw new ArgumentOutOfRangeException(nameof(resource));
    }
}
