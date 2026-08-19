using System;

namespace IK_Auto_ADB.Core.Navigation
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
            bool allowLegacyTerritoryFallback = true,
            int homePinAcquisitionAttempts = 3,
            bool requireVerifiedSameTerritory = true,
            int minimumWorldCoordinate = 0,
            int maximumWorldCoordinate = 2047,
            int coordinateInputVerificationAttempts = 2,
            int coordinateRollbackTimeoutSeconds = 5)
        {
            if (statePollIntervalMs <= 0) throw new ArgumentOutOfRangeException(nameof(statePollIntervalMs));
            if (stateTransitionTimeoutSeconds <= 0) throw new ArgumentOutOfRangeException(nameof(stateTransitionTimeoutSeconds));
            if (maxOpenSearchAttempts < 1 || maxOpenSearchAttempts > 3) throw new ArgumentOutOfRangeException(nameof(maxOpenSearchAttempts));
            if (coordinateTerritoryAttempts < 1 || coordinateTerritoryAttempts > 20) throw new ArgumentOutOfRangeException(nameof(coordinateTerritoryAttempts));
            if (minimumCoordinateOffset < 1) throw new ArgumentOutOfRangeException(nameof(minimumCoordinateOffset));
            if (maximumCoordinateOffset < minimumCoordinateOffset) throw new ArgumentOutOfRangeException(nameof(maximumCoordinateOffset));
            if (coordinateCandidateSettleTimeoutMs < statePollIntervalMs) throw new ArgumentOutOfRangeException(nameof(coordinateCandidateSettleTimeoutMs));
            if (homeTerritoryClassificationAttempts < 1 || homeTerritoryClassificationAttempts > 5) throw new ArgumentOutOfRangeException(nameof(homeTerritoryClassificationAttempts));
            if (homePinAcquisitionAttempts < 1 || homePinAcquisitionAttempts > 5) throw new ArgumentOutOfRangeException(nameof(homePinAcquisitionAttempts));
            if (minimumWorldCoordinate < 0) throw new ArgumentOutOfRangeException(nameof(minimumWorldCoordinate));
            if (maximumWorldCoordinate <= minimumWorldCoordinate) throw new ArgumentOutOfRangeException(nameof(maximumWorldCoordinate));
            if (coordinateInputVerificationAttempts < 1 || coordinateInputVerificationAttempts > 3) throw new ArgumentOutOfRangeException(nameof(coordinateInputVerificationAttempts));
            if (coordinateRollbackTimeoutSeconds < 1 || coordinateRollbackTimeoutSeconds > 15) throw new ArgumentOutOfRangeException(nameof(coordinateRollbackTimeoutSeconds));
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
            HomePinAcquisitionAttempts = homePinAcquisitionAttempts;
            RequireVerifiedSameTerritory = requireVerifiedSameTerritory;
            MinimumWorldCoordinate = minimumWorldCoordinate;
            MaximumWorldCoordinate = maximumWorldCoordinate;
            CoordinateInputVerificationAttempts = coordinateInputVerificationAttempts;
            CoordinateRollbackTimeoutSeconds = coordinateRollbackTimeoutSeconds;
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
        public int HomePinAcquisitionAttempts { get; }
        public bool RequireVerifiedSameTerritory { get; }
        public int MinimumWorldCoordinate { get; }
        public int MaximumWorldCoordinate { get; }
        public int CoordinateInputVerificationAttempts { get; }
        public int CoordinateRollbackTimeoutSeconds { get; }
    }
}
