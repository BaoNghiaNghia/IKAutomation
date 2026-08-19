using IK_Auto_ADB.Core.Abstractions;
using IK_Auto_ADB.Core.Diagnostics;
using IK_Auto_ADB.Core.Fruit2048;
using IK_Auto_ADB.Core.Vision;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace IK_Auto_ADB.Infrastructure.Fruit2048
{
    public sealed class Fruit2048BoardReader : IFruit2048BoardReader, IFruit2048BootstrapSessionController
    {
        // The initial board has already established its 14 Empty / 2 Tier 1
        // structure before this margin is considered.  Keep this separate from
        // the normal seed threshold: the decorative bottom row is visibly
        // darker than the packaged Empty seed.
        private const double InitialFreshBoardEmptyMargin = 0.03d;
        private readonly IFrameCapturingLdPlayerClient frameClient;
        private readonly ILdPlayerClient playerClient;
        private readonly IFrameImageMatcher matcher;
        private readonly Fruit2048TemplateCatalog catalog;
        private readonly Fruit2048ScreenProfile profile;
        private readonly IFruitTileLearningCatalog learningCatalog;
        private readonly IDiagnosticLogger logger;
        private readonly object bootstrapSync = new object();
        private readonly HashSet<string> initialBootstrapPending = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

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

        public void BeginInitialBootstrap(string deviceName)
        {
            if (string.IsNullOrWhiteSpace(deviceName)) return;
            lock (bootstrapSync) initialBootstrapPending.Add(deviceName);
        }

        public void CompleteInitialBootstrap(string deviceName)
        {
            if (string.IsNullOrWhiteSpace(deviceName)) return;
            lock (bootstrapSync) initialBootstrapPending.Remove(deviceName);
        }

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
                using (CapturedFrame frame = await CaptureFrameWithOneEndpointRefreshAsync(deviceName,
                    cancellationToken))
                {
                    ImageRegion boardRegion = profile.Scale(profile.BoardRegion, frame.Width, frame.Height);
                    ImageRegion boardAnchorRegion = profile.Scale(profile.BoardAnchorRegion, frame.Width, frame.Height);
                    byte[] boardTemplate;
                    ImageMatchResult boardAnchorMatch = ImageMatchResult.NotFound();
                    if (!catalog.TryGet(Fruit2048TemplateCatalog.NavigationBoardAnchor, out boardTemplate)
                        || !(boardAnchorMatch = matcher.Find(frame, boardTemplate, boardAnchorRegion)).Found)
                        return Failure(Fruit2048ScreenStatus.NotOpen, stopwatch,
                            "Không tìm thấy màn hình Lễ Hội Trái Cây.", missing);

                    byte[] emptySeed = null;
                    byte[] tier1Seed = null;
                    catalog.TryGetTile(0, out emptySeed);
                    catalog.TryGetTile(1, out tier1Seed);
                    bool initialBootstrap;
                    lock (bootstrapSync) initialBootstrap = initialBootstrapPending.Contains(deviceName);
                    var cells = new List<Fruit2048Cell>(16);
                    int directTier1Matches = 0;
                    for (int row = 0; row < 4; row++)
                    for (int column = 0; column < 4; column++)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        ImageRegion fullCellRegion = Fruit2048ScreenProfile.GetCellRegion(
                            boardRegion, row, column);
                        ImageRegion cellRegion = Fruit2048ScreenProfile.GetRecognitionCellRegion(
                            boardRegion, row, column);
                        FruitTileVisualFingerprint fingerprint = Fruit2048CellVisualNormalizer.CreateFingerprint(
                            frame.Bitmap, cellRegion);
                        // The number badge is the game value.  Prefer it whenever a
                        // calibrated number match is reliable; fruit artwork remains
                        // only the fallback needed for empty cells and unseen badges.
                        ImageRegion badgeRegion = Fruit2048ScreenProfile.GetTierBadgeRegion(cellRegion);
                        FruitTileVisualFingerprint badgeFingerprint = Fruit2048CellVisualNormalizer.CreateFingerprint(
                            frame.Bitmap, badgeRegion);
                        FruitTierBadgeRecognitionResult badgeRecognition = learningCatalog.RecognizeBadge(
                            badgeFingerprint);
                        // A persisted badge prototype may have been collected from a
                        // transient/incorrect earlier board.  The real Tier 1 seed is
                        // the only authoritative visual bootstrap label, so let its
                        // focused cell match correct a conflicting higher badge before
                        // creating a simulated transition.  Without this guard a Tier 1
                        // apple can be read as Tier 2 and make the very first swipe look
                        // structurally invalid.
                        FruitTileRecognitionResult tier1SeedOverride = badgeRecognition.IsReliable
                                && badgeRecognition.Tier.GetValueOrDefault() > 1
                            ? TryRecognizeTier1Seed(frame, fullCellRegion, tier1Seed)
                            : null;
                        FruitTileRecognitionResult recognition = tier1SeedOverride ?? (badgeRecognition.IsReliable
                            ? new FruitTileRecognitionResult
                            {
                                Tier = badgeRecognition.Tier,
                                Confidence = badgeRecognition.Confidence,
                                Source = Fruit2048RecognitionSource.TierBadgeBootstrap
                            }
                            : learningCatalog.Recognize(fingerprint));
                        // Seed diagnostics and the image-template fallback are only useful
                        // while bootstrapping or when the fast recognizers could not decide.
                        // Once a badge/prototype has a confident result, do not spend another
                        // per-cell template match on Empty/Tier 1 during every Auto move.
                        bool needsStaticSeedFallback = tier1SeedOverride == null
                            && !badgeRecognition.IsReliable
                            && (initialBootstrap || !recognition.Tier.HasValue);
                        FruitSeedRecognitionDiagnostics seedDiagnostics = tier1SeedOverride == null
                            && !badgeRecognition.IsReliable
                            && (initialBootstrap || !recognition.Tier.HasValue)
                            ? learningCatalog.DiagnoseSeedRecognition(fingerprint)
                            : null;
                        FruitTileRecognitionResult bootstrapRecognition = null;
                        if (needsStaticSeedFallback)
                        {
                            bootstrapRecognition = TryRecognizeBootstrapSeed(
                                frame, fullCellRegion, emptySeed, tier1Seed);

                            // Count the two real Tier 1 seeds independently of
                            // the fingerprint result. One apple can be recognized
                            // by the fingerprint while the other needs the native
                            // template; both still prove the safe fresh-board shape.
                            if (bootstrapRecognition?.Tier == 1) directTier1Matches++;
                        }
                        else if (tier1SeedOverride != null)
                        {
                            directTier1Matches++;
                        }
                        if (!recognition.Tier.HasValue)
                        {
                            if (bootstrapRecognition != null)
                            {
                                recognition = bootstrapRecognition;
                                if (seedDiagnostics != null)
                                    seedDiagnostics.Reason = "MatchedStaticSeedInCellRoi";
                            }
                        }
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
                            Fingerprint = fingerprint,
                            BadgeBounds = badgeRegion,
                            BadgeFingerprint = badgeFingerprint,
                            BadgeConfidence = badgeRecognition.Confidence,
                            BadgeBestTier = badgeRecognition.Tier,
                            BadgeSecondBestTier = badgeRecognition.SecondBestTier,
                            BadgeSecondBestConfidence = badgeRecognition.SecondBestConfidence,
                            SeedDiagnostics = seedDiagnostics,
                        });
                    }

                    bool freshBoardCompleted = initialBootstrap
                        && CompleteFreshSeedBoard(cells, directTier1Matches);
                    if (freshBoardCompleted)
                    {
                        // The static Empty seed is normally from an upper cell, while the
                        // board decoration varies noticeably by row.  Keep the bootstrap
                        // bounded (static seed + at most one proven Empty per row), but do
                        // not let row-major ordering discard the bottom-row variation.
                        Fruit2048Cell[] bootstrapEmptySamples = cells.Where(cell => cell.Tier == 0
                                && cell.Fingerprint != null)
                            .GroupBy(cell => cell.Row)
                            .Select(row => row.OrderBy(cell => cell.SeedDiagnostics?.EmptyScore ?? 1d)
                                .ThenBy(cell => cell.Column)
                                .First())
                            .ToArray();
                        foreach (Fruit2048Cell empty in bootstrapEmptySamples)
                            learningCatalog.ObserveBootstrapEmptyPrototype(empty.Fingerprint);

                        string bootstrapRows = string.Join(",", bootstrapEmptySamples
                            .Select(cell => cell.Row).OrderBy(row => row));
                        logger?.Info($"[Fruit2048 Bootstrap] DeviceName='{deviceName}', "
                            + "Completed=true, Reason='InitialFreshBoardConsensus', "
                            + $"EmptyPrototypeRows='{bootstrapRows}'");
                    }
                    else if (initialBootstrap)
                    {
                        logger?.Info($"[Fruit2048 Bootstrap] DeviceName='{deviceName}', "
                            + $"DirectTier1Matches={directTier1Matches}, ConfirmedEmptyCells={cells.Count(cell => cell.Tier == 0)}, "
                            + $"ConfirmedTier1Cells={cells.Count(cell => cell.Tier == 1)}, "
                            + $"UnknownCells={cells.Count(cell => !cell.Tier.HasValue)}, "
                            + "Completed=false, Reason='InitialFreshBoardConsensusNotMet'");
                    }

                    stopwatch.Stop();
                    Fruit2048BoardReadResult result = Fruit2048BoardAssembler.Assemble(
                        cells, stopwatch.ElapsedMilliseconds);
                    // Encoding a full PNG is relatively expensive. Normal reads, including
                    // bounded post-swipe animation retries, only need the decoded frame long
                    // enough for recognition. The runtime explicitly requests a retained
                    // image when it is about to persist a terminal diagnostic.
                    if (retainOriginalScreenshot)
                        result.OriginalScreenshotPng = frame.GetPngBytes();
                    result.MissingAssets = missing;
                    result.BoardRegion = boardRegion;
                    result.ScreenWidth = frame.Width;
                    result.ScreenHeight = frame.Height;
                    result.CaptureFingerprint = CreateCaptureFingerprint(frame, boardRegion);
                    foreach (Fruit2048Cell cell in cells.Where(cell => cell.Tier.HasValue && cell.Tier.Value > 0))
                        learningCatalog.ObserveTier(cell.Tier.Value);
                    result.Learning = learningCatalog.Snapshot;
                    result.FastPathCells = cells.Count(cell =>
                        cell.RecognitionSource == Fruit2048RecognitionSource.FastFingerprint);
                    result.StaticCells = cells.Count(cell => cell.RecognitionSource == Fruit2048RecognitionSource.StaticSeed);
                    result.PrototypeCells = cells.Count(cell => cell.RecognitionSource == Fruit2048RecognitionSource.PrototypeMatch);
                    result.BadgeBootstrapCells = cells.Count(cell =>
                        cell.RecognitionSource == Fruit2048RecognitionSource.TierBadgeBootstrap);
                    result.FallbackCells = result.StaticCells + result.PrototypeCells;
                    result.EmptyCells = cells.Count(cell => cell.Tier == 0);
                    result.Tier1Cells = cells.Count(cell => cell.Tier == 1);
                    double[] emptyScores = cells.Select(cell => cell.SeedDiagnostics?.EmptyScore ?? 0d).ToArray();
                    result.EmptyScoreMin = emptyScores.Min();
                    result.EmptyScoreAverage = emptyScores.Average();
                    result.EmptyScoreMax = emptyScores.Max();
                    result.BootstrapState = result.UnknownCells.Count > 0
                        ? initialBootstrap
                            ? Fruit2048BootstrapState.NeedsFreshBoard
                            : Fruit2048BootstrapState.Learning
                        : result.Learning.Mode == Fruit2048RecognitionMode.Fast
                            ? Fruit2048BootstrapState.Ready
                            : result.Learning.HighestObservedTier <= 1
                                ? Fruit2048BootstrapState.ReadyToLearn
                                : Fruit2048BootstrapState.Learning;
                    LogBoard(deviceName, result, boardAnchorMatch);
                    return result;
                }
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception exception)
            {
                logger?.Error("[Fruit2048 Board] Capture/read failed.", exception);
                return Failure(IsDeviceUnavailable(exception)
                        ? Fruit2048ScreenStatus.DeviceUnavailable
                        : Fruit2048ScreenStatus.CaptureUnavailable, stopwatch,
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

        private async Task<CapturedFrame> CaptureFrameWithOneEndpointRefreshAsync(string deviceName,
            CancellationToken cancellationToken)
        {
            try { return await frameClient.CaptureFrameAsync(deviceName, cancellationToken); }
            catch (Exception exception) when (IsDeviceUnavailable(exception))
            {
                IAdbEndpointRefreshable refreshable = playerClient as IAdbEndpointRefreshable;
                if (refreshable == null || !await refreshable.RefreshAdbEndpointAsync(deviceName, cancellationToken))
                    throw;
                return await frameClient.CaptureFrameAsync(deviceName, cancellationToken);
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

        /// <summary>
        /// Fingerprints are deliberately conservative.  A real static seed can
        /// however move a few pixels inside its wooden board cell, so an unknown
        /// cell gets one focused native-template check before it is rejected.
        /// Recognized Tier 1 cells are also checked to establish the two real
        /// seed anchors needed for a fresh-board completion. This never overrides
        /// a learned/prototype result and only recognizes bootstrap seed labels.
        /// </summary>
        private FruitTileRecognitionResult TryRecognizeBootstrapSeed(CapturedFrame frame,
            ImageRegion fullCellRegion, byte[] emptySeed, byte[] tier1Seed)
        {
            ImageMatchResult emptyMatch = emptySeed == null
                ? ImageMatchResult.NotFound()
                : matcher.Find(frame, emptySeed, fullCellRegion);
            ImageMatchResult tier1Match = tier1Seed == null
                ? ImageMatchResult.NotFound()
                : matcher.Find(frame, tier1Seed, fullCellRegion);

            // Require one unambiguous seed match; Empty must never win merely
            // because its background is present behind an actual fruit.
            if (emptyMatch.Found == tier1Match.Found) return null;
            ImageMatchResult accepted = emptyMatch.Found ? emptyMatch : tier1Match;
            return new FruitTileRecognitionResult
            {
                Tier = emptyMatch.Found ? 0 : 1,
                Confidence = accepted.Confidence ?? 1d,
                Source = Fruit2048RecognitionSource.StaticSeed
            };
        }

        private FruitTileRecognitionResult TryRecognizeTier1Seed(CapturedFrame frame,
            ImageRegion fullCellRegion, byte[] tier1Seed)
        {
            if (tier1Seed == null) return null;
            ImageMatchResult match = matcher.Find(frame, tier1Seed, fullCellRegion);
            if (!match.Found) return null;
            return new FruitTileRecognitionResult
            {
                Tier = 1,
                Confidence = match.Confidence ?? 1d,
                Source = Fruit2048RecognitionSource.StaticSeed
            };
        }

        /// <summary>
        /// A newly refreshed Fruit board is structurally 14 Empty + 2 Tier1.
        /// Once both real Tier1 seed templates are visible and at least two
        /// independent Empty cells were recognized, residual cells may only be
        /// Empty.  This covers the decorative bottom-row Empty variation without
        /// weakening global prototype thresholds or guessing higher tiers.
        /// </summary>
        private static bool CompleteFreshSeedBoard(IList<Fruit2048Cell> cells,
            int directTier1Matches)
        {
            if (cells == null || cells.Count != 16 || directTier1Matches != 2) return false;
            if (cells.Any(cell => cell.Tier.HasValue && cell.Tier.Value > 1)) return false;
            if (cells.Count(cell => cell.Tier == 0) < 2) return false;
            if (cells.Count(cell => cell.Tier == 1) != 2) return false;

            foreach (Fruit2048Cell cell in cells.Where(cell => !cell.Tier.HasValue))
            {
                FruitSeedRecognitionDiagnostics diagnostics = cell.SeedDiagnostics;
                if (diagnostics == null || diagnostics.EmptyScore <= diagnostics.Tier1Score
                    || diagnostics.EmptyScore - diagnostics.Tier1Score < InitialFreshBoardEmptyMargin)
                    return false;
            }

            foreach (Fruit2048Cell cell in cells.Where(cell => !cell.Tier.HasValue))
            {
                cell.Tier = 0;
                cell.Value = FruitTierCatalog.ToValue(0);
                cell.Confidence = 1d;
                cell.RecognitionSource = Fruit2048RecognitionSource.StaticSeed;
                if (cell.SeedDiagnostics != null)
                    cell.SeedDiagnostics.Reason = "InitialFreshBoardConsensus";
            }
            return true;
        }

        private static string CreateCaptureFingerprint(CapturedFrame frame, ImageRegion boardRegion)
        {
            FruitTileVisualFingerprint fingerprint = Fruit2048CellVisualNormalizer.CreateFingerprint(
                frame.Bitmap, boardRegion);
            return (fingerprint?.AverageHash ?? string.Empty) + ":" + (fingerprint?.EdgeHash ?? string.Empty);
        }

        private static bool IsDeviceUnavailable(Exception exception)
        {
            string message = exception?.ToString() ?? string.Empty;
            return message.IndexOf("device '", StringComparison.OrdinalIgnoreCase) >= 0
                    && message.IndexOf("not found", StringComparison.OrdinalIgnoreCase) >= 0
                || message.IndexOf("device offline", StringComparison.OrdinalIgnoreCase) >= 0
                || message.IndexOf("no devices/emulators found", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private void LogBoard(string deviceName, Fruit2048BoardReadResult result, ImageMatchResult boardAnchorMatch)
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
                + $"Mode='{learning.Mode}', StaticCells={result.StaticCells}, FastPathCells={result.FastPathCells}, "
                + $"PrototypeCells={result.PrototypeCells}, BadgeBootstrapCells={result.BadgeBootstrapCells}, "
                + $"UnknownCells={result.UnknownCells.Count}, "
                + $"RecognitionDurationMs={result.DurationMs}");
            logger?.Info($"[Fruit2048 Board Geometry] DeviceName='{deviceName}', BoardBounds='{FormatRegion(result.BoardRegion)}', "
                + $"BoardAnchorBounds='{boardAnchorMatch.X},{boardAnchorMatch.Y},{boardAnchorMatch.Width},{boardAnchorMatch.Height}', BoardAnchorIsStructural=true, "
                + "CellBounds='" + string.Join(";", result.Cells.Select(cell => "R" + cell.Row + "C" + cell.Column + "=" + FormatRegion(cell.Bounds))) + "'");
            logger?.Info($"[Fruit2048 Seed Distribution] DeviceName='{deviceName}', EmptyCells={result.EmptyCells}, "
                + $"Tier1Cells={result.Tier1Cells}, EmptyScoreMin={result.EmptyScoreMin:F3}, "
                + $"EmptyScoreAverage={result.EmptyScoreAverage:F3}, EmptyScoreMax={result.EmptyScoreMax:F3}");
            // Per-cell logs are useful only for an unresolved read.  Keeping
            // them out of successful Learning/Hybrid moves avoids 16 expensive
            // log writes per frame while the UI receives its compact progress.
            bool detailed = !result.Success;
            if (!detailed) return;
            foreach (Fruit2048Cell cell in result.Cells)
            {
                FruitSeedRecognitionDiagnostics d = cell.SeedDiagnostics ?? new FruitSeedRecognitionDiagnostics();
                string outcome = cell.Tier == 0 ? "Empty" : cell.Tier == 1 ? "Tier1"
                    : cell.Tier.HasValue ? "Tier" + cell.Tier.Value : "Unknown";
                bool primaryThresholdPassed = d.BestScore >= d.Threshold;
                logger?.Info($"[Fruit2048 Cell Recognition] DeviceName='{deviceName}', Row={cell.Row}, Column={cell.Column}, "
                    + $"CellBounds='{FormatRegion(cell.Bounds)}', CropWidth={cell.Bounds.Width}, CropHeight={cell.Bounds.Height}, "
                    + $"NormalizedWidth={Fruit2048CellVisualNormalizer.CanonicalWidth}, NormalizedHeight={Fruit2048CellVisualNormalizer.CanonicalHeight}, "
                    + $"EmptyScore={d.EmptyScore:F3}, Tier1Score={d.Tier1Score:F3}, PrimaryScoreClass='{d.BestClass}', "
                    + $"BestScore={d.BestScore:F3}, SecondBestScore={d.SecondBestScore:F3}, Margin={d.Margin:F3}, "
                    + $"PrimaryThresholdPassed={primaryThresholdPassed}, SecondaryMatcher='StaticSeedInCellRoi', "
                    + $"SecondaryMatcherResult='{d.Reason}', FinalClass='{outcome}', "
                    + $"FinalDecisionSource='{cell.RecognitionSource}', FinalConfidenceAvailable={cell.Confidence > 0d}, "
                    + $"Threshold={d.Threshold:F3}, Reason='{d.Reason}'");
            }
        }

        private static string FormatRegion(ImageRegion? region) => region.HasValue ? FormatRegion(region.Value) : string.Empty;
        private static string FormatRegion(ImageRegion region) => region.X + "," + region.Y + "," + region.Width + "," + region.Height;
    }
}
