using System;

namespace ADB_Tool_Automation_Post_FB.Core.ResourceSearch
{
    public sealed class ResourceSearchConfigurationOptions
    {
        public ResourceSearchConfigurationOptions(int statePollIntervalMs,
            int actionVerificationTimeoutSeconds, int maxSelectionAttempts,
            int minimumLevel, int maximumLevel, int resetMinusTapCount, int tapIntervalMs)
        {
            if (statePollIntervalMs <= 0) throw new ArgumentOutOfRangeException(nameof(statePollIntervalMs));
            if (actionVerificationTimeoutSeconds <= 0) throw new ArgumentOutOfRangeException(nameof(actionVerificationTimeoutSeconds));
            if (maxSelectionAttempts < 1 || maxSelectionAttempts > 3) throw new ArgumentOutOfRangeException(nameof(maxSelectionAttempts));
            if (minimumLevel < 1) throw new ArgumentOutOfRangeException(nameof(minimumLevel));
            if (maximumLevel < minimumLevel) throw new ArgumentOutOfRangeException(nameof(maximumLevel));
            if (resetMinusTapCount < maximumLevel) throw new ArgumentOutOfRangeException(nameof(resetMinusTapCount));
            if (tapIntervalMs <= 0) throw new ArgumentOutOfRangeException(nameof(tapIntervalMs));
            StatePollIntervalMs = statePollIntervalMs;
            ActionVerificationTimeoutSeconds = actionVerificationTimeoutSeconds;
            MaxSelectionAttempts = maxSelectionAttempts;
            MinimumLevel = minimumLevel;
            MaximumLevel = maximumLevel;
            ResetMinusTapCount = resetMinusTapCount;
            TapIntervalMs = tapIntervalMs;
        }

        public int StatePollIntervalMs { get; }
        public int ActionVerificationTimeoutSeconds { get; }
        public int MaxSelectionAttempts { get; }
        public int MinimumLevel { get; }
        public int MaximumLevel { get; }
        public int ResetMinusTapCount { get; }
        public int TapIntervalMs { get; }
    }
}
