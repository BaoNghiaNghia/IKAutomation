using ADB_Tool_Automation_Post_FB.Core.Vision;
using System;
using System.Drawing;
using System.IO;

namespace ADB_Tool_Automation_Post_FB.Infrastructure.Vision
{
    public sealed class FrameStabilityDetector : IFrameStabilityDetector
    {
        private const int SampleStep = 8;
        private readonly double stableThreshold;

        public FrameStabilityDetector(double stableThreshold)
        {
            if (stableThreshold < 0 || stableThreshold > 1)
                throw new ArgumentOutOfRangeException(nameof(stableThreshold));
            this.stableThreshold = stableThreshold;
        }

        public FrameComparisonResult Compare(byte[] previousPng, byte[] currentPng,
            ImageRegion? region = null)
        {
            using (Bitmap previous = Decode(previousPng, nameof(previousPng)))
            using (Bitmap current = Decode(currentPng, nameof(currentPng)))
            {
                if (previous.Width != current.Width || previous.Height != current.Height)
                    throw new ArgumentException("Frames must have the same dimensions.");

                ImageRegion target = region ?? new ImageRegion(0, 0, current.Width, current.Height);
                ValidateRegion(target, current.Width, current.Height);
                double total = 0;
                int samples = 0;
                for (int y = target.Y; y < target.Y + target.Height; y += SampleStep)
                {
                    for (int x = target.X; x < target.X + target.Width; x += SampleStep)
                    {
                        Color before = previous.GetPixel(x, y);
                        Color after = current.GetPixel(x, y);
                        total += Math.Abs(before.R - after.R)
                            + Math.Abs(before.G - after.G)
                            + Math.Abs(before.B - after.B);
                        samples++;
                    }
                }

                double ratio = samples == 0 ? 0 : total / (samples * 3d * 255d);
                return new FrameComparisonResult
                {
                    DifferenceRatio = ratio,
                    IsStable = ratio <= stableThreshold
                };
            }
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
                throw new ArgumentOutOfRangeException(nameof(region), "Comparison region exceeds the frame.");
        }
    }
}
