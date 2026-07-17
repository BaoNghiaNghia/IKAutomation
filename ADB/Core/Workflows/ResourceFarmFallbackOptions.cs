using ADB_Tool_Automation_Post_FB.Core.ResourceSearch;
using ADB_Tool_Automation_Post_FB.Core.StorageLimit;
using System;
using System.Collections.Generic;
using System.Linq;

namespace ADB_Tool_Automation_Post_FB.Core.Workflows
{
    public sealed class ResourceFarmFallbackOptions
    {
        public IReadOnlyList<ResourceType> ResourcePriority { get; set; } = new[]
            { ResourceType.Iron, ResourceType.Stone, ResourceType.Wood, ResourceType.Food };
        public IReadOnlyList<int> LevelPriority { get; set; } = new[] { 7, 6, 5 };
        public int AttemptsPerLevel { get; set; } = 1;
        public StorageLimitPolicy StorageLimitPolicy { get; set; } = StorageLimitPolicy.ConfirmAndSwitchResource;
        public bool SwitchOnStorageLimit { get; set; } = true;
        public bool SwitchWhenLevelsExhausted { get; set; } = true;
        public int MaxRecoveryTransitions { get; set; } = 3;
        public int RecoveryPollIntervalMs { get; set; } = 250;
        public int RecoveryTimeoutSeconds { get; set; } = 8;
        public bool StopOnFirstMarchStarted { get; set; } = true;
        public bool SaveAttemptScreenshots { get; set; } = true;
        public string ScreenshotDirectory { get; set; } = "Diagnostics/ResourceFarmFallback";

        public void Validate()
        {
            if (ResourcePriority == null || ResourcePriority.Count == 0
                || ResourcePriority.Distinct().Count() != ResourcePriority.Count)
                throw new ArgumentException("ResourcePriority must be non-empty and unique.");
            if (ResourcePriority.Any(x => !Enum.IsDefined(typeof(ResourceType), x)))
                throw new ArgumentException("ResourcePriority contains an unsupported resource.");
            if (LevelPriority == null || LevelPriority.Count == 0
                || LevelPriority.Distinct().Count() != LevelPriority.Count
                || LevelPriority.Any(x => x != 5 && x != 6 && x != 7))
                throw new ArgumentException("LevelPriority supports unique levels 7, 6, and 5.");
            if (StorageLimitPolicy != StorageLimitPolicy.ConfirmAndSwitchResource)
                throw new ArgumentException("One-shot fallback requires ConfirmAndSwitchResource.");
            if (MaxRecoveryTransitions < 1 || MaxRecoveryTransitions > 10) throw new ArgumentOutOfRangeException(nameof(MaxRecoveryTransitions));
            if (RecoveryPollIntervalMs < 50 || RecoveryPollIntervalMs > 5000) throw new ArgumentOutOfRangeException(nameof(RecoveryPollIntervalMs));
            if (RecoveryTimeoutSeconds < 1 || RecoveryTimeoutSeconds > 60) throw new ArgumentOutOfRangeException(nameof(RecoveryTimeoutSeconds));
            if (AttemptsPerLevel < 1 || AttemptsPerLevel > 3) throw new ArgumentOutOfRangeException(nameof(AttemptsPerLevel));
            if (string.IsNullOrWhiteSpace(ScreenshotDirectory)) throw new ArgumentException("ScreenshotDirectory is required.");
        }
    }
}
