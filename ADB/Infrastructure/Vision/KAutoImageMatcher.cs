using ADB_Tool_Automation_Post_FB.Core.Vision;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;

namespace ADB_Tool_Automation_Post_FB.Infrastructure.Vision
{
    public sealed class KAutoImageMatcher : IImageMatcher, IBatchImageMatcher
    {
        public IReadOnlyList<ImageMatchResult> FindMany(
            byte[] screenshotPng,
            IReadOnlyList<ImageMatchRequest> requests)
        {
            ValidateImageBytes(screenshotPng, nameof(screenshotPng));
            if (requests == null) throw new ArgumentNullException(nameof(requests));
            var results = new List<ImageMatchResult>(requests.Count);
            using (Bitmap screenshot = DecodeBitmap(screenshotPng, nameof(screenshotPng)))
            {
                foreach (ImageMatchRequest request in requests)
                {
                    if (request == null) throw new ArgumentException("A match request cannot be null.", nameof(requests));
                    results.Add(FindOnBitmap(screenshot, request.TemplatePng, request.SearchRegion));
                }
            }
            return results.AsReadOnly();
        }

        public ImageMatchResult Find(byte[] screenshotPng, byte[] templatePng, ImageRegion? searchRegion = null)
        {
            ValidateImageBytes(screenshotPng, nameof(screenshotPng));
            ValidateImageBytes(templatePng, nameof(templatePng));

            using (Bitmap screenshot = DecodeBitmap(screenshotPng, nameof(screenshotPng)))
                return FindOnBitmap(screenshot, templatePng, searchRegion);
        }

        private static ImageMatchResult FindOnBitmap(
            Bitmap screenshot, byte[] templatePng, ImageRegion? searchRegion)
        {
            ValidateImageBytes(templatePng, nameof(templatePng));
            using (Bitmap template = DecodeBitmap(templatePng, nameof(templatePng)))
            {
                int offsetX = 0;
                int offsetY = 0;
                Bitmap searchImage = null;

                try
                {
                    if (searchRegion.HasValue)
                    {
                        ImageRegion region = searchRegion.Value;
                        ValidateRegionBounds(region, screenshot.Width, screenshot.Height);

                        offsetX = region.X;
                        offsetY = region.Y;
                        searchImage = screenshot.Clone(
                            new Rectangle(region.X, region.Y, region.Width, region.Height),
                            screenshot.PixelFormat);
                    }
                    else
                    {
                        searchImage = (Bitmap)screenshot.Clone();
                    }

                    if (template.Width > searchImage.Width || template.Height > searchImage.Height)
                    {
                        throw new ArgumentException(
                            $"Template size {template.Width}x{template.Height} exceeds search image size " +
                            $"{searchImage.Width}x{searchImage.Height}.",
                            nameof(templatePng));
                    }

                    Point? topLeftPoint = KAutoHelper.ImageScanOpenCV.FindOutPoint(searchImage, template);
                    if (!topLeftPoint.HasValue)
                        return ImageMatchResult.NotFound();

                    int left = topLeftPoint.Value.X + offsetX;
                    int top = topLeftPoint.Value.Y + offsetY;

                    return ImageMatchResult.FoundAt(left, top, template.Width, template.Height, null);
                }
                finally
                {
                    searchImage?.Dispose();
                }
            }
        }

        private static Bitmap DecodeBitmap(byte[] imageBytes, string parameterName)
        {
            try
            {
                using (var stream = new MemoryStream(imageBytes, writable: false))
                using (var source = new Bitmap(stream))
                {
                    return new Bitmap(source);
                }
            }
            catch (Exception ex)
            {
                throw new ArgumentException("The supplied byte array is not a valid image.", parameterName, ex);
            }
        }

        private static void ValidateImageBytes(byte[] imageBytes, string parameterName)
        {
            if (imageBytes == null)
                throw new ArgumentNullException(parameterName);
            if (imageBytes.Length == 0)
                throw new ArgumentException("Image byte array cannot be empty.", parameterName);
        }

        private static void ValidateRegionBounds(ImageRegion region, int imageWidth, int imageHeight)
        {
            if ((long)region.X + region.Width > imageWidth ||
                (long)region.Y + region.Height > imageHeight)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(region),
                    $"Search region ({region.X}, {region.Y}, {region.Width}, {region.Height}) " +
                    $"exceeds screenshot size {imageWidth}x{imageHeight}.");
            }
        }
    }
}
