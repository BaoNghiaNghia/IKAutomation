using ADB_Tool_Automation_Post_FB.Core.ResourceSearch;
using System.Collections.Generic;

namespace ADB_Tool_Automation_Post_FB.Core.Workflows
{
    public sealed class ResourceFarmFallbackResult
    {
        public IReadOnlyList<ResourceType> AttemptedResources { get; set; }
        public IReadOnlyList<ResourceType> StorageFullResources { get; set; }
        public ResourceType? LocatedResource { get; set; }
        public int? LocatedLevel { get; set; }
        public ResourceType? DispatchedResource { get; set; }
        public int RecoveryTransitions { get; set; }
    }
}
