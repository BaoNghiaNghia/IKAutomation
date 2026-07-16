using ADB_Tool_Automation_Post_FB.Core.Vision;
using System;

namespace ADB_Tool_Automation_Post_FB.Core.ResourceSearch
{
    public sealed class ResourceSearchExecutionOptions
    {
        public ResourceSearchExecutionOptions(int notFoundObservationWindowMs,
            int notFoundFastPollIntervalMs, int normalPollIntervalMs,
            int searchResultTimeoutSeconds, int maxSearchTapAttempts,
            int searchTapVerificationTimeoutSeconds, ImageRegion toastRegion,
            int maxToastAnchorVerticalDistancePx, double cameraMovementThreshold,
            double cameraStableThreshold, int requiredStableFrames,
            int maxTransientUnknownFrames, bool saveResultScreenshots,
            bool saveObservationBurst, int maxObservationBurstFrames,
            string resultScreenshotDirectory, string observationBurstDirectory,
            int expectedWidth, int expectedHeight, ImageRegion mapRegion)
        {
            if (notFoundObservationWindowMs <= 0) throw new ArgumentOutOfRangeException(nameof(notFoundObservationWindowMs));
            if (notFoundFastPollIntervalMs <= 0) throw new ArgumentOutOfRangeException(nameof(notFoundFastPollIntervalMs));
            if (normalPollIntervalMs <= 0) throw new ArgumentOutOfRangeException(nameof(normalPollIntervalMs));
            if (searchResultTimeoutSeconds <= 0) throw new ArgumentOutOfRangeException(nameof(searchResultTimeoutSeconds));
            if (maxSearchTapAttempts < 1 || maxSearchTapAttempts > 3) throw new ArgumentOutOfRangeException(nameof(maxSearchTapAttempts));
            if (searchTapVerificationTimeoutSeconds <= 0) throw new ArgumentOutOfRangeException(nameof(searchTapVerificationTimeoutSeconds));
            if (maxToastAnchorVerticalDistancePx <= 0) throw new ArgumentOutOfRangeException(nameof(maxToastAnchorVerticalDistancePx));
            if (cameraStableThreshold < 0) throw new ArgumentOutOfRangeException(nameof(cameraStableThreshold));
            if (cameraMovementThreshold <= cameraStableThreshold) throw new ArgumentOutOfRangeException(nameof(cameraMovementThreshold));
            if (requiredStableFrames <= 0) throw new ArgumentOutOfRangeException(nameof(requiredStableFrames));
            if (maxTransientUnknownFrames < 0) throw new ArgumentOutOfRangeException(nameof(maxTransientUnknownFrames));
            if (maxObservationBurstFrames < 1 || maxObservationBurstFrames > 30) throw new ArgumentOutOfRangeException(nameof(maxObservationBurstFrames));
            if (expectedWidth <= 0 || expectedHeight <= 0) throw new ArgumentOutOfRangeException(nameof(expectedWidth));
            ValidateInside(toastRegion, expectedWidth, expectedHeight, nameof(toastRegion));
            ValidateInside(mapRegion, expectedWidth, expectedHeight, nameof(mapRegion));
            if (string.IsNullOrWhiteSpace(resultScreenshotDirectory)) throw new ArgumentException("Result screenshot directory is required.", nameof(resultScreenshotDirectory));
            if (string.IsNullOrWhiteSpace(observationBurstDirectory)) throw new ArgumentException("Observation directory is required.", nameof(observationBurstDirectory));

            NotFoundObservationWindowMs = notFoundObservationWindowMs;
            NotFoundFastPollIntervalMs = notFoundFastPollIntervalMs;
            NormalPollIntervalMs = normalPollIntervalMs;
            SearchResultTimeoutSeconds = searchResultTimeoutSeconds;
            MaxSearchTapAttempts = maxSearchTapAttempts;
            SearchTapVerificationTimeoutSeconds = searchTapVerificationTimeoutSeconds;
            ToastRegion = toastRegion;
            MaxToastAnchorVerticalDistancePx = maxToastAnchorVerticalDistancePx;
            CameraMovementThreshold = cameraMovementThreshold;
            CameraStableThreshold = cameraStableThreshold;
            RequiredStableFrames = requiredStableFrames;
            MaxTransientUnknownFrames = maxTransientUnknownFrames;
            SaveResultScreenshots = saveResultScreenshots;
            SaveObservationBurst = saveObservationBurst;
            MaxObservationBurstFrames = maxObservationBurstFrames;
            ResultScreenshotDirectory = resultScreenshotDirectory;
            ObservationBurstDirectory = observationBurstDirectory;
            ExpectedWidth = expectedWidth;
            ExpectedHeight = expectedHeight;
            MapRegion = mapRegion;
        }

        public int NotFoundObservationWindowMs { get; }
        public int NotFoundFastPollIntervalMs { get; }
        public int NormalPollIntervalMs { get; }
        public int SearchResultTimeoutSeconds { get; }
        public int MaxSearchTapAttempts { get; }
        public int SearchTapVerificationTimeoutSeconds { get; }
        public ImageRegion ToastRegion { get; }
        public int MaxToastAnchorVerticalDistancePx { get; }
        public double CameraMovementThreshold { get; }
        public double CameraStableThreshold { get; }
        public int RequiredStableFrames { get; }
        public int MaxTransientUnknownFrames { get; }
        public bool SaveResultScreenshots { get; }
        public bool SaveObservationBurst { get; }
        public int MaxObservationBurstFrames { get; }
        public string ResultScreenshotDirectory { get; }
        public string ObservationBurstDirectory { get; }
        public int ExpectedWidth { get; }
        public int ExpectedHeight { get; }
        public ImageRegion MapRegion { get; }

        private static void ValidateInside(ImageRegion region, int width, int height, string name)
        {
            if ((long)region.X + region.Width > width || (long)region.Y + region.Height > height)
                throw new ArgumentOutOfRangeException(name, "Region must be inside the expected screenshot.");
        }
    }
}
