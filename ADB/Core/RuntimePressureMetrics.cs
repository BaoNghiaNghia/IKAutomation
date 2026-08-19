using System;
using System.Collections.Generic;
using System.Linq;

namespace ADB_Tool_Automation_Post_FB.Core.Diagnostics
{
    public sealed class RuntimePressureSnapshot
    {
        public long SampleVersion { get; set; }
        public long ScreenshotP95WaitMs { get; set; }
        public long VisionP95WaitMs { get; set; }
        public double ScreenshotFailureRate { get; set; }
        public double VisionFailureRate { get; set; }
        public double AverageGameplayLeaseWaitMs { get; set; }
        public int ScreenshotQueueDepth { get; set; }
        public int ActiveScreenshotOperations { get; set; }
        public int ScreenshotConcurrencyLimit { get; set; }
        public int PeakActiveScreenshotOperations { get; set; }
        public long MaxScreenshotQueueWaitMs { get; set; }
        public long ScreenshotOperationCount { get; set; }
        public long ScreenshotTotalQueueWaitMs { get; set; }
        public int VisionQueueDepth { get; set; }
        public int ActiveVisionOperations { get; set; }
        public int VisionConcurrencyLimit { get; set; }
        public int PeakActiveVisionOperations { get; set; }
        public long MaxVisionQueueWaitMs { get; set; }
        public long VisionOperationCount { get; set; }
        public long VisionTotalQueueWaitMs { get; set; }
    }

    public static class RuntimePressureMetrics
    {
        private const int Capacity = 128;
        private static readonly object Sync = new object();
        private static readonly Queue<long> ScreenshotWaits = new Queue<long>();
        private static readonly Queue<bool> ScreenshotFailures = new Queue<bool>();
        private static readonly Queue<long> VisionWaits = new Queue<long>();
        private static readonly Queue<bool> VisionFailures = new Queue<bool>();
        private static readonly Queue<long> GameplayWaits = new Queue<long>();
        private static int screenshotQueueDepth, activeScreenshots, screenshotLimit,
            peakActiveScreenshots, visionQueueDepth, activeVision, visionLimit,
            peakActiveVision;
        private static long maxScreenshotWaitMs, maxVisionWaitMs, screenshotOperationCount,
            visionOperationCount, screenshotTotalQueueWaitMs, visionTotalQueueWaitMs;
        private static long sampleVersion;

        public static void ReportScreenshot(long waitMs, bool failed, int queued, int active,
            int concurrencyLimit = 0)
        {
            lock (Sync)
            {
                Enqueue(ScreenshotWaits, Math.Max(0, waitMs));
                Enqueue(ScreenshotFailures, failed);
                screenshotQueueDepth = Math.Max(0, queued);
                activeScreenshots = Math.Max(0, active);
                if (concurrencyLimit > 0) screenshotLimit = concurrencyLimit;
                peakActiveScreenshots = Math.Max(peakActiveScreenshots, activeScreenshots);
                maxScreenshotWaitMs = Math.Max(maxScreenshotWaitMs, Math.Max(0, waitMs));
                screenshotOperationCount++;
                screenshotTotalQueueWaitMs += Math.Max(0, waitMs);
                sampleVersion++;
            }
        }

        public static void ReportVision(long waitMs, bool failed, int queued, int active,
            int concurrencyLimit = 0)
        {
            lock (Sync)
            {
                Enqueue(VisionWaits, Math.Max(0, waitMs));
                Enqueue(VisionFailures, failed);
                visionQueueDepth = Math.Max(0, queued);
                activeVision = Math.Max(0, active);
                if (concurrencyLimit > 0) visionLimit = concurrencyLimit;
                peakActiveVision = Math.Max(peakActiveVision, activeVision);
                maxVisionWaitMs = Math.Max(maxVisionWaitMs, Math.Max(0, waitMs));
                visionOperationCount++;
                visionTotalQueueWaitMs += Math.Max(0, waitMs);
                sampleVersion++;
            }
        }

        public static void ReportGameplayLeaseWait(long waitMs)
        {
            lock (Sync)
            {
                Enqueue(GameplayWaits, Math.Max(0, waitMs));
                sampleVersion++;
            }
        }

        public static RuntimePressureSnapshot GetSnapshot()
        {
            lock (Sync)
            {
                return new RuntimePressureSnapshot
                {
                    SampleVersion = sampleVersion,
                    ScreenshotP95WaitMs = Percentile95(ScreenshotWaits),
                    VisionP95WaitMs = Percentile95(VisionWaits),
                    ScreenshotFailureRate = FailureRate(ScreenshotFailures),
                    VisionFailureRate = FailureRate(VisionFailures),
                    AverageGameplayLeaseWaitMs = GameplayWaits.Count == 0 ? 0 : GameplayWaits.Average(),
                    ScreenshotQueueDepth = screenshotQueueDepth,
                    ActiveScreenshotOperations = activeScreenshots,
                    ScreenshotConcurrencyLimit = screenshotLimit,
                    PeakActiveScreenshotOperations = peakActiveScreenshots,
                    MaxScreenshotQueueWaitMs = maxScreenshotWaitMs,
                    ScreenshotOperationCount = screenshotOperationCount,
                    ScreenshotTotalQueueWaitMs = screenshotTotalQueueWaitMs,
                    VisionQueueDepth = visionQueueDepth,
                    ActiveVisionOperations = activeVision,
                    VisionConcurrencyLimit = visionLimit,
                    PeakActiveVisionOperations = peakActiveVision,
                    MaxVisionQueueWaitMs = maxVisionWaitMs,
                    VisionOperationCount = visionOperationCount,
                    VisionTotalQueueWaitMs = visionTotalQueueWaitMs
                };
            }
        }

        public static void Reset()
        {
            lock (Sync)
            {
                ScreenshotWaits.Clear(); ScreenshotFailures.Clear();
                VisionWaits.Clear(); VisionFailures.Clear(); GameplayWaits.Clear();
                screenshotQueueDepth = activeScreenshots = visionQueueDepth = activeVision = 0;
                screenshotLimit = visionLimit = peakActiveScreenshots = peakActiveVision = 0;
                maxScreenshotWaitMs = maxVisionWaitMs = screenshotOperationCount
                    = visionOperationCount = screenshotTotalQueueWaitMs = visionTotalQueueWaitMs = 0;
                sampleVersion = 0;
            }
        }

        private static void Enqueue<T>(Queue<T> values, T value)
        {
            values.Enqueue(value);
            while (values.Count > Capacity) values.Dequeue();
        }

        private static long Percentile95(IEnumerable<long> values)
        {
            long[] sorted = values.OrderBy(value => value).ToArray();
            if (sorted.Length == 0) return 0;
            int index = (int)Math.Ceiling(sorted.Length * 0.95d) - 1;
            return sorted[Math.Max(0, Math.Min(sorted.Length - 1, index))];
        }

        private static double FailureRate(IEnumerable<bool> values)
        {
            bool[] window = values.ToArray();
            return window.Length == 0 ? 0 : window.Count(value => value) / (double)window.Length;
        }
    }
}
