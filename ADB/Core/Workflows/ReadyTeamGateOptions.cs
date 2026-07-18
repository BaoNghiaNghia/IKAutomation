using System;

namespace ADB_Tool_Automation_Post_FB.Core.Workflows
{
    public sealed class ReadyTeamGateOptions
    {
        public ReadyTeamGateOptions(int checkIntervalMs, int maxWaitMs = 43200000)
        {
            if (checkIntervalMs < 1)
                throw new ArgumentOutOfRangeException(nameof(checkIntervalMs));
            if (maxWaitMs < 1)
                throw new ArgumentOutOfRangeException(nameof(maxWaitMs));
            CheckIntervalMs = checkIntervalMs;
            MaxWaitMs = maxWaitMs;
        }

        public int CheckIntervalMs { get; }
        public int MaxWaitMs { get; }
    }
}
