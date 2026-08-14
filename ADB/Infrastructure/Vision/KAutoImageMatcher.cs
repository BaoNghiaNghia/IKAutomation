using ADB_Tool_Automation_Post_FB.Core.Vision;
using ADB_Tool_Automation_Post_FB.Core.Diagnostics;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace ADB_Tool_Automation_Post_FB.Infrastructure.Vision
{
    public sealed class KAutoImageMatcher : IImageMatcher, IBatchImageMatcher,
        IFrameImageMatcher, IAsyncFrameImageMatcher
    {
        private static readonly int VisionConcurrencyLimit = ReadPositiveSetting(
            "Operations.MaxConcurrentVisionOperations", 8);
        private static readonly System.Threading.SemaphoreSlim VisionGate =
            new System.Threading.SemaphoreSlim(VisionConcurrencyLimit,
                VisionConcurrencyLimit);
        private readonly SemaphoreSlim visionGate;
        private readonly Func<CancellationToken, Task> beforeProcessingAsync;
        private static long visionGateWaitMs, visionProcessingDurationMs,
            visionTotalDurationMs, templateCount, matchFound, visionFailures,
            visionOperationCount;
        private static int visionQueueDepth, activeVisionOperations;
        private static int peakActiveVisionOperations;
        private static long maxVisionGateWaitMs;

        // TemplateRegistry returns stable byte-array instances.  Keeping the decoded
        // bitmap alongside that instance removes a decode from every match while the
        // ConditionalWeakTable still releases dynamically-created fallback templates.
        private static readonly ConditionalWeakTable<byte[], Bitmap> DecodedTemplates =
            new ConditionalWeakTable<byte[], Bitmap>();

        public KAutoImageMatcher() : this(VisionGate, null) { }

        public KAutoImageMatcher(int maximumConcurrency,
            Func<CancellationToken, Task> beforeProcessingAsync = null)
            : this(new SemaphoreSlim(maximumConcurrency, maximumConcurrency),
                beforeProcessingAsync)
        {
            if (maximumConcurrency < 1) throw new ArgumentOutOfRangeException(nameof(maximumConcurrency));
        }

        private KAutoImageMatcher(SemaphoreSlim visionGate,
            Func<CancellationToken, Task> beforeProcessingAsync)
        {
            this.visionGate = visionGate ?? throw new ArgumentNullException(nameof(visionGate));
            this.beforeProcessingAsync = beforeProcessingAsync;
        }

        public static VisionOperationMetrics GetMetrics() => new VisionOperationMetrics
        {
            VisionGateWaitMs = Interlocked.Read(ref visionGateWaitMs),
            VisionProcessingDurationMs = Interlocked.Read(ref visionProcessingDurationMs),
            VisionTotalDurationMs = Interlocked.Read(ref visionTotalDurationMs),
            VisionQueueDepth = Volatile.Read(ref visionQueueDepth),
            ActiveVisionOperations = Volatile.Read(ref activeVisionOperations),
            VisionGateLimit = VisionConcurrencyLimit,
            VisionOperationCount = Interlocked.Read(ref visionOperationCount),
            PeakActiveVisionOperations = Volatile.Read(ref peakActiveVisionOperations),
            MaxVisionGateWaitMs = Interlocked.Read(ref maxVisionGateWaitMs),
            TemplateCount = Interlocked.Read(ref templateCount),
            MatchFound = Interlocked.Read(ref matchFound),
            FailureCount = Interlocked.Read(ref visionFailures)
        };

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

        private static void UpdateMaximum(ref int target, int value)
        {
            int observed;
            while ((observed = Volatile.Read(ref target)) < value
                && Interlocked.CompareExchange(ref target, value, observed) != observed) { }
        }

        private static void UpdateMaximum(ref long target, long value)
        {
            long observed;
            while ((observed = Interlocked.Read(ref target)) < value
                && Interlocked.CompareExchange(ref target, value, observed) != observed) { }
        }

        public IReadOnlyList<ImageMatchResult> FindMany(
            byte[] screenshotPng,
            IReadOnlyList<ImageMatchRequest> requests)
        {
            ValidateImageBytes(screenshotPng, nameof(screenshotPng));
            if (requests == null) throw new ArgumentNullException(nameof(requests));
            visionGate.Wait();
            try
            {
            using (Bitmap screenshot = DecodeBitmap(screenshotPng, nameof(screenshotPng)))
                return FindManyOnBitmap(screenshot, requests);
            }
            finally { visionGate.Release(); }
        }

        public IReadOnlyList<ImageMatchResult> FindMany(
            CapturedFrame frame,
            IReadOnlyList<ImageMatchRequest> requests)
        {
            if (frame == null) throw new ArgumentNullException(nameof(frame));
            if (requests == null) throw new ArgumentNullException(nameof(requests));

            visionGate.Wait();
            try
            {
                return FindManyOnBitmap(frame.Bitmap, requests);
            }
            finally { visionGate.Release(); }
        }

        public ImageMatchResult Find(byte[] screenshotPng, byte[] templatePng, ImageRegion? searchRegion = null)
        {
            ValidateImageBytes(screenshotPng, nameof(screenshotPng));
            ValidateImageBytes(templatePng, nameof(templatePng));

            visionGate.Wait();
            try
            {
                using (Bitmap screenshot = DecodeBitmap(screenshotPng, nameof(screenshotPng)))
                    return FindOnBitmap(screenshot, templatePng, searchRegion);
            }
            finally { visionGate.Release(); }
        }

        public ImageMatchResult Find(CapturedFrame frame, byte[] templatePng,
            ImageRegion? searchRegion = null)
        {
            if (frame == null) throw new ArgumentNullException(nameof(frame));
            ValidateImageBytes(templatePng, nameof(templatePng));

            visionGate.Wait();
            try { return FindOnBitmap(frame.Bitmap, templatePng, searchRegion); }
            finally { visionGate.Release(); }
        }

        public Task<ImageMatchResult> FindAsync(CapturedFrame frame, byte[] templatePng,
            ImageRegion? searchRegion, CancellationToken cancellationToken)
        {
            if (frame == null) throw new ArgumentNullException(nameof(frame));
            ValidateImageBytes(templatePng, nameof(templatePng));
            return ExecuteAsync(() => FindOnBitmap(frame.Bitmap, templatePng, searchRegion),
                1, cancellationToken);
        }

        public Task<IReadOnlyList<ImageMatchResult>> FindManyAsync(CapturedFrame frame,
            IReadOnlyList<ImageMatchRequest> requests, CancellationToken cancellationToken)
        {
            if (frame == null) throw new ArgumentNullException(nameof(frame));
            if (requests == null) throw new ArgumentNullException(nameof(requests));
            return ExecuteAsync(() => FindManyOnBitmap(frame.Bitmap, requests),
                requests.Count, cancellationToken);
        }

        private async Task<T> ExecuteAsync<T>(Func<T> operation, int templates,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Interlocked.Increment(ref visionOperationCount);
            var total = Stopwatch.StartNew();
            var wait = Stopwatch.StartNew();
            Interlocked.Increment(ref visionQueueDepth);
            bool entered = false;
            bool failed = false;
            try
            {
                await visionGate.WaitAsync(cancellationToken);
                entered = true;
                wait.Stop();
                Interlocked.Decrement(ref visionQueueDepth);
                Interlocked.Increment(ref activeVisionOperations);
                Interlocked.Add(ref visionGateWaitMs, wait.ElapsedMilliseconds);
                UpdateMaximum(ref peakActiveVisionOperations,
                    Volatile.Read(ref activeVisionOperations));
                UpdateMaximum(ref maxVisionGateWaitMs, wait.ElapsedMilliseconds);
                if (beforeProcessingAsync != null)
                    await beforeProcessingAsync(cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                var processing = Stopwatch.StartNew();
                try
                {
                    T result = operation();
                    Interlocked.Add(ref templateCount, templates);
                    Interlocked.Add(ref matchFound, CountMatches(result));
                    return result;
                }
                finally
                {
                    processing.Stop();
                    Interlocked.Add(ref visionProcessingDurationMs,
                        processing.ElapsedMilliseconds);
                }
            }
            catch
            {
                failed = true;
                Interlocked.Increment(ref visionFailures);
                throw;
            }
            finally
            {
                if (!entered) Interlocked.Decrement(ref visionQueueDepth);
                if (entered)
                {
                    Interlocked.Decrement(ref activeVisionOperations);
                    visionGate.Release();
                }
                total.Stop();
                Interlocked.Add(ref visionTotalDurationMs, total.ElapsedMilliseconds);
                RuntimePressureMetrics.ReportVision(wait.ElapsedMilliseconds, failed,
                    Volatile.Read(ref visionQueueDepth),
                    Volatile.Read(ref activeVisionOperations), VisionConcurrencyLimit);
            }
        }

        private static int CountMatches<T>(T result)
        {
            if (result is ImageMatchResult single) return single.Found ? 1 : 0;
            var many = result as IEnumerable<ImageMatchResult>;
            return many?.Count(match => match != null && match.Found) ?? 0;
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
