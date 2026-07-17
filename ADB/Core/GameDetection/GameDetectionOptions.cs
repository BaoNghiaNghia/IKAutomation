using System;
using ADB_Tool_Automation_Post_FB.Core.Vision;

namespace ADB_Tool_Automation_Post_FB.Core.GameDetection
{
    public sealed class GameDetectionOptions
    {
        public GameDetectionOptions(
            int expectedWidth,
            int expectedHeight,
            bool requireExpectedResolution,
            bool saveUnknownScreenshots,
            string unknownScreenshotDirectory,
            ImageRegion? resourcePopupRegion = null,
            ImageRegion? teamSelectionRegion = null,
            ImageRegion? storageLimitDialogRegion = null,
            ImageRegion? resourcePopupActionRegion = null)
        {
            if (expectedWidth <= 0)
                throw new ArgumentOutOfRangeException(nameof(expectedWidth));
            if (expectedHeight <= 0)
                throw new ArgumentOutOfRangeException(nameof(expectedHeight));
            if (string.IsNullOrWhiteSpace(unknownScreenshotDirectory))
                throw new ArgumentException("Unknown screenshot directory is required.", nameof(unknownScreenshotDirectory));

            ExpectedWidth = expectedWidth;
            ExpectedHeight = expectedHeight;
            RequireExpectedResolution = requireExpectedResolution;
            SaveUnknownScreenshots = saveUnknownScreenshots;
            UnknownScreenshotDirectory = unknownScreenshotDirectory.Trim();
            ResourcePopupRegion = resourcePopupRegion ?? new ImageRegion(450, 230, 680, 310);
            ResourcePopupActionRegion = resourcePopupActionRegion ?? new ImageRegion(560, 480, 500, 210);
            TeamSelectionRegion = teamSelectionRegion ?? new ImageRegion(0, 0, 780, 720);
            StorageLimitDialogRegion = storageLimitDialogRegion ?? new ImageRegion(250, 100, 780, 520);
            if ((long)ResourcePopupRegion.X + ResourcePopupRegion.Width > expectedWidth
                || (long)ResourcePopupRegion.Y + ResourcePopupRegion.Height > expectedHeight)
                throw new ArgumentOutOfRangeException(nameof(resourcePopupRegion),
                    "Resource popup region must be inside the expected screenshot.");
            if ((long)ResourcePopupActionRegion.X + ResourcePopupActionRegion.Width > expectedWidth
                || (long)ResourcePopupActionRegion.Y + ResourcePopupActionRegion.Height > expectedHeight)
                throw new ArgumentOutOfRangeException(nameof(resourcePopupActionRegion),
                    "Resource popup action region must be inside the expected screenshot.");
            if ((long)TeamSelectionRegion.X + TeamSelectionRegion.Width > expectedWidth
                || (long)TeamSelectionRegion.Y + TeamSelectionRegion.Height > expectedHeight)
                throw new ArgumentOutOfRangeException(nameof(teamSelectionRegion),
                    "Team Selection region must be inside the expected screenshot.");
            if ((long)StorageLimitDialogRegion.X + StorageLimitDialogRegion.Width > expectedWidth
                || (long)StorageLimitDialogRegion.Y + StorageLimitDialogRegion.Height > expectedHeight)
                throw new ArgumentOutOfRangeException(nameof(storageLimitDialogRegion),
                    "Storage-limit dialog region must be inside the expected screenshot.");
        }

        public int ExpectedWidth { get; }
        public int ExpectedHeight { get; }
        public bool RequireExpectedResolution { get; }
        public bool SaveUnknownScreenshots { get; }
        public string UnknownScreenshotDirectory { get; }
        public ImageRegion ResourcePopupRegion { get; }
        public ImageRegion ResourcePopupActionRegion { get; }
        public ImageRegion TeamSelectionRegion { get; }
        public ImageRegion StorageLimitDialogRegion { get; }
    }
}
