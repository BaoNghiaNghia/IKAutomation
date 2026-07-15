using ADB_Tool_Automation_Post_FB.Core.Vision;
using ADB_Tool_Automation_Post_FB.Infrastructure.Vision;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;

namespace IKAutomation.Vision.Tests
{
    internal static class Program
    {
        private static int Main()
        {
            var tests = new List<KeyValuePair<string, Action>>
            {
                Test("Registry returns relative path", RegistryReturnsRelativePath),
                Test("Registry reports missing file", RegistryReportsMissingFile),
                Test("Matcher finds generated template", MatcherFindsGeneratedTemplate),
                Test("Matcher translates ROI coordinates", MatcherTranslatesRoiCoordinates),
                Test("Matcher returns not found", MatcherReturnsNotFound),
                Test("Matcher rejects empty bytes", MatcherRejectsEmptyBytes),
                Test("Matching and registry do not lock files", MatchingDoesNotLockFiles)
            };

            int failed = 0;
            foreach (KeyValuePair<string, Action> test in tests)
            {
                try
                {
                    test.Value();
                    Console.WriteLine("PASS: " + test.Key);
                }
                catch (Exception ex)
                {
                    failed++;
                    Console.Error.WriteLine("FAIL: " + test.Key);
                    Console.Error.WriteLine(ex);
                }
            }

            Console.WriteLine($"Vision tests: {tests.Count - failed} passed, {failed} failed.");
            return failed == 0 ? 0 : 1;
        }

        private static KeyValuePair<string, Action> Test(string name, Action action)
        {
            return new KeyValuePair<string, Action>(name, action);
        }

