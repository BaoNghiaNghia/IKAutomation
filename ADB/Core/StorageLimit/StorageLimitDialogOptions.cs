using System;

namespace ADB_Tool_Automation_Post_FB.Core.StorageLimit
{
    public sealed class StorageLimitDialogOptions
    {
        public StorageLimitPolicy Policy { get; set; } = StorageLimitPolicy.ConfirmAndSwitchResource;
        public int PollIntervalMs { get; set; } = 200;
        public int TransitionTimeoutSeconds { get; set; } = 8;
        public int MaxActionAttempts { get; set; } = 2;
        public int ActionRetryDelayMs { get; set; } = 500;

        public void Validate()
        {
            if (PollIntervalMs < 50 || PollIntervalMs > 5000) throw new ArgumentOutOfRangeException(nameof(PollIntervalMs));
            if (TransitionTimeoutSeconds < 1 || TransitionTimeoutSeconds > 60) throw new ArgumentOutOfRangeException(nameof(TransitionTimeoutSeconds));
            if (MaxActionAttempts < 1 || MaxActionAttempts > 3) throw new ArgumentOutOfRangeException(nameof(MaxActionAttempts));
            if (ActionRetryDelayMs < 0 || ActionRetryDelayMs > 5000) throw new ArgumentOutOfRangeException(nameof(ActionRetryDelayMs));
        }
    }
}
