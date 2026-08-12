using ADB_Tool_Automation_Post_FB.Core.Abstractions;
using ADB_Tool_Automation_Post_FB.Core.Diagnostics;
using ADB_Tool_Automation_Post_FB.Core.Fruit2048;
using ADB_Tool_Automation_Post_FB.Core.Vision;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace ADB_Tool_Automation_Post_FB.Infrastructure.Fruit2048
{
    /// <summary>Focused City → Festival → Fruit 2048 board navigation; it owns no gameplay state.</summary>
    public sealed class Fruit2048NavigationService : IFruit2048NavigationService
    {
        private static readonly int[] Waits = { 500, 1000, 1600 };
        private readonly ILdPlayerClient player;
        private readonly IFrameCapturingLdPlayerClient frames;
        private readonly IFrameImageMatcher matcher;
        private readonly Fruit2048TemplateCatalog templates;
        private readonly Fruit2048ScreenProfile profile;
        private readonly IDiagnosticLogger logger;
        private readonly Fruit2048LearningDiagnosticStore diagnostics;
        private readonly Fruit2048BoardReadyDetector boardReadyDetector;

        public Fruit2048NavigationService(ILdPlayerClient player, IFrameCapturingLdPlayerClient frames,
            IFrameImageMatcher matcher, Fruit2048TemplateCatalog templates,
            Fruit2048ScreenProfile profile, IDiagnosticLogger logger,
            Fruit2048LearningDiagnosticStore diagnostics = null)
        {
            this.player = player; this.frames = frames; this.matcher = matcher;
            this.templates = templates; this.profile = profile; this.logger = logger;
            this.diagnostics = diagnostics;
            boardReadyDetector = new Fruit2048BoardReadyDetector(templates, profile);
        }

        public async Task<Fruit2048NavigationResult> EnsureFruit2048ScreenAsync(string deviceName,
            IProgress<string> status, CancellationToken cancellationToken)
        {
            NavigationAttemptState lastAttempt = null;
            bool fruitTabTapSent = false;
            for (int attempt = 1; attempt <= 2; attempt++)
            {
                var attemptState = new NavigationAttemptState(attempt);
                lastAttempt = attemptState;
                Observation observed = await ObserveAsync(deviceName, attemptState, cancellationToken);
                Log(deviceName, attempt, observed, "Observe", 0, "Observed", 0);
                if (observed.State == Fruit2048NavigationState.BoardReady) return Ready();
                if (observed.State == Fruit2048NavigationState.CaptureUnavailable)
                {
                    await PersistTerminalFailureAsync(deviceName, observed.Error ?? "NavigationTemplateInvalid", attemptState, cancellationToken);
                    return Failed(observed.Error);
                }
                if (observed.State == Fruit2048NavigationState.FruitFestivalTabVisible)
                {
                    if (fruitTabTapSent)
                    {
                        if (await WaitForBoardReadyAsync(deviceName, attemptState, cancellationToken) != null) return Ready();
                        continue;
                    }
                    status?.Report("Đang mở Lễ Hội Trái Cây");
                    if (!await TapAsync(deviceName, observed, "TapFruit2048Tab", attemptState, cancellationToken)) continue;
                    fruitTabTapSent = true;
                    if (await WaitForBoardReadyAsync(deviceName, attemptState, cancellationToken) != null) return Ready();
                    continue;
                }
                if (observed.State == Fruit2048NavigationState.CityFestivalEntryVisible)
                {
                    status?.Report("Đang mở sự kiện");
                    if (!await TapAsync(deviceName, observed, "TapEventEntry", attemptState, cancellationToken)) continue;
                    Observation afterEntry = await WaitForAnyAsync(deviceName, attemptState, cancellationToken,
                        Fruit2048NavigationState.FruitFestivalTabVisible,
                        Fruit2048NavigationState.BoardReady);
                    if (afterEntry == null) continue;
                    if (afterEntry.State == Fruit2048NavigationState.BoardReady) return Ready();
                    Observation tab = await ObserveAsync(deviceName, attemptState, cancellationToken);
                    if (tab.State != Fruit2048NavigationState.FruitFestivalTabVisible) continue;
                    status?.Report("Đang mở Lễ Hội Trái Cây");
                    if (fruitTabTapSent)
                    {
                        if (await WaitForBoardReadyAsync(deviceName, attemptState, cancellationToken) != null) return Ready();
                        continue;
                    }
                    if (!await TapAsync(deviceName, tab, "TapFruit2048Tab", attemptState, cancellationToken)) continue;
                    fruitTabTapSent = true;
                    if (await WaitForBoardReadyAsync(deviceName, attemptState, cancellationToken) != null) return Ready();
                }
            }
            status?.Report("Không thể mở Lễ Hội Trái Cây");
            string reason = FailureReasonFor(lastAttempt, fruitTabTapSent);
            await PersistTerminalFailureAsync(deviceName, reason, lastAttempt, cancellationToken);
            return Failed(reason);
        }

        private async Task<Observation> WaitForAnyAsync(string deviceName, NavigationAttemptState attempt,
            CancellationToken cancellationToken, params Fruit2048NavigationState[] expected)
        {
            foreach (int wait in Waits)
            {
                await Task.Delay(wait, cancellationToken);
                Observation observation = await ObserveAsync(deviceName, attempt, cancellationToken);
                if (expected.Contains(observation.State))
                {
                    Log(deviceName, attempt.Number, observation, "Wait", 0, "Ready", wait);
                    return observation;
                }
                Log(deviceName, attempt.Number, observation, "Wait", 0, "Continue", wait);
                if (observation.State == Fruit2048NavigationState.CaptureUnavailable) return null;
            }
            return null;
        }

        /// <summary>
        /// Once the Fruit tab was tapped, the tab can remain visible behind the
        /// open board.  These waits therefore inspect only BoardReady evidence.
        /// </summary>
        private async Task<Observation> WaitForBoardReadyAsync(string deviceName,
            NavigationAttemptState attempt, CancellationToken cancellationToken)
        {
            foreach (int wait in Waits)
            {
                await Task.Delay(wait, cancellationToken);
                Observation observation = await ObserveBoardReadyAsync(deviceName, attempt, cancellationToken);
                if (observation.State == Fruit2048NavigationState.BoardReady)
                {
                    Log(deviceName, attempt.Number, observation, "WaitForBoardReady", 0, "Ready", wait);
                    return observation;
                }
                Log(deviceName, attempt.Number, observation, "WaitForBoardReady", 0, "Continue", wait);
                if (observation.State == Fruit2048NavigationState.CaptureUnavailable) return null;
            }
            return null;
        }

        private async Task<Observation> ObserveAsync(string deviceName, NavigationAttemptState attempt,
            CancellationToken cancellationToken)
        {
            try
            {
                using (CapturedFrame frame = await frames.CaptureFrameAsync(deviceName, cancellationToken))
                {
                    Observation board = ObserveBoardReady(frame, deviceName, attempt);
                    if (board.State != Fruit2048NavigationState.Unknown) return board;

                    byte[] tab;
                    string validationError;
                    if (!TryGetValidatedTemplate(Fruit2048TemplateCatalog.Fruit2048Tab, frame, attempt, out tab, out validationError))
                    {
                        return new Observation { State = Fruit2048NavigationState.CaptureUnavailable, Error = validationError };
                    }
                    ImageMatchResult match;
                    if (TryMatchInExpectedRoi(deviceName, frame, tab, Fruit2048TemplateCatalog.Fruit2048Tab,
                        attempt, out match)) return new Observation { State = Fruit2048NavigationState.FruitFestivalTabVisible, Match = match, Anchor = Fruit2048TemplateCatalog.Fruit2048Tab };

                    // The City entry is independent from the later event-tab and board anchors.
                    // A missing later asset must never stop the direct screenshot/template click here.
                    byte[] city;
                    if (!TryGetValidatedTemplate(Fruit2048TemplateCatalog.CityFestivalEntry, frame, attempt, out city, out validationError))
                    {
                        return new Observation { State = Fruit2048NavigationState.CaptureUnavailable, Error = validationError };
                    }
                    if (TryMatchInExpectedRoi(deviceName, frame, city, Fruit2048TemplateCatalog.CityFestivalEntry,
                        attempt, out match)) return new Observation { State = Fruit2048NavigationState.CityFestivalEntryVisible, Match = match, Anchor = Fruit2048TemplateCatalog.CityFestivalEntry };

                    return new Observation { State = Fruit2048NavigationState.Unknown };
                }
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception exception) { return new Observation { State = Fruit2048NavigationState.CaptureUnavailable, Error = exception.Message }; }
        }

        private async Task<Observation> ObserveBoardReadyAsync(string deviceName, NavigationAttemptState attempt,
            CancellationToken cancellationToken)
        {
            try
            {
                using (CapturedFrame frame = await frames.CaptureFrameAsync(deviceName, cancellationToken))
                    return ObserveBoardReady(frame, deviceName, attempt);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception exception) { return new Observation { State = Fruit2048NavigationState.CaptureUnavailable, Error = exception.Message }; }
        }

        private Observation ObserveBoardReady(CapturedFrame frame, string deviceName, NavigationAttemptState attempt)
        {
            Fruit2048BoardReadyDetection detection = boardReadyDetector.Detect(frame);
            AddBoardReadyDiagnostics(attempt, detection);
            LogBoardReady(deviceName, detection);
            if (detection.IsBoardReady)
            {
                Fruit2048BoardReadyAnchorMatch anchor = detection.AcceptedAnchor;
                return new Observation
                {
                    State = Fruit2048NavigationState.BoardReady,
                    Anchor = anchor.Anchor,
                    Match = ImageMatchResult.FoundAt(anchor.MatchedBounds.X, anchor.MatchedBounds.Y,
                        anchor.MatchedBounds.Width, anchor.MatchedBounds.Height, anchor.Score)
                };
            }
            return detection.CaptureUnavailable
                ? new Observation { State = Fruit2048NavigationState.CaptureUnavailable, Error = detection.Reason }
                : new Observation { State = Fruit2048NavigationState.Unknown, Error = detection.Reason };
        }

        private async Task<bool> TapAsync(string deviceName, Observation observation, string action,
            NavigationAttemptState attempt, CancellationToken cancellationToken)
        {
            if (observation.Match == null || !observation.Match.Found) return false;
            byte[] template;
            if (!templates.TryGet(observation.Anchor, out template)) return false;

            using (CapturedFrame frame = await frames.CaptureFrameAsync(deviceName, cancellationToken))
            {
                ImageMatchResult latest;
                if (!TryMatchInExpectedRoi(deviceName, frame, template, observation.Anchor, attempt, out latest))
                {
                    Log(deviceName, attempt.Number, observation, action, 0, "FreshMatchNotFound", 0);
                    return false;
                }

                observation.Match = latest;
                await player.TapAsync(deviceName, latest.CenterX, latest.CenterY, cancellationToken);
                if (string.Equals(observation.Anchor, Fruit2048TemplateCatalog.Fruit2048Tab, StringComparison.OrdinalIgnoreCase))
                {
                    attempt.FruitTabTapped = true;
                    attempt.FruitTabTapX = latest.CenterX;
                    attempt.FruitTabTapY = latest.CenterY;
                }
                if (string.Equals(observation.Anchor, Fruit2048TemplateCatalog.CityFestivalEntry, StringComparison.OrdinalIgnoreCase)) attempt.CityEntryTapped = true;
                Log(deviceName, attempt.Number, observation, action, latest.CenterX, "Tapped", 0);
            }
            return true;
        }

        private ImageRegion RegionFor(string anchor, int width, int height)
        {
            if (string.Equals(anchor, Fruit2048TemplateCatalog.NavigationBoardAnchor, StringComparison.OrdinalIgnoreCase))
                return profile.Scale(profile.BoardAnchorRegion, width, height);
            if (string.Equals(anchor, Fruit2048TemplateCatalog.NavigationBoardSecondaryAnchor, StringComparison.OrdinalIgnoreCase))
                return profile.Scale(profile.BoardSecondaryAnchorRegion, width, height);
            if (string.Equals(anchor, Fruit2048TemplateCatalog.Fruit2048Tab, StringComparison.OrdinalIgnoreCase))
                return profile.Scale(profile.FruitFestivalTabRegion, width, height);
            return profile.Scale(profile.CityFestivalEntryRegion, width, height);
        }

        private void AddBoardReadyDiagnostics(NavigationAttemptState attempt, Fruit2048BoardReadyDetection detection)
        {
            AddBoardReadyDiagnostic(attempt, detection?.Primary);
            AddBoardReadyDiagnostic(attempt, detection?.Secondary);
        }

        private static void AddBoardReadyDiagnostic(NavigationAttemptState attempt,
            Fruit2048BoardReadyAnchorMatch evidence)
        {
            if (attempt == null || evidence == null) return;
            attempt.Diagnostics.RemoveAll(value => string.Equals(value.Anchor, evidence.Anchor,
                StringComparison.OrdinalIgnoreCase));
            attempt.Diagnostics.Add(new Fruit2048NavigationAnchorDiagnostic
            {
                Anchor = evidence.Anchor,
                TemplatePath = evidence.TemplatePath,
                TemplateExists = evidence.TemplateAvailable,
                SearchRoi = evidence.SearchRoi,
                MatcherMode = "LocalNormalizedGrayscale",
                MatchFound = evidence.Found,
                LocalMatchedBounds = evidence.MatchedBounds.Width > 0
                    ? (evidence.MatchedBounds.X - evidence.SearchRoi.X) + ","
                      + (evidence.MatchedBounds.Y - evidence.SearchRoi.Y) + ","
                      + evidence.MatchedBounds.Width + "," + evidence.MatchedBounds.Height
                    : string.Empty,
                AbsoluteMatchedBounds = evidence.MatchedBounds.Width > 0
                    ? Region(evidence.MatchedBounds) : string.Empty,
                ScoreAvailable = evidence.Score.HasValue,
                Score = evidence.Score,
                Outcome = evidence.Found ? "Accepted" : evidence.FailureReason ?? "NotFound"
            });
        }

        private void LogBoardReady(string deviceName, Fruit2048BoardReadyDetection detection)
        {
            LogBoardReadyAnchor(deviceName, detection?.Primary);
            if (detection?.Primary == null || !detection.Primary.Found)
                LogBoardReadyAnchor(deviceName, detection?.Secondary);
        }

        private void LogBoardReadyAnchor(string deviceName, Fruit2048BoardReadyAnchorMatch anchor)
        {
            if (anchor == null) return;
            logger?.Info("[Fruit2048 BoardReady] DeviceName='" + deviceName + "', Anchor='" + anchor.Anchor
                + "', Score=" + (anchor.Score.HasValue
                    ? anchor.Score.Value.ToString("F3", System.Globalization.CultureInfo.InvariantCulture) : "Unavailable")
                + ", Threshold=" + anchor.Threshold.ToString("F3", System.Globalization.CultureInfo.InvariantCulture)
                + ", MatchedBounds='" + (anchor.MatchedBounds.Width > 0 ? Region(anchor.MatchedBounds) : string.Empty)
                + "', ExpectedRoi='" + Region(anchor.SearchRoi) + "', Outcome='"
                + (anchor.Found ? "Accepted" : anchor.FailureReason ?? "NotFound") + "'");
        }

        private bool TryMatchInExpectedRoi(string deviceName, CapturedFrame frame, byte[] template, string anchor,
            NavigationAttemptState attempt, out ImageMatchResult match)
        {
            ImageRegion expected = RegionFor(anchor, frame.Width, frame.Height);
            // KAutoImageMatcher accepts the full decoded screenshot plus an ROI,
            // crops once internally, and adds the ROI origin back to the result.
            // The returned coordinates are therefore already absolute.
            match = matcher.Find(frame, template, expected);
            bool accepted = match != null
                && match.Found
                && Fruit2048ScreenProfile.IsAbsoluteBoundsInsideRegion(
                    match.X, match.Y, match.Width, match.Height, expected);
            attempt.Diagnostics.Add(CreateDiagnostic(anchor, expected, match, frame, template,
                accepted ? "Accepted" : match != null && match.Found ? "RejectedOutsideRoi" : "NotFound"));
            LogMatch(deviceName, anchor, match, expected, accepted, attempt.AttemptId);
            return accepted;
        }

        private void LogMatch(string deviceName, string anchor, ImageMatchResult match,
            ImageRegion expected, bool accepted, string navigationAttemptId)
        {
            logger?.Info("[Fruit2048 Navigation Match] DeviceName='" + deviceName + "', Anchor='" + anchor
                + "', NavigationAttemptId='" + navigationAttemptId + "', Score='Unavailable', Threshold='NativeMatcher', MatchedBounds='"
                + Bounds(match) + "', ExpectedRoi='" + Region(expected) + "', InsideExpectedRoi="
                + accepted + ", Outcome='" + (accepted ? "Accepted" : match != null && match.Found
                    ? "RejectedOutsideRoi" : "NotFound") + "'");
        }

        private bool TryGetValidatedTemplate(string anchor, CapturedFrame frame, NavigationAttemptState attempt,
            out byte[] template, out string failureReason)
        {
            template = null;
            failureReason = null;
            string path = templates.GetTemplatePath(anchor);
            if (!templates.TryGet(anchor, out template))
            {
                failureReason = "NavigationTemplateInvalid:TemplateFileMissingOrDecodeFailed";
                attempt.Diagnostics.Add(new Fruit2048NavigationAnchorDiagnostic
                {
                    Anchor = anchor, TemplatePath = path, TemplateExists = File.Exists(path), MatcherMode = "FullFrameWithNativeRoi",
                    SearchRoi = RegionFor(anchor, frame.Width, frame.Height), Outcome = failureReason
                });
                return false;
            }
            try
            {
                using (var stream = new MemoryStream(template, false))
                using (var bitmap = new Bitmap(stream))
                {
                    ImageRegion roi = RegionFor(anchor, frame.Width, frame.Height);
                    if (bitmap.Width <= 0 || bitmap.Height <= 0 || bitmap.Width > roi.Width || bitmap.Height > roi.Height)
                    {
                        failureReason = "NavigationTemplateInvalid:TemplateLargerThanSearchRoi";
                        attempt.Diagnostics.Add(new Fruit2048NavigationAnchorDiagnostic
                        {
                            Anchor = anchor, TemplatePath = path, TemplateExists = true, TemplateWidth = bitmap.Width,
                            TemplateHeight = bitmap.Height, MatcherMode = "FullFrameWithNativeRoi", SearchRoi = roi,
                            Outcome = failureReason
                        });
                        return false;
                    }
                    return true;
                }
            }
            catch (Exception exception)
            {
                failureReason = "NavigationTemplateInvalid:TemplateDecodeFailed";
                logger?.Error("[Fruit2048 Navigation Template] Anchor='" + anchor + "', Path='" + path + "'", exception);
                return false;
            }
        }

        private Fruit2048NavigationAnchorDiagnostic CreateDiagnostic(string anchor, ImageRegion roi,
            ImageMatchResult match, CapturedFrame frame, byte[] template, string outcome)
        {
            int width = 0, height = 0;
            try
            {
                using (var stream = new MemoryStream(template, false))
                using (var bitmap = new Bitmap(stream)) { width = bitmap.Width; height = bitmap.Height; }
            }
            catch { }
            string absolute = Bounds(match);
            string local = match != null && match.Found
                ? (match.X - roi.X) + "," + (match.Y - roi.Y) + "," + match.Width + "," + match.Height : string.Empty;
            return new Fruit2048NavigationAnchorDiagnostic
            {
                Anchor = anchor, TemplatePath = templates.GetTemplatePath(anchor), TemplateExists = true,
                TemplateWidth = width, TemplateHeight = height, SearchRoi = roi,
                MatcherMode = "FullFrameWithNativeRoi", MatchFound = match != null && match.Found,
                LocalMatchedBounds = local, AbsoluteMatchedBounds = absolute,
                ScoreAvailable = false, Score = null, Outcome = outcome
            };
        }

        private async Task PersistTerminalFailureAsync(string deviceName, string reason, NavigationAttemptState attempt,
            CancellationToken cancellationToken)
        {
            if (diagnostics == null) return;
            try
            {
                using (CapturedFrame frame = await frames.CaptureFrameAsync(deviceName, cancellationToken))
                {
                    foreach (string anchor in NavigationAnchors)
                    {
                        if (attempt.Diagnostics.Any(value => string.Equals(value.Anchor, anchor, StringComparison.OrdinalIgnoreCase))) continue;
                        byte[] template;
                        if (templates.TryGet(anchor, out template))
                        {
                            ImageRegion roi = RegionFor(anchor, frame.Width, frame.Height);
                            ImageMatchResult match = matcher.Find(frame, template, roi);
                            attempt.Diagnostics.Add(CreateDiagnostic(anchor, roi, match, frame, template,
                                match != null && match.Found ? "FoundAtTerminalCapture" : "NotFoundAtTerminalCapture"));
                        }
                    }
                    string path = diagnostics.SaveNavigationFailure(deviceName, reason, frame.GetPngBytes(),
                        null, attempt.AttemptId, attempt.Diagnostics, attempt.FruitTabTapped,
                        attempt.FruitTabTapX, attempt.FruitTabTapY);
                    logger?.Info("[Fruit2048 Navigation] DeviceName='" + deviceName
                        + "', Action='PersistFailureDiagnostic', DiagnosticPath='" + path + "'");
                }
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception exception)
            {
                logger?.Error("[Fruit2048 Navigation] Could not save terminal navigation diagnostic.", exception);
            }
        }
        private static readonly string[] NavigationAnchors =
        {
            Fruit2048TemplateCatalog.NavigationBoardAnchor,
            Fruit2048TemplateCatalog.NavigationBoardSecondaryAnchor,
            Fruit2048TemplateCatalog.Fruit2048Tab,
            Fruit2048TemplateCatalog.CityFestivalEntry
        };

        private static string FailureReasonFor(NavigationAttemptState attempt, bool fruitTabTapSent)
        {
            if (attempt == null) return "NavigationAnchorsNotDetected";
            if (fruitTabTapSent) return "FruitTabTappedButBoardNotDetected";
            if (attempt.CityEntryTapped) return "CityEntryTappedButFruitTabNotDetected";
            return "NavigationAnchorsNotDetected";
        }
        private static Fruit2048NavigationResult Ready() => new Fruit2048NavigationResult { Success = true, State = Fruit2048NavigationState.BoardReady };
        private static Fruit2048NavigationResult Failed(string error) => new Fruit2048NavigationResult { Success = false, State = Fruit2048NavigationState.Unknown, Error = error };
        private void Log(string device, int attempt, Observation observation, string action, int tapX, string outcome, int wait)
        {
            ImageRegion? expected = string.IsNullOrWhiteSpace(observation?.Anchor) ? (ImageRegion?)null
                : RegionFor(observation.Anchor, Fruit2048ScreenProfile.ReferenceWidth, Fruit2048ScreenProfile.ReferenceHeight);
            bool inside = expected.HasValue
                && observation?.Match != null
                && observation.Match.Found
                && Fruit2048ScreenProfile.IsAbsoluteBoundsInsideRegion(
                    observation.Match.X,
                    observation.Match.Y,
                    observation.Match.Width,
                    observation.Match.Height,
                    expected.Value);
            logger?.Info($"[Fruit2048 Navigation] DeviceName='{device}', Attempt={attempt}, DetectedState='{observation.State}', "
                + $"Anchor='{observation.Anchor}', Score='Unavailable', Threshold='NativeMatcher', MatchedBounds='{Bounds(observation.Match)}', "
                + $"ExpectedRoi='{(expected.HasValue ? Region(expected.Value) : string.Empty)}', InsideExpectedRoi={inside}, "
                + $"Action='{action}', TapX={tapX}, TapY={observation.Match?.CenterY ?? 0}, WaitMs={wait}, Outcome='{outcome}'");
        }
        private static string Bounds(ImageMatchResult match) => match == null || !match.Found ? string.Empty
            : match.X + "," + match.Y + "," + match.Width + "," + match.Height;
        private static string Region(ImageRegion region) => region.X + "," + region.Y + "," + region.Width + "," + region.Height;
        private sealed class Observation { public Fruit2048NavigationState State; public ImageMatchResult Match; public string Anchor; public string Error; }
        private sealed class NavigationAttemptState
        {
            public NavigationAttemptState(int number) { Number = number; AttemptId = Guid.NewGuid().ToString("N"); }
            public int Number { get; }
            public string AttemptId { get; }
            public bool FruitTabTapped { get; set; }
            public int FruitTabTapX { get; set; }
            public int FruitTabTapY { get; set; }
            public bool CityEntryTapped { get; set; }
            public List<Fruit2048NavigationAnchorDiagnostic> Diagnostics { get; } = new List<Fruit2048NavigationAnchorDiagnostic>();
        }
    }
}
