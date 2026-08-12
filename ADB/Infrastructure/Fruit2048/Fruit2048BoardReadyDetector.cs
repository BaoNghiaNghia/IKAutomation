using ADB_Tool_Automation_Post_FB.Core.Fruit2048;
using ADB_Tool_Automation_Post_FB.Core.Vision;
using System;
using System.Drawing;
using System.IO;

namespace ADB_Tool_Automation_Post_FB.Infrastructure.Fruit2048
{
    /// <summary>
    /// Detects only the stable wooden frame of the Fruit 2048 board.  This is
    /// intentionally separate from tile recognition and from the strict native
    /// template matcher used for the City and event-menu controls.
    /// </summary>
    public sealed class Fruit2048BoardReadyDetector
    {
        // These are local board-frame thresholds, not global template-matcher settings.
        private const double PrimaryThreshold = 0.86d;
        private const double SecondaryThreshold = 0.88d;
        private readonly Fruit2048TemplateCatalog templates;
        private readonly Fruit2048ScreenProfile profile;

        public Fruit2048BoardReadyDetector(Fruit2048TemplateCatalog templates,
            Fruit2048ScreenProfile profile)
        {
            this.templates = templates ?? throw new ArgumentNullException(nameof(templates));
            this.profile = profile ?? throw new ArgumentNullException(nameof(profile));
        }

        public Fruit2048BoardReadyDetection Detect(CapturedFrame frame)
        {
            if (frame == null) throw new ArgumentNullException(nameof(frame));
            ImageRegion primaryRoi = profile.Scale(profile.BoardAnchorRegion, frame.Width, frame.Height);
            Fruit2048BoardReadyAnchorMatch primary = Match(frame.Bitmap,
                Fruit2048TemplateCatalog.NavigationBoardAnchor, primaryRoi, PrimaryThreshold);
            if (primary.Found)
            {
                return new Fruit2048BoardReadyDetection
                {
                    IsBoardReady = true,
                    Primary = primary,
                    BoardBoundsHint = profile.Scale(profile.BoardRegion, frame.Width, frame.Height),
                    AcceptedAnchor = primary,
                    Reason = "PrimaryBoardFrameMatched"
                };
            }

            ImageRegion secondaryRoi = profile.Scale(profile.BoardSecondaryAnchorRegion, frame.Width, frame.Height);
            Fruit2048BoardReadyAnchorMatch secondary = Match(frame.Bitmap,
                Fruit2048TemplateCatalog.NavigationBoardSecondaryAnchor, secondaryRoi, SecondaryThreshold);
            if (secondary.Found)
            {
                return new Fruit2048BoardReadyDetection
                {
                    IsBoardReady = true,
                    Primary = primary,
                    Secondary = secondary,
                    BoardBoundsHint = profile.Scale(profile.BoardRegion, frame.Width, frame.Height),
                    AcceptedAnchor = secondary,
                    Reason = "SecondaryBoardFrameMatched"
                };
            }

            return new Fruit2048BoardReadyDetection
            {
                Primary = primary,
                Secondary = secondary,
                BoardBoundsHint = profile.Scale(profile.BoardRegion, frame.Width, frame.Height),
                CaptureUnavailable = !primary.TemplateAvailable && !secondary.TemplateAvailable,
                Reason = !primary.TemplateAvailable && !secondary.TemplateAvailable
                    ? "NavigationTemplateInvalid:BoardFrameTemplateUnavailable"
                    : "BoardFrameNotDetected"
            };
        }

        private Fruit2048BoardReadyAnchorMatch Match(Bitmap source, string anchor,
            ImageRegion roi, double threshold)
        {
            var result = new Fruit2048BoardReadyAnchorMatch
            {
                Anchor = anchor,
                TemplatePath = templates.GetTemplatePath(anchor),
                SearchRoi = roi,
                Threshold = threshold
            };
            byte[] bytes;
            if (!templates.TryGet(anchor, out bytes))
            {
                result.FailureReason = "TemplateFileMissingOrDecodeFailed";
                return result;
            }
            try
            {
                using (var stream = new MemoryStream(bytes, false))
                using (var template = new Bitmap(stream))
                {
                    result.TemplateAvailable = template.Width > 0 && template.Height > 0;
                    if (!result.TemplateAvailable)
                    {
                        result.FailureReason = "TemplateEmpty";
                        return result;
                    }
                    if (template.Width > roi.Width || template.Height > roi.Height)
                    {
                        result.FailureReason = "TemplateLargerThanSearchRoi";
                        return result;
                    }
                    return FindBestNormalizedGrayscale(source, template, result);
                }
            }
            catch (Exception)
            {
                result.TemplateAvailable = false;
                result.FailureReason = "TemplateDecodeFailed";
                return result;
            }
        }

