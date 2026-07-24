using ADB_Tool_Automation_Post_FB.Core.ResourceSearch;
using ADB_Tool_Automation_Post_FB.Core.Vision;
using System;
using System.Collections.Generic;

namespace ADB_Tool_Automation_Post_FB.Infrastructure.ResourceSearch
{
    public sealed class ResourceTemplateProfileProvider : IResourceTemplateProfileProvider
    {
        private readonly ITemplateRegistry registry;
        private static readonly IReadOnlyDictionary<ResourceType, ResourceTemplateProfile> Profiles =
            new Dictionary<ResourceType, ResourceTemplateProfile>
            {
                { ResourceType.Iron, Profile(ResourceType.Iron, "Sắt") },
                { ResourceType.Stone, Profile(ResourceType.Stone, "Mỏ Đá") },
                { ResourceType.Wood, Profile(ResourceType.Wood, "Rừng") },
                { ResourceType.Food, Profile(ResourceType.Food, "Đất nông nghiệp") }
            };

        public ResourceTemplateProfileProvider(ITemplateRegistry registry)
        {
            this.registry = registry ?? throw new ArgumentNullException(nameof(registry));
        }

        public ResourceTemplateProfile Get(ResourceType resourceType)
        {
            if (!Profiles.TryGetValue(resourceType, out ResourceTemplateProfile profile))
                throw new ArgumentOutOfRangeException(nameof(resourceType), resourceType, "Resource profile is not registered.");
            return profile;
        }

        public bool IsSupported(ResourceType resourceType)
        {
            ResourceTemplateProfile profile;
            try { profile = Get(resourceType); }
            catch (ArgumentOutOfRangeException) { return false; }
            return registry.Exists(profile.SelectedTemplate)
                && registry.Exists(profile.UnselectedTemplate)
                && registry.Exists(profile.PopupTitleTemplate);
        }

        public string GetUnsupportedReason(ResourceType resourceType)
        {
            ResourceTemplateProfile profile;
            try { profile = Get(resourceType); }
            catch (Exception exception) { return exception.Message; }
            foreach (TemplateId id in new[] { profile.SelectedTemplate, profile.UnselectedTemplate, profile.PopupTitleTemplate })
                if (!registry.Exists(id)) return $"Required template '{id}' was not found at '{registry.GetPath(id)}'.";
            return null;
        }

        private static ResourceTemplateProfile Profile(ResourceType type, string displayName) =>
            new ResourceTemplateProfile
            {
                ResourceType = type, SelectedTemplate = ResourceTemplateMap.Selected(type),
                UnselectedTemplate = ResourceTemplateMap.Unselected(type),
                PopupTitleTemplate = ResourceTemplateMap.PopupTitle(type), DisplayName = displayName
            };
    }
}
