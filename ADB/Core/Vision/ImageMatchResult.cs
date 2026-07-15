namespace ADB_Tool_Automation_Post_FB.Core.Vision
{
    public sealed class ImageMatchResult
    {
        private ImageMatchResult(bool found, int x, int y, int width, int height, double? confidence)
        {
            Found = found;
            X = x;
            Y = y;
            Width = width;
            Height = height;
            Confidence = confidence;
        }

        public bool Found { get; }

        /// <summary>Left coordinate of the matched template on the original screenshot.</summary>
        public int X { get; }

        /// <summary>Top coordinate of the matched template on the original screenshot.</summary>
        public int Y { get; }

        public int Width { get; }

        public int Height { get; }

        public double? Confidence { get; }

        public int CenterX => X + (Width / 2);

        public int CenterY => Y + (Height / 2);

        public static ImageMatchResult NotFound()
        {
            return new ImageMatchResult(false, 0, 0, 0, 0, null);
        }

        public static ImageMatchResult FoundAt(int x, int y, int width, int height, double? confidence = null)
        {
            return new ImageMatchResult(true, x, y, width, height, confidence);
        }
    }
}
