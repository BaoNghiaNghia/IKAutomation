using System;

namespace ADB_Tool_Automation_Post_FB.Core.Workflows
{
    public sealed class ReadyTeamGateOptions
    {
        public ReadyTeamGateOptions(int checkIntervalMs)
        {
            if (checkIntervalMs < 1)
                throw new ArgumentOutOfRangeException(nameof(checkIntervalMs));
            CheckIntervalMs = checkIntervalMs;
        }

        public int CheckIntervalMs { get; }
    }
}
