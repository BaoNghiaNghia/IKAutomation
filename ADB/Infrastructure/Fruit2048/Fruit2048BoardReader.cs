using ADB_Tool_Automation_Post_FB.Core.Abstractions;
using ADB_Tool_Automation_Post_FB.Core.Diagnostics;
using ADB_Tool_Automation_Post_FB.Core.Fruit2048;
using ADB_Tool_Automation_Post_FB.Core.Vision;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace ADB_Tool_Automation_Post_FB.Infrastructure.Fruit2048
{
    public sealed class Fruit2048BoardReader : IFruit2048BoardReader
    {
        private readonly IFrameCapturingLdPlayerClient frameClient;
        private readonly ILdPlayerClient playerClient;
        private readonly IFrameImageMatcher matcher;
        private readonly Fruit2048TemplateCatalog catalog;
        private readonly Fruit2048ScreenProfile profile;
        private readonly IFruitTileLearningCatalog learningCatalog;
        private readonly IDiagnosticLogger logger;

        public Fruit2048BoardReader(ILdPlayerClient playerClient,
            IFrameCapturingLdPlayerClient frameClient, IFrameImageMatcher matcher,
            Fruit2048TemplateCatalog catalog, Fruit2048ScreenProfile profile,
            IFruitTileLearningCatalog learningCatalog, IDiagnosticLogger logger)
        {
            this.playerClient = playerClient ?? throw new ArgumentNullException(nameof(playerClient));
            this.frameClient = frameClient ?? throw new ArgumentNullException(nameof(frameClient));
            this.matcher = matcher ?? throw new ArgumentNullException(nameof(matcher));
            this.catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
            this.profile = profile ?? throw new ArgumentNullException(nameof(profile));
            this.learningCatalog = learningCatalog ?? throw new ArgumentNullException(nameof(learningCatalog));
            this.logger = logger;
        }

        public Fruit2048SeedAvailability SeedAvailability => catalog.SeedAvailability;

        public async Task<Fruit2048BoardReadResult> ReadAsync(string deviceName,
            CancellationToken cancellationToken)
        {
            return await ReadAsync(deviceName, false, cancellationToken);
        }

        public async Task<Fruit2048BoardReadResult> ReadAsync(string deviceName,
            bool retainOriginalScreenshot, CancellationToken cancellationToken)
        {
            var stopwatch = Stopwatch.StartNew();
            IReadOnlyList<string> missing = catalog.MissingAssets;
            if (missing.Count > 0)
                return Failure(Fruit2048ScreenStatus.NotOpen, stopwatch,
                    "Thiếu template Fruit 2048: " + string.Join(", ", missing), missing);
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                using (CapturedFrame frame = await frameClient.CaptureFrameAsync(deviceName,
                    cancellationToken))
                {
                    ImageRegion boardRegion = profile.Scale(profile.BoardRegion, frame.Width, frame.Height);
                    byte[] boardTemplate;
                    if (!catalog.TryGet(Fruit2048TemplateCatalog.NavigationBoardAnchor, out boardTemplate)
                        || !matcher.Find(frame, boardTemplate, boardRegion).Found)
                        return Failure(Fruit2048ScreenStatus.NotOpen, stopwatch,
                            "Không tìm thấy màn hình Lễ Hội Trái Cây.", missing);

                    var cells = new List<Fruit2048Cell>(16);
                    for (int row = 0; row < 4; row++)
                    for (int column = 0; column < 4; column++)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        ImageRegion cellRegion = Fruit2048ScreenProfile.GetCellRegion(boardRegion, row, column);
                        FruitTileVisualFingerprint fingerprint = FruitTileFingerprint.Create(
                            frame.Bitmap, cellRegion);
                        FruitTileRecognitionResult recognition = learningCatalog.Recognize(fingerprint);
                        int? value = recognition.Tier.HasValue
                            ? (int?)FruitTierCatalog.ToValue(recognition.Tier.Value) : null;
                        cells.Add(new Fruit2048Cell
                        {
                            Row = row,
                            Column = column,
                            Bounds = cellRegion,
                            Tier = recognition.Tier,
                            Value = value,
                            Confidence = recognition.Confidence,
                            RecognitionSource = recognition.Source,
                            Fingerprint = fingerprint
                        });
                    }

                    stopwatch.Stop();
                    Fruit2048BoardReadResult result = Fruit2048BoardAssembler.Assemble(
                        cells, stopwatch.ElapsedMilliseconds);
                    if (retainOriginalScreenshot || !result.Success)
                        result.OriginalScreenshotPng = frame.GetPngBytes();
                    result.MissingAssets = missing;
                    result.BoardRegion = boardRegion;
                    result.ScreenWidth = frame.Width;
                    result.ScreenHeight = frame.Height;
                    foreach (Fruit2048Cell cell in cells.Where(cell => cell.Tier.HasValue && cell.Tier.Value > 0))
                        learningCatalog.ObserveTier(cell.Tier.Value);
                    result.Learning = learningCatalog.Snapshot;
                    result.FastPathCells = cells.Count(cell =>
                        cell.RecognitionSource == Fruit2048RecognitionSource.FastFingerprint);
                    result.FallbackCells = cells.Count(cell =>
                        cell.RecognitionSource == Fruit2048RecognitionSource.PrototypeMatch
                        || cell.RecognitionSource == Fruit2048RecognitionSource.StaticSeed);
                    result.BootstrapState = result.UnknownCells.Count > 0
                        ? Fruit2048BootstrapState.NeedsFreshBoard
                        : result.Learning.Mode == Fruit2048RecognitionMode.Fast
                            ? Fruit2048BootstrapState.Ready
                            : result.Learning.HighestObservedTier <= 1
                                ? Fruit2048BootstrapState.ReadyToLearn
                                : Fruit2048BootstrapState.Learning;
                    LogBoard(deviceName, result);
                    return result;
                }
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception exception)
            {
                logger?.Error("[Fruit2048 Board] Capture/read failed.", exception);
                return Failure(Fruit2048ScreenStatus.CaptureUnavailable, stopwatch,
                    exception.Message, missing);
            }
        }

        public async Task<bool> TryTapRefreshAsync(string deviceName,
            CancellationToken cancellationToken)
        {
            byte[] template;
            if (!catalog.TryGet(Fruit2048TemplateCatalog.RefreshButton, out template)) return false;
            using (CapturedFrame frame = await frameClient.CaptureFrameAsync(deviceName, cancellationToken))
            {
                ImageRegion roi = profile.Scale(profile.RefreshRegion, frame.Width, frame.Height);
                ImageMatchResult match = matcher.Find(frame, template, roi);
                if (!match.Found) return false;
                await playerClient.TapAsync(deviceName, match.CenterX, match.CenterY, cancellationToken);
                return true;
            }
        }

        private Fruit2048BoardReadResult Failure(Fruit2048ScreenStatus status,
            Stopwatch stopwatch, string error, IReadOnlyList<string> missing)
        {
            stopwatch.Stop();
            return new Fruit2048BoardReadResult
            {
                ScreenStatus = status,
                Success = false,
                Cells = new Fruit2048Cell[0],
                UnknownCells = new Fruit2048Cell[0],
                MissingAssets = missing,
                DurationMs = stopwatch.ElapsedMilliseconds,
                Error = error,
                BootstrapState = catalog.SeedAvailability.IsReady
                    ? Fruit2048BootstrapState.ReadyToLearn
                    : Fruit2048BootstrapState.MissingSeedAssets,
                Learning = learningCatalog.Snapshot
            };
        }

        private void LogBoard(string deviceName, Fruit2048BoardReadResult result)
        {
            logger?.Info($"[Fruit2048 Board] DeviceName='{deviceName}', "
                + $"Values='{result.Board}', HighestTile={result.HighestTile}, "
                + $"UnknownCells={result.UnknownCells.Count}, ReadSuccess={result.Success}, "
                + $"DurationMs={result.DurationMs}");
            Fruit2048LearningSnapshot learning = result.Learning ?? learningCatalog.Snapshot;
            logger?.Info($"[Fruit2048 Recognition] DeviceName='{deviceName}', "
                + $"Mode='{learning.Mode}', KnownCells={result.Cells.Count - result.UnknownCells.Count}, "
                + $"UnknownCells={result.UnknownCells.Count}, HighestObservedTier={learning.HighestObservedTier}, "
                + $"DurationMs={result.DurationMs}");
            logger?.Info($"[Fruit2048 Runtime Recognition] DeviceName='{deviceName}', "
                + $"Mode='{learning.Mode}', FastPathCells={result.FastPathCells}, "
                + $"FallbackCells={result.FallbackCells}, UnknownCells={result.UnknownCells.Count}, "
                + $"RecognitionDurationMs={result.DurationMs}");
        }
    }
}
