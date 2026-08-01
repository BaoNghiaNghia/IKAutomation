using ADB_Tool_Automation_Post_FB.Core.Vision;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Drawing;
using System.IO;
using System.Linq;
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
            using (Bitmap screenshot = DecodeBitmap(screenshotPng, nameof(screenshotPng)))
                return FindManyOnBitmap(screenshot, requests);
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
                return FindManyOnBitmap(frame.Bitmap, requests);
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
                    return FindOnSearchBitmap(searchImage, templatePng, offsetX, offsetY);
                }
                finally
                {
                    if (!ReferenceEquals(searchImage, screenshot)) searchImage?.Dispose();
                }
            }
        }

        // KAutoHelper exposes only Bitmap matching. Grouping equal ROIs removes
        // avoidable Clone allocations in FindMany without introducing another
        // OpenCV runtime or changing the existing VisionGate ownership.
        private static IReadOnlyList<ImageMatchResult> FindManyOnBitmap(Bitmap screenshot,
            IReadOnlyList<ImageMatchRequest> requests)
        {
            var results = new ImageMatchResult[requests.Count];
            foreach (var group in requests.Select((request, index) => new { request, index })
                .GroupBy(item => item.request?.SearchRegion))
            {
                if (group.Key.HasValue)
                    ValidateRegionBounds(group.Key.Value, screenshot.Width, screenshot.Height);
                Bitmap roi = group.Key.HasValue ? screenshot.Clone(new Rectangle(group.Key.Value.X,
                    group.Key.Value.Y, group.Key.Value.Width, group.Key.Value.Height),
                    screenshot.PixelFormat) : screenshot;
                try
                {
                    foreach (var item in group)
                    {
                        if (item.request == null)
                            throw new ArgumentException("A match request cannot be null.", nameof(requests));
                        int offsetX = group.Key.HasValue ? group.Key.Value.X : 0;
                        int offsetY = group.Key.HasValue ? group.Key.Value.Y : 0;
                        results[item.index] = FindOnSearchBitmap(roi,
                            item.request.TemplatePng, offsetX, offsetY);
                    }
                }
                finally { if (!ReferenceEquals(roi, screenshot)) roi.Dispose(); }
            }
            return Array.AsReadOnly(results);
        }

        private static ImageMatchResult FindOnSearchBitmap(Bitmap searchImage,
            byte[] templatePng, int offsetX, int offsetY)
        {
            // The cached image is an immutable master. Native matching receives a
            // per-call clone so concurrent workers never share a mutable Bitmap.
            using (Bitmap template = (Bitmap)GetDecodedTemplate(templatePng).Clone())
            {
                if (template.Width > searchImage.Width || template.Height > searchImage.Height)
                    throw new ArgumentException($"Template size {template.Width}x{template.Height} exceeds search image size "
                        + $"{searchImage.Width}x{searchImage.Height}.", nameof(templatePng));
                Point? topLeftPoint = KAutoHelper.ImageScanOpenCV.FindOutPoint(searchImage, template);
                return !topLeftPoint.HasValue ? ImageMatchResult.NotFound()
                    : ImageMatchResult.FoundAt(topLeftPoint.Value.X + offsetX,
                        topLeftPoint.Value.Y + offsetY, template.Width, template.Height, null);
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
