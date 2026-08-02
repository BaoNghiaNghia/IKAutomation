using System;

namespace ADB_Tool_Automation_Post_FB.Core.Navigation
{
    public sealed class WorldMapNavigationOptions
    {
        public WorldMapNavigationOptions(
            int statePollIntervalMs,
            int stateTransitionTimeoutSeconds,
            int maxOpenSearchAttempts,
            bool preferSameTerritoryCoordinateSearch = true,
            int coordinateTerritoryAttempts = 8,
            int maximumCoordinateOffset = 100,
            int minimumCoordinateOffset = 20,
            int coordinateCandidateSettleTimeoutMs = 3000,
            int homeTerritoryClassificationAttempts = 3,
            bool allowLegacyTerritoryFallback = true)
        {
            if (statePollIntervalMs <= 0) throw new ArgumentOutOfRangeException(nameof(statePollIntervalMs));
            if (stateTransitionTimeoutSeconds <= 0) throw new ArgumentOutOfRangeException(nameof(stateTransitionTimeoutSeconds));
            if (maxOpenSearchAttempts < 1 || maxOpenSearchAttempts > 3) throw new ArgumentOutOfRangeException(nameof(maxOpenSearchAttempts));
            if (coordinateTerritoryAttempts < 1 || coordinateTerritoryAttempts > 20) throw new ArgumentOutOfRangeException(nameof(coordinateTerritoryAttempts));
            if (minimumCoordinateOffset < 1) throw new ArgumentOutOfRangeException(nameof(minimumCoordinateOffset));
            if (maximumCoordinateOffset < minimumCoordinateOffset) throw new ArgumentOutOfRangeException(nameof(maximumCoordinateOffset));
            if (coordinateCandidateSettleTimeoutMs < statePollIntervalMs) throw new ArgumentOutOfRangeException(nameof(coordinateCandidateSettleTimeoutMs));
            if (homeTerritoryClassificationAttempts < 1 || homeTerritoryClassificationAttempts > 5) throw new ArgumentOutOfRangeException(nameof(homeTerritoryClassificationAttempts));
            StatePollIntervalMs = statePollIntervalMs;
            StateTransitionTimeoutSeconds = stateTransitionTimeoutSeconds;
            MaxOpenSearchAttempts = maxOpenSearchAttempts;
            PreferSameTerritoryCoordinateSearch = preferSameTerritoryCoordinateSearch;
            CoordinateTerritoryAttempts = coordinateTerritoryAttempts;
            MaximumCoordinateOffset = maximumCoordinateOffset;
            MinimumCoordinateOffset = minimumCoordinateOffset;
            CoordinateCandidateSettleTimeoutMs = coordinateCandidateSettleTimeoutMs;
            HomeTerritoryClassificationAttempts = homeTerritoryClassificationAttempts;
            AllowLegacyTerritoryFallback = allowLegacyTerritoryFallback;
        }

        public int StatePollIntervalMs { get; }
        public int StateTransitionTimeoutSeconds { get; }
        public int MaxOpenSearchAttempts { get; }
        public bool PreferSameTerritoryCoordinateSearch { get; }
        public int CoordinateTerritoryAttempts { get; }
        public int MaximumCoordinateOffset { get; }
        public int MinimumCoordinateOffset { get; }
        public int CoordinateCandidateSettleTimeoutMs { get; }
        public int HomeTerritoryClassificationAttempts { get; }
        public bool AllowLegacyTerritoryFallback { get; }
    }
}
