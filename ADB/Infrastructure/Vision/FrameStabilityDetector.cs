using ADB_Tool_Automation_Post_FB.Core.Vision;
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;

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
            using (var previousFrame = new CapturedFrame(
                Decode(previousPng, nameof(previousPng)), DateTimeOffset.UtcNow))
            using (var currentFrame = new CapturedFrame(
                Decode(currentPng, nameof(currentPng)), DateTimeOffset.UtcNow))
                return Compare(previousFrame, currentFrame, region);
        }

        public FrameComparisonResult Compare(CapturedFrame previousFrame,
            CapturedFrame currentFrame, ImageRegion? region = null)
        {
            if (previousFrame == null) throw new ArgumentNullException(nameof(previousFrame));
            if (currentFrame == null) throw new ArgumentNullException(nameof(currentFrame));
            Bitmap previous = previousFrame.Bitmap;
            Bitmap current = currentFrame.Bitmap;
            {
                if (previous.Width != current.Width || previous.Height != current.Height)
                    throw new ArgumentException("Frames must have the same dimensions.");

                ImageRegion target = region ?? new ImageRegion(0, 0, current.Width, current.Height);
                ValidateRegion(target, current.Width, current.Height);
                BitmapData previousData = null;
                BitmapData currentData = null;
                try
                {
                    previousData = previous.LockBits(new Rectangle(0, 0, previous.Width,
                        previous.Height), ImageLockMode.ReadOnly, previous.PixelFormat);
                    currentData = current.LockBits(new Rectangle(0, 0, current.Width,
                        current.Height), ImageLockMode.ReadOnly, current.PixelFormat);
                    int previousBytesPerPixel = Image.GetPixelFormatSize(previous.PixelFormat) / 8;
                    int currentBytesPerPixel = Image.GetPixelFormatSize(current.PixelFormat) / 8;
                    if (previousBytesPerPixel < 3 || currentBytesPerPixel < 3)
                        throw new InvalidOperationException("Frames must use an RGB pixel format.");

                    byte[] previousRow = new byte[Math.Abs(previousData.Stride)];
                    byte[] currentRow = new byte[Math.Abs(currentData.Stride)];
                    double total = 0;
                    int samples = 0;
                    for (int y = target.Y; y < target.Y + target.Height; y += SampleStep)
                    {
                        Marshal.Copy(RowPointer(previousData, y, previous.Height), previousRow,
                            0, previousRow.Length);
                        Marshal.Copy(RowPointer(currentData, y, current.Height), currentRow,
                            0, currentRow.Length);
                        for (int x = target.X; x < target.X + target.Width; x += SampleStep)
                        {
                            int previousOffset = x * previousBytesPerPixel;
                            int currentOffset = x * currentBytesPerPixel;
                            total += Math.Abs(previousRow[previousOffset] - currentRow[currentOffset])
                                + Math.Abs(previousRow[previousOffset + 1]
                                    - currentRow[currentOffset + 1])
                                + Math.Abs(previousRow[previousOffset + 2]
                                    - currentRow[currentOffset + 2]);
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
                finally
                {
                    if (previousData != null) previous.UnlockBits(previousData);
                    if (currentData != null) current.UnlockBits(currentData);
                }
            }
        }

        private static IntPtr RowPointer(BitmapData data, int y, int height)
        {
            int row = data.Stride < 0 ? height - 1 - y : y;
            return IntPtr.Add(data.Scan0, row * data.Stride);
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
