using System;

namespace ADB_Tool_Automation_Post_FB.Core.Navigation
{
    public sealed class WorldMapNavigationOptions
    {
        public WorldMapNavigationOptions(int statePollIntervalMs, int stateTransitionTimeoutSeconds, int maxOpenSearchAttempts)
        {
            if (statePollIntervalMs <= 0) throw new ArgumentOutOfRangeException(nameof(statePollIntervalMs));
            if (stateTransitionTimeoutSeconds <= 0) throw new ArgumentOutOfRangeException(nameof(stateTransitionTimeoutSeconds));
            if (maxOpenSearchAttempts < 1 || maxOpenSearchAttempts > 3) throw new ArgumentOutOfRangeException(nameof(maxOpenSearchAttempts));
            StatePollIntervalMs = statePollIntervalMs;
            StateTransitionTimeoutSeconds = stateTransitionTimeoutSeconds;
            MaxOpenSearchAttempts = maxOpenSearchAttempts;
        }

        public int StatePollIntervalMs { get; }
        public int StateTransitionTimeoutSeconds { get; }
        public int MaxOpenSearchAttempts { get; }
    }
}
