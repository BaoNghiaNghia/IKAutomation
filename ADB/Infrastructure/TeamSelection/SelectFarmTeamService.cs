using ADB_Tool_Automation_Post_FB.Core.Abstractions;
using ADB_Tool_Automation_Post_FB.Core.Concurrency;
using ADB_Tool_Automation_Post_FB.Core.Diagnostics;
using ADB_Tool_Automation_Post_FB.Core.GameDetection;
using ADB_Tool_Automation_Post_FB.Core.TeamSelection;
using ADB_Tool_Automation_Post_FB.Core.Vision;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace ADB_Tool_Automation_Post_FB.Infrastructure.TeamSelection
{
    public sealed class SelectFarmTeamService : ISelectFarmTeamService
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
        private readonly IDeviceOperationLock operationLock;
        private readonly FarmTeamSelectionOptions options;
        private readonly ISelectFarmTeamDiagnosticStore diagnosticStore;
        private readonly IDiagnosticLogger logger;
        private readonly ISelectedTeamDetector selectedTeamDetector;

        public SelectFarmTeamService(IGameStateDetector detector, ILdPlayerClient client,
            ITemplateRegistry registry, IImageMatcher matcher,
            IDeviceOperationLock operationLock, FarmTeamSelectionOptions options,
            ISelectFarmTeamDiagnosticStore diagnosticStore, IDiagnosticLogger logger)
            : this(detector, client, registry, matcher, operationLock, options,
                diagnosticStore, logger, null)
        {
        }

        public SelectFarmTeamService(IGameStateDetector detector, ILdPlayerClient client,
            ITemplateRegistry registry, IImageMatcher matcher,
            IDeviceOperationLock operationLock, FarmTeamSelectionOptions options,
            ISelectFarmTeamDiagnosticStore diagnosticStore, IDiagnosticLogger logger,
            ISelectedTeamDetector selectedTeamDetector)
        {
            this.detector = detector ?? throw new ArgumentNullException(nameof(detector));
            this.client = client ?? throw new ArgumentNullException(nameof(client));
            this.registry = registry ?? throw new ArgumentNullException(nameof(registry));
            this.matcher = matcher ?? throw new ArgumentNullException(nameof(matcher));
            this.operationLock = operationLock ?? throw new ArgumentNullException(nameof(operationLock));
            this.options = options ?? throw new ArgumentNullException(nameof(options));
            this.diagnosticStore = diagnosticStore ?? throw new ArgumentNullException(nameof(diagnosticStore));
            this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
            this.selectedTeamDetector = selectedTeamDetector;
        }

        public async Task<SelectFarmTeamResult> SelectAsync(string deviceName,
            TeamSelectionRequest request, CancellationToken cancellationToken)
        {
            string validationError = ValidateRequest(deviceName, request);
            if (validationError != null) return Empty(SelectFarmTeamOutcome.Failed, validationError);
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                return await operationLock.RunAsync(deviceName.Trim(),
                    token => SelectCoreAsync(deviceName.Trim(), request, token), cancellationToken);
            }
            catch (OperationCanceledException)
            {
                return Empty(SelectFarmTeamOutcome.Cancelled,
                    "Farm team selection was cancelled while waiting for the device lock.");
            }
        }

        private async Task<SelectFarmTeamResult> SelectCoreAsync(string deviceName,
            TeamSelectionRequest request, CancellationToken cancellationToken)
        {
            var watch = Stopwatch.StartNew();
            var attempts = new List<TeamSelectionAttempt>();
            var attemptedTeams = new List<TeamNumber>();
            var result = NewResult(attempts, attemptedTeams);
            byte[] lastFrame = null;
            try
            {
                logger.Info($"[Farm Team Selection] RunId='{request.RunId ?? string.Empty}', DeviceName='{deviceName}', Allowed='{Join(request.AllowedTeams)}', Priority='{Join(request.Priority)}', Cancellation=false, Phase='Starting'");
                if (!RequiredScreenTemplatesExist(out string screenTemplateError)
                    || !registry.Exists(TemplateId.TeamSelectedBorderAnchor))
                {
                    string error = screenTemplateError ?? $"Required template '{TemplateId.TeamSelectedBorderAnchor}' was not found at '{registry.GetPath(TemplateId.TeamSelectedBorderAnchor)}'.";
                    return Complete(result, SelectFarmTeamOutcome.Failed,
                        "Farm team selection templates are incomplete; no Tap was sent.", error, watch);
                }

                GameDetectionResult initial = await detector.DetectAsync(deviceName, cancellationToken);
                result.InitialState = initial.State;
                result.FinalState = initial.State;
                if (!IsSelectionScreen(initial))
                {
                    lastFrame = await TryCaptureAsync(deviceName, cancellationToken);
                    return await CompleteAsync(deviceName, result,
                        SelectFarmTeamOutcome.TeamSelectionNotReady,
                        "Team Selection is not ready; no Tap was sent.",
                        initial.ErrorMessage, lastFrame, watch, cancellationToken);
                }
                result.TeamSelectionScreenVerified = true;

                GameDetectionResult freshState = await ConfirmSelectionScreenAsync(
                    deviceName, cancellationToken, frame => lastFrame = frame);
                if (!IsSelectionScreen(freshState))
                    return await CompleteAsync(deviceName, result,
                        SelectFarmTeamOutcome.TeamSelectionNotReady,
                        "Team Selection was not ready on the fresh screenshot; no Tap was sent.",
                        freshState.ErrorMessage, lastFrame, watch, cancellationToken);

                IReadOnlyDictionary<TeamNumber, ImageRegion> initialRegions =
                    ResolveTeamRegions(lastFrame, freshState);
                result.VisibleTeams = initialRegions.Keys.OrderBy(item => (int)item).ToArray();
                SelectedScan selected = ScanSelected(lastFrame, initialRegions);
                bool rosterReconciled = result.VisibleTeams.Any(team =>
                    !(request.WorldMapAvailableTeams ?? new TeamNumber[0]).Contains(team));
                logger.Info($"[TeamSelection Full Scan] RunId='{request.RunId ?? string.Empty}', DeviceName='{deviceName}', ExpectedTeam='{request.ExpectedTeam}', WorldMapAvailableTeams='{Join(request.WorldMapAvailableTeams ?? new TeamNumber[0])}', WorldMapReadyTeams='{Join(request.WorldMapReadyTeams ?? new TeamNumber[0])}', ScreenExistsTeams='{Join(result.VisibleTeams)}', SelectedTeam='{(selected.Teams.Count == 1 ? selected.Teams[0].ToString() : string.Empty)}', SelectionAmbiguous={selected.IsAmbiguous}, RosterReconciled={rosterReconciled}, LayoutSource='BadgeAnchors'");
                if (selectedTeamDetector == null && selected.IsAmbiguous)
                    return await CompleteAsync(deviceName, result, SelectFarmTeamOutcome.Failed,
                        "Selected border appeared in multiple team ROIs; no Tap was sent.",
                        "Ambiguous selected-team evidence.", lastFrame, watch, cancellationToken);

                TeamSelectionTargetResolution target = ResolveTarget(request);
                result.ExpectedTeam = target.TargetTeam;
                logger.Info($"[Farm Team Selection Target] DeviceName='{deviceName}', RunId='{request.RunId ?? string.Empty}', ExpectedTeam='{request.ExpectedTeam}', ResolvedTargetTeam='{target.TargetTeam}', TargetSource='{target.Source}', PreTapSelectedTeam='{(selected.Teams.Count == 1 ? selected.Teams[0].ToString() : string.Empty)}', PreTapSelectedConfident={!selected.IsAmbiguous && selected.Teams.Count == 1}, AllowedTeams='{Join(request.AllowedTeams)}', WorldMapReadyTeams='{Join(request.WorldMapReadyTeams ?? new TeamNumber[0])}', WorldMapAvailableTeams='{Join(request.WorldMapAvailableTeams ?? new TeamNumber[0])}', RosterStatus='{request.WorldMapRosterStatus ?? string.Empty}', RosterConfidence='{request.WorldMapRosterConfidence ?? string.Empty}', AlreadySelectedAccepted=false, FailureReason='{target.FailureReason ?? string.Empty}'");
                if (!target.Success)
                    return await CompleteAsync(deviceName, result, target.Outcome,
                        target.Message, target.FailureReason, lastFrame, watch, cancellationToken);

                SelectedTeamConsensusResult detectedSelection = null;
                if (selectedTeamDetector != null)
                {
                    detectedSelection = await selectedTeamDetector.DetectAsync(deviceName,
                        new SelectedTeamDetectionContext
                        {
                            TeamRegions = initialRegions,
                            ExpectedWidth = options.ExpectedWidth,
                            ExpectedHeight = options.ExpectedHeight,
                            ConsensusFrames = options.SelectedConsensusFrames,
                            RequiredMatchingFrames = options.SelectedRequiredMatchingFrames,
                            FrameIntervalMs = options.SelectedFrameIntervalMs,
                            TimeoutMs = options.SelectedDetectionTimeoutMs,
                            MinimumScore = options.SelectedMinimumScore,
                            WinningMargin = options.SelectedWinningMargin
                        }, cancellationToken);
                    logger.Info($"[Farm Team Selection PreTap] DeviceName='{deviceName}', ExpectedTeam='{target.TargetTeam}', DetectedTeam='{detectedSelection.Team}', DetectionConfident={detectedSelection.IsConfident}, DetectionAmbiguous={detectedSelection.IsAmbiguous}, DetectionWinningMargin={detectedSelection.WinningMargin}, FramesObserved={detectedSelection.FramesObserved}, MatchingFrames={detectedSelection.MatchingFrames}, FailureReason='{detectedSelection.FailureReason ?? string.Empty}'");
                    LogFrameScores(deviceName, "PreTap", target.TargetTeam, detectedSelection);
                }

                bool expectedAlreadySelected = selectedTeamDetector != null
                    ? detectedSelection != null && detectedSelection.IsConfident
                        && target.TargetTeam.HasValue
                        && detectedSelection.Team == target.TargetTeam
                    : target.TargetTeam.HasValue && selected.Teams.Contains(target.TargetTeam.Value);
                if (expectedAlreadySelected
                    && HasEnabledAction(freshState))
                {
                    result.SelectedTeam = target.TargetTeam;
                    result.ActualSelectedTeam = target.TargetTeam;
                    result.SelectedStateVerified = true;
                    attempts.Add(new TeamSelectionAttempt
                    {
                        TeamNumber = target.TargetTeam.Value,
                        AlreadySelected = true,
                        SelectedVerified = true,
                        SelectedBorderMatch = selected.Matches.ContainsKey(target.TargetTeam.Value)
                            ? selected.Matches[target.TargetTeam.Value] : null,
                        Message = "Đội dự kiến đã được chọn sẵn; không gửi lệnh chọn."
                    });
                    return Complete(result, SelectFarmTeamOutcome.AlreadySelected,
                        $"Đội {((int)target.TargetTeam.Value)} đã được chọn sẵn.", null, watch);
                }
                result.ActualSelectedTeam = selectedTeamDetector != null && detectedSelection != null
                    && detectedSelection.IsConfident ? detectedSelection.Team
                    : selected.Teams.Count == 1 ? (TeamNumber?)selected.Teams[0] : null;
                if (result.ActualSelectedTeam.HasValue
                    && result.ActualSelectedTeam != target.TargetTeam)
                    logger.Info($"[Farm Team Selection Target] DeviceName='{deviceName}', PreTapSelectedTeam='{result.ActualSelectedTeam}', ResolvedTargetTeam='{target.TargetTeam}', AlreadySelectedAccepted=false, NextAction='TapTarget'.");

                DateTimeOffset selectionDeadline = DateTimeOffset.UtcNow.AddSeconds(
                    options.SelectionTimeoutSeconds);
                bool continueAfterConfirmedUnavailable = false;
                bool postTapDifferentExpectedTeamObserved = false;
                // The detector-backed production path resolves exactly one target for
                // this operation.  Priority is only a legacy compatibility plan.
                TeamNumber resolvedTargetTeam = target.TargetTeam.Value;
                IEnumerable<TeamNumber> candidateTeams = selectedTeamDetector != null
                    ? new[] { resolvedTargetTeam }
                    : BuildCandidatePlan(request, target);
                foreach (TeamNumber team in candidateTeams)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (!request.AllowedTeams.Contains(team) || (!request.AllowTeam1 && team == TeamNumber.Team1))
                        continue;
                    attemptedTeams.Add(team);
                    TemplateId? badgeIdValue = BadgeId(team);
                    if (!badgeIdValue.HasValue)
                    {
                        attempts.Add(new TeamSelectionAttempt { TeamNumber = team,
                            Message = $"No badge template is registered for {team}; no Tap was sent." });
                        continue;
                    }
                    TemplateId badgeId = badgeIdValue.Value;
                    if (!registry.Exists(badgeId))
                    {
                        attempts.Add(new TeamSelectionAttempt
                        {
                            TeamNumber = team,
                            Message = $"Badge template '{badgeId}' was not found at '{registry.GetPath(badgeId)}'; no Tap was sent."
                        });
                        continue;
                    }

                    RosterFrame visible = await EnsureTeamVisibleAsync(deviceName, team,
                        lastFrame, freshState, cancellationToken);
                    lastFrame = visible.Frame;
                    freshState = visible.State;
                    initialRegions = visible.Regions;
                    result.ScrollAttempts += visible.ScrollAttempts;
                    result.VisibleTeams = initialRegions.Keys.OrderBy(item => (int)item).ToArray();
                    if (!initialRegions.TryGetValue(team, out ImageRegion teamRegion))
                    {
                        attempts.Add(new TeamSelectionAttempt
                        {
                            TeamNumber = team,
                            ScrollAttempt = result.ScrollAttempts,
                            Message = "Expected numbered team badge is not visible after bounded scrolling."
                        });
                        if (selectedTeamDetector == null && request.Priority.Count == 1)
                        {
                            result.FailureReason = "ExpectedTeamNotVisible";
                            return await CompleteAsync(deviceName, result,
                                SelectFarmTeamOutcome.ExpectedTeamNotVisible,
                                "Không tìm thấy đội dự kiến trong danh sách đội sau khi cuộn giới hạn.",
                                null, lastFrame, watch, cancellationToken);
                        }
                        if (selectedTeamDetector == null)
                            continue;
                    }

                    for (int attemptNumber = 1;
                        attemptNumber <= options.MaxSelectionAttemptsPerTeam;
                        attemptNumber++)
                    {
                        bool selectedButUnavailable = false;
                        cancellationToken.ThrowIfCancellationRequested();
                        if (DateTimeOffset.UtcNow >= selectionDeadline
                            && !continueAfterConfirmedUnavailable)
                        {
                            LogTapSkippedWithoutRow(deviceName, resolvedTargetTeam,
                                attemptNumber, "SelectionDeadlineElapsed", result.TeamTapCount);
                            return await CompleteAsync(deviceName, result,
                                SelectFarmTeamOutcome.SelectionTimeout,
                                "Farm team selection timed out before another safe attempt.",
                                null, lastFrame, watch, cancellationToken);
                        }
                        continueAfterConfirmedUnavailable = false;

                        lastFrame = await client.CaptureScreenshotPngAsync(deviceName, cancellationToken);
                        DateTimeOffset inputFrameCapturedAt = DateTimeOffset.UtcNow;
                        GameDetectionResult state = detector.Detect(lastFrame);
                        if (!IsSelectionScreen(state))
                            return await CompleteAsync(deviceName, result,
                                SelectFarmTeamOutcome.TeamSelectionNotReady,
                                "Team Selection stopped being ready; no further Tap was sent.",
                                state.ErrorMessage, lastFrame, watch, cancellationToken);

                        IReadOnlyDictionary<TeamNumber, ImageRegion> currentRegions =
                            ResolveTeamRegions(lastFrame, state);
                        result.VisibleTeams = currentRegions.Keys.OrderBy(item => (int)item).ToArray();
                        if (!currentRegions.TryGetValue(team, out teamRegion))
                        {
                            attempts.Add(new TeamSelectionAttempt
                            {
                                TeamNumber = team,
                                TapAttempt = attemptNumber,
                                ScrollAttempt = result.ScrollAttempts,
                                Message = "Expected team row was not visible on the fresh attempt screenshot; no Tap was sent."
                            });
                            result.FailureReason = "ExpectedTeamNotVisible";
                            LogTapSkippedWithoutRow(deviceName, resolvedTargetTeam,
                                attemptNumber, "TargetRowNotVisible", result.TeamTapCount);
                            continue;
                        }
                        // The confidence detector owns all production selected-team
                        // decisions.  The legacy template scan remains only for the
                        // compatibility constructor, where no detector was injected.
                        if (selectedTeamDetector == null)
                        {
                            SelectedScan currentSelected = ScanSelected(lastFrame, currentRegions);
                            result.ActualSelectedTeam = currentSelected.Teams.Count == 1
                                ? (TeamNumber?)currentSelected.Teams[0] : null;
                            if (currentSelected.IsAmbiguous)
                                return await CompleteAsync(deviceName, result,
                                    SelectFarmTeamOutcome.Failed,
                                    "Selected border appeared in multiple team ROIs before Tap.",
                                    "Ambiguous selected-team evidence.", lastFrame, watch, cancellationToken);
                            if (currentSelected.Teams.Count == 1
                                && currentSelected.Teams[0] == team)
                            {
                                bool actionAvailable = HasEnabledAction(state);
                                attempts.Add(new TeamSelectionAttempt
                                {
                                    TeamNumber = team,
                                    SelectedVerified = true,
                                    SelectedBorderMatch = currentSelected.Matches[team],
                                    Message = actionAvailable
                                        ? "Team selection became visible on the fresh retry frame; no repeated Tap was sent."
                                        : "Team selection became visible on the fresh retry frame but has no enabled farm action; trying the next eligible team."
                                });
                                if (actionAvailable)
                                {
                                    result.SelectedTeam = team;
                                    result.SelectedStateVerified = true;
                                    result.FinalState = GameState.TeamSelection;
                                    return Complete(result, SelectFarmTeamOutcome.TeamSelected,
                                        $"{team} was selected and verified on a fresh retry frame.", null, watch);
                                }
                                continueAfterConfirmedUnavailable = true;
                                break;
                            }
                        }

                        ImageRegion region = teamRegion;
                        ImageMatchResult badge = Match(lastFrame, badgeId, region);
                        bool disabled = IsDisabled(lastFrame, region);
                        var attempt = new TeamSelectionAttempt
                        {
                            TeamNumber = team,
                            BadgeFound = HasBounds(badge),
                            BadgeMatch = badge,
                            DisabledDetected = disabled,
                            RowBounds = region,
                            TapAttempt = attemptNumber,
                            ScrollAttempt = result.ScrollAttempts,
                            SelectedBefore = result.ActualSelectedTeam
                        };
                        attempts.Add(attempt);
                        LogMatch(deviceName, team, attemptNumber, region, badge, disabled);
                        if (!HasBounds(badge))
                        {
                            attempt.Message = "Team badge was not found with valid bounds; no Tap was sent.";
                            LogTapPlanned(deviceName, resolvedTargetTeam, attemptNumber, badge,
                                disabled, region, 0, 0, false, "BadgeBoundsInvalid",
                                result.TeamTapCount);
                            continue;
                        }
                        if (disabled)
                        {
                            attempt.Message = "Disabled team evidence was found; no Tap was sent.";
                            LogTapPlanned(deviceName, resolvedTargetTeam, attemptNumber, badge,
                                true, region, 0, 0, false, "TargetDisabled",
                                result.TeamTapCount);
                            continue;
                        }

                        int tapX;
                        int tapY;
                        string tapSkipReason;
                        bool tapPointValid = TryGetSafeTapPoint(region, currentRegions,
                            out tapX, out tapY, out tapSkipReason);
                        LogTapPlanned(deviceName, resolvedTargetTeam, attemptNumber, badge,
                            false, region, tapX, tapY, tapPointValid, tapSkipReason,
                            result.TeamTapCount);
                        if (!tapPointValid)
                        {
                            attempt.Message = "The expected team row has no safe selectable area; no Tap was sent."
                                + " Reason=" + tapSkipReason + ".";
                            continue;
                        }
                        int inputFrameAgeMs = (int)Math.Max(0,
                            (DateTimeOffset.UtcNow - inputFrameCapturedAt).TotalMilliseconds);
                        if (selectedTeamDetector == null
                            && inputFrameAgeMs > options.MaxInputFrameAgeMs)
                        {
                            attempt.Message = "The team-selection screenshot was stale; no Tap was sent.";
                            LogTapPlanned(deviceName, resolvedTargetTeam, attemptNumber, badge,
                                false, region, tapX, tapY, false, "InputFrameStale",
                                result.TeamTapCount);
                            continue;
                        }
                        try
                        {
                            await client.TapAsync(deviceName, tapX, tapY, cancellationToken);
                            result.TeamTapCount++;
                            attempt.TapSent = true;
                            LogTapIssued(deviceName, resolvedTargetTeam, attemptNumber, tapX,
                                tapY, true, result.TeamTapCount, null);
                        }
                        catch (OperationCanceledException)
                        {
                            throw;
                        }
                        catch (Exception exception)
                        {
                            attempt.Message = "Tap command failed; the resolved target will be retried.";
                            LogTapIssued(deviceName, resolvedTargetTeam, attemptNumber, tapX,
                                tapY, false, result.TeamTapCount, exception.Message);
                            if (attemptNumber < options.MaxSelectionAttemptsPerTeam)
                                await Task.Delay(options.TapRetryDelayMs, cancellationToken);
                            continue;
                        }
                        // The ready-team decision was made on WorldMap and this exact
                        // team row was just tapped from fresh badge bounds. Do not
                        // spend the selection deadline on a second screen detector:
                        // Dispatch owns the mandatory fresh TeamSelection/action
                        // verification immediately before the yellow Gather tap.
                        if (selectedTeamDetector != null)
                        {
                            attempt.SelectedAfter = team;
                            attempt.SelectedVerified = true;
                            attempt.Message = "Đội sẵn sàng đã được chọn; Dispatch sẽ kiểm tra lại nút Thu thập mới nhất.";
                            result.SelectedTeam = team;
                            result.ActualSelectedTeam = team;
                            result.SelectedStateVerified = true;
                            result.FinalState = GameState.TeamSelection;
                            logger.Info($"[Farm Team Selection PostTap] DeviceName='{deviceName}', ExpectedTeam='{team}', ReadyTeamTapAccepted=true, Attempt={attemptNumber}, TeamTapCount={result.TeamTapCount}, NextAction='DispatchFreshAction'");
                            return Complete(result, SelectFarmTeamOutcome.TeamSelected,
                                $"{team} was tapped from the ready-team plan; Dispatch will rematch the fresh action.",
                                null, watch);
                        }
                        logger.Info($"[Team Selection Mapping] RunId='{request.RunId ?? string.Empty}', DeviceName='{deviceName}', ExpectedTeam='{team}', VisibleTeams='{Join(result.VisibleTeams)}', SelectedBefore='{result.ActualSelectedTeam}', BadgeBounds=({badge.X},{badge.Y},{badge.Width},{badge.Height}), RowBounds=({region.X},{region.Y},{region.Width},{region.Height}), TapPointValidated=true, ScrollAttempt={result.ScrollAttempts}, TapAttempt={attemptNumber}, TapCoordinates=({tapX},{tapY}), InputFrameAgeMs={inputFrameAgeMs}, NextAction='VerifyExactTeam'");

                        // Keep the legacy detector path only as a conservative fallback
                        // when the fresh screen itself cannot be confirmed.
                        PostTapVerificationSnapshot freshPostTap = null;
                        int consistentSelectionFrames = 0;
                        // Keep post-Tap confirmation bounded by frames, not by the
                        // entire selection timeout.  Otherwise a missing border can
                        // consume the deadline and prevent the configured retry.
                        for (int postTapObservation = 1;
                            postTapObservation <= 2; postTapObservation++)
                        {
                            if (selectedTeamDetector != null)
                            {
                                bool expectedTeamVerified = freshPostTap != null
                                    && freshPostTap.Detection != null
                                    && freshPostTap.Detection.IsConfident
                                    && !freshPostTap.Detection.IsAmbiguous
                                    && freshPostTap.Detection.Team.HasValue
                                    && freshPostTap.Detection.Team.Value == team;
                                // The panel can expose a false second candidate when a
                                // missing badge stretches a neighbouring row.  For the
                                // post-tap operation we know exactly which row was
                                // tapped, so qualified fresh border evidence in that
                                // row is sufficient; an unrelated row must not block
                                // dispatch.
                                bool expectedRowVerified = HasQualifiedExpectedRow(
                                    freshPostTap?.Detection, team);
                                expectedTeamVerified |= expectedRowVerified;
                                result.ActualSelectedTeam = expectedTeamVerified
                                    ? (TeamNumber?)team : freshPostTap?.Detection?.Team;
                                attempt.SelectedAfter = result.ActualSelectedTeam;
                                if (expectedTeamVerified)
                                {
                                    attempt.SelectedVerified = true;
                                    attempt.Message = expectedRowVerified
                                        ? "Fresh target-row border verified the expected team after Tap."
                                        : "Fresh detector consensus verified the expected team after Tap.";
                                    result.SelectedTeam = team;
                                    result.ActualSelectedTeam = team;
                                    result.SelectedStateVerified = true;
                                    result.FinalState = GameState.TeamSelection;
                                    return Complete(result, SelectFarmTeamOutcome.TeamSelected,
                                        $"{team} was selected and verified.", null, watch);
                                }
                                if (freshPostTap?.Detection?.IsConfident == true
                                    && freshPostTap.Detection.Team.HasValue
                                    && freshPostTap.Detection.Team.Value != team)
                                    postTapDifferentExpectedTeamObserved = true;
                                attempt.Message = "Fresh post-tap detector verification did not confirm the expected team.";
                                break;
                            }
                            await Task.Delay(options.PollIntervalMs, cancellationToken);
                            lastFrame = await client.CaptureScreenshotPngAsync(deviceName, cancellationToken);
                            GameDetectionResult observedState = detector.Detect(lastFrame);
                            if (!IsSelectionScreen(observedState)) continue;
                            IReadOnlyDictionary<TeamNumber, ImageRegion> observedRegions =
                                ResolveTeamRegions(lastFrame, observedState);
                            SelectedScan observed = ScanSelected(lastFrame, observedRegions);
                            result.SelectionVerificationFrames++;
                            result.VisibleTeams = observedRegions.Keys.OrderBy(item => (int)item).ToArray();
                            result.ActualSelectedTeam = observed.Teams.Count == 1
                                ? (TeamNumber?)observed.Teams[0] : null;
                            attempt.SelectedAfter = result.ActualSelectedTeam;
                            if (observed.IsAmbiguous)
                                return await CompleteAsync(deviceName, result,
                                    SelectFarmTeamOutcome.Failed,
                                    "Selected border appeared in multiple team ROIs after Tap.",
                                    "Ambiguous selected-team evidence.", lastFrame, watch, cancellationToken);
                            if (observed.Teams.Count == 1 && observed.Teams[0] == team)
                            {
                                if (!HasEnabledAction(observedState))
                                {
                                    attempt.SelectedVerified = true;
                                    attempt.SelectedBorderMatch = observed.Matches[team];
                                    attempt.Message = "Team was selected but has no enabled farm action; trying the next eligible team.";
                                    selectedButUnavailable = true;
                                    break;
                                }
                                consistentSelectionFrames++;
                                attempt.SelectedVerified = true;
                                attempt.SelectedBorderMatch = observed.Matches[team];
                                attempt.Message = consistentSelectionFrames < 2
                                    ? "Team selected border was observed once; waiting for a second consistent frame."
                                    : "Team selected border was verified in two consecutive target-row frames.";
                                if (consistentSelectionFrames < 2)
                                    continue;
                                result.SelectedTeam = team;
                                result.ActualSelectedTeam = team;
                                result.SelectedStateVerified = true;
                                result.FinalState = GameState.TeamSelection;
                                return Complete(result, SelectFarmTeamOutcome.TeamSelected,
                                    $"{team} was selected and verified.", null, watch);
                            }
                            if (request.ExpectedTeam.HasValue
                                && observed.Teams.Count == 1
                                && observed.Teams[0] != request.ExpectedTeam.Value)
                                postTapDifferentExpectedTeamObserved = true;
                            consistentSelectionFrames = 0;
                        }

                        if (selectedButUnavailable)
                        {
                            continueAfterConfirmedUnavailable = true;
                            break;
                        }
                        attempt.Message = "Tap was sent but selected border was not verified in the target ROI.";
                        if (attemptNumber < options.MaxSelectionAttemptsPerTeam
                            && DateTimeOffset.UtcNow < selectionDeadline)
                            await Task.Delay(options.TapRetryDelayMs, cancellationToken);
                    }
                }

                // A different pre-existing selected team is normal: the target must
                // first be found and tapped.  A mismatch is meaningful only after
                // at least one verified target-row Tap has been sent and fresh
                // post-Tap observations still show a different team.
                bool wrongTeam = request.ExpectedTeam.HasValue
                    && result.TeamTapCount > 0
                    && postTapDifferentExpectedTeamObserved;
                SelectFarmTeamOutcome outcome = wrongTeam
                    ? SelectFarmTeamOutcome.TeamSelectionMismatch
                    : DateTimeOffset.UtcNow >= selectionDeadline
                    ? SelectFarmTeamOutcome.SelectionTimeout
                    : SelectFarmTeamOutcome.NoEligibleTeam;
                if (wrongTeam)
                {
                    result.FailureReason = "WrongTeamSelected";
                    // Android Back from TeamSelection can leave the game at City.
                    // Preserve the verified screen and let the owning transaction retry
                    // selection; no blind cleanup input is safe here.
                    result.CleanupAttempted = false;
                    result.FinalState = GameState.TeamSelection;
                    result.StateAfterCleanup = GameState.TeamSelection;
                    result.CleanupSucceeded = false;
                }
                return await CompleteAsync(deviceName, result, outcome,
                    outcome == SelectFarmTeamOutcome.TeamSelectionMismatch
                        ? "Không thể chuyển sang đội dự kiến sau các lần thử; đã dừng trước lệnh thu thập."
                    : outcome == SelectFarmTeamOutcome.SelectionTimeout
                        ? "Farm team selection timed out without a verified team."
                        : "No eligible team could be selected and verified.",
                    null, lastFrame, watch, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                return Complete(result, SelectFarmTeamOutcome.Cancelled,
                    "Farm team selection was cancelled.", null, watch);
            }
            catch (Exception exception)
            {
                logger.Error($"[Farm Team Selection] DeviceName='{deviceName}', Error='{exception.Message}'", exception);
                return await CompleteAsync(deviceName, result, SelectFarmTeamOutcome.Failed,
                    "Farm team selection failed.", exception.Message,
                    lastFrame, watch, cancellationToken);
            }
        }

        private async Task<PostTapVerificationSnapshot> CaptureFreshPostTapVerificationAsync(
            string deviceName, int attempt, int teamTapCount, TeamNumber expectedTeam,
            CancellationToken cancellationToken)
        {
            await Task.Delay(options.PollIntervalMs, cancellationToken);
            var snapshot = new PostTapVerificationSnapshot
            {
                Attempt = attempt,
                TeamTapCount = teamTapCount
            };
            snapshot.Detection = await selectedTeamDetector.DetectAsync(deviceName,
                new SelectedTeamDetectionContext
                {
                    TeamRegions = options.TeamRegions,
                    ExpectedTeam = expectedTeam,
                    ResolveRowsFromFreshBadges = true,
                    TeamBadgeSearchRegion = options.TeamSelectionRosterRegion,
                    ExpectedWidth = options.ExpectedWidth,
                    ExpectedHeight = options.ExpectedHeight,
                    // This is a targeted post-tap check for the exact row we just
                    // tapped, not an account-wide roster observation.  Waiting for
                    // the normal three-frame consensus here can exhaust the
                    // selection deadline before Dispatch gets a chance to rematch
                    // and tap the enabled yellow action button.
                    ConsensusFrames = 1,
                    RequiredMatchingFrames = 1,
                    FrameIntervalMs = 0,
                    TimeoutMs = options.PollIntervalMs,
                    MinimumScore = options.SelectedMinimumScore,
                    WinningMargin = options.SelectedWinningMargin
                }, cancellationToken);
            snapshot.FrameState = await ConfirmSelectionScreenAsync(deviceName,
                cancellationToken, frame => { });
            snapshot.HasFreshFrameState = snapshot.FrameState != null;
            if (!snapshot.HasFreshFrameState)
                snapshot.FailureReason = "FreshTeamSelectionStateUnavailable";
            return snapshot;
        }

        private sealed class PostTapVerificationSnapshot
        {
            public SelectedTeamConsensusResult Detection { get; set; }
            public GameDetectionResult FrameState { get; set; }
            public bool HasFreshFrameState { get; set; }
            public string FailureReason { get; set; }
            public int Attempt { get; set; }
            public int TeamTapCount { get; set; }
        }

        private void LogFrameScores(string deviceName, string phase, TeamNumber? expectedTeam,
            SelectedTeamFrameResult result)
        {
            if (result?.RowDetails == null) return;
            foreach (SelectedTeamRowScore row in result.RowDetails.Values)
                logger.Info($"[Selected Team Frame Score] DeviceName='{deviceName}', Phase='{phase}', ExpectedTeam='{expectedTeam}', FrameIndex=0, Team='{row.Team}', RowBounds=({row.RowBounds.X},{row.RowBounds.Y},{row.RowBounds.Width},{row.RowBounds.Height}), GeometryValid={row.GeometryValid}, TemplateConfidence={row.TemplateConfidence:F3}, TopBorderScore={row.TopBorderScore:F3}, BottomBorderScore={row.BottomBorderScore:F3}, LeftBorderScore={row.LeftBorderScore:F3}, RightBorderScore={row.RightBorderScore:F3}, BorderEdgesFound={row.BorderEdgesFound}, BorderEvidenceScore={row.BorderEvidenceScore:F3}, ContrastScore={row.ContrastScore:F3}, TemplatePathScore={row.TemplatePathScore:F3}, BorderPathScore={row.BorderPathScore:F3}, EffectiveScore={row.EffectiveScore:F3}, BorderOnlyQualified={row.BorderOnlyQualified}, CandidateQualified={row.CandidateQualified}, Offsets=({row.TopBorderOffset},{row.BottomBorderOffset},{row.LeftBorderOffset},{row.RightBorderOffset}), FailureReason='{row.FailureReason ?? result.FailureReason ?? string.Empty}'");
        }

        private bool HasQualifiedExpectedRow(SelectedTeamConsensusResult detection,
            TeamNumber expectedTeam)
        {
            if (detection?.RowDetails == null
                || !detection.RowDetails.TryGetValue(expectedTeam,
                    out SelectedTeamRowScore expectedRow))
                return false;
            return expectedRow.GeometryValid
                && expectedRow.CandidateQualified
                && expectedRow.BorderOnlyQualified
                && expectedRow.BorderEdgesFound >= options.SelectedRequiredBorderEdges
                && expectedRow.EffectiveScore >= options.SelectedMinimumScore;
        }

        private SelectedScan ScanSelected(byte[] frame,
            IReadOnlyDictionary<TeamNumber, ImageRegion> regions)
        {
            var matches = new Dictionary<TeamNumber, ImageMatchResult>();
            foreach (KeyValuePair<TeamNumber, ImageRegion> item in regions)
            {
                ImageMatchResult match = Match(frame,
                    TemplateId.TeamSelectedBorderAnchor, item.Value);
                if (HasBounds(match) && Overlaps(item.Value, match)) matches[item.Key] = match;
            }
            return new SelectedScan(matches);
        }

        private IReadOnlyDictionary<TeamNumber, ImageRegion> ResolveTeamRegions(
            byte[] frame, GameDetectionResult state)
        {
            TeamNumber[] teams = { TeamNumber.Team1, TeamNumber.Team2,
                TeamNumber.Team3, TeamNumber.Team4 };
            var requests = teams.Select(team => new ImageMatchRequest(
                registry.LoadBytes(BadgeId(team).Value),
                options.TeamSelectionRosterRegion)).ToArray();
            IReadOnlyList<ImageMatchResult> results = matcher is IBatchImageMatcher batch
                ? batch.FindMany(frame, requests)
                : requests.Select(item => matcher.Find(frame, item.TemplatePng,
                    item.SearchRegion)).ToArray();
            var badges = new Dictionary<TeamNumber, ImageMatchResult>();
            for (int index = 0; index < teams.Length; index++)
                if (HasBounds(results[index])) badges[teams[index]] = results[index];
            return TeamSelectionRosterLayoutResolver.Resolve(badges,
                options.TeamSelectionRosterRegion, options.ExpectedWidth,
                options.ExpectedHeight).Rows;
        }

        private async Task<RosterFrame> EnsureTeamVisibleAsync(string deviceName,
            TeamNumber expectedTeam, byte[] frame, GameDetectionResult state,
            CancellationToken token)
        {
            IReadOnlyDictionary<TeamNumber, ImageRegion> regions =
                ResolveTeamRegions(frame, state);
            int scrolls = 0;
            while (!regions.ContainsKey(expectedTeam)
                && scrolls < options.MaxRosterScrollAttempts)
            {
                token.ThrowIfCancellationRequested();
                TeamNumber[] visible = regions.Keys.OrderBy(item => (int)item).ToArray();
                bool searchBelow = visible.Length == 0
                    || (int)expectedTeam > (int)visible.Max();
                ImageRegion list = options.TeamSelectionRosterRegion;
                double x = (list.X + list.Width / 2d) / options.ExpectedWidth;
                double startY = (list.Y + list.Height * (searchBelow ? .75 : .25))
                    / options.ExpectedHeight;
                double endY = (list.Y + list.Height * (searchBelow ? .25 : .75))
                    / options.ExpectedHeight;
                await client.SwipeByPercentAsync(deviceName, x, startY, x, endY,
                    options.RosterScrollDurationMs, token);
                scrolls++;
                await Task.Delay(options.PollIntervalMs, token);
                frame = await client.CaptureScreenshotPngAsync(deviceName, token);
                state = detector.Detect(frame);
                if (!IsSelectionScreen(state)) break;
                regions = ResolveTeamRegions(frame, state);
            }
            return new RosterFrame(frame, state, regions, scrolls);
        }

        private ImageMatchResult Match(byte[] frame, TemplateId id, ImageRegion region) =>
            matcher.Find(frame, registry.LoadBytes(id), region) ?? ImageMatchResult.NotFound();

        private bool IsDisabled(byte[] frame, ImageRegion region) =>
            registry.Exists(TemplateId.TeamDisabledAnchor)
            && Match(frame, TemplateId.TeamDisabledAnchor, region).Found;

        private bool RequiredScreenTemplatesExist(out string error)
        {
            foreach (TemplateId id in ReadyTemplates)
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

        private async Task<GameDetectionResult> ConfirmSelectionScreenAsync(
            string deviceName, CancellationToken cancellationToken, Action<byte[]> capture)
        {
            GameDetectionResult last = null;
            for (int observation = 1; observation <= 3; observation++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                byte[] frame = await client.CaptureScreenshotPngAsync(deviceName,
                    cancellationToken);
                capture(frame);
                last = detector.Detect(frame);
                if (IsSelectionScreen(last)) return last;
                if (observation < 3)
                    await Task.Delay(250, cancellationToken);
            }
            return last;
        }

        private static bool IsSelectionScreen(GameDetectionResult state) =>
            TeamSelectionEvidence.IsConfirmed(state);

        private static bool HasEnabledAction(GameDetectionResult state) =>
            Found(state, TemplateId.TeamActionButtonEnabled);

        private static bool Found(GameDetectionResult state, TemplateId id) =>
            state != null && state.Evidence != null
            && state.Evidence.Any(item => item.TemplateId == id && item.Found);

        private static TeamSelectionTargetResolution ResolveTarget(TeamSelectionRequest request)
        {
            if (request.ExpectedTeam.HasValue)
            {
                TeamNumber expected = request.ExpectedTeam.Value;
                if (!IsAllowedCandidate(request, expected))
                    return TeamSelectionTargetResolution.Failure(
                        SelectFarmTeamOutcome.ExpectedTeamNotAllowed,
                        "Đội dự kiến không nằm trong danh sách được phép.",
                        "ExpectedTeamNotAllowed");
                if (IsTrustedRoster(request)
                    && request.WorldMapAvailableTeams != null
                    && !request.WorldMapAvailableTeams.Contains(expected))
                    return TeamSelectionTargetResolution.Failure(
                        SelectFarmTeamOutcome.ExpectedTeamUnavailable,
                        "Chưa thể xác nhận đội dự kiến khả dụng.",
                        "ExpectedTeamUnavailable");
                return TeamSelectionTargetResolution.ForTarget(expected,
                    TeamSelectionTargetSource.ExpectedTeam);
            }

            foreach (TeamNumber team in request.WorldMapReadyTeams ?? new TeamNumber[0])
                if (IsAllowedCandidate(request, team))
                    return TeamSelectionTargetResolution.ForTarget(team,
                        TeamSelectionTargetSource.WorldMapReadyTeams);
            foreach (TeamNumber team in request.WorldMapAvailableTeams ?? new TeamNumber[0])
                if (IsAllowedCandidate(request, team))
                    return TeamSelectionTargetResolution.ForTarget(team,
                        TeamSelectionTargetSource.WorldMapAvailableTeams);
            foreach (TeamNumber team in request.Priority ?? new TeamNumber[0])
                if (IsAllowedCandidate(request, team))
                    return TeamSelectionTargetResolution.ForTarget(team,
                        TeamSelectionTargetSource.PriorityFallback);
            return TeamSelectionTargetResolution.Failure(SelectFarmTeamOutcome.NoEligibleTeam,
                "Không có đội phù hợp để chọn.", "NoEligibleTeam");
        }

        private static IReadOnlyList<TeamNumber> BuildCandidatePlan(
            TeamSelectionRequest request, TeamSelectionTargetResolution target)
        {
            if (!target.Success || !target.TargetTeam.HasValue)
                return new TeamNumber[0];
            if (request.ExpectedTeam.HasValue)
                return new[] { target.TargetTeam.Value };

            var plan = new List<TeamNumber> { target.TargetTeam.Value };
            foreach (IReadOnlyList<TeamNumber> source in new[]
            {
                request.WorldMapReadyTeams, request.WorldMapAvailableTeams, request.Priority
            })
                if (source != null)
                    foreach (TeamNumber team in source)
                        if (IsAllowedCandidate(request, team) && !plan.Contains(team))
                            plan.Add(team);
            return plan;
        }

        private static bool IsAllowedCandidate(TeamSelectionRequest request, TeamNumber team) =>
            Enum.IsDefined(typeof(TeamNumber), team)
            && request.AllowedTeams.Contains(team)
            && (request.AllowTeam1 || team != TeamNumber.Team1);

        private static bool IsTrustedRoster(TeamSelectionRequest request) =>
            !string.Equals(request.WorldMapRosterConfidence, "Uncertain",
                StringComparison.OrdinalIgnoreCase)
            && !string.Equals(request.WorldMapRosterStatus, "Uncertain",
                StringComparison.OrdinalIgnoreCase)
            && request.WorldMapAvailableTeams != null;

        private enum TeamSelectionTargetSource
        {
            ExpectedTeam,
            WorldMapReadyTeams,
            WorldMapAvailableTeams,
            PriorityFallback,
            None
        }

        private sealed class TeamSelectionTargetResolution
        {
            public TeamNumber? TargetTeam { get; private set; }
            public TeamSelectionTargetSource Source { get; private set; }
            public bool Success { get; private set; }
            public SelectFarmTeamOutcome Outcome { get; private set; }
            public string Message { get; private set; }
            public string FailureReason { get; private set; }

            public static TeamSelectionTargetResolution ForTarget(TeamNumber team,
                TeamSelectionTargetSource source) => new TeamSelectionTargetResolution
                {
                    TargetTeam = team, Source = source, Success = true,
                    Outcome = SelectFarmTeamOutcome.NoEligibleTeam
                };

            public static TeamSelectionTargetResolution Failure(SelectFarmTeamOutcome outcome,
                string message, string reason) => new TeamSelectionTargetResolution
                {
                    Source = TeamSelectionTargetSource.None, Success = false,
                    Outcome = outcome, Message = message, FailureReason = reason
                };
        }

        private static TemplateId? BadgeId(TeamNumber team)
        {
            switch (team)
            {
                case TeamNumber.Team1: return TemplateId.Team1Badge;
                case TeamNumber.Team2: return TemplateId.Team2Badge;
                case TeamNumber.Team3: return TemplateId.Team3Badge;
                case TeamNumber.Team4: return TemplateId.Team4Badge;
                default: return null;
            }
        }

        private static string ValidateRequest(string deviceName, TeamSelectionRequest request)
        {
            if (string.IsNullOrWhiteSpace(deviceName)) return "LDPlayer device name is required.";
            if (request == null) return "Team selection request is required.";
            if (request.AllowedTeams == null || request.AllowedTeams.Count == 0) return "AllowedTeams cannot be empty.";
            if (request.Priority == null || request.Priority.Count == 0) return "Priority cannot be empty.";
            if (request.AllowedTeams.Distinct().Count() != request.AllowedTeams.Count) return "AllowedTeams cannot contain duplicates.";
            if (request.Priority.Distinct().Count() != request.Priority.Count) return "Priority cannot contain duplicates.";
            if (request.Priority.Any(team => !request.AllowedTeams.Contains(team))) return "Priority can only contain allowed teams.";
            if (!request.AllowTeam1 && (request.AllowedTeams.Contains(TeamNumber.Team1)
                || request.Priority.Contains(TeamNumber.Team1))) return "Team1 is not allowed when AllowTeam1 is false.";
            return null;
        }

        private async Task<byte[]> TryCaptureAsync(string deviceName, CancellationToken token)
        {
            if (!options.SaveFailureScreenshots) return null;
            try { return await client.CaptureScreenshotPngAsync(deviceName, token); }
            catch (OperationCanceledException) { throw; }
            catch (Exception exception)
            {
                logger.Error($"[Farm Team Selection] DiagnosticCaptureError='{exception.Message}'", exception);
                return null;
            }
        }

        private async Task<SelectFarmTeamResult> CompleteAsync(string deviceName,
            SelectFarmTeamResult result, SelectFarmTeamOutcome outcome,
            string message, string error, byte[] frame, Stopwatch watch, CancellationToken token)
        {
            Complete(result, outcome, message, error, watch);
            if (options.SaveFailureScreenshots && frame != null
                && outcome != SelectFarmTeamOutcome.AlreadySelected
                && outcome != SelectFarmTeamOutcome.TeamSelected
                && outcome != SelectFarmTeamOutcome.Cancelled)
            {
                try { result.DiagnosticScreenshotPath = await diagnosticStore.SaveAsync(deviceName, outcome, frame, token); }
                catch (OperationCanceledException) { throw; }
                catch (Exception exception) { logger.Error($"[Farm Team Selection] DiagnosticSaveError='{exception.Message}'", exception); }
            }
            return result;
        }

        private SelectFarmTeamResult Complete(SelectFarmTeamResult result,
            SelectFarmTeamOutcome outcome, string message, string error, Stopwatch watch)
        {
            result.Outcome = outcome;
            result.Success = outcome == SelectFarmTeamOutcome.AlreadySelected
                || outcome == SelectFarmTeamOutcome.TeamSelected;
            result.Duration = watch.Elapsed;
            result.Message = message;
            result.ErrorMessage = error;
            logger.Info($"[Farm Team Selection] ExpectedTeam='{result.ExpectedTeam}', ObservedSelectedTeam='{result.ActualSelectedTeam}', InitialState='{result.InitialState}', FinalState='{result.FinalState}', TeamTapCount={result.TeamTapCount}, SelectedTeam='{result.SelectedTeam}', SelectedVerified={result.SelectedStateVerified}, CleanupAttempted={result.CleanupAttempted}, CleanupSucceeded={result.CleanupSucceeded}, StateAfterCleanup='{result.StateAfterCleanup}', Outcome='{outcome}', DurationMs={result.Duration.TotalMilliseconds:F0}, Cancellation={outcome == SelectFarmTeamOutcome.Cancelled}, Error='{error ?? string.Empty}'");
            return result;
        }

        private static SelectFarmTeamResult NewResult(
            IReadOnlyList<TeamSelectionAttempt> attempts,
            IReadOnlyList<TeamNumber> attemptedTeams) => new SelectFarmTeamResult
            {
                Outcome = SelectFarmTeamOutcome.Failed,
                InitialState = GameState.Unknown,
                FinalState = GameState.Unknown,
                Attempts = attempts,
                AttemptedTeams = attemptedTeams,
                VisibleTeams = new TeamNumber[0]
            };

        private static SelectFarmTeamResult Empty(SelectFarmTeamOutcome outcome, string error) =>
            new SelectFarmTeamResult
            {
                Outcome = outcome,
                Success = false,
                InitialState = GameState.Unknown,
                FinalState = GameState.Unknown,
                Message = error,
                ErrorMessage = error,
                Attempts = new TeamSelectionAttempt[0],
                AttemptedTeams = new TeamNumber[0],
                VisibleTeams = new TeamNumber[0]
            };

        private static bool HasBounds(ImageMatchResult match) =>
            match != null && match.Found && match.Width > 0 && match.Height > 0;
        private static bool Overlaps(ImageRegion region, ImageMatchResult match) =>
            match.X < region.X + region.Width && match.X + match.Width > region.X
            && match.Y < region.Y + region.Height && match.Y + match.Height > region.Y;
        private static string Join(IEnumerable<TeamNumber> teams) =>
            string.Join(",", teams.Select(team => ((int)team).ToString()));

        private bool TryGetSafeTapPoint(ImageRegion targetRow,
            IReadOnlyDictionary<TeamNumber, ImageRegion> currentRows,
            out int tapX, out int tapY, out string skipReason)
        {
            int safeLeft = Math.Max(targetRow.X + 8, options.MinimumSafeTapX);
            int safeRight = Math.Min(targetRow.X + targetRow.Width - 8,
                options.MaximumSafeTapX);
            tapX = 0;
            tapY = 0;
            skipReason = null;
            if (safeRight < safeLeft)
            {
                skipReason = "NoSafeHorizontalTargetArea";
                return false;
            }

            tapX = Math.Max(safeLeft, Math.Min(safeRight,
                targetRow.X + (targetRow.Width * 3 / 4)));
            tapY = targetRow.Y + (targetRow.Height / 2);
            if (!ContainsPoint(targetRow, tapX, tapY))
            {
                skipReason = "TapOutsideTargetRow";
                return false;
            }
            if (tapX < 0 || tapX >= options.ExpectedWidth
                || tapY < 0 || tapY >= options.ExpectedHeight)
            {
                skipReason = "TapOutsideScreenshot";
                return false;
            }
            foreach (var currentRow in currentRows)
            {
                if (!IsSameRegion(currentRow.Value, targetRow)
                    && ContainsPoint(currentRow.Value, tapX, tapY))
                {
                    skipReason = "TapOverlapsAnotherTeamRow";
                    return false;
                }
            }
            return true;
        }

        private static bool ContainsPoint(ImageRegion region, int x, int y) =>
            x >= region.X && x < region.X + region.Width
            && y >= region.Y && y < region.Y + region.Height;

        private static bool IsSameRegion(ImageRegion left, ImageRegion right) =>
            left.X == right.X && left.Y == right.Y
            && left.Width == right.Width && left.Height == right.Height;

        private void LogTapPlanned(string deviceName, TeamNumber resolvedTargetTeam,
            int attempt, ImageMatchResult badge, bool disabled, ImageRegion targetRow,
            int tapX, int tapY, bool tapPointValid, string skipReason,
            int teamTapCountBefore)
        {
            string badgeBounds = HasBounds(badge)
                ? $"({badge.X},{badge.Y},{badge.Width},{badge.Height})" : string.Empty;
            logger.Info($"[Farm Team Selection Tap Planned] DeviceName='{deviceName}', ExpectedTeam='{resolvedTargetTeam}', ResolvedTargetTeam='{resolvedTargetTeam}', Attempt={attempt}, BadgeFound={HasBounds(badge)}, BadgeBounds={badgeBounds}, Disabled={disabled}, TargetRowBounds=({targetRow.X},{targetRow.Y},{targetRow.Width},{targetRow.Height}), TapX={tapX}, TapY={tapY}, TapPointValid={tapPointValid}, SkipReason='{skipReason ?? string.Empty}', TeamTapCountBefore={teamTapCountBefore}");
        }

        private void LogTapIssued(string deviceName, TeamNumber resolvedTargetTeam,
            int attempt, int tapX, int tapY, bool tapCommandSucceeded,
            int teamTapCountAfter, string error)
        {
            logger.Info($"[Farm Team Selection Tap Issued] DeviceName='{deviceName}', ResolvedTargetTeam='{resolvedTargetTeam}', Attempt={attempt}, TapX={tapX}, TapY={tapY}, TapCommandIssued=true, TapCommandSucceeded={tapCommandSucceeded}, TeamTapCountAfter={teamTapCountAfter}, Error='{error ?? string.Empty}'");
        }

        private void LogTapSkippedWithoutRow(string deviceName,
            TeamNumber resolvedTargetTeam, int attempt, string skipReason,
            int teamTapCountBefore)
        {
            logger.Info($"[Farm Team Selection Tap Planned] DeviceName='{deviceName}', ExpectedTeam='{resolvedTargetTeam}', ResolvedTargetTeam='{resolvedTargetTeam}', Attempt={attempt}, BadgeFound=false, BadgeBounds='', Disabled=false, TargetRowBounds='', TapX=0, TapY=0, TapPointValid=false, SkipReason='{skipReason}', TeamTapCountBefore={teamTapCountBefore}");
        }

        private void LogMatch(string deviceName, TeamNumber team, int attempt,
            ImageRegion region, ImageMatchResult badge, bool disabled)
        {
            string bounds = HasBounds(badge)
                ? $"({badge.X},{badge.Y},{badge.Width},{badge.Height})" : string.Empty;
            logger.Info($"[Farm Team Selection] DeviceName='{deviceName}', Team='{team}', Attempt={attempt}, ROI=({region.X},{region.Y},{region.Width},{region.Height}), BadgeFound={HasBounds(badge)}, BadgeBounds={bounds}, Disabled={disabled}");
        }

        private sealed class SelectedScan
        {
            public SelectedScan(Dictionary<TeamNumber, ImageMatchResult> matches)
            {
                Matches = matches;
                Teams = matches.Keys.OrderBy(team => (int)team).ToArray();
            }
            public IReadOnlyDictionary<TeamNumber, ImageMatchResult> Matches { get; }
            public IReadOnlyList<TeamNumber> Teams { get; }
            public bool IsAmbiguous => Teams.Count > 1;
        }

        private sealed class RosterFrame
        {
            public RosterFrame(byte[] frame, GameDetectionResult state,
                IReadOnlyDictionary<TeamNumber, ImageRegion> regions, int scrollAttempts)
            {
                Frame = frame;
                State = state;
                Regions = regions;
                ScrollAttempts = scrollAttempts;
            }

            public byte[] Frame { get; }
            public GameDetectionResult State { get; }
            public IReadOnlyDictionary<TeamNumber, ImageRegion> Regions { get; }
            public int ScrollAttempts { get; }
        }
    }
}
