using ADB_Tool_Automation_Post_FB.Core.MarchDispatch;
using ADB_Tool_Automation_Post_FB.Core.Vision;
using System;
using System.Drawing;
using System.IO;

namespace ADB_Tool_Automation_Post_FB.Infrastructure.MarchDispatch
{
    public sealed class TeamMarchTimerDetector : ITeamMarchTimerDetector
    {
        private const double BrightPixelLuminance = 170d;
        private readonly DispatchSelectedTeamOptions options;

        public TeamMarchTimerDetector(DispatchSelectedTeamOptions options)
        {
            this.options = options ?? throw new ArgumentNullException(nameof(options));
        }

        public TeamMarchTimerDetectionResult DetectContent(byte[] screenshotPng,
            ImageRegion timerRegion)
        {
            using (Bitmap screenshot = Decode(screenshotPng, nameof(screenshotPng)))
            {
                ValidateRegion(timerRegion, screenshot.Width, screenshot.Height);
                bool[] foreground = Foreground(screenshot, timerRegion);
                double ratio = Ratio(foreground);
                return new TeamMarchTimerDetectionResult
                {
                    ContentDetected = IsContent(ratio),
                    ForegroundRatio = ratio,
                    TimerRegion = timerRegion
                };
            }
        }

        public TeamMarchTimerProgressionResult Compare(byte[] previousScreenshotPng,
            byte[] currentScreenshotPng, ImageRegion timerRegion)
        {
            using (Bitmap previous = Decode(previousScreenshotPng, nameof(previousScreenshotPng)))
            using (Bitmap current = Decode(currentScreenshotPng, nameof(currentScreenshotPng)))
            {
                if (previous.Width != current.Width || previous.Height != current.Height)
                    throw new ArgumentException("Timer frames must have the same dimensions.");
                ValidateRegion(timerRegion, current.Width, current.Height);
                bool[] before = Foreground(previous, timerRegion);
                bool[] after = Foreground(current, timerRegion);
                double beforeRatio = Ratio(before);
                double afterRatio = Ratio(after);
                int changed = 0;
                for (int index = 0; index < before.Length; index++)
                    if (before[index] != after[index]) changed++;
                double difference = before.Length == 0 ? 0d : changed / (double)before.Length;
                bool beforeContent = IsContent(beforeRatio);
                bool afterContent = IsContent(afterRatio);
                return new TeamMarchTimerProgressionResult
                {
                    PreviousContentDetected = beforeContent,
                    CurrentContentDetected = afterContent,
                    ProgressionDetected = beforeContent && afterContent
                        && difference >= options.MinimumTimerDifferenceRatio
                        && difference <= options.MaximumTimerDifferenceRatio,
                    PreviousForegroundRatio = beforeRatio,
                    CurrentForegroundRatio = afterRatio,
                    DifferenceRatio = difference,
                    TimerRegion = timerRegion
                };
            }
        }

        private bool IsContent(double ratio) => ratio >= options.MinimumTimerForegroundRatio
            && ratio <= options.MaximumTimerForegroundRatio;

        private static bool[] Foreground(Bitmap image, ImageRegion region)
        {
            var result = new bool[region.Width * region.Height];
            int index = 0;
            for (int y = region.Y; y < region.Y + region.Height; y++)
            {
                for (int x = region.X; x < region.X + region.Width; x++)
                {
                    Color pixel = image.GetPixel(x, y);
                    double luminance = (0.2126d * pixel.R) + (0.7152d * pixel.G)
                        + (0.0722d * pixel.B);
                    result[index++] = luminance >= BrightPixelLuminance;
                }
            }
            return result;
        }

        private static double Ratio(bool[] values)
        {
            int count = 0;
            foreach (bool value in values) if (value) count++;
            return values.Length == 0 ? 0d : count / (double)values.Length;
        }

        private static Bitmap Decode(byte[] png, string parameterName)
        {
            if (png == null) throw new ArgumentNullException(parameterName);
            if (png.Length == 0) throw new ArgumentException("PNG data is required.", parameterName);
            using (var stream = new MemoryStream(png, writable: false))
            using (var source = new Bitmap(stream))
                return new Bitmap(source);
        }

        private static void ValidateRegion(ImageRegion region, int width, int height)
        {
            if ((long)region.X + region.Width > width || (long)region.Y + region.Height > height)
                throw new ArgumentOutOfRangeException(nameof(region),
                    "Timer region exceeds the screenshot bounds.");
        }
    }
}
