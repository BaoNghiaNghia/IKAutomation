using System;
using System.IO;

namespace ADB_Tool_Automation_Post_FB.Core.MarchDispatch
{
    public sealed class DispatchSelectedTeamOptions
    {
        public DispatchSelectedTeamOptions(int pollIntervalMs, int transitionTimeoutSeconds,
            int maxActionTapAttempts, int actionTapRetryDelayMs,
            int maxTransientUnknownFrames, int requiredConsecutiveSuccessFrames,
            double teamRegionChangeThreshold, bool allowStructuralVerificationFallback,
            bool saveFailureScreenshots, string failureScreenshotDirectory)
        {
            if (pollIntervalMs <= 0) throw new ArgumentOutOfRangeException(nameof(pollIntervalMs));
            if (transitionTimeoutSeconds <= 0) throw new ArgumentOutOfRangeException(nameof(transitionTimeoutSeconds));
            if (maxActionTapAttempts < 1 || maxActionTapAttempts > 3) throw new ArgumentOutOfRangeException(nameof(maxActionTapAttempts));
            if (actionTapRetryDelayMs <= 0) throw new ArgumentOutOfRangeException(nameof(actionTapRetryDelayMs));
            if (maxTransientUnknownFrames < 0) throw new ArgumentOutOfRangeException(nameof(maxTransientUnknownFrames));
            if (requiredConsecutiveSuccessFrames < 1 || requiredConsecutiveSuccessFrames > 3) throw new ArgumentOutOfRangeException(nameof(requiredConsecutiveSuccessFrames));
            if (teamRegionChangeThreshold <= 0 || teamRegionChangeThreshold >= 1) throw new ArgumentOutOfRangeException(nameof(teamRegionChangeThreshold));
            ValidateRelativePath(failureScreenshotDirectory);
            PollIntervalMs = pollIntervalMs;
            TransitionTimeoutSeconds = transitionTimeoutSeconds;
            MaxActionTapAttempts = maxActionTapAttempts;
            ActionTapRetryDelayMs = actionTapRetryDelayMs;
            MaxTransientUnknownFrames = maxTransientUnknownFrames;
            RequiredConsecutiveSuccessFrames = requiredConsecutiveSuccessFrames;
            TeamRegionChangeThreshold = teamRegionChangeThreshold;
            AllowStructuralVerificationFallback = allowStructuralVerificationFallback;
            SaveFailureScreenshots = saveFailureScreenshots;
            FailureScreenshotDirectory = failureScreenshotDirectory.Trim();
        }

        public int PollIntervalMs { get; }
        public int TransitionTimeoutSeconds { get; }
        public int MaxActionTapAttempts { get; }
        public int ActionTapRetryDelayMs { get; }
        public int MaxTransientUnknownFrames { get; }
        public int RequiredConsecutiveSuccessFrames { get; }
        public double TeamRegionChangeThreshold { get; }
        public bool AllowStructuralVerificationFallback { get; }
        public bool SaveFailureScreenshots { get; }
        public string FailureScreenshotDirectory { get; }

        private static void ValidateRelativePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || Path.IsPathRooted(path))
                throw new ArgumentException("Diagnostic directory must be a relative path.", nameof(path));
            string[] parts = path.Replace('\\', '/').Split('/');
            foreach (string part in parts)
                if (part == "..") throw new ArgumentException("Diagnostic directory cannot contain path traversal.", nameof(path));
        }
    }
}
