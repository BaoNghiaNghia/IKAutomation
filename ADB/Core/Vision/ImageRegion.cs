using System;

namespace ADB_Tool_Automation_Post_FB.Core.Vision
{
    public struct ImageRegion
    {
        public ImageRegion(int x, int y, int width, int height)
        {
            if (x < 0)
                throw new ArgumentOutOfRangeException(nameof(x), "X cannot be negative.");
            if (y < 0)
                throw new ArgumentOutOfRangeException(nameof(y), "Y cannot be negative.");
            if (width <= 0)
                throw new ArgumentOutOfRangeException(nameof(width), "Width must be greater than zero.");
            if (height <= 0)
                throw new ArgumentOutOfRangeException(nameof(height), "Height must be greater than zero.");

            X = x;
            Y = y;
            Width = width;
            Height = height;
        }

        public int X { get; }

        public int Y { get; }

        public int Width { get; }

        public int Height { get; }
    }
}
