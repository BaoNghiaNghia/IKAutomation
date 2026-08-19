using IK_Auto_ADB.Core.Fruit2048;
using IK_Auto_ADB.Core.Vision;
using System;
using System.Drawing;
using System.Globalization;

namespace IK_Auto_ADB.Infrastructure.Fruit2048
{
    public static class FruitTileFingerprint
    {
        public static FruitTileVisualFingerprint Create(Bitmap bitmap, ImageRegion region)
        {
            if (bitmap == null) throw new ArgumentNullException(nameof(bitmap));
            int left = Math.Max(0, region.X), top = Math.Max(0, region.Y);
            int right = Math.Min(bitmap.Width, region.X + region.Width);
            int bottom = Math.Min(bitmap.Height, region.Y + region.Height);
            if (right <= left || bottom <= top) throw new ArgumentOutOfRangeException(nameof(region));
            var luminance = new byte[64];
            long red = 0, green = 0, blue = 0;
            for (int y = 0; y < 8; y++)
            for (int x = 0; x < 8; x++)
            {
                int sampleX = left + Math.Min(right - left - 1,
                    (int)Math.Round((x + 0.5) * (right - left) / 8.0));
                int sampleY = top + Math.Min(bottom - top - 1,
                    (int)Math.Round((y + 0.5) * (bottom - top) / 8.0));
                Color color = bitmap.GetPixel(sampleX, sampleY);
                int index = (y * 8) + x;
                luminance[index] = (byte)((color.R * 299 + color.G * 587 + color.B * 114) / 1000);
                red += color.R; green += color.G; blue += color.B;
            }
            double average = 0;
            for (int index = 0; index < luminance.Length; index++) average += luminance[index];
            average /= luminance.Length;
            ulong averageHash = 0, edgeHash = 0;
            for (int index = 0; index < luminance.Length; index++)
            {
                if (luminance[index] >= average) averageHash |= 1UL << index;
                int x = index % 8;
                if (x < 7 && luminance[index] < luminance[index + 1]) edgeHash |= 1UL << index;
            }
            return new FruitTileVisualFingerprint
            {
                AverageHash = averageHash.ToString("X16", CultureInfo.InvariantCulture),
                EdgeHash = edgeHash.ToString("X16", CultureInfo.InvariantCulture),
                MeanRed = (int)(red / 64), MeanGreen = (int)(green / 64), MeanBlue = (int)(blue / 64)
            };
        }
    }
}
