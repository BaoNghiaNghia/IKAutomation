using System;

namespace ADB_Tool_Automation_Post_FB.Core.Workflows
{
    public sealed class ReadyTeamGateOptions
    {
        public ReadyTeamGateOptions(int checkIntervalMs, int maxWaitMs = 43200000,
            int noReadyConfirmations = 3, int postDispatchRecheckDelayMs = 750)
        {
            if (checkIntervalMs < 1)
                throw new ArgumentOutOfRangeException(nameof(checkIntervalMs));
            if (maxWaitMs < 1)
                throw new ArgumentOutOfRangeException(nameof(maxWaitMs));
            if (noReadyConfirmations < 1 || noReadyConfirmations > 10)
                throw new ArgumentOutOfRangeException(nameof(noReadyConfirmations));
            if (postDispatchRecheckDelayMs < 1 || postDispatchRecheckDelayMs > 30000)
                throw new ArgumentOutOfRangeException(nameof(postDispatchRecheckDelayMs));
            CheckIntervalMs = checkIntervalMs;
            MaxWaitMs = maxWaitMs;
            NoReadyConfirmations = noReadyConfirmations;
            PostDispatchRecheckDelayMs = postDispatchRecheckDelayMs;
        }

        public int CheckIntervalMs { get; }
        public int MaxWaitMs { get; }
        public int NoReadyConfirmations { get; }
        public int PostDispatchRecheckDelayMs { get; }
    }
}
