using ADB_Tool_Automation_Post_FB.Core.Abstractions;
using ADB_Tool_Automation_Post_FB.Core.Concurrency;
using ADB_Tool_Automation_Post_FB.Core.Diagnostics;
using ADB_Tool_Automation_Post_FB.Core.Fruit2048;
using ADB_Tool_Automation_Post_FB.Core.Vision;
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace ADB_Tool_Automation_Post_FB.Infrastructure.Fruit2048
{
    public sealed class Fruit2048SeedCalibrationService : IFruit2048SeedCalibrationService
    {
        private readonly IFrameCapturingLdPlayerClient frames;
        private readonly IFrameImageMatcher matcher;
        private readonly Fruit2048TemplateCatalog templates;
        private readonly Fruit2048ScreenProfile profile;
        private readonly FruitTileLearningCatalog learning;
        private readonly IDeviceAutomationOwnershipService ownership;
        private readonly IDiagnosticLogger logger;
        private readonly IFruit2048NavigationService navigation;

        public Fruit2048SeedCalibrationService(IFrameCapturingLdPlayerClient frames,
            IFrameImageMatcher matcher, Fruit2048TemplateCatalog templates,
            Fruit2048ScreenProfile profile, FruitTileLearningCatalog learning,
            IDeviceAutomationOwnershipService ownership, IDiagnosticLogger logger,
            IFruit2048NavigationService navigation = null)
        {
            this.frames = frames; this.matcher = matcher; this.templates = templates;
            this.profile = profile; this.learning = learning; this.ownership = ownership; this.logger = logger; this.navigation = navigation;
        }

        public async Task<Fruit2048CalibrationCapture> CaptureAsync(string deviceName, CancellationToken cancellationToken)
        {
            IDeviceAutomationLease lease;
            if (!ownership.TryAcquire(deviceName, DeviceAutomationOwner.Fruit2048, out lease))
                return Failure("Thiết bị đang được tác vụ khác sử dụng.");
            using (lease)
            {
                if (navigation != null)
                {
                    Fruit2048NavigationResult navigationResult = await navigation.EnsureFruit2048ScreenAsync(
                        deviceName, null, cancellationToken);
                    if (!navigationResult.Success) return Failure(navigationResult.Error);
                }
                using (CapturedFrame frame = await frames.CaptureFrameAsync(deviceName, cancellationToken))
                {
                ImageRegion board = profile.Scale(profile.BoardRegion, frame.Width, frame.Height);
                ImageRegion boardAnchorRegion = profile.Scale(profile.BoardAnchorRegion, frame.Width, frame.Height);
                byte[] boardAnchor;
                if (!templates.TryGet(Fruit2048TemplateCatalog.NavigationBoardAnchor, out boardAnchor))
                    return Failure("Không tìm thấy template mở màn Fruit2048.");
                if (!matcher.Find(frame, boardAnchor, boardAnchorRegion).Found)
                    return Failure("Không tìm thấy bàn chơi Lễ Hội Trái Cây.");
                var result = new Fruit2048CalibrationCapture
                {
                    DeviceName = deviceName, ScreenWidth = frame.Width, ScreenHeight = frame.Height,
                    BoardBounds = board, ScreenshotPng = frame.GetPngBytes()
                };
                Log(deviceName, frame.Width, frame.Height, "Capture", -1, -1, string.Empty, true, string.Empty, "Captured");
                return result;
                }
            }
        }

        public async Task<Fruit2048CalibrationResult> SaveAsync(Fruit2048CalibrationCapture capture,
            int emptyRow, int emptyColumn, int tier1Row, int tier1Column, CancellationToken cancellationToken)
        {
            if (capture == null || !capture.Success) return Result(false, "Chưa có ảnh bàn chơi để hiệu chỉnh.");
            if (!ValidCell(emptyRow, emptyColumn) || !ValidCell(tier1Row, tier1Column)
                || (emptyRow == tier1Row && emptyColumn == tier1Column)) return Result(false, "Hãy chọn hai ô khác nhau.");
            try
            {
                byte[] empty; byte[] tier1; FruitTileVisualFingerprint emptyFingerprint; FruitTileVisualFingerprint tier1Fingerprint;
                using (var stream = new MemoryStream(capture.ScreenshotPng, false))
                using (var screenshot = new Bitmap(stream))
                {
                    empty = CropCell(screenshot, capture.BoardBounds, emptyRow, emptyColumn);
                    tier1 = CropCell(screenshot, capture.BoardBounds, tier1Row, tier1Column);
                    emptyFingerprint = CreateFingerprint(empty);
                    tier1Fingerprint = CreateFingerprint(tier1);
                }
                if (!IsUsable(empty, emptyFingerprint) || !HasVisualVariance(empty))
                    return Result(false, "Ô đã chọn không phù hợp làm mẫu Empty. Hãy chọn lại.");
                if (!IsUsable(tier1, tier1Fingerprint) || !HasVisualVariance(tier1)
                    || IsTooSimilar(emptyFingerprint, tier1Fingerprint))
                    return Result(false, "Ô Tier 1 quá giống mẫu Empty. Hãy chọn lại.");

                var metadata = new Fruit2048SeedMetadata
                {
                    Resolution = capture.ScreenWidth + "x" + capture.ScreenHeight,
                    CreatedAtUtc = DateTime.UtcNow, DeviceName = capture.DeviceName,
                    BoardX = capture.BoardBounds.X, BoardY = capture.BoardBounds.Y,
                    BoardWidth = capture.BoardBounds.Width, BoardHeight = capture.BoardBounds.Height,
                    EmptyCellRow = emptyRow, EmptyCellColumn = emptyColumn,
                    Tier1CellRow = tier1Row, Tier1CellColumn = tier1Column
                };
                Log(capture.DeviceName, capture.ScreenWidth, capture.ScreenHeight, "Validate", emptyRow, emptyColumn, "Empty", true, Fingerprint(emptyFingerprint), "Valid");
                Log(capture.DeviceName, capture.ScreenWidth, capture.ScreenHeight, "Validate", tier1Row, tier1Column, "Tier1", true, Fingerprint(tier1Fingerprint), "Valid");
                templates.CalibratedSeeds.SavePair(capture.ScreenWidth, capture.ScreenHeight, empty, tier1, metadata);
                templates.ReloadSeeds();
                learning.ReplaceBootstrapSeed(0, emptyFingerprint, templates.EmptySeedPath);
                learning.ReplaceBootstrapSeed(1, tier1Fingerprint, templates.Tier1SeedPath);
                Log(capture.DeviceName, capture.ScreenWidth, capture.ScreenHeight, "Persist", -1, -1, "Pair", true, string.Empty, templates.CalibratedSeeds.GetResolutionDirectory(capture.ScreenWidth, capture.ScreenHeight));

                // A fresh board read verifies the active seed pair and selected positions before Auto is enabled.
                Fruit2048CalibrationCapture fresh = await CaptureAsync(capture.DeviceName, cancellationToken);
                if (!fresh.Success || !VerifyFreshSelections(fresh, emptyRow, emptyColumn, tier1Row, tier1Column))
                    return Result(false, "Hiệu chỉnh chưa đạt. Hãy chọn lại mẫu.");
                Log(capture.DeviceName, capture.ScreenWidth, capture.ScreenHeight, "Complete", -1, -1, "Pair", true, string.Empty, "ReadyToLearn");
                return new Fruit2048CalibrationResult { Success = true, BootstrapState = Fruit2048BootstrapState.ReadyToLearn,
                    Message = "Hiệu chỉnh hoàn tất. Fruit2048 đã sẵn sàng tự học." };
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception exception)
            {
                Log(capture.DeviceName, capture.ScreenWidth, capture.ScreenHeight, "Persist", -1, -1, string.Empty, false, string.Empty, exception.Message);
                return Result(false, "Hiệu chỉnh chưa đạt. Hãy chọn lại mẫu.");
            }
        }

        public void DeleteCalibratedSeeds()
        {
            templates.CalibratedSeeds.DeletePair(1280, 720);
            templates.ReloadSeeds();
            learning.ResetLearningData();
        }

        public Task<Fruit2048LearningResult> SaveTierBadgeAsync(Fruit2048CalibrationCapture capture,
            int row, int column, int tier, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (capture == null || !capture.Success || !ValidCell(row, column) || tier < 1)
                return Task.FromResult(new Fruit2048LearningResult
                { Action = Fruit2048LearningAction.ConflictRejected, Error = "Lựa chọn tier không hợp lệ." });
            try
            {
                FruitTileVisualFingerprint fruit; FruitTileVisualFingerprint badge;
                using (var stream = new MemoryStream(capture.ScreenshotPng, false))
                using (var screenshot = new Bitmap(stream))
                {
                    ImageRegion fruitRegion = Fruit2048ScreenProfile.GetRecognitionCellRegion(
                        capture.BoardBounds, row, column);
                    ImageRegion badgeRegion = Fruit2048ScreenProfile.GetTierBadgeRegion(fruitRegion);
                    fruit = FruitTileFingerprint.Create(screenshot, fruitRegion);
                    badge = FruitTileFingerprint.Create(screenshot, badgeRegion);
                }
                if (fruit == null || badge == null || string.IsNullOrEmpty(badge.AverageHash))
                    return Task.FromResult(new Fruit2048LearningResult
                    { Action = Fruit2048LearningAction.ConflictRejected, Error = "Không thể đọc badge tier của ô đã chọn." });
                string evidenceId = "manual-badge:" + capture.DeviceName + ":" + tier + ":"
                    + row + ":" + column + ":" + badge.AverageHash;
                Fruit2048LearningResult result = learning.ObserveBadgeBootstrap(tier, fruit, badge,
                    evidenceId, row, column);
                Log(capture.DeviceName, capture.ScreenWidth, capture.ScreenHeight, "BadgePersist", row,
                    column, "Tier" + tier, result.Action != Fruit2048LearningAction.ConflictRejected,
                    Fingerprint(badge), result.Evidence);
                return Task.FromResult(result);
            }
            catch (Exception exception) when (!(exception is OperationCanceledException))
            {
                Log(capture.DeviceName, capture.ScreenWidth, capture.ScreenHeight, "BadgePersist", row,
                    column, "Tier" + tier, false, string.Empty, exception.Message);
                return Task.FromResult(new Fruit2048LearningResult
                { Action = Fruit2048LearningAction.ConflictRejected, Error = exception.Message });
            }
        }

        private bool VerifyFreshSelections(Fruit2048CalibrationCapture fresh, int emptyRow, int emptyColumn, int tier1Row, int tier1Column)
        {
            using (var stream = new MemoryStream(fresh.ScreenshotPng, false))
            using (var screenshot = new Bitmap(stream))
            {
                // Read each cell with the exact runtime crop geometry; only the two selected
                // bootstrap labels are required to pass this immediate calibration check.
                for (int row = 0; row < 4; row++)
                for (int column = 0; column < 4; column++)
                    learning.Recognize(CreateFingerprint(CropCell(screenshot, fresh.BoardBounds, row, column)));
                FruitTileRecognitionResult empty = learning.Recognize(CreateFingerprint(CropCell(screenshot, fresh.BoardBounds, emptyRow, emptyColumn)));
                FruitTileRecognitionResult tier1 = learning.Recognize(CreateFingerprint(CropCell(screenshot, fresh.BoardBounds, tier1Row, tier1Column)));
                return empty.Tier == 0 && tier1.Tier == 1;
            }
        }

        private static byte[] CropCell(Bitmap screenshot, ImageRegion board, int row, int column)
        {
            ImageRegion region = Fruit2048ScreenProfile.GetRecognitionCellRegion(board, row, column);
            using (var crop = new Bitmap(region.Width, region.Height))
            using (Graphics graphics = Graphics.FromImage(crop))
            using (var stream = new MemoryStream())
            {
                graphics.DrawImage(screenshot, new Rectangle(0, 0, crop.Width, crop.Height),
                    new Rectangle(region.X, region.Y, region.Width, region.Height), GraphicsUnit.Pixel);
                crop.Save(stream, ImageFormat.Png);
                return stream.ToArray();
            }
        }

        private static FruitTileVisualFingerprint CreateFingerprint(byte[] png)
        {
            using (var stream = new MemoryStream(png, false))
            using (var bitmap = new Bitmap(stream))
                return Fruit2048CellVisualNormalizer.CreateFingerprint(bitmap, new ImageRegion(0, 0, bitmap.Width, bitmap.Height));
        }

        private static bool IsUsable(byte[] png, FruitTileVisualFingerprint fingerprint)
        {
            return png != null && png.Length > 32 && fingerprint != null
                && !string.IsNullOrEmpty(fingerprint.AverageHash) && !string.IsNullOrEmpty(fingerprint.EdgeHash);
        }

        private static bool HasVisualVariance(byte[] png)
        {
            using (var stream = new MemoryStream(png, false))
            using (var bitmap = new Bitmap(stream))
            {
                if (bitmap.Width < 8 || bitmap.Height < 8) return false;
                long total = 0, totalSquared = 0, count = 0;
                for (int y = 0; y < bitmap.Height; y += Math.Max(1, bitmap.Height / 16))
                for (int x = 0; x < bitmap.Width; x += Math.Max(1, bitmap.Width / 16))
                {
                    Color color = bitmap.GetPixel(x, y);
                    int luma = (color.R * 30 + color.G * 59 + color.B * 11) / 100;
                    total += luma; totalSquared += luma * luma; count++;
                }
                double mean = total / (double)count;
                return totalSquared / (double)count - mean * mean >= 2.0;
            }
        }

        private static bool IsTooSimilar(FruitTileVisualFingerprint left, FruitTileVisualFingerprint right)
        {
            return FruitFingerprintDistance.Hamming(left.AverageHash, right.AverageHash) <= 2
                && FruitFingerprintDistance.Hamming(left.EdgeHash, right.EdgeHash) <= 2;
        }
        private static bool ValidCell(int row, int column) => row >= 0 && row < 4 && column >= 0 && column < 4;
        private static string Fingerprint(FruitTileVisualFingerprint value) => value?.AverageHash ?? string.Empty;
        private static Fruit2048CalibrationCapture Failure(string error) => new Fruit2048CalibrationCapture { Error = error };
        private static Fruit2048CalibrationResult Result(bool success, string message) => new Fruit2048CalibrationResult
        { Success = success, BootstrapState = success ? Fruit2048BootstrapState.ReadyToLearn : Fruit2048BootstrapState.MissingSeedAssets, Message = message, Error = success ? string.Empty : message };
        private void Log(string device, int width, int height, string stage, int row, int column, string seed, bool success, string fingerprint, string outcome) => logger?.Info(
            $"[Fruit2048 Calibration] DeviceName='{device}', Resolution='{width}x{height}', Stage='{stage}', BoardResolved={width > 0}, SelectedRow={row}, SelectedColumn={column}, SeedType='{seed}', Fingerprint='{fingerprint}', ValidationSuccess={success}, StoragePath='{templates.CalibratedSeeds.RootPath}', Outcome='{outcome}', Error='{(success ? string.Empty : outcome)}'");
    }
}
