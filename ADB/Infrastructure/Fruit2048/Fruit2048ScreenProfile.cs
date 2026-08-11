using ADB_Tool_Automation_Post_FB.Core.Vision;
using System;

namespace ADB_Tool_Automation_Post_FB.Infrastructure.Fruit2048
{
    public sealed class Fruit2048ScreenProfile
    {
        public const int ReferenceWidth = 1280;
        public const int ReferenceHeight = 720;

        public Fruit2048ScreenProfile()
            : this(new ImageRegion(390, 110, 500, 500),
                new ImageRegion(300, 20, 680, 150),
                new ImageRegion(930, 500, 300, 180))
        {
        }

        public Fruit2048ScreenProfile(ImageRegion boardRegion, ImageRegion titleRegion,
            ImageRegion refreshRegion)
        {
            BoardRegion = boardRegion;
            TitleRegion = titleRegion;
            RefreshRegion = refreshRegion;
        }

        public ImageRegion BoardRegion { get; }
        public ImageRegion TitleRegion { get; }
        public ImageRegion RefreshRegion { get; }
        public ImageRegion CityFestivalEntryRegion => new ImageRegion(790, 120, 200, 170);
        public ImageRegion FruitFestivalTabRegion => new ImageRegion(0, 330, 160, 230);

        public ImageRegion Scale(ImageRegion region, int width, int height)
        {
            if (width <= 0 || height <= 0) throw new ArgumentOutOfRangeException();
            int x = (int)Math.Round(region.X * width / (double)ReferenceWidth);
            int y = (int)Math.Round(region.Y * height / (double)ReferenceHeight);
            int right = (int)Math.Round((region.X + region.Width) * width / (double)ReferenceWidth);
            int bottom = (int)Math.Round((region.Y + region.Height) * height / (double)ReferenceHeight);
            x = Math.Max(0, Math.Min(width - 1, x));
            y = Math.Max(0, Math.Min(height - 1, y));
            right = Math.Max(x + 1, Math.Min(width, right));
            bottom = Math.Max(y + 1, Math.Min(height, bottom));
            return new ImageRegion(x, y, right - x, bottom - y);
        }

        public static ImageRegion GetCellRegion(ImageRegion board, int row, int column)
        {
            if (row < 0 || row >= 4) throw new ArgumentOutOfRangeException(nameof(row));
            if (column < 0 || column >= 4) throw new ArgumentOutOfRangeException(nameof(column));
            int left = board.X + ((board.Width * column) / 4);
            int top = board.Y + ((board.Height * row) / 4);
            int right = board.X + ((board.Width * (column + 1)) / 4);
            int bottom = board.Y + ((board.Height * (row + 1)) / 4);
            return new ImageRegion(left, top, right - left, bottom - top);
        }
    }
}
