using ADB_Tool_Automation_Post_FB.Core.Abstractions;
using ADB_Tool_Automation_Post_FB.Core.Concurrency;
using ADB_Tool_Automation_Post_FB.Core.Diagnostics;
using ADB_Tool_Automation_Post_FB.Core.GameDetection;
using ADB_Tool_Automation_Post_FB.Core.MarchDispatch;
using ADB_Tool_Automation_Post_FB.Core.ResourceSearch;
using ADB_Tool_Automation_Post_FB.Core.TeamSelection;
using ADB_Tool_Automation_Post_FB.Core.StorageLimit;
using ADB_Tool_Automation_Post_FB.Core.Vision;
using ADB_Tool_Automation_Post_FB.Infrastructure.TeamSelection;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
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
        private readonly ITeamMarchTimerDetector timerDetector;

        public DispatchSelectedTeamService(IGameStateDetector detector, ILdPlayerClient client,
            ITemplateRegistry registry, IImageMatcher matcher, IFrameStabilityDetector frameComparer,
            IDeviceOperationLock operationLock, FarmTeamSelectionOptions teamOptions,
            DispatchSelectedTeamOptions options, IDispatchMarchDiagnosticStore diagnosticStore,
            IDiagnosticLogger logger)
            : this(detector, client, registry, matcher, frameComparer, operationLock,
                teamOptions, options, diagnosticStore, logger, null, null)
        {
        }

        public DispatchSelectedTeamService(IGameStateDetector detector, ILdPlayerClient client,
            ITemplateRegistry registry, IImageMatcher matcher, IFrameStabilityDetector frameComparer,
            IDeviceOperationLock operationLock, FarmTeamSelectionOptions teamOptions,
            DispatchSelectedTeamOptions options, IDispatchMarchDiagnosticStore diagnosticStore,
            IDiagnosticLogger logger, IStorageLimitDialogService storageLimitDialog)
            : this(detector, client, registry, matcher, frameComparer, operationLock,
                teamOptions, options, diagnosticStore, logger, storageLimitDialog, null)
        {
        }

        public DispatchSelectedTeamService(IGameStateDetector detector, ILdPlayerClient client,
            ITemplateRegistry registry, IImageMatcher matcher, IFrameStabilityDetector frameComparer,
            IDeviceOperationLock operationLock, FarmTeamSelectionOptions teamOptions,
            DispatchSelectedTeamOptions options, IDispatchMarchDiagnosticStore diagnosticStore,
            IDiagnosticLogger logger, IStorageLimitDialogService storageLimitDialog,
            ITeamMarchTimerDetector timerDetector)
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
            this.timerDetector = timerDetector ?? new TeamMarchTimerDetector(options);
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
                result.RunId = request.RunId;
                logger.Info($"[March Dispatch] RunId='{request.RunId ?? string.Empty}', DeviceName='{deviceName}', ExpectedTeam='{request.ExpectedTeam}', Phase='Starting', Cancellation=false");
                if (!RequiredTemplatesExist(request.ExpectedTeam, out string templateError))
                    return Complete(result, DispatchMarchOutcome.Failed,
                        "Required march-dispatch templates are incomplete; no Tap was sent.", templateError, watch);

                // Dispatch is entered only after OpenTeamSelection and SelectTeam have
                // already accepted the panel.  A full game-state pass here used every
                // template and could keep a visibly ready "thu thập" dialog open for
                // 20-30 seconds before its action was tapped.  Use one fresh frame and
                // only the three stable team-selection controls instead.
                // Keep the hot pre-dispatch checks on the decoded capture path.  Encoding a
                // screenshot to PNG only to immediately decode it in the matcher was a
                // substantial source of avoidable queue time with many devices.
                FocusedTeamSelectionEvidence initial;
                using (CapturedFrame initialFrame = await CaptureFrameAsync(deviceName, cancellationToken))
                    initial = GetFocusedTeamSelectionEvidence(initialFrame);
                result.InitialState = initial.IsReady
                    ? GameState.TeamSelection : GameState.Unknown;
                result.FinalState = result.InitialState;
                logger.Info($"[March Dispatch Ready Check] DeviceName='{deviceName}', "
                    + $"Phase='Initial', PanelFound={initial.PanelFound}, "
                    + $"AdjustFound={initial.AdjustFound}, ActionFound={initial.ActionFound}, "
                    + $"ActionBounds={FormatBounds(initial.Action)}, Ready={initial.IsReady}");
                if (!initial.IsReady)
                    return await CompleteAsync(deviceName, result, DispatchMarchOutcome.TeamSelectionNotReady,
                        "Team Selection is not ready; no Tap was sent.", null,
                        lastFrame, watch, cancellationToken);
                result.TeamSelectionVerified = true;

                ImageRegion timerRegion = options.TeamTimerRegions[request.ExpectedTeam];
                TemplateId badgeId = BadgeId(request.ExpectedTeam);
                ImageRegion teamRegion = teamOptions.TeamRegions[request.ExpectedTeam];
                if (request.RequireExpectedTeamSelected)
                {
                    Verification precheck;
                    using (CapturedFrame precheckFrame = await CaptureFrameAsync(deviceName, cancellationToken))
                        precheck = VerifySelection(precheckFrame, request.ExpectedTeam, badgeId);
                    result.ActualSelectedTeam = precheck.ActualSelectedTeam;
                    result.ObservedSelectedTeam = precheck.ActualSelectedTeam;
                    result.VisibleTeams = precheck.VisibleTeams;
                    logger.Info($"[Dispatch Guard] RunId='{request.RunId ?? string.Empty}', DeviceName='{deviceName}', ExpectedTeam='{request.ExpectedTeam}', ObservedSelectedTeam='{precheck.ActualSelectedTeam}', ExpectedBadgeFound={precheck.BadgeFound}, ExpectedSelected={precheck.SelectedFound}, ActionTapSent=false, Outcome='Precheck'");
                    if (precheck.Ambiguous)
                        return await CompleteAsync(deviceName, result, DispatchMarchOutcome.VerificationIndeterminate,
                            "Selected border appeared in multiple team ROIs; no Tap was sent.",
                            "Ambiguous selected-team evidence.", lastFrame, watch, cancellationToken);
                    if (!precheck.SelectedFound)
                    {
                        result.FailureReason = "WrongTeamSelected";
                        return await CompleteAsync(deviceName, result, DispatchMarchOutcome.ExpectedTeamNotSelected,
                            "Đội dự kiến chưa được chọn chính xác; chưa thực hiện lệnh thu thập.", null,
                            lastFrame, watch, cancellationToken);
                    }
                    teamRegion = precheck.ExpectedRowBounds;
                    result.ExpectedTeamSelectedBeforeTap = true;
                }
                else
                {
                    // The ready-team workflow has just tapped this expected row.
                    // Avoid reclassifying a selected border; the fresh action-button
                    // match below remains mandatory immediately before the Tap.
                    result.ActualSelectedTeam = request.ExpectedTeam;
                    result.ObservedSelectedTeam = request.ExpectedTeam;
                    result.ExpectedTeamSelectedBeforeTap = true;
                    logger.Info($"[Dispatch Guard] RunId='{request.RunId ?? string.Empty}', DeviceName='{deviceName}', ExpectedTeam='{request.ExpectedTeam}', ActionTapSent=false, Outcome='TrustedReadyTeam'");
                }

                byte[] beforeDispatch;
                TeamMarchTimerDetectionResult timerBefore;
                using (CapturedFrame beforeDispatchFrame = await CaptureFrameAsync(deviceName, cancellationToken))
                {
                    FocusedTeamSelectionEvidence fresh = GetFocusedTeamSelectionEvidence(beforeDispatchFrame);
                    result.FinalState = fresh.IsReady ? GameState.TeamSelection : GameState.Unknown;
                    Verification freshSelection = request.RequireExpectedTeamSelected
                        ? VerifySelection(beforeDispatchFrame, request.ExpectedTeam, badgeId) : null;
                    if (freshSelection != null)
                    {
                        result.ActualSelectedTeam = freshSelection.ActualSelectedTeam;
                        result.ObservedSelectedTeam = freshSelection.ActualSelectedTeam;
                        result.VisibleTeams = freshSelection.VisibleTeams;
                    }
                    ImageMatchResult action = fresh.Action;
                    logger.Info($"[March Dispatch] DeviceName='{deviceName}', FreshState='{result.FinalState}', FocusedReady={fresh.IsReady}, ExpectedSelectionRequired={request.RequireExpectedTeamSelected}, ExpectedBadgeFound={freshSelection?.BadgeFound}, ExpectedSelected={freshSelection?.SelectedFound}, ActionButtonFound={HasBounds(action)}, ActionButtonBounds={FormatBounds(action)}");
                    if (!fresh.IsReady || (request.RequireExpectedTeamSelected
                        && (freshSelection.Ambiguous || !freshSelection.SelectedFound)))
                    {
                        beforeDispatch = beforeDispatchFrame.GetPngBytes();
                        return await CompleteAsync(deviceName, result, DispatchMarchOutcome.ExpectedTeamNotSelected,
                            "Đội dự kiến chưa được chọn chính xác; chưa thực hiện lệnh thu thập.", null,
                            beforeDispatch, watch, cancellationToken);
                    }
                    if (!HasBounds(action))
                    {
                        beforeDispatch = beforeDispatchFrame.GetPngBytes();
                        return await CompleteAsync(deviceName, result, DispatchMarchOutcome.ActionButtonUnavailable,
                            "Team action button has no valid fresh bounds; no Tap was sent.", null,
                            beforeDispatch, watch, cancellationToken);
                    }

                    // The timer analyser is byte based today.  Encode only this one capture
                    // because it is also needed as the transition-comparison baseline.
                    beforeDispatch = beforeDispatchFrame.GetPngBytes();
                    timerBefore = timerDetector.DetectContent(beforeDispatch, timerRegion);
                    result.TimerContentBeforeDispatch = timerBefore.ContentDetected;
                    result.FinalTimerForegroundRatio = timerBefore.ForegroundRatio;
                    result.ExpectedTeamReadyBeforeDispatch = options.EnableReadyDisappearanceVerification
                        && OptionalFound(beforeDispatchFrame, TemplateId.WorldMapTeamReadyAnchor, timerRegion);
                    logger.Info($"[March Dispatch] DeviceName='{deviceName}', ExpectedTeam='{request.ExpectedTeam}', ReadyBeforeDispatch={result.ExpectedTeamReadyBeforeDispatch}, TimerContentBeforeDispatch={timerBefore.ContentDetected}, TimerForegroundRatio={timerBefore.ForegroundRatio:F4}, TimerRegion=({timerRegion.X},{timerRegion.Y},{timerRegion.Width},{timerRegion.Height})");
                    result.ActionButtonVerified = true;
                    lastFrame = beforeDispatch;
                    await TapActionAsync(deviceName, result, action, cancellationToken, false);
                }
                DateTimeOffset lastTapAt = DateTimeOffset.UtcNow;
                DateTimeOffset transitionDeadline = DateTimeOffset.UtcNow.AddSeconds(
                    options.TransitionTimeoutSeconds);
                bool transitionObserved = false;
                MarchVerificationMode consecutiveMode = MarchVerificationMode.None;

                while (DateTimeOffset.UtcNow < transitionDeadline)
                {
                    await Task.Delay(options.PollIntervalMs, cancellationToken);
                    lastFrame = await client.CaptureScreenshotPngAsync(deviceName, cancellationToken);
                    GameDetectionResult state = detector.Detect(lastFrame);
                    result.FinalState = state.State;
                    ImageMatchResult unclassifiedPostGatherCancel = state.State == GameState.Unknown
                        && registry.Exists(TemplateId.StorageLimitCancelButton)
                        ? Match(lastFrame, TemplateId.StorageLimitCancelButton, null)
                        : ImageMatchResult.NotFound();
                    bool cancelOnlyPostGatherDialog = state.State == GameState.Unknown
                        && HasBounds(unclassifiedPostGatherCancel);
                    if (state.State == GameState.StorageLimitDialog
                        || state.State == GameState.ResourceExpiryDialog
                        || cancelOnlyPostGatherDialog)
                    {
                        // Some expiry messages contain dynamic resource amounts
                        // (for example "0 Gỗ"), so the text anchor can miss while
                        // the fresh Cancel action remains stable. This fallback is
                        // deliberately scoped to the transition immediately after
                        // the verified yellow Gather tap.
                        bool resourceExpiry = state.State == GameState.ResourceExpiryDialog
                            || cancelOnlyPostGatherDialog;
                        if (cancelOnlyPostGatherDialog)
                            logger.Info($"[March Dispatch PostGather Dialog] DeviceName='{deviceName}', State='Unknown', CancelBounds=({unclassifiedPostGatherCancel.X},{unclassifiedPostGatherCancel.Y},{unclassifiedPostGatherCancel.Width},{unclassifiedPostGatherCancel.Height}), Classification='ResourceExpiryDialog', NextAction='CancelAndSwitchResource'");
                        result.ResourceExpiryDialogDetected = resourceExpiry;
                        result.StorageLimitDialogDetected = !resourceExpiry;
                        if (storageLimitDialog == null)
                            return Complete(result, DispatchMarchOutcome.Failed,
                                $"{state.State} was detected but no dialog handler is configured.",
                                "IStorageLimitDialogService is required.", watch);
                        StorageLimitDialogResult handled = resourceExpiry
                            ? await storageLimitDialog.HandleResourceExpiryAsync(deviceName, cancellationToken)
                            : await storageLimitDialog.HandleAsync(deviceName,
                                StorageLimitPolicy.CancelAndSwitchResource, cancellationToken);
                        result.StorageLimitResult = handled;
                        result.FinalState = handled.FinalState;
                        if (handled.Outcome == StorageLimitDialogOutcome.Cancelled)
                            throw new OperationCanceledException(cancellationToken);
                        if (handled.Outcome == StorageLimitDialogOutcome.CancelledForResourceSwitch)
                        {
                            result.StorageLimitCancelled = !resourceExpiry;
                            result.ResourceExpiryCancelled = resourceExpiry;
                            result.ResourceSwitchRequired = true;
                            result.StorageFullResource = resourceExpiry
                                ? (ResourceType?)null : request.CurrentResource;
                            result.DispatchedTeam = null;
                            result.MarchStartedVerified = false;
                            return Complete(result,
                                resourceExpiry
                                    ? DispatchMarchOutcome.ResourceExpiryResourceSwitchRequired
                                    : DispatchMarchOutcome.StorageLimitResourceSwitchRequired,
                                resourceExpiry
                                    ? $"{request.CurrentResource} expires too soon; warning was cancelled, TeamSelection was closed, and the next resource is required."
                                    : $"Storage for {request.CurrentResource} is full; warning was cancelled and the next resource is required.",
                                handled.ErrorMessage, watch);
                        }
                        return Complete(result, DispatchMarchOutcome.Failed,
                            handled.Message, handled.ErrorMessage, watch);
                    }
                    TeamMarchTimerDetectionResult timerContent = options.EnableTimerProgressionVerification
                        ? timerDetector.DetectContent(lastFrame, timerRegion)
                        : new TeamMarchTimerDetectionResult { TimerRegion = timerRegion };
                    TeamMarchTimerProgressionResult timerProgression = null;
                    if (options.EnableTimerProgressionVerification && timerContent.ContentDetected)
                    {
                        byte[] timerPreviousFrame = lastFrame;
                        await Task.Delay(options.TimerSampleIntervalMs, cancellationToken);
                        lastFrame = await client.CaptureScreenshotPngAsync(deviceName, cancellationToken);
                        timerProgression = timerDetector.Compare(timerPreviousFrame,
                            lastFrame, timerRegion);
                        state = detector.Detect(lastFrame);
                        result.FinalState = state.State;
                    }
                    MarchDispatchObservation observation = Observe(lastFrame, beforeDispatch,
                        state, request, badgeId, teamRegion, timerRegion,
                        result.ExpectedTeamReadyBeforeDispatch, result.TimerContentBeforeDispatch,
                        timerContent, timerProgression);
                    observations.Add(observation);
                    result.ObservedFrameCount = observations.Count;
                    Apply(result, observation);

                    if (state.State == GameState.Unknown) result.TransientUnknownFrameCount++;
                    bool success = observation.SuccessRuleMatched;
                    if (!success)
                    {
                        result.ConsecutiveSuccessFrames = 0;
                        consecutiveMode = MarchVerificationMode.None;
                    }
                    else if (observation.VerificationMode == consecutiveMode)
                    {
                        result.ConsecutiveSuccessFrames++;
                    }
                    else
                    {
                        consecutiveMode = observation.VerificationMode;
                        result.ConsecutiveSuccessFrames = 1;
                    }
                    int requiredSuccessFrames =
                        IsStrongTimerVerification(observation.VerificationMode)
                            ? 1
                            : options.RequiredConsecutiveSuccessFrames;
                    observation.Message = success
                        ? $"March-start rule matched ({result.ConsecutiveSuccessFrames}/{requiredSuccessFrames})."
                        : "March-start rule was not yet satisfied.";
                    LogObservation(deviceName, result, observation);

                    if (result.ConsecutiveSuccessFrames >= requiredSuccessFrames)
                    {
                        result.DispatchedTeam = request.ExpectedTeam;
                        result.MarchStartedVerified = true;
                        return Complete(result, DispatchMarchOutcome.MarchStarted,
                            $"{request.ExpectedTeam} march start was verified.", null, watch);
                    }

                    transitionObserved |= !observation.TeamSelectionFound || observation.WorldMapFound
                        || observation.ReadyAnchorDisappeared || observation.TimerContentDetected
                        || observation.TimerProgressionDetected || !observation.SelectedBorderFound;
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
            ImageRegion teamRegion, ImageRegion timerRegion, bool readyBeforeDispatch,
            bool timerBeforeDispatch,
            TeamMarchTimerDetectionResult timerContent,
            TeamMarchTimerProgressionResult timerProgression)
        {
            bool panel = Match(frame, TemplateId.TeamSelectionPanelAnchor, null).Found;
            bool world = state.State == GameState.WorldMap
                || Match(frame, TemplateId.WorldMapAnchor, null).Found;
            bool badge = Match(frame, badgeId, teamRegion).Found;
            bool selected = Match(frame, TemplateId.TeamSelectedBorderAnchor, teamRegion).Found;
            bool readyAfter = options.EnableReadyDisappearanceVerification
                && OptionalFound(frame, TemplateId.WorldMapTeamReadyAnchor, timerRegion);
            bool readyDisappeared = options.EnableReadyDisappearanceVerification
                && readyBeforeDispatch && !readyAfter;
            bool timerFound = timerProgression != null
                ? timerProgression.CurrentContentDetected
                : timerContent != null && timerContent.ContentDetected;
            bool timerChanged = timerProgression != null
                && timerProgression.ProgressionDetected;
            double timerForeground = timerProgression != null
                ? timerProgression.CurrentForegroundRatio
                : timerContent?.ForegroundRatio ?? 0d;
            double timerDifference = timerProgression?.DifferenceRatio ?? 0d;
            FrameComparisonResult comparison = frameComparer.Compare(before, frame, teamRegion);
            bool changed = comparison.DifferenceRatio > options.TeamRegionChangeThreshold;
            bool allowFallback = options.AllowStructuralVerificationFallback
                && request.AllowStructuralVerificationFallback;
            bool structural = allowFallback && !panel && world && !selected && changed;
            bool direct = !panel && world && !selected && readyDisappeared
                && timerFound && timerChanged;
            bool worldMapTimer = !panel && world && !readyBeforeDispatch
                && timerFound && timerChanged;
            bool worldMapTimerAppeared = !panel && world && !selected
                && !timerBeforeDispatch && timerFound
                && (!readyBeforeDispatch || readyDisappeared);
            bool timerPlusStructural = worldMapTimer && structural;
            MarchVerificationMode mode = direct
                ? MarchVerificationMode.ReadyDisappearedAndTimerProgression
                : timerPlusStructural ? MarchVerificationMode.TimerProgressionPlusStructural
                : worldMapTimerAppeared ? MarchVerificationMode.WorldMapTimerAppeared
                : worldMapTimer ? MarchVerificationMode.WorldMapTimerProgression
                : structural ? MarchVerificationMode.StructuralFallback
                : MarchVerificationMode.None;
            return new MarchDispatchObservation
            {
                Timestamp = DateTimeOffset.UtcNow,
                State = state.State,
                TeamSelectionFound = panel,
                WorldMapFound = world,
                ExpectedTeamBadgeFound = badge,
                SelectedBorderFound = selected,
                BusyStatusFound = false,
                MarchTimerFound = timerFound,
                ExpectedTeamReadyBeforeDispatch = readyBeforeDispatch,
                ExpectedTeamReadyAfterDispatch = readyAfter,
                ReadyAnchorDisappeared = readyDisappeared,
                TimerContentDetected = timerFound,
                TimerProgressionDetected = timerChanged,
                TimerForegroundRatio = timerForeground,
                TimerDifferenceRatio = timerDifference,
                TimerRegion = timerRegion,
                VerificationMode = mode,
                DirectSuccessRuleMatched = direct || (worldMapTimerAppeared && !structural)
                    || (worldMapTimer && !structural),
                StructuralSuccessRuleMatched = structural,
                TeamRegionDifference = comparison.DifferenceRatio,
                TeamRegionChanged = changed,
                SuccessRuleMatched = direct || worldMapTimerAppeared || worldMapTimer || structural
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
            TeamNumber[] teams = { TeamNumber.Team1, TeamNumber.Team2,
                TeamNumber.Team3, TeamNumber.Team4 };
            var badgeRequests = teams.Where(team => registry.Exists(BadgeId(team)))
                .Select(team => new KeyValuePair<TeamNumber, ImageMatchRequest>(team,
                    new ImageMatchRequest(registry.LoadBytes(BadgeId(team)),
                        teamOptions.TeamSelectionRosterRegion))).ToArray();
            IReadOnlyList<ImageMatchResult> badgeResults = matcher is IBatchImageMatcher batch
                ? batch.FindMany(frame, badgeRequests.Select(item => item.Value).ToArray())
                : badgeRequests.Select(item => matcher.Find(frame,
                    item.Value.TemplatePng, item.Value.SearchRegion)).ToArray();
            var badges = new Dictionary<TeamNumber, ImageMatchResult>();
            for (int index = 0; index < badgeRequests.Length; index++)
                if (HasBounds(badgeResults[index]))
                    badges[badgeRequests[index].Key] = badgeResults[index];
            TeamSelectionRosterLayout layout = TeamSelectionRosterLayoutResolver.Resolve(
                badges, teamOptions.TeamSelectionRosterRegion,
                teamOptions.ExpectedWidth, teamOptions.ExpectedHeight);
            bool badge = layout.Rows.TryGetValue(expectedTeam,
                out ImageRegion expectedRegion);
            var selected = new List<TeamNumber>();
            foreach (KeyValuePair<TeamNumber, ImageRegion> item in layout.Rows)
            {
                ImageMatchResult selectedMatch = Match(frame,
                    TemplateId.TeamSelectedBorderAnchor, item.Value);
                if (HasBounds(selectedMatch) && Overlaps(item.Value, selectedMatch))
                    selected.Add(item.Key);
            }
            return new Verification
            {
                BadgeFound = badge,
                // The selection workflow has already selected ExpectedTeam.  A border
                // template may also match a neighbouring row's bright artwork, so do
                // not discard a valid expected-row border merely because that happens.
                // A border on another row *without* the expected-row border is still
                // treated as a mismatch and no action button is tapped.
                SelectedFound = selected.Contains(expectedTeam),
                Ambiguous = selected.Count > 1 && !selected.Contains(expectedTeam),
                ActualSelectedTeam = selected.Contains(expectedTeam)
                    ? (TeamNumber?)expectedTeam
                    : selected.Count == 1 ? (TeamNumber?)selected[0] : null,
                VisibleTeams = layout.VisibleTeams,
                ExpectedRowBounds = expectedRegion
            };
        }

        private Verification VerifySelection(CapturedFrame frame, TeamNumber expectedTeam,
            TemplateId badgeId)
        {
            TeamNumber[] teams = { TeamNumber.Team1, TeamNumber.Team2,
                TeamNumber.Team3, TeamNumber.Team4 };
            var badgeRequests = teams.Where(team => registry.Exists(BadgeId(team)))
                .Select(team => new KeyValuePair<TeamNumber, ImageMatchRequest>(team,
                    new ImageMatchRequest(registry.LoadBytes(BadgeId(team)),
                        teamOptions.TeamSelectionRosterRegion))).ToArray();
            IReadOnlyList<ImageMatchResult> badgeResults = FindMany(frame,
                badgeRequests.Select(item => item.Value).ToArray());
            var badges = new Dictionary<TeamNumber, ImageMatchResult>();
            for (int index = 0; index < badgeRequests.Length; index++)
                if (HasBounds(badgeResults[index]))
                    badges[badgeRequests[index].Key] = badgeResults[index];
            TeamSelectionRosterLayout layout = TeamSelectionRosterLayoutResolver.Resolve(
                badges, teamOptions.TeamSelectionRosterRegion,
                teamOptions.ExpectedWidth, teamOptions.ExpectedHeight);
            bool badge = layout.Rows.TryGetValue(expectedTeam,
                out ImageRegion expectedRegion);
            var selected = new List<TeamNumber>();
            foreach (KeyValuePair<TeamNumber, ImageRegion> item in layout.Rows)
            {
                ImageMatchResult selectedMatch = Match(frame,
                    TemplateId.TeamSelectedBorderAnchor, item.Value);
                if (HasBounds(selectedMatch) && Overlaps(item.Value, selectedMatch))
                    selected.Add(item.Key);
            }
            return new Verification
            {
                BadgeFound = badge,
                SelectedFound = selected.Contains(expectedTeam),
                Ambiguous = selected.Count > 1 && !selected.Contains(expectedTeam),
                ActualSelectedTeam = selected.Contains(expectedTeam)
                    ? (TeamNumber?)expectedTeam
                    : selected.Count == 1 ? (TeamNumber?)selected[0] : null,
                VisibleTeams = layout.VisibleTeams,
                ExpectedRowBounds = expectedRegion
            };
        }

        private ImageMatchResult Match(byte[] frame, TemplateId id, ImageRegion? region) =>
            matcher.Find(frame, registry.LoadBytes(id), region) ?? ImageMatchResult.NotFound();
        private ImageMatchResult Match(CapturedFrame frame, TemplateId id, ImageRegion? region)
        {
            var frameMatcher = matcher as IFrameImageMatcher;
            return frameMatcher != null
                ? frameMatcher.Find(frame, registry.LoadBytes(id), region) ?? ImageMatchResult.NotFound()
                : Match(frame.GetPngBytes(), id, region);
        }
        private IReadOnlyList<ImageMatchResult> FindMany(CapturedFrame frame,
            IReadOnlyList<ImageMatchRequest> requests)
        {
            var frameMatcher = matcher as IFrameImageMatcher;
            if (frameMatcher != null) return frameMatcher.FindMany(frame, requests);
            var batchMatcher = matcher as IBatchImageMatcher;
            if (batchMatcher != null) return batchMatcher.FindMany(frame.GetPngBytes(), requests);
            return requests.Select(request => matcher.Find(frame.GetPngBytes(),
                request.TemplatePng, request.SearchRegion)).ToArray();
        }
        private FocusedTeamSelectionEvidence GetFocusedTeamSelectionEvidence(byte[] frame)
        {
            ImageMatchResult panel = Match(frame, TemplateId.TeamSelectionPanelAnchor, null);
            ImageMatchResult adjust = Match(frame, TemplateId.TeamAdjustFormationButton, null);
            ImageMatchResult action = Match(frame, TemplateId.TeamActionButtonEnabled, null);
            return new FocusedTeamSelectionEvidence
            {
                PanelFound = HasBounds(panel),
                AdjustFound = HasBounds(adjust),
                ActionFound = HasBounds(action),
                Action = action
            };
        }
        private FocusedTeamSelectionEvidence GetFocusedTeamSelectionEvidence(CapturedFrame frame)
        {
            ImageMatchResult panel = Match(frame, TemplateId.TeamSelectionPanelAnchor, null);
            ImageMatchResult adjust = Match(frame, TemplateId.TeamAdjustFormationButton, null);
            ImageMatchResult action = Match(frame, TemplateId.TeamActionButtonEnabled, null);
            return new FocusedTeamSelectionEvidence
            {
                PanelFound = HasBounds(panel),
                AdjustFound = HasBounds(adjust),
                ActionFound = HasBounds(action),
                Action = action
            };
        }
        private static string FormatBounds(ImageMatchResult match) => HasBounds(match)
            ? $"({match.X},{match.Y},{match.Width},{match.Height})" : string.Empty;
        private bool OptionalFound(byte[] frame, TemplateId id, ImageRegion region) =>
            registry.Exists(id) && Match(frame, id, region).Found;
        private bool OptionalFound(CapturedFrame frame, TemplateId id, ImageRegion region) =>
            registry.Exists(id) && Match(frame, id, region).Found;
        private static bool HasBounds(ImageMatchResult match) =>
            match != null && match.Found && match.Width > 0 && match.Height > 0;
        private static bool Overlaps(ImageRegion region, ImageMatchResult match) =>
            match.X < region.X + region.Width && match.X + match.Width > region.X
            && match.Y < region.Y + region.Height && match.Y + match.Height > region.Y;

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

        private sealed class FocusedTeamSelectionEvidence
        {
            public bool PanelFound { get; set; }
            public bool AdjustFound { get; set; }
            public bool ActionFound { get; set; }
            public ImageMatchResult Action { get; set; }
            public bool IsReady => PanelFound && AdjustFound && ActionFound;
        }
        private static TemplateId BadgeId(TeamNumber team)
        {
            switch (team)
            {
                case TeamNumber.Team1: return TemplateId.Team1Badge;
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
            if (request.ExpectedTeam != TeamNumber.Team1 && request.ExpectedTeam != TeamNumber.Team2 && request.ExpectedTeam != TeamNumber.Team3
                && request.ExpectedTeam != TeamNumber.Team4)
                return "ExpectedTeam must be Team1, Team2, Team3, or Team4.";
            return null;
        }

        private static void Apply(DispatchMarchResult result, MarchDispatchObservation item)
        {
            result.TeamSelectionClosed |= !item.TeamSelectionFound;
            result.WorldMapVerified |= item.WorldMapFound;
            result.SelectedBorderDisappeared |= !item.SelectedBorderFound;
            result.TeamRegionChanged |= item.TeamRegionChanged;
            result.BusyStatusVerified |= item.BusyStatusFound;
            result.MarchTimerVerified |= item.TimerProgressionDetected;
            result.ExpectedTeamReadyBeforeDispatch |= item.ExpectedTeamReadyBeforeDispatch;
            result.ExpectedTeamReadyAfterDispatch = item.ExpectedTeamReadyAfterDispatch;
            result.ReadyAnchorDisappeared |= item.ReadyAnchorDisappeared;
            result.ExpectedTeamTimerVerified |= item.TimerProgressionDetected
                || item.VerificationMode == MarchVerificationMode.WorldMapTimerAppeared;
            result.FinalTimerForegroundRatio = item.TimerForegroundRatio;
            result.FinalTimerDifferenceRatio = item.TimerDifferenceRatio;
            result.DirectMarchVerified |= item.DirectSuccessRuleMatched;
            result.StructuralMarchVerified |= item.StructuralSuccessRuleMatched;
            if (ModePriority(item.VerificationMode) > ModePriority(result.VerificationMode))
                result.VerificationMode = item.VerificationMode;
            result.TeamRegionDifference = item.TeamRegionDifference;
        }

        private static int ModePriority(MarchVerificationMode mode)
        {
            switch (mode)
            {
                case MarchVerificationMode.ReadyDisappearedAndTimerProgression: return 5;
                case MarchVerificationMode.TimerProgressionPlusStructural: return 4;
                case MarchVerificationMode.WorldMapTimerAppeared: return 3;
                case MarchVerificationMode.WorldMapTimerProgression: return 2;
                case MarchVerificationMode.StructuralFallback: return 1;
                default: return 0;
            }
        }

        private static bool IsStrongTimerVerification(MarchVerificationMode mode) =>
            mode == MarchVerificationMode.ReadyDisappearedAndTimerProgression
            || mode == MarchVerificationMode.TimerProgressionPlusStructural
            || mode == MarchVerificationMode.WorldMapTimerAppeared
            || mode == MarchVerificationMode.WorldMapTimerProgression;

        private async Task<CapturedFrame> CaptureFrameAsync(string deviceName,
            CancellationToken cancellationToken)
        {
            var frameClient = client as IFrameCapturingLdPlayerClient;
            if (frameClient != null)
                return await frameClient.CaptureFrameAsync(deviceName, cancellationToken);

            byte[] png = await client.CaptureScreenshotPngAsync(deviceName, cancellationToken);
            using (var stream = new MemoryStream(png, writable: false))
            using (var source = new Bitmap(stream))
                return new CapturedFrame(new Bitmap(source), DateTimeOffset.UtcNow);
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
            result.ObservedSelectedTeam = result.ActualSelectedTeam;
            result.SelectionMismatch = result.ActualSelectedTeam.HasValue
                && result.ActualSelectedTeam.Value != result.ExpectedTeam;
            result.ActionTapSent = result.ActionTapCount > 0;
            result.Success = outcome == DispatchMarchOutcome.MarchStarted
                || outcome == DispatchMarchOutcome.AlreadyMarching;
            result.Duration = watch.Elapsed;
            result.Message = message;
            result.ErrorMessage = error;
            logger.Info($"[March Dispatch] ExpectedTeam='{result.ExpectedTeam}', InitialState='{result.InitialState}', FinalState='{result.FinalState}', ReadyBeforeDispatch={result.ExpectedTeamReadyBeforeDispatch}, ReadyAfterDispatch={result.ExpectedTeamReadyAfterDispatch}, ReadyAnchorDisappeared={result.ReadyAnchorDisappeared}, TimerVerified={result.ExpectedTeamTimerVerified}, TimerForegroundRatio={result.FinalTimerForegroundRatio:F4}, TimerDifferenceRatio={result.FinalTimerDifferenceRatio:F4}, VerificationMode='{result.VerificationMode}', DirectVerified={result.DirectMarchVerified}, StructuralVerified={result.StructuralMarchVerified}, ActionTapCount={result.ActionTapCount}, ObservedFrames={result.ObservedFrameCount}, ConsecutiveSuccessFrames={result.ConsecutiveSuccessFrames}, Outcome='{outcome}', DurationMs={result.Duration.TotalMilliseconds:F0}, Cancellation={outcome == DispatchMarchOutcome.Cancelled}, Error='{error ?? string.Empty}'");
            return result;
        }

        private void LogObservation(string deviceName, DispatchMarchResult result,
            MarchDispatchObservation item) => logger.Info(
            $"[March Dispatch] DeviceName='{deviceName}', Observation={result.ObservedFrameCount}, GameState='{item.State}', TeamSelectionClosed={!item.TeamSelectionFound}, WorldMapVerified={item.WorldMapFound}, SelectedBorderFound={item.SelectedBorderFound}, ReadyBeforeDispatch={item.ExpectedTeamReadyBeforeDispatch}, ReadyAfterDispatch={item.ExpectedTeamReadyAfterDispatch}, ReadyAnchorDisappeared={item.ReadyAnchorDisappeared}, TimerContentDetected={item.TimerContentDetected}, TimerProgressionDetected={item.TimerProgressionDetected}, TimerForegroundRatio={item.TimerForegroundRatio:F4}, TimerDifferenceRatio={item.TimerDifferenceRatio:F4}, TimerRegion=({item.TimerRegion.X},{item.TimerRegion.Y},{item.TimerRegion.Width},{item.TimerRegion.Height}), VerificationMode='{item.VerificationMode}', DirectRule={item.DirectSuccessRuleMatched}, StructuralRule={item.StructuralSuccessRuleMatched}, TeamRegionDifference={item.TeamRegionDifference?.ToString("F4") ?? "n/a"}, ConsecutiveSuccessFrames={result.ConsecutiveSuccessFrames}, ActionTapCount={result.ActionTapCount}");

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
            ,VisibleTeams = new TeamNumber[0]
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
            ,VisibleTeams = new TeamNumber[0]
        };

        private sealed class Verification
        {
            public bool BadgeFound { get; set; }
            public bool SelectedFound { get; set; }
            public bool Ambiguous { get; set; }
            public TeamNumber? ActualSelectedTeam { get; set; }
            public IReadOnlyList<TeamNumber> VisibleTeams { get; set; }
            public ImageRegion ExpectedRowBounds { get; set; }
        }
    }
}
