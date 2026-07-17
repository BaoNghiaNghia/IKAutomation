using ADB_Tool_Automation_Post_FB.Core.ResourceSearch;
using ADB_Tool_Automation_Post_FB.Core.StorageLimit;
using System;
using System.Collections.Generic;
using System.Linq;

namespace ADB_Tool_Automation_Post_FB.Core.Workflows
{
    public sealed class ResourceFarmFallbackPolicy
    {
        public IReadOnlyList<ResourceType> ResourcePriority { get; set; } = new[]
            { ResourceType.Iron, ResourceType.Stone, ResourceType.Wood, ResourceType.Food };
        public IReadOnlyList<int> LevelPriority { get; set; } = new[] { 7, 6, 5 };
        public int AttemptsPerLevel { get; set; } = 1;
        public bool SwitchWhenLevelsExhausted { get; set; } = true;
        public bool SwitchOnStorageLimit { get; set; } = true;
        public bool StopOnFirstMarchStarted { get; set; } = true;
        public StorageLimitPolicy StorageLimitPolicy { get; set; } = StorageLimitPolicy.CancelAndSwitchResource;

        public void Validate()
        {
            if (ResourcePriority == null || ResourcePriority.Count == 0
                || ResourcePriority.Distinct().Count() != ResourcePriority.Count)
                throw new ArgumentException("ResourcePriority must be non-empty and unique.");
            if (ResourcePriority.Any(x => !Enum.IsDefined(typeof(ResourceType), x)))
                throw new ArgumentException("ResourcePriority contains an unsupported resource.");
            if (LevelPriority == null || LevelPriority.Count == 0
                || LevelPriority.Distinct().Count() != LevelPriority.Count
                || LevelPriority.Any(x => x < 5 || x > 7))
                throw new ArgumentException("LevelPriority must contain unique supported levels 5 through 7.");
            if (AttemptsPerLevel < 1 || AttemptsPerLevel > 3)
                throw new ArgumentOutOfRangeException(nameof(AttemptsPerLevel));
        }
    }
}
