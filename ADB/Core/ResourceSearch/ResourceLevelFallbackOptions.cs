using ADB_Tool_Automation_Post_FB.Core.Vision;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace ADB_Tool_Automation_Post_FB.Core.ResourceSearch
{
    public sealed class ResourceLevelFallbackOptions
    {
        public ResourceLevelFallbackOptions(IReadOnlyList<int> levels, int attemptsPerLevel,
            int requiredConsecutiveToastClearFrames, int toastClearPollIntervalMs,
            int toastClearTimeoutSeconds, bool stopOnFirstLocated,
            bool waitForToastClearBetweenAttempts, bool saveExhaustedScreenshot,
            string screenshotDirectory, ImageRegion toastRegion,
            int maxToastAnchorVerticalDistancePx = 140)
        {
            if (levels == null || levels.Count == 0) throw new ArgumentException("Levels cannot be empty.", nameof(levels));
            if (levels.Distinct().Count() != levels.Count) throw new ArgumentException("Levels cannot contain duplicates.", nameof(levels));
            if (levels.Any(level => level != 5 && level != 6 && level != 7)) throw new ArgumentOutOfRangeException(nameof(levels));
            if (attemptsPerLevel < 1 || attemptsPerLevel > 3) throw new ArgumentOutOfRangeException(nameof(attemptsPerLevel));
            if (requiredConsecutiveToastClearFrames < 1 || requiredConsecutiveToastClearFrames > 5) throw new ArgumentOutOfRangeException(nameof(requiredConsecutiveToastClearFrames));
            if (toastClearPollIntervalMs <= 0) throw new ArgumentOutOfRangeException(nameof(toastClearPollIntervalMs));
            if (toastClearTimeoutSeconds <= 0) throw new ArgumentOutOfRangeException(nameof(toastClearTimeoutSeconds));
            if (string.IsNullOrWhiteSpace(screenshotDirectory)) throw new ArgumentException("ScreenshotDirectory is required.", nameof(screenshotDirectory));
            if (Path.IsPathRooted(screenshotDirectory) || screenshotDirectory.Split('/', '\\').Any(part => part == ".."))
                throw new ArgumentException("ScreenshotDirectory must be a safe relative path.", nameof(screenshotDirectory));
            if (toastRegion.Width <= 0 || toastRegion.Height <= 0) throw new ArgumentOutOfRangeException(nameof(toastRegion));
            if (maxToastAnchorVerticalDistancePx <= 0)
                throw new ArgumentOutOfRangeException(nameof(maxToastAnchorVerticalDistancePx));

            Levels = levels.ToArray(); AttemptsPerLevel = attemptsPerLevel;
            RequiredConsecutiveToastClearFrames = requiredConsecutiveToastClearFrames;
            ToastClearPollIntervalMs = toastClearPollIntervalMs;
            ToastClearTimeoutSeconds = toastClearTimeoutSeconds;
            StopOnFirstLocated = stopOnFirstLocated;
            WaitForToastClearBetweenAttempts = waitForToastClearBetweenAttempts;
            SaveExhaustedScreenshot = saveExhaustedScreenshot;
            ScreenshotDirectory = screenshotDirectory;
            ToastRegion = toastRegion;
            MaxToastAnchorVerticalDistancePx = maxToastAnchorVerticalDistancePx;
        }

        public IReadOnlyList<int> Levels { get; }
        public int AttemptsPerLevel { get; }
        public int RequiredConsecutiveToastClearFrames { get; }
        public int ToastClearPollIntervalMs { get; }
        public int ToastClearTimeoutSeconds { get; }
        public bool StopOnFirstLocated { get; }
        public bool WaitForToastClearBetweenAttempts { get; }
        public bool SaveExhaustedScreenshot { get; }
        public string ScreenshotDirectory { get; }
        public ImageRegion ToastRegion { get; }
        public int MaxToastAnchorVerticalDistancePx { get; }
    }
}