        private static void RegistryReturnsRelativePath()
        {
            string root = CreateTemporaryDirectory();
            try
            {
                var registry = new TemplateRegistry(root);
                TemplateDefinition definition = registry.GetDefinition(TemplateId.WorldMapAnchor);

                AssertEqual("Global/world_map_anchor.png", definition.RelativePath, "Unexpected relative path.");
                AssertEqual(
                    Path.Combine(root, "Global", "world_map_anchor.png"),
                    registry.GetPath(TemplateId.WorldMapAnchor),
                    "Unexpected full path.");
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }

        private static void RegistryReportsMissingFile()
        {
            string root = CreateTemporaryDirectory();
            try
            {
                var registry = new TemplateRegistry(root);

                try
                {
                    registry.LoadBytes(TemplateId.SearchButton);
                    throw new Exception("Expected FileNotFoundException was not thrown.");
                }
                catch (FileNotFoundException ex)
                {
                    AssertTrue(ex.Message.Contains(nameof(TemplateId.SearchButton)), "Error must contain TemplateId.");
                    AssertTrue(ex.Message.Contains(registry.GetPath(TemplateId.SearchButton)), "Error must contain path.");
                }
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }

        private static void MatcherFindsGeneratedTemplate()
        {
            GeneratedImages images = CreateGeneratedImages(47, 31);
            var matcher = new KAutoImageMatcher();

            ImageMatchResult result = matcher.Find(images.ScreenshotPng, images.TemplatePng);

            AssertTrue(result.Found, "Expected template to be found.");
            AssertNear(47, result.X, 1, "Unexpected match X.");
            AssertNear(31, result.Y, 1, "Unexpected match Y.");
            AssertEqual(images.TemplateWidth, result.Width, "Unexpected match width.");
            AssertEqual(images.TemplateHeight, result.Height, "Unexpected match height.");
            AssertTrue(!result.Confidence.HasValue, "KAutoHelper confidence should be unavailable.");
        }

        private static void MatcherTranslatesRoiCoordinates()
        {
            GeneratedImages images = CreateGeneratedImages(58, 42);
            var matcher = new KAutoImageMatcher();
            var region = new ImageRegion(40, 25, 90, 70);

            ImageMatchResult result = matcher.Find(images.ScreenshotPng, images.TemplatePng, region);

            AssertTrue(result.Found, "Expected template to be found inside ROI.");
            AssertNear(58, result.X, 1, "ROI X offset was not applied.");
            AssertNear(42, result.Y, 1, "ROI Y offset was not applied.");
        }

        private static void MatcherReturnsNotFound()
        {
            GeneratedImages images = CreateGeneratedImages(47, 31);
            byte[] differentTemplate = CreateDifferentTemplate(images.TemplateWidth, images.TemplateHeight);
            var matcher = new KAutoImageMatcher();

            ImageMatchResult result = matcher.Find(images.ScreenshotPng, differentTemplate);

            AssertTrue(!result.Found, "Unexpected template match.");
            AssertTrue(!result.Confidence.HasValue, "Not-found confidence should be null.");
        }

        private static void MatcherRejectsEmptyBytes()
        {
            var matcher = new KAutoImageMatcher();

            AssertThrows<ArgumentException>(() => matcher.Find(new byte[0], new byte[] { 1 }));
            AssertThrows<ArgumentException>(() => matcher.Find(new byte[] { 1 }, new byte[0]));
        }

        private static void MatchingDoesNotLockFiles()
        {
            string root = CreateTemporaryDirectory();
            try
            {
                GeneratedImages images = CreateGeneratedImages(47, 31);
                string globalDirectory = Path.Combine(root, "Global");
                Directory.CreateDirectory(globalDirectory);

                string templatePath = Path.Combine(globalDirectory, "world_map_anchor.png");
                string screenshotPath = Path.Combine(root, "screenshot.png");
                File.WriteAllBytes(templatePath, images.TemplatePng);
                File.WriteAllBytes(screenshotPath, images.ScreenshotPng);

                var registry = new TemplateRegistry(root);
                byte[] template = registry.LoadBytes(TemplateId.WorldMapAnchor);
                byte[] screenshot = File.ReadAllBytes(screenshotPath);
                var matcher = new KAutoImageMatcher();

                ImageMatchResult result = matcher.Find(screenshot, template);
                AssertTrue(result.Found, "Expected template to be found before lock check.");

                using (new FileStream(templatePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                {
                }

                using (new FileStream(screenshotPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                {
                }

                File.Delete(templatePath);
                File.Delete(screenshotPath);
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }

        private static GeneratedImages CreateGeneratedImages(int targetX, int targetY)
        {
            const int screenshotWidth = 180;
            const int screenshotHeight = 120;
            const int templateWidth = 24;
            const int templateHeight = 18;

            using (var template = new Bitmap(templateWidth, templateHeight, PixelFormat.Format24bppRgb))
            using (Graphics templateGraphics = Graphics.FromImage(template))
            {
                templateGraphics.Clear(Color.FromArgb(23, 31, 47));
                templateGraphics.FillRectangle(Brushes.OrangeRed, 1, 1, 8, 6);
                templateGraphics.FillEllipse(Brushes.LimeGreen, 11, 2, 10, 10);
                templateGraphics.DrawLine(Pens.DeepSkyBlue, 2, 15, 21, 12);
                template.SetPixel(20, 16, Color.White);

                using (var screenshot = new Bitmap(screenshotWidth, screenshotHeight, PixelFormat.Format24bppRgb))
                using (Graphics screenshotGraphics = Graphics.FromImage(screenshot))
                {
                    screenshotGraphics.Clear(Color.FromArgb(210, 214, 219));
                    screenshotGraphics.DrawImageUnscaled(template, targetX, targetY);

                    return new GeneratedImages
                    {
                        ScreenshotPng = EncodePng(screenshot),
                        TemplatePng = EncodePng(template),
                        TemplateWidth = templateWidth,
                        TemplateHeight = templateHeight
                    };
                }
            }
        }

        private static byte[] CreateDifferentTemplate(int width, int height)
        {
            using (var bitmap = new Bitmap(width, height, PixelFormat.Format24bppRgb))
            using (Graphics graphics = Graphics.FromImage(bitmap))
            {
                graphics.Clear(Color.Purple);
                graphics.FillRectangle(Brushes.Yellow, 2, 2, width - 4, height - 4);
                graphics.DrawLine(Pens.Black, 0, 0, width - 1, height - 1);
                graphics.DrawLine(Pens.Black, width - 1, 0, 0, height - 1);
                return EncodePng(bitmap);
            }
        }

        private static byte[] EncodePng(Bitmap bitmap)
        {
            using (var stream = new MemoryStream())
            {
                bitmap.Save(stream, ImageFormat.Png);
                return stream.ToArray();
            }
        }

        private static string CreateTemporaryDirectory()
        {
            string path = Path.Combine(Path.GetTempPath(), "IKAutomation.Vision.Tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            return path;
        }

        private static void AssertTrue(bool condition, string message)
        {
            if (!condition)
                throw new Exception(message);
        }

        private static void AssertEqual<T>(T expected, T actual, string message)
        {
            if (!EqualityComparer<T>.Default.Equals(expected, actual))
                throw new Exception($"{message} Expected='{expected}', Actual='{actual}'.");
        }

        private static void AssertNear(int expected, int actual, int tolerance, string message)
        {
            if (Math.Abs(expected - actual) > tolerance)
                throw new Exception($"{message} Expected={expected}±{tolerance}, Actual={actual}.");
        }

        private static void AssertThrows<TException>(Action action) where TException : Exception
        {
            try
            {
                action();
            }
            catch (TException)
            {
                return;
            }

            throw new Exception("Expected exception " + typeof(TException).Name + " was not thrown.");
        }

        private sealed class GeneratedImages
        {
            public byte[] ScreenshotPng { get; set; }

            public byte[] TemplatePng { get; set; }

            public int TemplateWidth { get; set; }

            public int TemplateHeight { get; set; }
        }
    }
}
