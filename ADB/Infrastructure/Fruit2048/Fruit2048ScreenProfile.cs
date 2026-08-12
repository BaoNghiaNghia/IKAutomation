using ADB_Tool_Automation_Post_FB.Core.Vision;
using System;

namespace ADB_Tool_Automation_Post_FB.Infrastructure.Fruit2048
{
    public sealed class Fruit2048ScreenProfile
    {
        public const int ReferenceWidth = 1280;
        public const int ReferenceHeight = 720;

        public Fruit2048ScreenProfile()
            // Measured from the real 1280x720 Fruit Festival board. This is the
            // 4x4 cell grid, not the decorative outer frame.
            : this(new ImageRegion(386, 139, 524, 536),
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
        // The real board-frame template begins around (368,122), outside the
        // cell grid. Keep its focused search region separate from BoardRegion
        // so anchor detection never distorts 4x4 cell and swipe geometry.
        public ImageRegion BoardAnchorRegion => new ImageRegion(320, 85, 170, 160);
        // The secondary board-frame segment stays in the same focused upper
        // board area.  It is only a fallback when the primary corner is
        // partially softened by the renderer.
        public ImageRegion BoardSecondaryAnchorRegion => new ImageRegion(320, 85, 170, 160);
        public ImageRegion TitleRegion { get; }
        public ImageRegion RefreshRegion { get; }
        // The real City reference places the festival character at about
        // (858,158).  Keep this focused around the event icon rather than
        // matching the complete City screen.
        public ImageRegion CityFestivalEntryRegion => new ImageRegion(790, 110, 200, 190);
        // The Fruit 2048 card is the third item in the event side rail.  Its 2048
        // glyph is around (40,415) in the real 1280x720 screen.  Searching the
        // lower card instead clicked the unrelated "Nguyên Năng Vọng Rõ" item.
        public ImageRegion FruitFestivalTabRegion => new ImageRegion(20, 365, 120, 140);

        /// <summary>
        /// Navigation match coordinates are absolute screenshot coordinates.
        /// The matcher adds an ROI offset before returning them, so validation and
        /// taps must use these values directly rather than applying the ROI offset
        /// a second time. Keeping this helper on primitive bounds also lets the
        /// geometry profile remain independent of a particular matcher result type.
        /// </summary>
        public static bool IsAbsoluteBoundsInsideRegion(int x, int y, int width, int height, ImageRegion region)
        {
            if (width <= 0 || height <= 0)
                return false;
            int centerX = x + (width / 2);
            int centerY = y + (height / 2);
            return centerX >= region.X
                && centerX < region.X + region.Width
                && centerY >= region.Y
                && centerY < region.Y + region.Height;
        }

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

        /// <summary>
        /// Returns the drawable part of a board cell. Static Empty/Tier1 seeds
        /// were cropped from this inner area, excluding the thick wood frame and
        /// the separator on the right/bottom of each cell.
        /// </summary>
        public static ImageRegion GetRecognitionCellRegion(ImageRegion board, int row, int column)
        {
            ImageRegion cell = GetCellRegion(board, row, column);
            int leftInset = Math.Max(1, (int)Math.Round(cell.Width * 10.0 / 131.0));
            int rightInset = Math.Max(1, (int)Math.Round(cell.Width * 23.0 / 131.0));
            int topInset = Math.Max(1, (int)Math.Round(cell.Height * 9.0 / 134.0));
            int bottomInset = Math.Max(1, (int)Math.Round(cell.Height * 26.0 / 134.0));
            return new ImageRegion(cell.X + leftInset, cell.Y + topInset,
                Math.Max(1, cell.Width - leftInset - rightInset),
                Math.Max(1, cell.Height - topInset - bottomInset));
        }

        /// <summary>
        /// The numeric tier badge is drawn near the lower-middle portion of the
        /// normalized fruit visual. Keeping this relative to the same inner
        /// cell crop makes calibration and runtime recognition use identical
        /// geometry at every supported resolution.
        /// </summary>
        public static ImageRegion GetTierBadgeRegion(ImageRegion recognitionCell)
        {
            int x = recognitionCell.X + (int)Math.Round(recognitionCell.Width * 0.34);
            int y = recognitionCell.Y + (int)Math.Round(recognitionCell.Height * 0.52);
            int right = recognitionCell.X + (int)Math.Round(recognitionCell.Width * 0.72);
            int bottom = recognitionCell.Y + (int)Math.Round(recognitionCell.Height * 0.97);
            return new ImageRegion(x, y, Math.Max(1, right - x), Math.Max(1, bottom - y));
        }
    }
}
