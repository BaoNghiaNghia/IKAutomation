using ADB_Tool_Automation_Post_FB.Core.Fruit2048;
using ADB_Tool_Automation_Post_FB.Core.Vision;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;

namespace ADB_Tool_Automation_Post_FB.Infrastructure.Fruit2048
{
    /// <summary>Bounded persistence for terminal Fruit anomalies only.</summary>
    public sealed class Fruit2048LearningDiagnosticStore
    {
        private const int MaxDiagnosticTransitions = 100;
        private readonly string root;

        public Fruit2048LearningDiagnosticStore(string root = null)
        {
            this.root = root ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "IKAutomation", "Fruit2048", "LearningDiagnostics");
        }

        public string Save(string deviceName, string sessionId, string transitionId, string reason,
            Fruit2048Board before, Fruit2048Board expected, IReadOnlyList<Fruit2048BoardReadResult> frames)
        {
            string safeDevice = SafePathSegment(deviceName, "unknown");
            string safeTransition = SafePathSegment(transitionId, "board");
            string directory = Path.Combine(root, safeDevice, DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmssfff")
                + "_" + safeTransition + "_" + Guid.NewGuid().ToString("N").Substring(0, 8));
            string stagingDirectory = directory + ".tmp";
            Directory.CreateDirectory(stagingDirectory);
            var usable = (frames ?? new Fruit2048BoardReadResult[0]).Where(value => value != null).ToArray();
            try
            {
                for (int index = 0; index < usable.Length; index++)
                {
                    byte[] png = usable[index].OriginalScreenshotPng;
                    if (png == null || png.Length == 0) continue;
                    string name = transitionId == null ? "board_" + (index + 1) + ".png" : "after_" + (index + 1) + ".png";
                    File.WriteAllBytes(Path.Combine(stagingDirectory, name), png);
                }
                string badgeMetadata = string.Join(";", usable.SelectMany(frame => frame.UnknownCells ?? new Fruit2048Cell[0])
                    .Where(cell => cell.BadgeBounds.HasValue)
                    .Select(cell => "r" + cell.Row + "c" + cell.Column + ":bounds=" + cell.BadgeBounds.Value
                        + ",best=" + (cell.BadgeBestTier?.ToString() ?? string.Empty)
                        + ",score=" + cell.BadgeConfidence.ToString("F3")
                        + ",second=" + (cell.BadgeSecondBestTier?.ToString() ?? string.Empty)
                        + ",secondScore=" + cell.BadgeSecondBestConfidence.ToString("F3")));
                string metadata = "{\"DeviceName\":\"" + Escape(deviceName) + "\",\"FruitSessionId\":\"" + Escape(sessionId)
                    + "\",\"TransitionId\":\"" + Escape(transitionId) + "\",\"TimestampUtc\":\"" + DateTimeOffset.UtcNow.ToString("o")
                    + "\",\"FailureReason\":\"" + Escape(reason) + "\",\"BoardBefore\":\"" + Escape(before?.ToString())
                    + "\",\"ExpectedBoardAfterMove\":\"" + Escape(expected?.ToString()) + "\",\"BadgeAmbiguities\":\"" + Escape(badgeMetadata)
                    + "\",\"CaptureCount\":" + usable.Length + "}";
                File.WriteAllText(Path.Combine(stagingDirectory, "metadata.json"), metadata);
                Directory.Move(stagingDirectory, directory);
            }
            catch
            {
                if (Directory.Exists(stagingDirectory)) Directory.Delete(stagingDirectory, true);
                throw;
            }
            Trim();
            return directory;
        }

        public string GetLatestForDevice(string deviceName)
        {
            string deviceDirectory = Path.Combine(root, SafePathSegment(deviceName, "unknown"));
            return Directory.Exists(deviceDirectory)
                ? Directory.GetDirectories(deviceDirectory).OrderByDescending(Directory.GetCreationTimeUtc).FirstOrDefault()
                : null;
        }

