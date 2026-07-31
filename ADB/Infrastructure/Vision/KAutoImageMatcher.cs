using ADB_Tool_Automation_Post_FB.Core.Vision;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Drawing;
using System.IO;
using System.Runtime.CompilerServices;

namespace ADB_Tool_Automation_Post_FB.Infrastructure.Vision
{
    public sealed class KAutoImageMatcher : IImageMatcher, IBatchImageMatcher, IFrameImageMatcher
    {
        private static readonly System.Threading.SemaphoreSlim VisionGate =
            new System.Threading.SemaphoreSlim(ReadPositiveSetting(
                "Operations.MaxConcurrentVisionOperations", 6));

        // TemplateRegistry returns stable byte-array instances.  Keeping the decoded
        // bitmap alongside that instance removes a decode from every match while the
        // ConditionalWeakTable still releases dynamically-created fallback templates.
        private static readonly ConditionalWeakTable<byte[], Bitmap> DecodedTemplates =
            new ConditionalWeakTable<byte[], Bitmap>();

        private static int ReadPositiveSetting(string key, int fallback)
        {
            try
            {
                Type manager = Type.GetType(
                    "System.Configuration.ConfigurationManager, System.Configuration")
                    ?? Type.GetType(
                        "System.Configuration.ConfigurationManager, System.Configuration.ConfigurationManager");
                PropertyInfo appSettings = manager?.GetProperty("AppSettings");
                object settings = appSettings?.GetValue(null, null);
                string configured = settings?.GetType().GetProperty("Item")
                    ?.GetValue(settings, new object[] { key }) as string;
                int value;
                return int.TryParse(configured, out value) && value > 0 ? value : fallback;
            }
            catch { return fallback; }
        }

        public IReadOnlyList<ImageMatchResult> FindMany(
            byte[] screenshotPng,
            IReadOnlyList<ImageMatchRequest> requests)
        {
            ValidateImageBytes(screenshotPng, nameof(screenshotPng));
            if (requests == null) throw new ArgumentNullException(nameof(requests));
            VisionGate.Wait();
            try
            {
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
            finally { VisionGate.Release(); }
        }

        public IReadOnlyList<ImageMatchResult> FindMany(
            CapturedFrame frame,
            IReadOnlyList<ImageMatchRequest> requests)
        {
            if (frame == null) throw new ArgumentNullException(nameof(frame));
            if (requests == null) throw new ArgumentNullException(nameof(requests));

            VisionGate.Wait();
            try
            {
                var results = new List<ImageMatchResult>(requests.Count);
                foreach (ImageMatchRequest request in requests)
                {
                    if (request == null)
                        throw new ArgumentException("A match request cannot be null.", nameof(requests));
                    results.Add(FindOnBitmap(frame.Bitmap, request.TemplatePng, request.SearchRegion));
                }
                return results.AsReadOnly();
            }
            finally { VisionGate.Release(); }
        }

        public ImageMatchResult Find(byte[] screenshotPng, byte[] templatePng, ImageRegion? searchRegion = null)
        {
            ValidateImageBytes(screenshotPng, nameof(screenshotPng));
            ValidateImageBytes(templatePng, nameof(templatePng));

            VisionGate.Wait();
            try
            {
                using (Bitmap screenshot = DecodeBitmap(screenshotPng, nameof(screenshotPng)))
                    return FindOnBitmap(screenshot, templatePng, searchRegion);
            }
            finally { VisionGate.Release(); }
        }

        public ImageMatchResult Find(CapturedFrame frame, byte[] templatePng,
            ImageRegion? searchRegion = null)
        {
            if (frame == null) throw new ArgumentNullException(nameof(frame));
            ValidateImageBytes(templatePng, nameof(templatePng));

            VisionGate.Wait();
            try { return FindOnBitmap(frame.Bitmap, templatePng, searchRegion); }
            finally { VisionGate.Release(); }
        }

        private static ImageMatchResult FindOnBitmap(
            Bitmap screenshot, byte[] templatePng, ImageRegion? searchRegion)
        {
            ValidateImageBytes(templatePng, nameof(templatePng));
            Bitmap template = GetDecodedTemplate(templatePng);
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
                    else searchImage = screenshot;

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
                    if (!ReferenceEquals(searchImage, screenshot)) searchImage?.Dispose();
                }
            }
        }

        private static Bitmap GetDecodedTemplate(byte[] templatePng)
        {
            ValidateImageBytes(templatePng, nameof(templatePng));
            return DecodedTemplates.GetValue(templatePng,
                bytes => DecodeBitmap(bytes, nameof(templatePng)));
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
