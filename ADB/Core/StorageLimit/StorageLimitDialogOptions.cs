using System;
using ADB_Tool_Automation_Post_FB.Core.Vision;

namespace ADB_Tool_Automation_Post_FB.Core.StorageLimit
{
    public sealed class StorageLimitDialogOptions
    {
        public StorageLimitPolicy Policy { get; set; } = StorageLimitPolicy.CancelAndSwitchResource;
        public int PollIntervalMs { get; set; } = 200;
        public int TransitionTimeoutSeconds { get; set; } = 8;
        public int MaxActionAttempts { get; set; } = 2;
        public int ActionRetryDelayMs { get; set; } = 500;
        public int MaxTransientUnknownFrames { get; set; } = 5;
        public int MaxBackAttempts { get; set; } = 1;
        public ImageRegion DialogRegion { get; set; } = new ImageRegion(250, 100, 780, 520);

        public int MaxCancelAttempts { get => MaxActionAttempts; set => MaxActionAttempts = value; }
        public int CancelRetryDelayMs { get => ActionRetryDelayMs; set => ActionRetryDelayMs = value; }

        public void Validate()
        {
            if (PollIntervalMs < 50 || PollIntervalMs > 5000) throw new ArgumentOutOfRangeException(nameof(PollIntervalMs));
            if (TransitionTimeoutSeconds < 1 || TransitionTimeoutSeconds > 60) throw new ArgumentOutOfRangeException(nameof(TransitionTimeoutSeconds));
            if (MaxActionAttempts < 1 || MaxActionAttempts > 3) throw new ArgumentOutOfRangeException(nameof(MaxActionAttempts));
            if (ActionRetryDelayMs < 0 || ActionRetryDelayMs > 5000) throw new ArgumentOutOfRangeException(nameof(ActionRetryDelayMs));
            if (MaxTransientUnknownFrames < 0) throw new ArgumentOutOfRangeException(nameof(MaxTransientUnknownFrames));
            if (MaxBackAttempts < 1 || MaxBackAttempts > 2) throw new ArgumentOutOfRangeException(nameof(MaxBackAttempts));
        }
    }
}