        /// <summary>Persists the exact full frame and focused navigation ROIs only after terminal failure.</summary>
        public string SaveNavigationFailure(string deviceName, string reason, byte[] fullScreenshotPng,
            string fruitSessionId = null, string navigationAttemptId = null,
            IReadOnlyList<Fruit2048NavigationAnchorDiagnostic> anchors = null,
            bool fruitTabTapSent = false, int fruitTabTapX = 0, int fruitTabTapY = 0)
        {
            if (fullScreenshotPng == null || fullScreenshotPng.Length == 0)
                throw new ArgumentException("A navigation screenshot is required.", nameof(fullScreenshotPng));
            string safeDevice = string.IsNullOrWhiteSpace(deviceName) ? "unknown"
                : deviceName.Replace(Path.DirectorySeparatorChar, '_');
            string directory = Path.Combine(root, safeDevice, DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmssfff")
                + "_navigation_" + Guid.NewGuid().ToString("N").Substring(0, 8));
            string stagingDirectory = directory + ".tmp";
            Directory.CreateDirectory(stagingDirectory);
            try
            {
                File.WriteAllBytes(Path.Combine(stagingDirectory, "full.png"), fullScreenshotPng);
                Fruit2048NavigationAnchorDiagnostic[] evidence = (anchors ?? new Fruit2048NavigationAnchorDiagnostic[0]).ToArray();
                using (var stream = new MemoryStream(fullScreenshotPng, false))
                using (var full = new Bitmap(stream))
                {
                    foreach (Fruit2048NavigationAnchorDiagnostic anchor in evidence)
                    {
                        ImageRegion roi = anchor.SearchRoi;
                        if (roi.Width <= 0 || roi.Height <= 0 || roi.X < 0 || roi.Y < 0
                            || roi.X + roi.Width > full.Width || roi.Y + roi.Height > full.Height) continue;
                        string name = NavigationRoiFileName(anchor.Anchor);
                        using (Bitmap crop = Crop(full, roi))
                            crop.Save(Path.Combine(stagingDirectory, name), ImageFormat.Png);
                    }
                }
                string anchorJson = string.Join(",", evidence.Select(anchor => "{\"Anchor\":\"" + Escape(anchor.Anchor)
                    + "\",\"TemplatePath\":\"" + Escape(anchor.TemplatePath) + "\",\"TemplateExists\":" + anchor.TemplateExists
                    + ",\"TemplateWidth\":" + anchor.TemplateWidth + ",\"TemplateHeight\":" + anchor.TemplateHeight
                    + ",\"SearchRoi\":\"" + Escape(anchor.SearchRoi.ToString()) + "\",\"MatcherMode\":\"" + Escape(anchor.MatcherMode)
                    + "\",\"MatchFound\":" + anchor.MatchFound + ",\"LocalMatchedBounds\":\"" + Escape(anchor.LocalMatchedBounds)
                    + "\",\"AbsoluteMatchedBounds\":\"" + Escape(anchor.AbsoluteMatchedBounds) + "\",\"ScoreAvailable\":" + anchor.ScoreAvailable
                    + ",\"Score\":" + (anchor.Score.HasValue ? anchor.Score.Value.ToString(System.Globalization.CultureInfo.InvariantCulture) : "null")
                    + ",\"Outcome\":\"" + Escape(anchor.Outcome) + "\"}"));
                string metadata = "{\"DeviceName\":\"" + Escape(deviceName) + "\",\"FruitSessionId\":\"" + Escape(fruitSessionId)
                    + "\",\"NavigationAttemptId\":\"" + Escape(navigationAttemptId) + "\",\"TimestampUtc\":\""
                    + DateTimeOffset.UtcNow.ToString("o") + "\",\"ScreenshotWidth\":" + GetWidth(fullScreenshotPng)
                    + ",\"ScreenshotHeight\":" + GetHeight(fullScreenshotPng) + ",\"FailureReason\":\"" + Escape(reason)
                    + "\",\"RuntimeState\":\"Navigation\",\"FruitTabTapSent\":" + fruitTabTapSent.ToString().ToLowerInvariant()
                    + ",\"FruitTabTapX\":" + fruitTabTapX + ",\"FruitTabTapY\":" + fruitTabTapY
                    + ",\"Anchors\":[" + anchorJson + "]}";
                File.WriteAllText(Path.Combine(stagingDirectory, "metadata.json"), metadata);
                Directory.Move(stagingDirectory, directory);
            }
            catch
            {
                if (Directory.Exists(stagingDirectory)) Directory.Delete(stagingDirectory, true);
                throw;
            }
            Trim();
            return directory;
        }

        private static int GetWidth(byte[] png)
        {
            using (var stream = new MemoryStream(png, false))
            using (var bitmap = new Bitmap(stream)) return bitmap.Width;
        }

        private static int GetHeight(byte[] png)
        {
            using (var stream = new MemoryStream(png, false))
            using (var bitmap = new Bitmap(stream)) return bitmap.Height;
        }

        private static string SafeFileName(string value)
        {
            string name = string.IsNullOrWhiteSpace(value) ? "unknown" : value.Replace('\\', '_').Replace('/', '_');
            return string.Concat(name.Select(character => Path.GetInvalidFileNameChars().Contains(character) ? '_' : character));
        }

        private static string NavigationRoiFileName(string anchor)
        {
            if (string.Equals(anchor, Fruit2048TemplateCatalog.NavigationBoardAnchor,
                StringComparison.OrdinalIgnoreCase)) return "roi_board_anchor.png";
            if (string.Equals(anchor, Fruit2048TemplateCatalog.NavigationBoardSecondaryAnchor,
                StringComparison.OrdinalIgnoreCase)) return "roi_board_secondary.png";
            return "roi_" + SafeFileName(anchor) + ".png";
        }