        /// <summary>Small local normalized grayscale matcher for board-frame pixels only.</summary>
        private static Fruit2048BoardReadyAnchorMatch FindBestNormalizedGrayscale(
            Bitmap source, Bitmap template, Fruit2048BoardReadyAnchorMatch result)
        {
            ImageRegion roi = result.SearchRoi;
            int templateWidth = template.Width;
            int templateHeight = template.Height;
            PixelSample[] templatePixels = ReadSamples(template, templateWidth, templateHeight);
            double templateMean = Mean(templatePixels);
            double templateMagnitude = Magnitude(templatePixels, templateMean);
            if (templateMagnitude <= 0.00001d)
            {
                result.FailureReason = "TemplateHasNoVisualVariance";
                return result;
            }

            double bestScore = double.NegativeInfinity;
            int bestX = 0;
            int bestY = 0;
            for (int y = roi.Y; y <= roi.Y + roi.Height - templateHeight; y++)
            {
                for (int x = roi.X; x <= roi.X + roi.Width - templateWidth; x++)
                {
                    double candidateMean = Mean(source, x, y, templatePixels);
                    double candidateMagnitude = Magnitude(source, x, y, templatePixels, candidateMean);
                    if (candidateMagnitude <= 0.00001d) continue;
                    double dot = 0d;
                    for (int index = 0; index < templatePixels.Length; index++)
                    {
                        PixelSample sample = templatePixels[index];
                        double candidate = Luminance(source.GetPixel(x + sample.X, y + sample.Y));
                        dot += (sample.Value - templateMean) * (candidate - candidateMean);
                    }
                    double score = dot / (templateMagnitude * candidateMagnitude);
                    if (score > bestScore)
                    {
                        bestScore = score;
                        bestX = x;
                        bestY = y;
                    }
                }
            }
            result.Score = double.IsNegativeInfinity(bestScore) ? (double?)null : bestScore;
            if (result.Score.HasValue)
                result.MatchedBounds = new ImageRegion(bestX, bestY, templateWidth, templateHeight);
            else result.FailureReason = "MatcherExecutionFailed";
            return result;
        }

        private static PixelSample[] ReadSamples(Bitmap bitmap, int width, int height)
        {
            const int samplingStep = 2;
            var values = new PixelSample[((width + samplingStep - 1) / samplingStep)
                * ((height + samplingStep - 1) / samplingStep)];
            int index = 0;
            for (int row = 0; row < height; row += samplingStep)
            for (int column = 0; column < width; column += samplingStep)
            {
                Color color = bitmap.GetPixel(column, row);
                values[index++] = new PixelSample(column, row, Luminance(color));
            }
            return values;
        }

        private static double Mean(PixelSample[] values)
        {
            double total = 0d;
            for (int index = 0; index < values.Length; index++) total += values[index].Value;
            return total / values.Length;
        }

        private static double Mean(Bitmap bitmap, int x, int y, PixelSample[] samples)
        {
            double total = 0d;
            for (int index = 0; index < samples.Length; index++)
            {
                PixelSample sample = samples[index];
                total += Luminance(bitmap.GetPixel(x + sample.X, y + sample.Y));
            }
            return total / samples.Length;
        }

        private static double Magnitude(PixelSample[] values, double mean)
        {
            double total = 0d;
            for (int index = 0; index < values.Length; index++)
            {
                double value = values[index].Value - mean;
                total += value * value;
            }
            return Math.Sqrt(total);
        }

        private static double Magnitude(Bitmap bitmap, int x, int y, PixelSample[] samples, double mean)
        {
            double total = 0d;
            for (int index = 0; index < samples.Length; index++)
            {
                PixelSample sample = samples[index];
                double value = Luminance(bitmap.GetPixel(x + sample.X, y + sample.Y)) - mean;
                total += value * value;
            }
            return Math.Sqrt(total);
        }

        private static double Luminance(Color color) => (color.R * 0.299d) + (color.G * 0.587d) + (color.B * 0.114d);

        private struct PixelSample
        {
            public PixelSample(int x, int y, double value) { X = x; Y = y; Value = value; }
            public int X { get; }
            public int Y { get; }
            public double Value { get; }
        }
    }
}
