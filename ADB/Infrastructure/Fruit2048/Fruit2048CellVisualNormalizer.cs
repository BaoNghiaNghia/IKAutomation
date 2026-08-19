using IK_Auto_ADB.Core.Fruit2048;
using IK_Auto_ADB.Core.Vision;
using System;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace IK_Auto_ADB.Infrastructure.Fruit2048
{
    /// <summary>Canonicalizes both packaged seeds and live inner-cell crops before fingerprinting.</summary>
    public static class Fruit2048CellVisualNormalizer
    {
        public const int CanonicalWidth = 98;
        public const int CanonicalHeight = 99;

        public static FruitTileVisualFingerprint CreateFingerprint(Bitmap source, ImageRegion region)
        {
            using (Bitmap normalized = Normalize(source, region))
                return FruitTileFingerprint.Create(normalized,
                    new ImageRegion(0, 0, normalized.Width, normalized.Height));
        }

        public static Bitmap Normalize(Bitmap source, ImageRegion region)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            int left = Math.Max(0, region.X), top = Math.Max(0, region.Y);
            int right = Math.Min(source.Width, region.X + region.Width);
            int bottom = Math.Min(source.Height, region.Y + region.Height);
            if (right <= left || bottom <= top) throw new ArgumentOutOfRangeException(nameof(region));
            var normalized = new Bitmap(CanonicalWidth, CanonicalHeight);
            using (Graphics graphics = Graphics.FromImage(normalized))
            {
                graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
                graphics.DrawImage(source, new Rectangle(0, 0, CanonicalWidth, CanonicalHeight),
                    new Rectangle(left, top, right - left, bottom - top), GraphicsUnit.Pixel);
            }
            return normalized;
        }
    }
}