        /// <summary>Explicit user-requested board geometry package; never called from normal moves.</summary>
        public string ExportBoardInspection(string deviceName, byte[] fullScreenshotPng, ImageRegion boardBounds,
            Fruit2048BoardReadResult readResult = null)
        {
            if (fullScreenshotPng == null || fullScreenshotPng.Length == 0)
                throw new ArgumentException("A captured full screenshot is required.", nameof(fullScreenshotPng));
            string safeDevice = string.IsNullOrWhiteSpace(deviceName) ? "unknown" : deviceName.Replace(Path.DirectorySeparatorChar, '_');
            string directory = Path.Combine(root, "BoardInspection", safeDevice,
                DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmssfff"));
            string staging = directory + ".tmp";
            Directory.CreateDirectory(Path.Combine(staging, "cells"));
            try
            {
                File.WriteAllBytes(Path.Combine(staging, "full.png"), fullScreenshotPng);
                using (var sourceStream = new MemoryStream(fullScreenshotPng, false))
                using (var full = new Bitmap(sourceStream))
                using (var board = Crop(full, boardBounds))
                {
                    board.Save(Path.Combine(staging, "board.png"), ImageFormat.Png);
                    using (var overlay = new Bitmap(board))
                    using (Graphics graphics = Graphics.FromImage(overlay))
                    using (var pen = new Pen(Color.Lime, 2))
                    using (var font = new Font(FontFamily.GenericSansSerif, 11, FontStyle.Bold))
                    {
                        for (int row = 0; row < 4; row++)
                        for (int column = 0; column < 4; column++)
                        {
                            ImageRegion cell = Fruit2048ScreenProfile.GetCellRegion(boardBounds, row, column);
                            using (var crop = Crop(full, cell))
                                crop.Save(Path.Combine(staging, "cells", "r" + row + "_c" + column + ".png"), ImageFormat.Png);
                            var relative = new Rectangle(cell.X - boardBounds.X, cell.Y - boardBounds.Y, cell.Width, cell.Height);
                            graphics.DrawRectangle(pen, relative);
                            graphics.DrawString("R" + row + "C" + column, font, Brushes.Lime,
                                relative.Left + 3, relative.Top + 3);
                        }
                        overlay.Save(Path.Combine(staging, "board_debug.png"), ImageFormat.Png);
                    }
                }
                string cells = string.Join(";", Enumerable.Range(0, 4).SelectMany(row => Enumerable.Range(0, 4)
                    .Select(column => "R" + row + "C" + column + "=" + Fruit2048ScreenProfile.GetCellRegion(boardBounds, row, column))));
                const string expectedInitialFixture = "R0C0=Empty;R0C1=Empty;R0C2=Empty;R0C3=Empty;"
                    + "R1C0=Empty;R1C1=Empty;R1C2=Tier1;R1C3=Empty;"
                    + "R2C0=Empty;R2C1=Empty;R2C2=Empty;R2C3=Empty;"
                    + "R3C0=Empty;R3C1=Empty;R3C2=Tier1;R3C3=Empty";
                string metadata = "{\"DeviceName\":\"" + Escape(deviceName) + "\",\"TimestampUtc\":\""
                    + DateTimeOffset.UtcNow.ToString("o") + "\",\"BoardBounds\":\"" + Escape(boardBounds.ToString())
                    + "\",\"CellBounds\":\"" + Escape(cells) + "\",\"KnownCells\":"
                    + ((readResult?.Cells.Count ?? 0) - (readResult?.UnknownCells.Count ?? 0)) + ",\"UnknownCells\":"
                    + (readResult?.UnknownCells.Count ?? 0) + ",\"ExpectedInitialFixture\":\""
                    + expectedInitialFixture + "\"}";
                File.WriteAllText(Path.Combine(staging, "metadata.json"), metadata);
                Directory.Move(staging, directory);
                return directory;
            }
            catch
            {
                if (Directory.Exists(staging)) Directory.Delete(staging, true);
                throw;
            }
        }

        private static Bitmap Crop(Bitmap source, ImageRegion region)
        {
            var copy = new Bitmap(region.Width, region.Height);
            using (Graphics graphics = Graphics.FromImage(copy))
                graphics.DrawImage(source, new Rectangle(0, 0, copy.Width, copy.Height),
                    new Rectangle(region.X, region.Y, region.Width, region.Height), GraphicsUnit.Pixel);
            return copy;
        }

        private void Trim()
        {
            if (!Directory.Exists(root)) return;
            string[] directories = Directory.GetDirectories(root, "*", SearchOption.AllDirectories)
                .Where(directory => File.Exists(Path.Combine(directory, "metadata.json")))
                .OrderBy(directory => Directory.GetCreationTimeUtc(directory)).ToArray();
            foreach (string directory in directories.Take(Math.Max(0, directories.Length - MaxDiagnosticTransitions)))
                Directory.Delete(directory, true);
        }

        private static string SafePathSegment(string value, string fallback)
        {
            if (string.IsNullOrWhiteSpace(value)) return fallback;
            char[] invalid = Path.GetInvalidFileNameChars();
            var result = new System.Text.StringBuilder(value.Length);
            foreach (char character in value)
            {
                bool isInvalid = character == ':' || character == Path.DirectorySeparatorChar
                    || character == Path.AltDirectorySeparatorChar || invalid.Contains(character);
                result.Append(isInvalid ? '_' : character);
            }
            string sanitized = result.ToString().Trim();
            return string.IsNullOrWhiteSpace(sanitized) ? fallback : sanitized;
        }

        private static string Escape(string value) => (value ?? string.Empty).Replace("\\", "\\\\").Replace("\"", "\\\"");
    }
}
