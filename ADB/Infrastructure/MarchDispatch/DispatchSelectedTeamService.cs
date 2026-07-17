using ADB_Tool_Automation_Post_FB.Core.Abstractions;
using ADB_Tool_Automation_Post_FB.Core.Concurrency;
using ADB_Tool_Automation_Post_FB.Core.Diagnostics;
using ADB_Tool_Automation_Post_FB.Core.GameDetection;
using ADB_Tool_Automation_Post_FB.Core.MarchDispatch;
using ADB_Tool_Automation_Post_FB.Core.TeamSelection;
using ADB_Tool_Automation_Post_FB.Core.StorageLimit;
using ADB_Tool_Automation_Post_FB.Core.Vision;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace ADB_Tool_Automation_Post_FB.Infrastructure.MarchDispatch
{
    public sealed class DispatchSelectedTeamService : IDispatchSelectedTeamService
    {
        private static readonly TemplateId[] ReadyTemplates =
        {
            TemplateId.TeamSelectionPanelAnchor,
            TemplateId.TeamAdjustFormationButton,
            TemplateId.TeamActionButtonEnabled
        };

        private readonly IGameStateDetector detector;
        private readonly ILdPlayerClient client;
        private readonly ITemplateRegistry registry;
        private readonly IImageMatcher matcher;
        private readonly IFrameStabilityDetector frameComparer;
        private readonly IDeviceOperationLock operationLock;
        private readonly FarmTeamSelectionOptions teamOptions;
        private readonly DispatchSelectedTeamOptions options;
        private readonly IDispatchMarchDiagnosticStore diagnosticStore;
        private readonly IDiagnosticLogger logger;
        private readonly IStorageLimitDialogService storageLimitDialog;

        public DispatchSelectedTeamService(IGameStateDetector detector, ILdPlayerClient client,
            ITemplateRegistry registry, IImageMatcher matcher, IFrameStabilityDetector frameComparer,
            IDeviceOperationLock operationLock, FarmTeamSelectionOptions teamOptions,
            DispatchSelectedTeamOptions options, IDispatchMarchDiagnosticStore diagnosticStore,
            IDiagnosticLogger logger)
            : this(detector, client, registry, matcher, frameComparer, operationLock,
                teamOptions, options, diagnosticStore, logger, null)
        {
        }

        public DispatchSelectedTeamService(IGameStateDetector detector, ILdPlayerClient client,
            ITemplateRegistry registry, IImageMatcher matcher, IFrameStabilityDetector frameComparer,
            IDeviceOperationLock operationLock, FarmTeamSelectionOptions teamOptions,
            DispatchSelectedTeamOptions options, IDispatchMarchDiagnosticStore diagnosticStore,
            IDiagnosticLogger logger, IStorageLimitDialogService storageLimitDialog)
        {
            this.detector = detector ?? throw new ArgumentNullException(nameof(detector));
            this.client = client ?? throw new ArgumentNullException(nameof(client));
            this.registry = registry ?? throw new ArgumentNullException(nameof(registry));
            this.matcher = matcher ?? throw new ArgumentNullException(nameof(matcher));
            this.frameComparer = frameComparer ?? throw new ArgumentNullException(nameof(frameComparer));
            this.operationLock = operationLock ?? throw new ArgumentNullException(nameof(operationLock));
            this.teamOptions = teamOptions ?? throw new ArgumentNullException(nameof(teamOptions));
            this.options = options ?? throw new ArgumentNullException(nameof(options));
            this.diagnosticStore = diagnosticStore ?? throw new ArgumentNullException(nameof(diagnosticStore));
            this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
            this.storageLimitDialog = storageLimitDialog;
        }

        public async Task<DispatchMarchResult> DispatchAsync(string deviceName,
            DispatchMarchRequest request, CancellationToken cancellationToken)
        {
            string error = Validate(deviceName, request);
            if (error != null) return Empty(request, DispatchMarchOutcome.Failed, error);
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                return await operationLock.RunAsync(deviceName.Trim(),
                    token => DispatchCoreAsync(deviceName.Trim(), request, token), cancellationToken);
            }
            catch (OperationCanceledException)
            {
                return Empty(request, DispatchMarchOutcome.Cancelled,
                    "March dispatch was cancelled while waiting for the device lock.");
            }
        }

        private async Task<DispatchMarchResult> DispatchCoreAsync(string deviceName,
            DispatchMarchRequest request, CancellationToken cancellationToken)
        {
            var watch = Stopwatch.StartNew();
            var observations = new List<MarchDispatchObservation>();
            DispatchMarchResult result = NewResult(request, observations);
            byte[] lastFrame = null;
            try
            {
                logger.Info($"[March Dispatch] DeviceName='{deviceName}', ExpectedTeam='{request.ExpectedTeam}', Phase='Starting', Cancellation=false");
                if (!RequiredTemplatesExist(request.ExpectedTeam, out string templateError))
                    return Complete(result, DispatchMarchOutcome.Failed,
                        "Required march-dispatch templates are incomplete; no Tap was sent.", templateError, watch);

                GameDetectionResult initial = await detector.DetectAsync(deviceName, cancellationToken);
                result.InitialState = initial.State;
                result.FinalState = initial.State;
                if (!IsReady(initial))
                {
                    lastFrame = await TryCaptureAsync(deviceName, cancellationToken);
                    return await CompleteAsync(deviceName, result, DispatchMarchOutcome.TeamSelectionNotReady,
                        "Team Selection is not ready; no Tap was sent.", initial.ErrorMessage,
                        lastFrame, watch, cancellationToken);
                }
                result.TeamSelectionVerified = true;

                ImageRegion teamRegion = teamOptions.TeamRegions[request.ExpectedTeam];
                TemplateId badgeId = BadgeId(request.ExpectedTeam);
                lastFrame = await client.CaptureScreenshotPngAsync(deviceName, cancellationToken);
                Verification precheck = VerifySelection(lastFrame, request.ExpectedTeam, badgeId);
                logger.Info($"[March Dispatch] DeviceName='{deviceName}', TeamSelectionVerified={result.TeamSelectionVerified}, ExpectedTeam='{request.ExpectedTeam}', ExpectedBadgeFound={precheck.BadgeFound}, ExpectedSelected={precheck.SelectedFound}, AmbiguousSelection={precheck.Ambiguous}");
                if (precheck.Ambiguous)
                    return await CompleteAsync(deviceName, result, DispatchMarchOutcome.VerificationIndeterminate,
                        "Selected border appeared in multiple team ROIs; no Tap was sent.",
                        "Ambiguous selected-team evidence.", lastFrame, watch, cancellationToken);
                if (!precheck.SelectedFound)
                    return await CompleteAsync(deviceName, result, DispatchMarchOutcome.ExpectedTeamNotSelected,
                        "Expected team is not selected; no Tap was sent.", null,
                        lastFrame, watch, cancellationToken);
                result.ExpectedTeamSelectedBeforeTap = true;

                bool optionalBeforeTap = OptionalFound(lastFrame, TemplateId.TeamBusyStatusAnchor, teamRegion)
                    || OptionalFound(lastFrame, TemplateId.TeamMarchTimerAnchor, teamRegion);
                if (optionalBeforeTap)
                    return await CompleteAsync(deviceName, result, DispatchMarchOutcome.VerificationIndeterminate,
                        "Busy or march-timer evidence appeared while Team Selection was still ready; no Tap was sent.",
                        "Pre-dispatch state is ambiguous.", lastFrame, watch, cancellationToken);

                byte[] beforeDispatch = await client.CaptureScreenshotPngAsync(deviceName, cancellationToken);
                GameDetectionResult freshState = detector.Detect(beforeDispatch);
                Verification freshSelection = VerifySelection(beforeDispatch, request.ExpectedTeam, badgeId);
                ImageMatchResult action = Match(beforeDispatch, TemplateId.TeamActionButtonEnabled, null);
                logger.Info($"[March Dispatch] DeviceName='{deviceName}', FreshState='{freshState.State}', ExpectedBadgeFound={freshSelection.BadgeFound}, ExpectedSelected={freshSelection.SelectedFound}, ActionButtonFound={HasBounds(action)}, ActionButtonBounds={(HasBounds(action) ? $"({action.X},{action.Y},{action.Width},{action.Height})" : string.Empty)}");
                if (!IsReady(freshState) || freshSelection.Ambiguous
                    || !freshSelection.SelectedFound)
                    return await CompleteAsync(deviceName, result, DispatchMarchOutcome.ExpectedTeamNotSelected,
                        "Expected team selection changed before dispatch; no Tap was sent.", null,
                        beforeDispatch, watch, cancellationToken);
                if (!HasBounds(action))
                    return await CompleteAsync(deviceName, result, DispatchMarchOutcome.ActionButtonUnavailable,
                        "Team action button has no valid fresh bounds; no Tap was sent.", null,
                        beforeDispatch, watch, cancellationToken);

                result.ActionButtonVerified = true;
                lastFrame = beforeDispatch;
                await TapActionAsync(deviceName, result, action, cancellationToken, false);
                DateTimeOffset lastTapAt = DateTimeOffset.UtcNow;
                DateTimeOffset transitionDeadline = DateTimeOffset.UtcNow.AddSeconds(
                    options.TransitionTimeoutSeconds);
                bool transitionObserved = false;

                while (DateTimeOffset.UtcNow < transitionDeadline)
                {
                    await Task.Delay(options.PollIntervalMs, cancellationToken);
                    lastFrame = await client.CaptureScreenshotPngAsync(deviceName, cancellationToken);
                    GameDetectionResult state = detector.Detect(lastFrame);
                    result.FinalState = state.State;
                    if (state.State == GameState.StorageLimitDialog)
                    {
                        result.StorageLimitDialogDetected = true;
                        if (storageLimitDialog == null)
                            return Complete(result, DispatchMarchOutcome.Failed,
                                "StorageLimitDialog was detected but no dialog handler is configured.",
                                "IStorageLimitDialogService is required.", watch);
                        StorageLimitDialogResult handled = await storageLimitDialog.HandleAsync(
                            deviceName, StorageLimitPolicy.ConfirmAndSwitchResource, cancellationToken);
                        result.StorageLimitResult = handled;
                        result.FinalState = handled.StateAfterConfirmation;
                        if (handled.Outcome == StorageLimitDialogOutcome.Cancelled)
                            throw new OperationCanceledException(cancellationToken);
                        if (handled.Outcome == StorageLimitDialogOutcome.ConfirmedForResourceSwitch)
                        {
                            result.StorageLimitConfirmed = true;
                            result.ResourceSwitchRequired = true;
                            result.StorageFullResource = request.CurrentResource;
                            result.DispatchedTeam = null;
                            result.MarchStartedVerified = false;
                            return Complete(result,
                                DispatchMarchOutcome.StorageLimitResourceSwitchRequired,
                                $"Storage for {request.CurrentResource} is full; switch to the next resource.",
                                handled.ErrorMessage, watch);
                        }
                        return Complete(result, DispatchMarchOutcome.Failed,
                            handled.Message, handled.ErrorMessage, watch);
                    }
                    MarchDispatchObservation observation = Observe(lastFrame, beforeDispatch,
                        state, request, badgeId, teamRegion);
                    observations.Add(observation);
                    result.ObservedFrameCount = observations.Count;
                    Apply(result, observation);

                    if (state.State == GameState.Unknown) result.TransientUnknownFrameCount++;
                    bool success = observation.SuccessRuleMatched;
                    result.ConsecutiveSuccessFrames = success
                        ? result.ConsecutiveSuccessFrames + 1 : 0;
                    observation.Message = success
                        ? $"March-start rule matched ({result.ConsecutiveSuccessFrames}/{options.RequiredConsecutiveSuccessFrames})."
                        : "March-start rule was not yet satisfied.";
                    LogObservation(deviceName, result, observation);

                    if (result.ConsecutiveSuccessFrames >= options.RequiredConsecutiveSuccessFrames)
                    {
                        result.DispatchedTeam = request.ExpectedTeam;
                        result.MarchStartedVerified = true;
                        return Complete(result, DispatchMarchOutcome.MarchStarted,
                            $"{request.ExpectedTeam} march start was verified.", null, watch);
                    }

                    transitionObserved |= !observation.TeamSelectionFound || observation.WorldMapFound
                        || observation.BusyStatusFound || observation.MarchTimerFound
                        || !observation.SelectedBorderFound;
                    if (result.TransientUnknownFrameCount > options.MaxTransientUnknownFrames
                        && !observation.WorldMapFound)
                        return await CompleteAsync(deviceName, result, DispatchMarchOutcome.VerificationIndeterminate,
                            "Too many transient Unknown frames after the action Tap.", null,
                            lastFrame, watch, cancellationToken);

                    if (CanRetry(state, observation, transitionObserved, result.ActionTapCount,
                        lastTapAt, lastFrame, request.ExpectedTeam, badgeId, out ImageMatchResult retryAction))
                    {
                        await TapActionAsync(deviceName, result, retryAction, cancellationToken, true);
                        lastTapAt = DateTimeOffset.UtcNow;
                    }
                }

                return await CompleteAsync(deviceName, result, DispatchMarchOutcome.TransitionTimeout,
                    "March start was not verified before the transition timeout.", null,
                    lastFrame, watch, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                return Complete(result, DispatchMarchOutcome.Cancelled,
                    "March dispatch was cancelled.", null, watch);
            }
            catch (Exception exception)
            {
                logger.Error($"[March Dispatch] DeviceName='{deviceName}', Error='{exception.Message}'", exception);
                return await CompleteAsync(deviceName, result, DispatchMarchOutcome.Failed,
                    "March dispatch failed.", exception.Message, lastFrame, watch, cancellationToken);
            }
        }

        private MarchDispatchObservation Observe(byte[] frame, byte[] before,
            GameDetectionResult state, DispatchMarchRequest request, TemplateId badgeId,
            ImageRegion teamRegion)
        {
            bool panel = Match(frame, TemplateId.TeamSelectionPanelAnchor, null).Found;
            bool world = state.State == GameState.WorldMap
                || Match(frame, TemplateId.WorldMapAnchor, null).Found;
            bool badge = Match(frame, badgeId, teamRegion).Found;
            bool selected = Match(frame, TemplateId.TeamSelectedBorderAnchor, teamRegion).Found;
            bool busy = OptionalFound(frame, TemplateId.TeamBusyStatusAnchor, teamRegion);
            bool timer = OptionalFound(frame, TemplateId.TeamMarchTimerAnchor, teamRegion);
            FrameComparisonResult comparison = frameComparer.Compare(before, frame, teamRegion);
            bool changed = comparison.DifferenceRatio > options.TeamRegionChangeThreshold;
            bool strong = !panel && world && (busy || timer);
            bool allowFallback = options.AllowStructuralVerificationFallback
                && request.AllowStructuralVerificationFallback;
            bool structural = allowFallback && !panel && world && !selected && changed;
            return new MarchDispatchObservation
            {
                Timestamp = DateTimeOffset.UtcNow,
                State = state.State,
                TeamSelectionFound = panel,
                WorldMapFound = world,
                ExpectedTeamBadgeFound = badge,
                SelectedBorderFound = selected,
                BusyStatusFound = busy,
                MarchTimerFound = timer,
                TeamRegionDifference = comparison.DifferenceRatio,
                TeamRegionChanged = changed,
                SuccessRuleMatched = strong || structural
            };
        }

        private bool CanRetry(GameDetectionResult state, MarchDispatchObservation observation,
            bool transitionObserved, int tapCount, DateTimeOffset lastTapAt, byte[] frame,
            TeamNumber team, TemplateId badgeId, out ImageMatchResult action)
        {
            action = ImageMatchResult.NotFound();
            if (transitionObserved || tapCount >= options.MaxActionTapAttempts
                || DateTimeOffset.UtcNow - lastTapAt < TimeSpan.FromMilliseconds(options.ActionTapRetryDelayMs)
                || state.State != GameState.TeamSelection || !observation.TeamSelectionFound)
                return false;
            Verification selection = VerifySelection(frame, team, badgeId);
            if (selection.Ambiguous || !selection.SelectedFound) return false;
            ImageMatchResult adjust = Match(frame, TemplateId.TeamAdjustFormationButton, null);
            action = Match(frame, TemplateId.TeamActionButtonEnabled, null);
            return adjust.Found && HasBounds(action);
        }

        private async Task TapActionAsync(string deviceName, DispatchMarchResult result,
            ImageMatchResult action, CancellationToken token, bool retry)
        {
            token.ThrowIfCancellationRequested();
            await client.TapAsync(deviceName, action.CenterX, action.CenterY, token);
            result.ActionTapCount++;
            logger.Info($"[March Dispatch] DeviceName='{deviceName}', TeamActionBounds=({action.X},{action.Y},{action.Width},{action.Height}), Tap=({action.CenterX},{action.CenterY}), ActionTapCount={result.ActionTapCount}, Retry={retry}, Cancellation=false");
        }

        private Verification VerifySelection(byte[] frame, TeamNumber expectedTeam, TemplateId badgeId)
        {
            ImageRegion expectedRegion = teamOptions.TeamRegions[expectedTeam];
            bool badge = HasBounds(Match(frame, badgeId, expectedRegion));
            var selected = new List<TeamNumber>();
            foreach (KeyValuePair<TeamNumber, ImageRegion> item in teamOptions.TeamRegions)
                if (Match(frame, TemplateId.TeamSelectedBorderAnchor, item.Value).Found)
                    selected.Add(item.Key);
            return new Verification
            {
                BadgeFound = badge,
                SelectedFound = selected.Count == 1 && selected[0] == expectedTeam,
                Ambiguous = selected.Count > 1
            };
        }

        private ImageMatchResult Match(byte[] frame, TemplateId id, ImageRegion? region) =>
            matcher.Find(frame, registry.LoadBytes(id), region) ?? ImageMatchResult.NotFound();
        private bool OptionalFound(byte[] frame, TemplateId id, ImageRegion region) =>
            registry.Exists(id) && Match(frame, id, region).Found;
        private static bool HasBounds(ImageMatchResult match) =>
            match != null && match.Found && match.Width > 0 && match.Height > 0;

        private bool RequiredTemplatesExist(TeamNumber team, out string error)
        {
            foreach (TemplateId id in ReadyTemplates.Concat(new[]
            {
                TemplateId.WorldMapAnchor,
                TemplateId.TeamSelectedBorderAnchor,
                BadgeId(team)
            }))
            {
                if (!registry.Exists(id))
                {
                    error = $"Required template '{id}' was not found at '{registry.GetPath(id)}'.";
                    return false;
                }
            }
            error = null;
            return true;
        }

        private static bool IsReady(GameDetectionResult result) =>
            result != null && result.IsSuccessful && result.State == GameState.TeamSelection
            && ReadyTemplates.All(id => result.Evidence != null
                && result.Evidence.Any(item => item.TemplateId == id && item.Found));
        private static TemplateId BadgeId(TeamNumber team)
        {
            switch (team)
            {
                case TeamNumber.Team2: return TemplateId.Team2Badge;
                case TeamNumber.Team3: return TemplateId.Team3Badge;
                case TeamNumber.Team4: return TemplateId.Team4Badge;
                default: throw new ArgumentOutOfRangeException(nameof(team));
            }
        }
        private static string Validate(string deviceName, DispatchMarchRequest request)
        {
            if (string.IsNullOrWhiteSpace(deviceName)) return "LDPlayer device name is required.";
            if (request == null) return "March dispatch request is required.";
            if (request.ExpectedTeam != TeamNumber.Team2 && request.ExpectedTeam != TeamNumber.Team3
                && request.ExpectedTeam != TeamNumber.Team4)
                return "ExpectedTeam must be Team2, Team3, or Team4 for the MVP.";
            return null;
        }

        private static void Apply(DispatchMarchResult result, MarchDispatchObservation item)
        {
            result.TeamSelectionClosed |= !item.TeamSelectionFound;
            result.WorldMapVerified |= item.WorldMapFound;
            result.SelectedBorderDisappeared |= !item.SelectedBorderFound;
            result.TeamRegionChanged |= item.TeamRegionChanged;
            result.BusyStatusVerified |= item.BusyStatusFound;
            result.MarchTimerVerified |= item.MarchTimerFound;
            result.TeamRegionDifference = item.TeamRegionDifference;
        }

        private async Task<byte[]> TryCaptureAsync(string deviceName, CancellationToken token)
        {
            if (!options.SaveFailureScreenshots) return null;
            try { return await client.CaptureScreenshotPngAsync(deviceName, token); }
            catch (OperationCanceledException) { throw; }
            catch (Exception exception) { logger.Error($"[March Dispatch] DiagnosticCaptureError='{exception.Message}'", exception); return null; }
        }

        private async Task<DispatchMarchResult> CompleteAsync(string deviceName,
            DispatchMarchResult result, DispatchMarchOutcome outcome, string message,
            string error, byte[] frame, Stopwatch watch, CancellationToken token)
        {
            Complete(result, outcome, message, error, watch);
            if (options.SaveFailureScreenshots && frame != null && IsFailure(outcome))
            {
                try { result.DiagnosticScreenshotPath = await diagnosticStore.SaveAsync(deviceName, outcome, frame, token); }
                catch (OperationCanceledException) { throw; }
                catch (Exception exception) { logger.Error($"[March Dispatch] DiagnosticSaveError='{exception.Message}'", exception); }
            }
            return result;
        }

        private DispatchMarchResult Complete(DispatchMarchResult result,
            DispatchMarchOutcome outcome, string message, string error, Stopwatch watch)
        {
            result.Outcome = outcome;
            result.Success = outcome == DispatchMarchOutcome.MarchStarted
                || outcome == DispatchMarchOutcome.AlreadyMarching;
            result.Duration = watch.Elapsed;
            result.Message = message;
            result.ErrorMessage = error;
            logger.Info($"[March Dispatch] ExpectedTeam='{result.ExpectedTeam}', InitialState='{result.InitialState}', FinalState='{result.FinalState}', ActionTapCount={result.ActionTapCount}, ObservedFrames={result.ObservedFrameCount}, ConsecutiveSuccessFrames={result.ConsecutiveSuccessFrames}, Outcome='{outcome}', DurationMs={result.Duration.TotalMilliseconds:F0}, Cancellation={outcome == DispatchMarchOutcome.Cancelled}, Error='{error ?? string.Empty}'");
            return result;
        }

        private void LogObservation(string deviceName, DispatchMarchResult result,
            MarchDispatchObservation item) => logger.Info(
            $"[March Dispatch] DeviceName='{deviceName}', Observation={result.ObservedFrameCount}, GameState='{item.State}', TeamSelectionClosed={!item.TeamSelectionFound}, WorldMapVerified={item.WorldMapFound}, SelectedBorderFound={item.SelectedBorderFound}, TeamRegionDifference={item.TeamRegionDifference?.ToString("F4") ?? "n/a"}, BusyStatusFound={item.BusyStatusFound}, MarchTimerFound={item.MarchTimerFound}, ConsecutiveSuccessFrames={result.ConsecutiveSuccessFrames}");

        private static bool IsFailure(DispatchMarchOutcome outcome) =>
            outcome != DispatchMarchOutcome.MarchStarted
            && outcome != DispatchMarchOutcome.AlreadyMarching
            && outcome != DispatchMarchOutcome.Cancelled;
        private static DispatchMarchResult NewResult(DispatchMarchRequest request,
            IReadOnlyList<MarchDispatchObservation> observations) => new DispatchMarchResult
        {
            ExpectedTeam = request.ExpectedTeam,
            InitialState = GameState.Unknown,
            FinalState = GameState.Unknown,
            Observations = observations
        };
        private static DispatchMarchResult Empty(DispatchMarchRequest request,
            DispatchMarchOutcome outcome, string error) => new DispatchMarchResult
        {
            Outcome = outcome,
            Success = false,
            ExpectedTeam = request == null ? TeamNumber.Team4 : request.ExpectedTeam,
            InitialState = GameState.Unknown,
            FinalState = GameState.Unknown,
            Message = error,
            ErrorMessage = error,
            Observations = new MarchDispatchObservation[0]
        };

        private sealed class Verification
        {
            public bool BadgeFound { get; set; }
            public bool SelectedFound { get; set; }
            public bool Ambiguous { get; set; }
        }
    }
}
