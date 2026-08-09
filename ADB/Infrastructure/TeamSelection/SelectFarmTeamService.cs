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
            TeamNumber? authoritativeTarget = request.ExpectedTeam;
            bool useInitialCityTeam = IsInitialCityTeamAuthoritative(request,
                authoritativeTarget);
            TeamSelectionTargetResolution target = ResolveTarget(request);
            result.ExpectedTeam = authoritativeTarget ?? target.TargetTeam;
            byte[] lastFrame = null;
            try
            {
                logger.Info($"[Farm Team Selection] RunId='{request.RunId ?? string.Empty}', DeviceName='{deviceName}', ExpectedTeam='{request.ExpectedTeam}', AuthoritativeTargetTeam='{authoritativeTarget}', TargetSource='{(authoritativeTarget.HasValue ? TeamSelectionTargetSource.ExpectedTeam.ToString() : target.Source.ToString())}', Allowed='{Join(request.AllowedTeams)}', Priority='{Join(request.Priority ?? new TeamNumber[0])}', Cancellation=false, Phase='Starting'");
                if (!target.Success)
                {
                    result.FailureReason = target.FailureReason;
                    return Complete(result, target.Outcome, target.Message,
                        target.FailureReason, watch);
                }
                if (!RequiredScreenTemplatesExist(out string screenTemplateError)
                    || (!useInitialCityTeam
                        && !registry.Exists(TemplateId.TeamSelectedBorderAnchor)))
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

                FullScanVerificationSnapshot preTapFullScan =
                    CreateFullScanVerification(deviceName, request, lastFrame,
                        freshState, target.TargetTeam.Value, "PreTap", 1,
                        evaluateSelectedBorder: !useInitialCityTeam);
                IReadOnlyDictionary<TeamNumber, ImageRegion> initialRegions =
                    preTapFullScan.Regions;
                result.VisibleTeams = initialRegions.Keys.OrderBy(item => (int)item).ToArray();
                SelectedScan selected = preTapFullScan.Selected;
                if (!authoritativeTarget.HasValue
                    && selectedTeamDetector == null && selected.IsAmbiguous)
                    return await CompleteAsync(deviceName, result, SelectFarmTeamOutcome.Failed,
                        "Selected border appeared in multiple team ROIs; no Tap was sent.",
                        "Ambiguous selected-team evidence.", lastFrame, watch, cancellationToken);

                logger.Info($"[Farm Team Selection Target] DeviceName='{deviceName}', RunId='{request.RunId ?? string.Empty}', ExpectedTeam='{request.ExpectedTeam}', AuthoritativeTargetTeam='{authoritativeTarget}', ResolvedTargetTeam='{target.TargetTeam}', TargetSource='{target.Source}', PreTapSelectedTeam='{(selected.Teams.Count == 1 ? selected.Teams[0].ToString() : string.Empty)}', PreTapSelectedConfident={!selected.IsAmbiguous && selected.Teams.Count == 1}, AllowedTeams='{Join(request.AllowedTeams)}', WorldMapReadyTeams='{Join(request.WorldMapReadyTeams ?? new TeamNumber[0])}', WorldMapAvailableTeams='{Join(request.WorldMapAvailableTeams ?? new TeamNumber[0])}', RosterStatus='{request.WorldMapRosterStatus ?? string.Empty}', RosterConfidence='{request.WorldMapRosterConfidence ?? string.Empty}', AlreadySelectedAccepted=false, FailureReason='{target.FailureReason ?? string.Empty}'");

                SelectedTeamConsensusResult detectedSelection = null;
                if (selectedTeamDetector != null && !useInitialCityTeam)
                {
                    try
                    {
                        detectedSelection = await selectedTeamDetector.DetectAsync(deviceName,
                            new SelectedTeamDetectionContext
                            {
                                TeamRegions = initialRegions,
                                ExpectedTeam = authoritativeTarget ?? target.TargetTeam,
                                ExpectedWidth = options.ExpectedWidth,
                                ExpectedHeight = options.ExpectedHeight,
                                ConsensusFrames = options.SelectedConsensusFrames,
                                RequiredMatchingFrames = options.SelectedRequiredMatchingFrames,
                                FrameIntervalMs = options.SelectedFrameIntervalMs,
                                TimeoutMs = options.SelectedDetectionTimeoutMs,
                                MinimumScore = options.SelectedMinimumScore,
                                WinningMargin = options.SelectedWinningMargin
                            }, cancellationToken);
                        logger.Info($"[Farm Team Selection Border Detector] DeviceName='{deviceName}', Phase='PreTap', ExpectedTeam='{target.TargetTeam}', BorderDetectorTeam='{detectedSelection?.Team}', BorderDetectorConfident={detectedSelection?.IsConfident == true}, BorderDetectorAmbiguous={detectedSelection?.IsAmbiguous == true}, BorderDetectorFailureReason='{detectedSelection?.FailureReason ?? string.Empty}', BorderDetectorWasRequired=false");
                        LogFrameScores(deviceName, "PreTap", target.TargetTeam, detectedSelection);
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (Exception exception)
                    {
                        logger.Info($"[Farm Team Selection Border Detector] DeviceName='{deviceName}', Phase='PreTap', ExpectedTeam='{target.TargetTeam}', BorderDetectorWasRequired=false, DiagnosticFailed=true, Error='{exception.Message}'");
                    }
                }

                bool expectedAlreadySelected = useInitialCityTeam
                    ? false
                    : authoritativeTarget.HasValue
                        ? preTapFullScan.ExactTeamVerified
                    : selectedTeamDetector != null
                        ? detectedSelection != null && detectedSelection.IsConfident
                            && !detectedSelection.IsAmbiguous
                            && detectedSelection.Team.HasValue
                            && target.TargetTeam.HasValue
                            && detectedSelection.Team.Value == target.TargetTeam.Value
                        : target.TargetTeam.HasValue
                            && selected.Teams.Contains(target.TargetTeam.Value)
                            && HasEnabledAction(freshState);
                if (expectedAlreadySelected)
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
                    logger.Info($"[Farm Team Selection PreTap] DeviceName='{deviceName}', ExpectedTeam='{request.ExpectedTeam}', AuthoritativeTargetTeam='{authoritativeTarget}', TargetSource='{(authoritativeTarget.HasValue ? TeamSelectionTargetSource.ExpectedTeam.ToString() : target.Source.ToString())}', DetectedTeam='{(authoritativeTarget.HasValue ? preTapFullScan.SelectedTeam : selectedTeamDetector != null ? detectedSelection?.Team : target.TargetTeam)}', AlreadySelectedAccepted=true, TeamTapCount={result.TeamTapCount}, Outcome='{SelectFarmTeamOutcome.AlreadySelected}'");
                    return Complete(result, SelectFarmTeamOutcome.AlreadySelected,
                        $"Đội {((int)target.TargetTeam.Value)} đã được chọn sẵn.", null, watch);
                }
                result.ActualSelectedTeam = authoritativeTarget.HasValue
                    ? preTapFullScan.SelectedTeam
                    : selectedTeamDetector != null && detectedSelection != null
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
                int staleInputFrameAttempts = 0;
                IEnumerable<TeamNumber> candidateTeams = authoritativeTarget.HasValue
                    ? new[] { authoritativeTarget.Value }
                    : selectedTeamDetector != null
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

                    // The numbered badge can be temporarily obscured by row artwork even
                    // though the fresh WorldMap roster already confirmed this exact team.
                    // In that narrow authoritative case, keep the fixed row as a safe
                    // candidate instead of scrolling the list and losing its original
                    // alignment. Post-tap border verification still has to prove that the
                    // expected team was selected before the workflow may continue.
                    initialRegions = AddTrustedConfiguredTargetRow(request, team,
                        initialRegions, deviceName, "Initial");
                    RosterFrame visible = await EnsureTeamVisibleAsync(deviceName, team,
                        lastFrame, freshState, initialRegions, cancellationToken);
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
                        if (authoritativeTarget.HasValue)
                        {
                            result.FailureReason = "ExpectedTeamNotVisible";
                            return await CompleteAsync(deviceName, result,
                                SelectFarmTeamOutcome.ExpectedTeamNotVisible,
                                "Không tìm thấy đội dự kiến trong danh sách đội sau khi cuộn giới hạn.",
                                null, lastFrame, watch, cancellationToken);
                        }
                        if (selectedTeamDetector == null) continue;
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

                        FullScanVerificationSnapshot inputFullScan = authoritativeTarget.HasValue
                            ? CreateFullScanVerification(deviceName, request, lastFrame, state,
                                authoritativeTarget.Value, "PreTapAttempt", attemptNumber,
                                initialRegions,
                                evaluateSelectedBorder: !useInitialCityTeam)
                            : null;
                        IReadOnlyDictionary<TeamNumber, ImageRegion> currentRegions =
                            inputFullScan != null
                                ? inputFullScan.Regions
                                : ResolveTeamRegions(lastFrame, state);
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
                        if (inputFullScan != null)
                        {
                            result.ActualSelectedTeam = inputFullScan.SelectedTeam;
                            if (inputFullScan.ExactTeamVerified)
                            {
                                attempts.Add(new TeamSelectionAttempt
                                {
                                    TeamNumber = team,
                                    AlreadySelected = true,
                                    SelectedVerified = true,
                                    SelectedBorderMatch = inputFullScan.Selected.Matches.ContainsKey(team)
                                        ? inputFullScan.Selected.Matches[team] : null,
                                    Message = "Ảnh mới ngay trước thao tác xác nhận đội dự kiến đã được chọn; không gửi lệnh chọn."
                                });
                                result.SelectedTeam = team;
                                result.SelectedStateVerified = true;
                                result.FinalState = GameState.TeamSelection;
                                logger.Info($"[Farm Team Selection PreTap] DeviceName='{deviceName}', ExpectedTeam='{request.ExpectedTeam}', AuthoritativeTargetTeam='{authoritativeTarget}', TargetSource='{TeamSelectionTargetSource.ExpectedTeam}', DetectedTeam='{inputFullScan.SelectedTeam}', AlreadySelectedAccepted=true, TeamTapCount={result.TeamTapCount}, Outcome='{SelectFarmTeamOutcome.AlreadySelected}'");
                                return Complete(result, SelectFarmTeamOutcome.AlreadySelected,
                                    $"Đội {((int)team)} đã được chọn sẵn.", null, watch);
                            }
                            if (inputFullScan.Selected.IsAmbiguous)
                            {
                                result.FailureReason = inputFullScan.FailureReason;
                                LogTapSkippedWithoutRow(deviceName, resolvedTargetTeam,
                                    attemptNumber, "SelectionAmbiguous", result.TeamTapCount);
                                continue;
                            }
                        }
                        else if (selectedTeamDetector == null)
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
                        ImageMatchResult badge = inputFullScan != null
                            ? inputFullScan.ExpectedBadge
                            : Match(lastFrame, badgeId, region);
                        // Calls without ExpectedTeam retain the compatibility-row
                        // fallback. The authoritative path must use only the fresh
                        // badge-derived layout and badge center.
                        if (!authoritativeTarget.HasValue
                            && !HasBounds(badge)
                            && options.TeamRegions.TryGetValue(team,
                                out ImageRegion configuredRegion))
                        {
                            ImageMatchResult configuredBadge = Match(lastFrame,
                                badgeId, configuredRegion);
                            if (HasBounds(configuredBadge))
                            {
                                region = configuredRegion;
                                badge = configuredBadge;
                                currentRegions = options.TeamRegions;
                                logger.Info($"[Farm Team Selection] DeviceName='{deviceName}', Team='{team}', LayoutFallback='ConfiguredRow', DynamicRow=({teamRegion.X},{teamRegion.Y},{teamRegion.Width},{teamRegion.Height}), ConfiguredRow=({configuredRegion.X},{configuredRegion.Y},{configuredRegion.Width},{configuredRegion.Height})");
                            }
                        }
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
                        bool trustedConfiguredRow = CanUseTrustedConfiguredTargetRow(
                            request, team, currentRegions);
                        if (!HasBounds(badge) && !trustedConfiguredRow)
                        {
                            result.FailureReason = "TargetBadgeNotFound";
                            attempt.Message = "Team badge was not found with valid bounds; no Tap was sent.";
                            LogTapPlanned(deviceName, resolvedTargetTeam, attemptNumber, badge,
                                disabled, region, 0, 0, false, "BadgeBoundsInvalid",
                                result.TeamTapCount);
                            continue;
                        }
                        if (disabled)
                        {
                            result.FailureReason = "TargetTeamDisabled";
                            attempt.Message = "Disabled team evidence was found; no Tap was sent.";
                            LogTapPlanned(deviceName, resolvedTargetTeam, attemptNumber, badge,
                                true, region, 0, 0, false, "TargetDisabled",
                                result.TeamTapCount);
                            continue;
                        }

                        int tapX;
                        int tapY;
                        string tapSkipReason;
                        bool tapPointValid = TryGetSafeTapPoint(region, badge,
                            currentRegions,
                            out tapX, out tapY, out tapSkipReason);
                        LogTapPlanned(deviceName, resolvedTargetTeam, attemptNumber, badge,
                            false, region, tapX, tapY, tapPointValid, tapSkipReason,
                            result.TeamTapCount, inputFrameCapturedAt);
                        if (!tapPointValid)
                        {
                            result.FailureReason = tapSkipReason ?? "TargetGeometryInvalid";
                            attempt.Message = "The expected team row has no safe selectable area; no Tap was sent."
                                + " Reason=" + tapSkipReason + ".";
                            continue;
                        }
                        int inputFrameAgeMs = (int)Math.Max(0,
                            (DateTimeOffset.UtcNow - inputFrameCapturedAt).TotalMilliseconds);
                        if ((authoritativeTarget.HasValue || selectedTeamDetector == null)
                            && inputFrameAgeMs > options.MaxInputFrameAgeMs)
                        {
                            LogTapFreshness(deviceName, resolvedTargetTeam, attemptNumber,
                                inputFrameCapturedAt, inputFrameAgeMs, false, "InputFrameStale");

                            // Full selected-team scoring can legitimately consume more
                            // than the input freshness budget. Re-capture once and only
                            // re-match the authoritative target row before issuing input.
                            // This keeps the strict freshness limit without repeating the
                            // expensive full scan or tapping coordinates from an old frame.
                            lastFrame = await client.CaptureScreenshotPngAsync(
                                deviceName, cancellationToken);
                            inputFrameCapturedAt = DateTimeOffset.UtcNow;
                            badge = Match(lastFrame, badgeId, region);
                            disabled = IsDisabled(lastFrame, region);
                            attempt.BadgeFound = HasBounds(badge);
                            attempt.BadgeMatch = badge;
                            attempt.DisabledDetected = disabled;
                            attempt.RowBounds = region;

                            trustedConfiguredRow = CanUseTrustedConfiguredTargetRow(
                                request, team, currentRegions);
                            if (!HasBounds(badge) && !trustedConfiguredRow)
                            {
                                result.FailureReason = "TargetBadgeNotFound";
                                attempt.Message = "The expected team badge was not visible in the focused freshness recapture; no Tap was sent.";
                                LogTapPlanned(deviceName, resolvedTargetTeam, attemptNumber,
                                    badge, disabled, region, 0, 0, false,
                                    "BadgeMissingAfterFreshnessRecapture",
                                    result.TeamTapCount, inputFrameCapturedAt);
                                continue;
                            }
                            if (disabled)
                            {
                                result.FailureReason = "TargetTeamDisabled";
                                attempt.Message = "The focused freshness recapture found disabled team evidence; no Tap was sent.";
                                LogTapPlanned(deviceName, resolvedTargetTeam, attemptNumber,
                                    badge, true, region, 0, 0, false,
                                    "TargetDisabledAfterFreshnessRecapture",
                                    result.TeamTapCount, inputFrameCapturedAt);
                                continue;
                            }

                            tapPointValid = TryGetSafeTapPoint(region, badge,
                                currentRegions, out tapX, out tapY, out tapSkipReason);
                            LogTapPlanned(deviceName, resolvedTargetTeam, attemptNumber,
                                badge, false, region, tapX, tapY, tapPointValid,
                                tapSkipReason, result.TeamTapCount, inputFrameCapturedAt);
                            if (!tapPointValid)
                            {
                                result.FailureReason = tapSkipReason
                                    ?? "TargetGeometryInvalidAfterFreshnessRecapture";
                                attempt.Message = "The refreshed expected-team row has no safe selectable area; no Tap was sent.";
                                continue;
                            }

                            inputFrameAgeMs = (int)Math.Max(0,
                                (DateTimeOffset.UtcNow - inputFrameCapturedAt).TotalMilliseconds);
                            if (inputFrameAgeMs > options.MaxInputFrameAgeMs)
                            {
                                staleInputFrameAttempts++;
                                attempt.Message = "The focused team-selection recapture was stale; no Tap was sent.";
                                result.FailureReason = "InputFrameStale";
                                LogTapFreshness(deviceName, resolvedTargetTeam,
                                    attemptNumber, inputFrameCapturedAt,
                                    inputFrameAgeMs, false, "FocusedRecaptureStale");
                                continue;
                            }
                        }
                        LogTapFreshness(deviceName, resolvedTargetTeam, attemptNumber,
                            inputFrameCapturedAt, inputFrameAgeMs, true, null);
                        if (trustedConfiguredRow && !HasBounds(badge))
                            logger.Info($"[Farm Team Selection Row Fallback] DeviceName='{deviceName}', ExpectedTeam='{team}', RowBounds=({region.X},{region.Y},{region.Width},{region.Height}), Source='FreshWorldMapConfirmedConfiguredRow', NextAction='TapExpectedTeamAndVerifyActionReady'");
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
                        logger.Info($"[Team Selection Mapping] RunId='{request.RunId ?? string.Empty}', DeviceName='{deviceName}', ExpectedTeam='{team}', VisibleTeams='{Join(result.VisibleTeams)}', SelectedBefore='{result.ActualSelectedTeam}', BadgeBounds=({badge.X},{badge.Y},{badge.Width},{badge.Height}), RowBounds=({region.X},{region.Y},{region.Width},{region.Height}), TapPointValidated=true, ScrollAttempt={result.ScrollAttempts}, TapAttempt={attemptNumber}, TapCoordinates=({tapX},{tapY}), InputFrameAgeMs={inputFrameAgeMs}, NextAction='VerifyExactTeam'");

                        // ExpectedTeam uses fresh badge-anchor full scans. The
                        // confidence detector is retained only for legacy calls that
                        // do not provide an authoritative target.
                        PostTapVerificationSnapshot freshPostTap = null;
                        int consistentSelectionFrames = 0;
                        int fullScanFramesObserved = 0;
                        int exactFullScanFrames = 0;
                        FullScanVerificationSnapshot lastFullScan = null;
                        // Keep post-Tap confirmation bounded to three fresh full
                        // scans so uncertain evidence cannot consume the deadline.
                        int maximumPostTapObservations = authoritativeTarget.HasValue ? 3 : 2;
                        for (int postTapObservation = 1;
                            postTapObservation <= maximumPostTapObservations;
                            postTapObservation++)
                        {
                            if (useInitialCityTeam)
                            {
                                await Task.Delay(options.PollIntervalMs,
                                    cancellationToken);
                                lastFrame = await client.CaptureScreenshotPngAsync(
                                    deviceName, cancellationToken);
                                GameDetectionResult postTapState = detector.Detect(lastFrame);
                                fullScanFramesObserved++;
                                result.SelectionVerificationFrames++;
                                bool selectionReady = IsSelectionScreen(postTapState);
                                bool actionEnabled = selectionReady
                                    && HasEnabledAction(postTapState);
                                logger.Info($"[Farm Team Selection PostTap] DeviceName='{deviceName}', ExpectedTeam='{authoritativeTarget}', Attempt={attemptNumber}, Observation={postTapObservation}, TeamSelectionReady={selectionReady}, ActionEnabled={actionEnabled}, SelectedBorderEvaluated=false, Outcome='{(actionEnabled ? "Accepted" : "Retry")}'");
                                if (actionEnabled)
                                {
                                    attempt.SelectedVerified = true;
                                    attempt.Message = "Đã tap đội dự kiến ban đầu và màn hình chọn đội sẵn sàng; không xác minh viền chọn.";
                                    result.SelectedTeam = authoritativeTarget.Value;
                                    result.ActualSelectedTeam = authoritativeTarget.Value;
                                    result.SelectedStateVerified = true;
                                    result.FinalState = GameState.TeamSelection;
                                    return Complete(result,
                                        SelectFarmTeamOutcome.TeamSelected,
                                        $"Đã chọn đội {((int)authoritativeTarget.Value)} từ lần quét ban đầu.",
                                        null, watch);
                                }
                                result.FailureReason = selectionReady
                                    ? "PostTapActionDisabled"
                                    : "TeamSelectionNotConfirmedAfterTap";
                                attempt.Message = "Màn hình chọn đội chưa sẵn sàng sau thao tác; sẽ thử lại có giới hạn.";
                                continue;
                            }
                            if (authoritativeTarget.HasValue)
                            {
                                lastFullScan = await CaptureFreshFullScanAsync(deviceName,
                                    request, authoritativeTarget.Value, "PostTap",
                                    postTapObservation, currentRegions,
                                    cancellationToken);
                                lastFrame = lastFullScan.Frame;
                                fullScanFramesObserved++;
                                result.SelectionVerificationFrames++;
                                result.VisibleTeams = lastFullScan.Regions.Keys
                                    .OrderBy(item => (int)item).ToArray();
                                result.ActualSelectedTeam = lastFullScan.SelectedTeam;
                                attempt.SelectedAfter = lastFullScan.SelectedTeam;
                                if (lastFullScan.ExactTeamVerified)
                                {
                                    exactFullScanFrames++;
                                    attempt.SelectedBorderMatch =
                                        lastFullScan.Selected.Matches.ContainsKey(team)
                                            ? lastFullScan.Selected.Matches[team] : null;
                                }
                                else if (lastFullScan.SelectedTeam.HasValue
                                    && lastFullScan.SelectedTeam.Value
                                        != authoritativeTarget.Value)
                                {
                                    postTapDifferentExpectedTeamObserved = true;
                                    result.FailureReason = "WrongTeamSelected";
                                }
                                else
                                    result.FailureReason = lastFullScan.FailureReason;

                                if (exactFullScanFrames >= 2)
                                {
                                    attempt.SelectedVerified = true;
                                    attempt.Message = "Two fresh badge-anchor full scans verified the expected team and action.";
                                    result.SelectedTeam = authoritativeTarget.Value;
                                    result.ActualSelectedTeam = authoritativeTarget.Value;
                                    result.SelectedStateVerified = true;
                                    result.FinalState = GameState.TeamSelection;
                                    return Complete(result,
                                        SelectFarmTeamOutcome.TeamSelected,
                                        $"{authoritativeTarget.Value} was selected and verified by fresh full scans.",
                                        null, watch);
                                }
                                attempt.Message = "Fresh badge-anchor full scan has not reached exact-team consensus.";
                                continue;
                            }
                            if (selectedTeamDetector != null)
                            {
                                freshPostTap = await CaptureFreshPostTapVerificationAsync(
                                    deviceName, attemptNumber, result.TeamTapCount, team,
                                    cancellationToken);
                                result.SelectionVerificationFrames++;
                                bool expectedTeamVerified = freshPostTap.Detection != null
                                    && freshPostTap.Detection.IsConfident
                                    && !freshPostTap.Detection.IsAmbiguous
                                    && freshPostTap.Detection.Team.HasValue
                                    && freshPostTap.Detection.Team.Value == team
                                    && freshPostTap.HasFreshFrameState
                                    && IsSelectionScreen(freshPostTap.FrameState)
                                    && HasEnabledAction(freshPostTap.FrameState);
                                result.ActualSelectedTeam = expectedTeamVerified
                                    ? (TeamNumber?)team : freshPostTap.Detection?.Team;
                                attempt.SelectedAfter = result.ActualSelectedTeam;
                                logger.Info($"[Farm Team Selection PostTap] DeviceName='{deviceName}', ExpectedTeam='{request.ExpectedTeam}', AuthoritativeTargetTeam='{authoritativeTarget}', TargetSource='{(authoritativeTarget.HasValue ? TeamSelectionTargetSource.ExpectedTeam.ToString() : target.Source.ToString())}', Attempt={attemptNumber}, Observation={postTapObservation}, TapTeam='{team}', TeamTapCount={result.TeamTapCount}, VerificationTeam='{freshPostTap.Detection?.Team}', VerificationConfident={freshPostTap.Detection?.IsConfident == true}, VerificationAmbiguous={freshPostTap.Detection?.IsAmbiguous == true}, ActionEnabled={HasEnabledAction(freshPostTap.FrameState)}, Outcome='{(expectedTeamVerified ? "Verified" : "Rejected")}', FailureReason='{freshPostTap.FailureReason ?? freshPostTap.Detection?.FailureReason ?? string.Empty}'");
                                if (expectedTeamVerified)
                                {
                                    attempt.SelectedVerified = true;
                                    attempt.Message = "Fresh detector consensus and action state verified the expected team after Tap.";
                                    result.SelectedTeam = team;
                                    result.ActualSelectedTeam = team;
                                    result.SelectedStateVerified = true;
                                    result.FinalState = GameState.TeamSelection;
                                    return Complete(result, SelectFarmTeamOutcome.TeamSelected,
                                        $"{team} was selected and verified.", null, watch);
                                }
                                if (freshPostTap?.Detection?.IsConfident == true
                                    && !freshPostTap.Detection.IsAmbiguous
                                    && freshPostTap.Detection.Team.HasValue
                                    && freshPostTap.Detection.Team.Value != team)
                                {
                                    postTapDifferentExpectedTeamObserved = true;
                                    result.FailureReason = "WrongTeamSelected";
                                }
                                else if (freshPostTap?.Detection?.IsAmbiguous == true)
                                    result.FailureReason = "PostTapSelectionAmbiguous";
                                else if (freshPostTap?.Detection?.IsConfident != true
                                    || freshPostTap.Detection.Team.HasValue == false)
                                    result.FailureReason = "PostTapSelectionUncertain";
                                else if (!HasEnabledAction(freshPostTap.FrameState))
                                    result.FailureReason = "PostTapActionDisabled";
                                attempt.Message = "Fresh post-tap detector verification did not confirm the expected team.";
                                if (postTapObservation < 2) continue;
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

                        if (useInitialCityTeam)
                            logger.Info($"[Farm Team Selection PostTap] DeviceName='{deviceName}', ExpectedTeam='{authoritativeTarget}', FramesObserved={fullScanFramesObserved}, SelectedBorderEvaluated=false, Outcome='Rejected', FailureReason='{result.FailureReason ?? string.Empty}'");
                        else if (authoritativeTarget.HasValue)
                            logger.Info($"[Farm Team Full Scan Consensus] DeviceName='{deviceName}', ExpectedTeam='{authoritativeTarget}', FramesObserved={fullScanFramesObserved}, ExactMatchingFrames={exactFullScanFrames}, RequiredMatchingFrames=2, FinalSelectedTeam='{lastFullScan?.SelectedTeam}', VerificationConfident=false, Outcome='Rejected', FailureReason='{lastFullScan?.FailureReason ?? result.FailureReason ?? string.Empty}'");

                        if (selectedButUnavailable)
                        {
                            continueAfterConfirmedUnavailable = true;
                            break;
                        }
                        attempt.Message = useInitialCityTeam
                            ? "Đã tap đội dự kiến nhưng màn hình chọn đội hoặc nút hành động chưa sẵn sàng."
                            : "Tap was sent but selected border was not verified in the target ROI.";
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
                bool freshnessTimedOut = result.TeamTapCount == 0
                    && staleInputFrameAttempts > 0
                    && string.Equals(result.FailureReason, "InputFrameStale",
                        StringComparison.Ordinal);
                if (freshnessTimedOut)
                    result.FailureReason = "SelectionFrameFreshnessTimeout";
                SelectFarmTeamOutcome outcome = wrongTeam
                    ? SelectFarmTeamOutcome.TeamSelectionMismatch
                    : authoritativeTarget.HasValue
                        && string.Equals(result.FailureReason, "TargetBadgeNotFound",
                            StringComparison.Ordinal)
                    ? SelectFarmTeamOutcome.TargetBadgeNotFound
                    : authoritativeTarget.HasValue
                        && string.Equals(result.FailureReason, "TargetTeamDisabled",
                            StringComparison.Ordinal)
                    ? SelectFarmTeamOutcome.TargetTeamDisabled
                    : authoritativeTarget.HasValue
                        && (string.Equals(result.FailureReason, "PostTapSelectionUncertain",
                                StringComparison.Ordinal)
                            || string.Equals(result.FailureReason, "PostTapSelectionAmbiguous",
                                StringComparison.Ordinal)
                            || string.Equals(result.FailureReason, "PostTapActionDisabled",
                                StringComparison.Ordinal)
                            || string.Equals(result.FailureReason, "SelectedTeamMissing",
                                StringComparison.Ordinal)
                            || string.Equals(result.FailureReason, "SelectionAmbiguous",
                                StringComparison.Ordinal)
                            || string.Equals(result.FailureReason, "ActionDisabled",
                                StringComparison.Ordinal)
                            || string.Equals(result.FailureReason, "TeamSelectionNotConfirmed",
                                StringComparison.Ordinal)
                            || string.Equals(result.FailureReason, "ExpectedTeamBadgeMissing",
                                StringComparison.Ordinal))
                    ? SelectFarmTeamOutcome.SelectionEvidenceUncertain
                    : freshnessTimedOut
                    ? SelectFarmTeamOutcome.SelectionTimeout
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
                    : outcome == SelectFarmTeamOutcome.TargetBadgeNotFound
                        ? "Không tìm thấy huy hiệu của đội dự kiến; không thử đội khác."
                    : outcome == SelectFarmTeamOutcome.TargetTeamDisabled
                        ? "Đội dự kiến đang bị khóa hoặc vô hiệu hóa; không thử đội khác."
                    : outcome == SelectFarmTeamOutcome.SelectionEvidenceUncertain
                        ? "Không thể xác minh chắc chắn đúng đội dự kiến và nút hành động mới; đã dừng an toàn."
                    : outcome == SelectFarmTeamOutcome.SelectionTimeout
                        ? string.Equals(result.FailureReason,
                            "SelectionFrameFreshnessTimeout", StringComparison.Ordinal)
                            ? "Khung chọn đội bị cũ trước khi có thể gửi lệnh; đã chụp lại và hết số lần thử an toàn."
                            : "Farm team selection timed out without a verified team."
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
                    TimeoutMs = options.SelectedDetectionTimeoutMs,
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

        private FullScanVerificationSnapshot CreateFullScanVerification(
            string deviceName, TeamSelectionRequest request, byte[] frame,
            GameDetectionResult state, TeamNumber expectedTeam, string phase,
            int frameIndex,
            IReadOnlyDictionary<TeamNumber, ImageRegion> authoritativeRows = null,
            bool evaluateSelectedBorder = true)
        {
            bool isSelectionScreen = IsSelectionScreen(state);
            TeamSelectionRosterLayout layout = isSelectionScreen
                ? ResolveTeamLayout(frame)
                : new TeamSelectionRosterLayout(
                    new Dictionary<TeamNumber, ImageMatchResult>(),
                    new Dictionary<TeamNumber, ImageRegion>());
            bool useAuthoritativeRows = isSelectionScreen
                && authoritativeRows != null
                && authoritativeRows.ContainsKey(expectedTeam);
            IReadOnlyDictionary<TeamNumber, ImageRegion> regions = useAuthoritativeRows
                ? authoritativeRows.ToDictionary(item => item.Key, item => item.Value)
                : layout.Rows;
            SelectedScan selected = isSelectionScreen && evaluateSelectedBorder
                ? ScanSelected(frame, regions)
                : new SelectedScan(new Dictionary<TeamNumber, ImageMatchResult>());
            TeamNumber? selectedTeam = selected.Teams.Count == 1
                ? (TeamNumber?)selected.Teams[0] : null;
            ImageRegion targetRow = regions.TryGetValue(expectedTeam,
                out ImageRegion resolvedRow) ? resolvedRow : default(ImageRegion);
            ImageMatchResult expectedBadge = useAuthoritativeRows
                ? Match(frame, BadgeId(expectedTeam).Value, targetRow)
                : layout.BadgeMatches.TryGetValue(expectedTeam,
                    out ImageMatchResult resolvedBadge)
                    ? resolvedBadge : ImageMatchResult.NotFound();
            bool rosterReconciled = regions.ContainsKey(expectedTeam)
                && (!IsTrustedRoster(request)
                    || request.WorldMapAvailableTeams == null
                    || request.WorldMapAvailableTeams.Contains(expectedTeam));
            bool actionEnabled = isSelectionScreen && HasEnabledAction(state);
            // Selecting a row can visually replace or obscure its numbered badge.
            // For post-Tap verification, the row itself was freshly established
            // before input; require the selected border and enabled action on that
            // authoritative row instead of requiring the badge to remain visible.
            bool expectedBadgeConfirmed = useAuthoritativeRows
                || HasBounds(expectedBadge);
            bool exact = isSelectionScreen
                && evaluateSelectedBorder
                && rosterReconciled
                && expectedBadgeConfirmed
                && !selected.IsAmbiguous
                && selectedTeam.HasValue
                && selectedTeam.Value == expectedTeam
                && actionEnabled;
            string failureReason = !isSelectionScreen ? "TeamSelectionNotConfirmed"
                : !evaluateSelectedBorder ? string.Empty
                : !rosterReconciled || !expectedBadgeConfirmed ? "ExpectedTeamBadgeMissing"
                : selected.IsAmbiguous ? "SelectionAmbiguous"
                : !selectedTeam.HasValue ? "SelectedTeamMissing"
                : selectedTeam.Value != expectedTeam ? "WrongTeamSelected"
                : !actionEnabled ? "ActionDisabled"
                : string.Empty;
            string badgeBounds = HasBounds(expectedBadge)
                ? $"({expectedBadge.X},{expectedBadge.Y},{expectedBadge.Width},{expectedBadge.Height})"
                : string.Empty;
            string rowBounds = regions.ContainsKey(expectedTeam)
                ? $"({targetRow.X},{targetRow.Y},{targetRow.Width},{targetRow.Height})"
                : string.Empty;
            string layoutSource = useAuthoritativeRows
                ? "PreTapAuthoritativeRows" : "BadgeAnchors";
            logger.Info($"[TeamSelection Full Scan] RunId='{request.RunId ?? string.Empty}', DeviceName='{deviceName}', Phase='{phase}', ExpectedTeam='{expectedTeam}', WorldMapAvailableTeams='{Join(request.WorldMapAvailableTeams ?? new TeamNumber[0])}', WorldMapReadyTeams='{Join(request.WorldMapReadyTeams ?? new TeamNumber[0])}', ScreenExistsTeams='{Join(regions.Keys)}', SelectedTeam='{selectedTeam}', SelectionAmbiguous={selected.IsAmbiguous}, RosterReconciled={rosterReconciled}, LayoutSource='{layoutSource}', SelectedBorderEvaluated={evaluateSelectedBorder}");
            logger.Info($"[Farm Team Full Scan Verification] DeviceName='{deviceName}', Phase='{phase}', ExpectedTeam='{expectedTeam}', FrameIndex={frameIndex}, VisibleTeams='{Join(regions.Keys)}', BadgeBoundsForExpectedTeam='{badgeBounds}', DerivedTargetRowBounds='{rowBounds}', SelectedTeam='{selectedTeam}', SelectionAmbiguous={selected.IsAmbiguous}, RosterReconciled={rosterReconciled}, LayoutSource='{layoutSource}', ActionEnabled={actionEnabled}, SelectedBorderEvaluated={evaluateSelectedBorder}, ExactTeamVerified={exact}, FailureReason='{failureReason}'");
            return new FullScanVerificationSnapshot(frame, state, regions,
                selected, expectedBadge, targetRow, selectedTeam,
                rosterReconciled, actionEnabled, exact, failureReason);
        }

        private async Task<FullScanVerificationSnapshot> CaptureFreshFullScanAsync(
            string deviceName, TeamSelectionRequest request, TeamNumber expectedTeam,
            string phase, int frameIndex,
            IReadOnlyDictionary<TeamNumber, ImageRegion> authoritativeRows,
            CancellationToken cancellationToken)
        {
            await Task.Delay(options.PollIntervalMs, cancellationToken);
            byte[] frame = await client.CaptureScreenshotPngAsync(deviceName,
                cancellationToken);
            GameDetectionResult state = detector.Detect(frame);
            return CreateFullScanVerification(deviceName, request, frame, state,
                expectedTeam, phase, frameIndex, authoritativeRows);
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
            return ResolveTeamLayout(frame).Rows;
        }

        private TeamSelectionRosterLayout ResolveTeamLayout(byte[] frame)
        {
            TeamNumber[] teams = { TeamNumber.Team1, TeamNumber.Team2,
                TeamNumber.Team3, TeamNumber.Team4 };
            var requests = teams.Select(team => new ImageMatchRequest(
                registry.LoadBytes(BadgeId(team).Value),
                options.TeamRegions[team])).ToArray();
            IReadOnlyList<ImageMatchResult> results = matcher is IBatchImageMatcher batch
                ? batch.FindMany(frame, requests)
                : requests.Select(item => matcher.Find(frame, item.TemplatePng,
                    item.SearchRegion)).ToArray();
            var badges = new Dictionary<TeamNumber, ImageMatchResult>();
            for (int index = 0; index < teams.Length; index++)
                if (HasBounds(results[index])) badges[teams[index]] = results[index];
            return TeamSelectionRosterLayoutResolver.Resolve(badges,
                options.TeamSelectionRosterRegion, options.ExpectedWidth,
                options.ExpectedHeight);
        }

        private async Task<RosterFrame> EnsureTeamVisibleAsync(string deviceName,
            TeamNumber expectedTeam, byte[] frame, GameDetectionResult state,
            IReadOnlyDictionary<TeamNumber, ImageRegion> initialRegions,
            CancellationToken token)
        {
            IReadOnlyDictionary<TeamNumber, ImageRegion> regions = initialRegions
                ?? ResolveTeamRegions(frame, state);
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
                if (!Enum.IsDefined(typeof(TeamNumber), expected))
                    return TeamSelectionTargetResolution.Failure(
                        SelectFarmTeamOutcome.ExpectedTeamNotAllowed,
                        "Đội dự kiến nằm ngoài phạm vi hỗ trợ.",
                        "ExpectedTeamInvalid");
                if (request.TeamOperation != null
                    && expected != request.TeamOperation.ExpectedTeam)
                    return TeamSelectionTargetResolution.Failure(
                        SelectFarmTeamOutcome.ExpectedTeamNotAllowed,
                        "Đội dự kiến không khớp lần quét đội sẵn sàng mới nhất.",
                        "ExpectedTeamOperationMismatch");
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
                if (IsTrustedRoster(request)
                    && request.WorldMapReadyTeams != null
                    && !request.WorldMapReadyTeams.Contains(expected))
                    return TeamSelectionTargetResolution.Failure(
                        SelectFarmTeamOutcome.ExpectedTeamUnavailable,
                        "Đội dự kiến không nằm trong danh sách đội sẵn sàng mới nhất.",
                        "ExpectedTeamNotReady");
                return TeamSelectionTargetResolution.ForTarget(expected,
                    TeamSelectionTargetSource.ExpectedTeam);
            }
            if (request.TeamOperation != null)
            {
                if (!IsAllowedCandidate(request, request.TeamOperation.ExpectedTeam))
                    return TeamSelectionTargetResolution.Failure(
                        SelectFarmTeamOutcome.ExpectedTeamNotAllowed,
                        "Đội từ lần quét mới không nằm trong danh sách được phép.",
                        "ExpectedTeamNotAllowed");
                return TeamSelectionTargetResolution.ForTarget(
                    request.TeamOperation.ExpectedTeam,
                    TeamSelectionTargetSource.FreshWorldMapReadyScan);
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

        private static bool IsInitialCityTeamAuthoritative(
            TeamSelectionRequest request, TeamNumber? expectedTeam)
        {
            return expectedTeam.HasValue
                && IsTrustedRoster(request)
                && request.WorldMapReadyTeams != null
                && request.WorldMapAvailableTeams.Contains(expectedTeam.Value)
                && request.WorldMapReadyTeams.Contains(expectedTeam.Value);
        }

        private IReadOnlyDictionary<TeamNumber, ImageRegion> AddTrustedConfiguredTargetRow(
            TeamSelectionRequest request, TeamNumber expectedTeam,
            IReadOnlyDictionary<TeamNumber, ImageRegion> detectedRows,
            string deviceName, string phase)
        {
            if (detectedRows != null && detectedRows.ContainsKey(expectedTeam))
                return detectedRows;
            if (!IsTrustedRoster(request)
                || request.WorldMapReadyTeams == null
                || !request.WorldMapAvailableTeams.Contains(expectedTeam)
                || !request.WorldMapReadyTeams.Contains(expectedTeam)
                || !options.TeamRegions.TryGetValue(expectedTeam,
                    out ImageRegion configuredRow))
                return detectedRows ?? new Dictionary<TeamNumber, ImageRegion>();

            // Do not mix one configured row with badge-derived neighbour rows:
            // their boundaries can overlap and make the safe tap validator reject
            // the correct point. The fresh trusted roster supplies the exact set;
            // use one coherent configured layout for that set.
            var resolved = request.WorldMapAvailableTeams
                .Where(options.TeamRegions.ContainsKey)
                .Distinct()
                .ToDictionary(item => item, item => options.TeamRegions[item]);
            resolved[expectedTeam] = configuredRow;
            logger.Info($"[Farm Team Selection Row Resolution] DeviceName='{deviceName}', ExpectedTeam='{expectedTeam}', Phase='{phase}', BadgeFound=false, WorldMapAvailable=true, WorldMapReady=true, ConfiguredRow=({configuredRow.X},{configuredRow.Y},{configuredRow.Width},{configuredRow.Height}), Resolution='TrustedConfiguredRow'");
            return resolved;
        }

        private bool CanUseTrustedConfiguredTargetRow(TeamSelectionRequest request,
            TeamNumber expectedTeam,
            IReadOnlyDictionary<TeamNumber, ImageRegion> currentRows)
        {
            if (!IsTrustedRoster(request)
                || request.WorldMapReadyTeams == null
                || !request.WorldMapAvailableTeams.Contains(expectedTeam)
                || !request.WorldMapReadyTeams.Contains(expectedTeam)
                || currentRows == null
                || !currentRows.TryGetValue(expectedTeam, out ImageRegion currentRow)
                || !options.TeamRegions.TryGetValue(expectedTeam,
                    out ImageRegion configuredRow))
                return false;
            return IsSameRegion(currentRow, configuredRow);
        }

        private enum TeamSelectionTargetSource
        {
            ExpectedTeam,
            FreshWorldMapReadyScan,
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
            if (request.AllowedTeams.Distinct().Count() != request.AllowedTeams.Count) return "AllowedTeams cannot contain duplicates.";
            if (!request.ExpectedTeam.HasValue)
            {
                if (request.Priority == null || request.Priority.Count == 0) return "Priority cannot be empty.";
                if (request.Priority.Distinct().Count() != request.Priority.Count) return "Priority cannot contain duplicates.";
                if (request.Priority.Any(team => !request.AllowedTeams.Contains(team))) return "Priority can only contain allowed teams.";
            }
            if (!request.AllowTeam1 && (request.AllowedTeams.Contains(TeamNumber.Team1)
                || (!request.ExpectedTeam.HasValue
                    && request.Priority.Contains(TeamNumber.Team1)))) return "Team1 is not allowed when AllowTeam1 is false.";
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
            logger.Info($"[Farm Team Selection] ExpectedTeam='{result.ExpectedTeam}', ObservedSelectedTeam='{result.ActualSelectedTeam}', InitialState='{result.InitialState}', FinalState='{result.FinalState}', TeamTapCount={result.TeamTapCount}, SelectedTeam='{result.SelectedTeam}', SelectedVerified={result.SelectedStateVerified}, CleanupAttempted={result.CleanupAttempted}, CleanupSucceeded={result.CleanupSucceeded}, StateAfterCleanup='{result.StateAfterCleanup}', Outcome='{outcome}', FailureReason='{result.FailureReason ?? string.Empty}', DurationMs={result.Duration.TotalMilliseconds:F0}, Cancellation={outcome == SelectFarmTeamOutcome.Cancelled}, Error='{error ?? string.Empty}'");
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
            ImageMatchResult targetBadge,
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
            tapY = HasBounds(targetBadge)
                ? targetBadge.CenterY
                : targetRow.Y + (targetRow.Height / 2);
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
            int teamTapCountBefore, DateTimeOffset? capturedAtUtc = null)
        {
            string badgeBounds = HasBounds(badge)
                ? $"({badge.X},{badge.Y},{badge.Width},{badge.Height})" : string.Empty;
            logger.Info($"[Farm Team Selection Tap Plan] DeviceName='{deviceName}', ExpectedTeam='{resolvedTargetTeam}', Attempt={attempt}, CapturedAtUtc='{(capturedAtUtc.HasValue ? capturedAtUtc.Value.ToString("O") : string.Empty)}', BadgeBounds={badgeBounds}, TargetRowBounds=({targetRow.X},{targetRow.Y},{targetRow.Width},{targetRow.Height}), TapX={tapX}, TapY={tapY}, GeometryValid={tapPointValid}, Disabled={disabled}, TargetResolvedFromSameFrame={tapPointValid && HasBounds(badge)}, Outcome='Planned', SkipReason='{skipReason ?? string.Empty}', TeamTapCountBefore={teamTapCountBefore}");
        }

        private void LogTapFreshness(string deviceName, TeamNumber resolvedTargetTeam,
            int attempt, DateTimeOffset capturedAtUtc, int frameAgeMs,
            bool isFresh, string failureReason)
        {
            logger.Info($"[Farm Team Selection Tap Freshness] DeviceName='{deviceName}', ExpectedTeam='{resolvedTargetTeam}', Attempt={attempt}, CapturedAtUtc='{capturedAtUtc:O}', FrameAgeMs={frameAgeMs}, FreshnessLimitMs={options.MaxInputFrameAgeMs}, IsFresh={isFresh}, NextAction='{(isFresh ? "Tap" : "Recapture")}', FailureReason='{failureReason ?? string.Empty}'");
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
            logger.Info($"[Farm Team Selection Tap Plan] DeviceName='{deviceName}', ExpectedTeam='{resolvedTargetTeam}', Attempt={attempt}, BadgeBounds='', TargetRowBounds='', TapX=0, TapY=0, GeometryValid=false, Disabled=false, TargetResolvedFromSameFrame=false, Outcome='Planned', SkipReason='{skipReason}', TeamTapCountBefore={teamTapCountBefore}");
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

        private sealed class FullScanVerificationSnapshot
        {
            public FullScanVerificationSnapshot(byte[] frame,
                GameDetectionResult state,
                IReadOnlyDictionary<TeamNumber, ImageRegion> regions,
                SelectedScan selected, ImageMatchResult expectedBadge,
                ImageRegion targetRow, TeamNumber? selectedTeam,
                bool rosterReconciled, bool actionEnabled,
                bool exactTeamVerified, string failureReason)
            {
                Frame = frame;
                State = state;
                Regions = regions;
                Selected = selected;
                ExpectedBadge = expectedBadge;
                TargetRow = targetRow;
                SelectedTeam = selectedTeam;
                RosterReconciled = rosterReconciled;
                ActionEnabled = actionEnabled;
                ExactTeamVerified = exactTeamVerified;
                FailureReason = failureReason;
            }

            public byte[] Frame { get; }
            public GameDetectionResult State { get; }
            public IReadOnlyDictionary<TeamNumber, ImageRegion> Regions { get; }
            public SelectedScan Selected { get; }
            public ImageMatchResult ExpectedBadge { get; }
            public ImageRegion TargetRow { get; }
            public TeamNumber? SelectedTeam { get; }
            public bool RosterReconciled { get; }
            public bool ActionEnabled { get; }
            public bool ExactTeamVerified { get; }
            public string FailureReason { get; }
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
