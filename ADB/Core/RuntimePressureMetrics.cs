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
        public int VisionQueueDepth { get; set; }
        public int ActiveVisionOperations { get; set; }
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
        private static int screenshotQueueDepth, activeScreenshots;
        private static int visionQueueDepth, activeVision;
        private static long sampleVersion;

        public static void ReportScreenshot(long waitMs, bool failed, int queued, int active)
        {
            lock (Sync)
            {
                Enqueue(ScreenshotWaits, Math.Max(0, waitMs));
                Enqueue(ScreenshotFailures, failed);
                screenshotQueueDepth = Math.Max(0, queued);
                activeScreenshots = Math.Max(0, active);
                sampleVersion++;
            }
        }

        public static void ReportVision(long waitMs, bool failed, int queued, int active)
        {
            lock (Sync)
            {
                Enqueue(VisionWaits, Math.Max(0, waitMs));
                Enqueue(VisionFailures, failed);
                visionQueueDepth = Math.Max(0, queued);
                activeVision = Math.Max(0, active);
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
                    VisionQueueDepth = visionQueueDepth,
                    ActiveVisionOperations = activeVision
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
