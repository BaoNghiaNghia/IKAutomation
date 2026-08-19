using System;

namespace IK_Auto_ADB.Core.Workflows
{
    public sealed class ReadyTeamGateRunOptions
    {
        public ReadyTeamGateRunOptions(int checkIntervalMs, int maxWaitMs)
        {
            if (checkIntervalMs < 1) throw new ArgumentOutOfRangeException(nameof(checkIntervalMs));
            if (maxWaitMs < checkIntervalMs) throw new ArgumentOutOfRangeException(nameof(maxWaitMs));
            CheckIntervalMs = checkIntervalMs;
            MaxWaitMs = maxWaitMs;
        }

        public int CheckIntervalMs { get; }
        public int MaxWaitMs { get; }
    }
}
