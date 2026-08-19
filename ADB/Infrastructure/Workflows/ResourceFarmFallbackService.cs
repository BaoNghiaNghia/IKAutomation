using IK_Auto_ADB.Core.Diagnostics;
using IK_Auto_ADB.Core.GameDetection;
using IK_Auto_ADB.Core.MarchDispatch;
using IK_Auto_ADB.Core.Navigation;
using IK_Auto_ADB.Core.ResourcePopup;
using IK_Auto_ADB.Core.ResourceSearch;
using IK_Auto_ADB.Core.TeamSelection;
using IK_Auto_ADB.Core.Workflows;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace IK_Auto_ADB.Infrastructure.Workflows
{
    public sealed class ResourceFarmFallbackService : IResourceFarmFallbackService
    {
        private readonly IWorldMapNavigationService navigation;
        private readonly IResourceLevelFallbackService levelFallback;
        private readonly IResourceAwarePopupVerificationService popup;
        private readonly IOpenTeamSelectionService openTeam;
        private readonly ISelectFarmTeamService selectTeam;
        private readonly IDispatchSelectedTeamService dispatch;
        private readonly IResourceTemplateProfileProvider profiles;
        private readonly ResourceFarmFallbackOptions options;
        private readonly IDiagnosticLogger logger;
        private readonly IResourceAreaLv2RecoveryCoordinator areaLv2Recovery;

        public ResourceFarmFallbackService(IWorldMapNavigationService navigation,
            IResourceLevelFallbackService levelFallback,
            IResourceAwarePopupVerificationService popup,
            IOpenTeamSelectionService openTeam, ISelectFarmTeamService selectTeam,
            IDispatchSelectedTeamService dispatch, IResourceTemplateProfileProvider profiles,
            ResourceFarmFallbackOptions options, IDiagnosticLogger logger,
            IResourceAreaLv2RecoveryCoordinator areaLv2Recovery = null)
        {
            this.navigation = navigation ?? throw new ArgumentNullException(nameof(navigation));
            this.levelFallback = levelFallback ?? throw new ArgumentNullException(nameof(levelFallback));
            this.popup = popup ?? throw new ArgumentNullException(nameof(popup));
            this.openTeam = openTeam ?? throw new ArgumentNullException(nameof(openTeam));
            this.selectTeam = selectTeam ?? throw new ArgumentNullException(nameof(selectTeam));
            this.dispatch = dispatch ?? throw new ArgumentNullException(nameof(dispatch));
            this.profiles = profiles ?? throw new ArgumentNullException(nameof(profiles));
            this.options = options ?? throw new ArgumentNullException(nameof(options));
            this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
            this.areaLv2Recovery = areaLv2Recovery;
            options.Validate();
        }

        public async Task<ResourceFarmFallbackResult> RunAsync(string deviceName,
            OneShotFarmRequest request, GameState initialState,
            IProgress<ResourceFarmFallbackProgress> progress,
            CancellationToken cancellationToken)
        {
            var watch = Stopwatch.StartNew();
            var attempts = new List<ResourceFarmAttemptResult>();
            var attempted = new List<ResourceType>();
            var storageFull = new List<ResourceType>();
            var exhausted = new List<ResourceType>();
            ResourceFarmFallbackResult result = NewResult(request, initialState, attempts,
                attempted, storageFull, exhausted);
            string runId = string.IsNullOrWhiteSpace(request?.RunId)
                ? Guid.NewGuid().ToString() : request.RunId;
            ResourceAreaLv2RecoveryRequest activeAreaLv2Request = null;
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                string validation = Validate(request);
                if (validation != null) return Complete(result, ResourceFarmFallbackOutcome.Failed,
                    watch, "Resource fallback request is invalid.", validation);

                ResourceType[] resourceOrder = NormalizeResourceOrder(request.ResourcePriority);
                if (resourceOrder.Length == 0)
                    return Complete(result, ResourceFarmFallbackOutcome.Failed, watch,
                        "Resource fallback request has no supported resource.", null);

                result.RequestedResources = resourceOrder;
                int areaEpoch = 0;
                int repositionCount = 0;
                int resourceIndex = 0;
                var attemptedResourcesInCurrentArea = new HashSet<ResourceType>();
                var failedResourcesInCurrentArea = new HashSet<ResourceType>();
                LogAreaSweepStarted(runId, deviceName, areaEpoch, repositionCount, resourceOrder);

                for (;;)
                {
                    for (; resourceIndex < resourceOrder.Length; resourceIndex++)
                    {
                    cancellationToken.ThrowIfCancellationRequested();
                    ResourceType resource = resourceOrder[resourceIndex];
                    if (attemptedResourcesInCurrentArea.Contains(resource) || storageFull.Contains(resource)) continue;
                    attemptedResourcesInCurrentArea.Add(resource);
                    AddUnique(attempted, resource);
                    var attemptWatch = Stopwatch.StartNew();
                    var attempt = new ResourceFarmAttemptResult
                    {
                        ResourceType = resource, AttemptedLevels = new int[0]
                    };
                    attempts.Add(attempt);
                    Log(runId, deviceName, resource, null, "Preflight", null);

                    if (!profiles.IsSupported(resource))
                    {
                        attempt.ErrorMessage = profiles.GetUnsupportedReason(resource);
                        attempt.Message = "Resource is unsupported because a required runtime template is missing; no input was sent.";
                        attempt.Duration = attemptWatch.Elapsed;
                        return Complete(result, ResourceFarmFallbackOutcome.SearchFailed, watch,
                            attempt.Message, attempt.ErrorMessage);
                    }

                    ReportStep(progress, OneShotFarmStep.OpenSearchPanel,
                        clearTerritoryColor: false, resource: resource);
                    NavigationResult panel = await navigation.OpenResourceSearchPanelAsync(
                        deviceName, cancellationToken);
                    result.FinalState = panel.FinalState;
                    if (!panel.Success || (panel.FinalState != GameState.ResourceSearchPanel
                        && !panel.ScreenshotConfirmed))
                    {
                        attempt.Message = panel.Message; attempt.ErrorMessage = panel.ErrorMessage;
                        attempt.Duration = attemptWatch.Elapsed;
                        return Complete(result, ResourceFarmFallbackOutcome.RecoveryFailed, watch,
                            "ResourceSearchPanel could not be prepared for the next resource.",
                            panel.FailureReason ?? panel.ErrorMessage ?? panel.Message);
                    }

                    var levelPolicy = new ResourceLevelFallbackPolicy
                    {
                        Levels = request.ResourceLevelPriority,
                        AttemptsPerLevel = request.AttemptsPerResourceLevel,
                        StopOnFirstLocated = true,
                        WaitForToastClearBetweenAttempts = true,
                        RunId = runId,
                        PanelReady = true
                    };
                    ReportStep(progress, OneShotFarmStep.SearchWithLevelFallback,
                        clearTerritoryColor: false, resource: resource);
                    ResourceLevelFallbackResult level = await levelFallback.SearchAsync(
                        deviceName, resource, levelPolicy, request.UnoccupiedOnly, cancellationToken);
                    attempt.LevelFallbackResult = level;
                    attempt.AttemptedLevels = level.Attempts?.Select(x => x.Level).Distinct().ToArray()
                        ?? new int[0];
                    attempt.LocatedLevel = level.LocatedLevel;
                    result.FinalState = level.FinalState;
                    Log(runId, deviceName, resource, level.LocatedLevel, "Search", level.Outcome.ToString());

                    if (level.Outcome == ResourceLevelFallbackOutcome.Cancelled)
                        throw new OperationCanceledException(cancellationToken);
                    if (level.Outcome == ResourceLevelFallbackOutcome.ResourceAreaLv2Redirect)
                    {
                        var exactFallback = levelFallback as IExactResourceLevelFallbackService;
                        var pointRetryFallback = levelFallback as IResourceAreaLv2PointRetryFallbackService;
                        var lastLevelAttempt = level.Attempts?.LastOrDefault();
                        int exactLevel = lastLevelAttempt?.ConfigurationResult?.EffectiveLevel
                            ?? lastLevelAttempt?.ConfigurationResult?.ObservedLevel
                            ?? level.LastAttemptedLevel
                            ?? lastLevelAttempt?.Level
                            ?? request.ResourceLevelPriority.FirstOrDefault();
                        ReportToast(progress, deviceName, request.FarmRunId, resource, exactLevel,
                            lastLevelAttempt?.MatchedNotFoundVariant
                                ?? level.MatchedNotFoundVariant
                                ?? "ResourceAreaLv2Redirect");
                        if (areaLv2Recovery == null || exactFallback == null)
                            return Complete(result, ResourceFarmFallbackOutcome.ResourceAreaLv2Redirect,
                                watch, level.Message, level.ErrorMessage);
                        TeamNumber? expectedTeam = request.TeamOperation?.ExpectedTeam
                            ?? request.ExpectedTeam;
                        ResourceLevelFallbackResult retryResult = level;
                        ResourceAreaLv2RecoveryResult recovery = null;
                        bool locatedBySpecialRetry = false;
                        bool specialAttemptsExhausted = false;
                        int maxSpecialAttempts = 0;
                        int specialAttempt = 0;
                        while (true)
                        {
                            specialAttempt++;
                            var recoveryRequest = new ResourceAreaLv2RecoveryRequest
                            {
                                RunId = runId,
                                DeviceName = deviceName,
                                Resource = resource,
                                Level = exactLevel,
                                UnoccupiedOnly = request.UnoccupiedOnly,
                                AreaEpoch = areaEpoch,
                                ExpectedTeam = expectedTeam
                            };
                            activeAreaLv2Request = recoveryRequest;
                            recovery = await areaLv2Recovery.RecoverAsync(
                                recoveryRequest, cancellationToken);
                            maxSpecialAttempts = recovery.MaxAttempts > 0
                                ? recovery.MaxAttempts : specialAttempt;
                            logger.Info($"[Resource Area Lv2 Retry Loop] RunId='{runId}', "
                                + $"DeviceName='{deviceName}', ExpectedTeam='{expectedTeam}', "
                                + $"Resource='{resource}', ExactRetryLevel={exactLevel}, "
                                + $"UnoccupiedOnly={request.UnoccupiedOnly}, AreaEpoch={areaEpoch}, "
                                + $"Attempt={specialAttempt}, MaxAttempts={maxSpecialAttempts}, "
                                + $"NavigationOutcome='{(recovery.Success ? "Success" : "Failed")}', "
                                + "GenericMapRecoveryInvoked=false, "
                                + $"NextAction='{(recovery.Success ? "RetryExactLevel" : "Stop")}', "
                                + $"FailureReason='{recovery.FailureReason ?? string.Empty}'");

                            if (recovery.Exhausted)
                                break;
                            if (!recovery.Success)
                                return Complete(result, ResourceFarmFallbackOutcome.RecoveryFailed,
                                    watch, "Không thể chuyển tới khu tài nguyên Lv2.",
                                    recovery.FailureReason);

                            retryResult = pointRetryFallback != null
                                ? await pointRetryFallback.SearchSingleLevelForPointRetryAsync(
                                    deviceName, resource, exactLevel, request.UnoccupiedOnly,
                                    runId, areaEpoch, expectedTeam, cancellationToken)
                                : await exactFallback.SearchSingleLevelAsync(
                                    deviceName, resource, exactLevel, request.UnoccupiedOnly,
                                    runId, cancellationToken);
                            if (retryResult.Outcome == ResourceLevelFallbackOutcome.ResourceLocated)
                            {
                                locatedBySpecialRetry = true;
                                areaLv2Recovery.Clear(recoveryRequest);
                                level = retryResult;
                                break;
                            }
                            if (retryResult.Outcome != ResourceLevelFallbackOutcome.ResourceAreaLv2Redirect)
                            {
                                areaLv2Recovery.Clear(recoveryRequest);
                                level = retryResult;
                                break;
                            }
                            if (specialAttempt >= maxSpecialAttempts)
                            {
                                specialAttemptsExhausted = true;
                                break;
                            }
                        }

                        if (!locatedBySpecialRetry)
                        {
                            if (specialAttemptsExhausted || (recovery != null && recovery.Exhausted))
                            {
                                areaLv2Recovery.Clear(new ResourceAreaLv2RecoveryRequest
                                {
                                    RunId = runId, DeviceName = deviceName, Resource = resource,
                                    Level = exactLevel, AreaEpoch = areaEpoch
                                });
                                failedResourcesInCurrentArea.Add(resource);
                                attempt.Message = "ResourceAreaLv2PointAttemptsExhausted";
                                attempt.ErrorMessage = level.ErrorMessage;
                                LogAreaSweepProgress(runId, deviceName, areaEpoch, repositionCount,
                                    resourceIndex, resourceOrder, attemptedResourcesInCurrentArea,
                                    failedResourcesInCurrentArea,
                                    "ResourceAreaLv2PointAttemptsExhausted", true,
                                    "ContinueResourceSweep");
                                level = new ResourceLevelFallbackResult
                                {
                                    Outcome = ResourceLevelFallbackOutcome.ResourceLevelsExhausted,
                                    ResourceType = resource,
                                    LastAttemptedLevel = exactLevel,
                                    FailureReason = ResourceSearchFailureReason.ResourceAreaLv2PointAttemptsExhausted,
                                    Message = attempt.Message,
                                    RequestedLevels = new[] { exactLevel },
                                    Attempts = level.Attempts,
                                    InitialState = level.InitialState,
                                    FinalState = level.FinalState
                                };
                                retryResult = level;
                            }
                            if (retryResult.Outcome == ResourceLevelFallbackOutcome.ResourceAreaLv2Redirect)
                                return Complete(result, ResourceFarmFallbackOutcome.ResourceAreaLv2Redirect,
                                    watch, retryResult.Message, retryResult.ErrorMessage);
                        }
                    }
                    if (level.Outcome == ResourceLevelFallbackOutcome.ResourceLevelsExhausted)
                    {
                        attempt.SearchLevelsExhausted = true;
                        AddUnique(exhausted, resource);
                        attempt.Message = level.Message; attempt.Duration = attemptWatch.Elapsed;
                        failedResourcesInCurrentArea.Add(resource);
                        LogAreaSweepProgress(runId, deviceName, areaEpoch, repositionCount,
                            resourceIndex, resourceOrder, attemptedResourcesInCurrentArea,
                            failedResourcesInCurrentArea, level.Outcome.ToString(), true,
                            "TryNextResource");
                        if (options.SwitchWhenLevelsExhausted) continue;
                        return Complete(result, ResourceFarmFallbackOutcome.ResourcePlanExhausted,
                            watch, "Resource level plan was exhausted.", null);
                    }
                    if (!level.Success || level.Outcome != ResourceLevelFallbackOutcome.ResourceLocated)
                    {
                        attempt.Message = level.Message; attempt.ErrorMessage = level.ErrorMessage;
                        attempt.Duration = attemptWatch.Elapsed;
                        return Complete(result, ResourceFarmFallbackOutcome.SearchFailed, watch,
                            level.Message, level.ErrorMessage);
                    }

                    result.LocatedResource = resource; result.LocatedLevel = level.LocatedLevel;
                    result.LastCompletedStep = OneShotFarmStep.SearchWithLevelFallback;
                    ReportStep(progress, OneShotFarmStep.VerifyResourcePopup,
                        clearTerritoryColor: false, resource: resource,
                        effectiveLevel: level.LocatedLevel);
                    ResourcePopupVerificationResult popupResult = await popup.VerifyAsync(
                        deviceName, resource, cancellationToken);
                    attempt.PopupResult = popupResult; result.FinalState = popupResult.FinalState;
                    if (popupResult.Outcome == ResourcePopupOutcome.Cancelled)
                        throw new OperationCanceledException(cancellationToken);
                    if (!popupResult.Success || popupResult.Outcome != ResourcePopupOutcome.ResourcePopupReady
                        || !popupResult.ExpectedResourceVerified || !popupResult.GatherButtonVerified)
                    {
                        result.LastCompletedStep = OneShotFarmStep.VerifyResourcePopup;
                        attempt.Message = popupResult.Message; attempt.ErrorMessage = popupResult.ErrorMessage;
                        attempt.Duration = attemptWatch.Elapsed;
                        return Complete(result, ResourceFarmFallbackOutcome.PopupFailed, watch,
                            popupResult.Message, popupResult.ErrorMessage);
                    }

                    result.LastCompletedStep = OneShotFarmStep.VerifyResourcePopup;

                    ReportStep(progress, OneShotFarmStep.OpenTeamSelection, clearTerritoryColor: true);
                    OpenTeamSelectionResult opened = openTeam is IResourceAwareOpenTeamSelectionService resourceAwareOpen
                        ? await resourceAwareOpen.OpenAsync(deviceName, resource, cancellationToken)
                        : await openTeam.OpenAsync(deviceName, cancellationToken);
                    attempt.OpenTeamResult = opened; result.FinalState = opened.FinalState;
                    if (opened.Outcome == OpenTeamSelectionOutcome.Cancelled)
                        throw new OperationCanceledException(cancellationToken);
                    if (!opened.Success || !opened.TeamSelectionVerified || !opened.TeamSelectionReady)
                    {
                        attempt.Message = opened.Message; attempt.ErrorMessage = opened.ErrorMessage;
                        attempt.Duration = attemptWatch.Elapsed;
                        return Complete(result, ResourceFarmFallbackOutcome.TeamSelectionFailed, watch,
                            opened.Message, opened.ErrorMessage);
                    }

                    ReportStep(progress, OneShotFarmStep.SelectTeam, clearTerritoryColor: true);
                    SelectFarmTeamResult selected = await selectTeam.SelectAsync(deviceName,
                        new TeamSelectionRequest { AllowedTeams = request.AllowedTeams,
                            Priority = request.TeamPriority,
                            // Preserve the ready-team decision made before resource
                            // search.  Without these fields this inner workflow falls
                            // back to global priority and can tap a non-ready team.
                            ExpectedTeam = request.TeamOperation?.ExpectedTeam
                                ?? request.ExpectedTeam,
                            TeamOperation = request.TeamOperation,
                            WorldMapAvailableTeams = request.WorldMapAvailableTeams,
                            WorldMapReadyTeams = request.WorldMapReadyTeams,
                            WorldMapRosterStatus = request.WorldMapRosterStatus,
                            WorldMapRosterConfidence = request.WorldMapRosterConfidence,
                            AllowTeam1 = request.AllowTeam1, RunId = runId }, cancellationToken);
                    attempt.SelectTeamResult = selected; result.FinalState = selected.FinalState;
                    if (selected.Outcome == SelectFarmTeamOutcome.Cancelled)
                        throw new OperationCanceledException(cancellationToken);
                    if (selected.Outcome == SelectFarmTeamOutcome.NoEligibleTeam)
                        return CompleteAttempt(result, attempt, attemptWatch,
                            ResourceFarmFallbackOutcome.NoEligibleTeam, watch, selected.Message, selected.ErrorMessage);
                    if (!selected.Success || !selected.SelectedTeam.HasValue || !selected.SelectedStateVerified)
                        return CompleteAttempt(result, attempt, attemptWatch,
                            ResourceFarmFallbackOutcome.TeamSelectionFailed, watch, selected.Message, selected.ErrorMessage);
                    if (request.TeamOperation != null
                        && selected.SelectedTeam.Value != request.TeamOperation.ExpectedTeam)
                        return CompleteAttempt(result, attempt, attemptWatch,
                            ResourceFarmFallbackOutcome.TeamSelectionFailed, watch,
                            "Đội được chọn không khớp đội sẵn sàng từ lần quét bản đồ mới.",
                            "SelectedTeamDoesNotMatchExpectedTeam");

                    DispatchMarchResult dispatched = await dispatch.DispatchAsync(deviceName,
                        new DispatchMarchRequest { ExpectedTeam = request.TeamOperation?.ExpectedTeam
                                ?? selected.SelectedTeam.Value,
                            // SelectFarmTeam has just tapped the WorldMap-confirmed
                            // ready team. Dispatch only needs a fresh Team Selection
                            // state and the current enabled yellow action button.
                            RequireExpectedTeamSelected = false,
                            AllowStructuralVerificationFallback = true, CurrentResource = resource,
                            RunId = runId },
                        cancellationToken);
                    attempt.DispatchResult = dispatched; result.FinalState = dispatched.FinalState;
                    if (dispatched.Outcome == DispatchMarchOutcome.Cancelled)
                        throw new OperationCanceledException(cancellationToken);
                    if (dispatched.Outcome == DispatchMarchOutcome.StorageLimitResourceSwitchRequired
                        || dispatched.Outcome == DispatchMarchOutcome.ResourceExpiryResourceSwitchRequired)
                    {
                        bool resourceExpiry = dispatched.Outcome
                            == DispatchMarchOutcome.ResourceExpiryResourceSwitchRequired;
                        attempt.ResourceExpiryDetected = dispatched.ResourceExpiryDialogDetected;
                        attempt.StorageLimitDetected = !resourceExpiry && dispatched.StorageLimitDialogDetected;
                        attempt.StorageLimitConfirmed = !resourceExpiry && dispatched.StorageLimitCancelled;
                        attempt.MarkedStorageFull = !resourceExpiry && dispatched.StorageLimitCancelled;
                        attempt.RecoverySucceeded = dispatched.StorageLimitResult != null
                            && (dispatched.StorageLimitResult.ReturnedToWorldMap
                            || dispatched.StorageLimitResult.ReturnedToSearchPanel);
                        result.RecoveryTransitions += dispatched.StorageLimitResult?.RecoveryTransitions ?? 0;
                        attempt.Message = dispatched.Message; attempt.ErrorMessage = dispatched.ErrorMessage;
                        attempt.Duration = attemptWatch.Elapsed;
                        if ((!resourceExpiry && !attempt.MarkedStorageFull)
                            || (resourceExpiry && !dispatched.ResourceExpiryCancelled)
                            || !attempt.RecoverySucceeded)
                            return Complete(result, ResourceFarmFallbackOutcome.RecoveryFailed, watch,
                                dispatched.Message, dispatched.ErrorMessage);
                        if (resourceExpiry)
                        {
                            failedResourcesInCurrentArea.Add(resource);
                            LogAreaSweepProgress(runId, deviceName, areaEpoch, repositionCount,
                                resourceIndex, resourceOrder, attemptedResourcesInCurrentArea,
                                failedResourcesInCurrentArea, dispatched.Outcome.ToString(), true,
                                "TryNextResource");
                            Log(runId, deviceName, resource, level.LocatedLevel,
                                "ResourceExpiry", dispatched.Outcome.ToString());
                            continue;
                        }
                        AddUnique(storageFull, resource);
                        Log(runId, deviceName, resource, level.LocatedLevel,
                            "StorageFull", dispatched.Outcome.ToString());
                        if (options.SwitchOnStorageLimit) continue;
                        return Complete(result, ResourceFarmFallbackOutcome.DispatchFailed, watch,
                            dispatched.Message, dispatched.ErrorMessage);
                    }

                    bool marchStarted = dispatched.Success && dispatched.MarchStartedVerified
                        && (dispatched.Outcome == DispatchMarchOutcome.MarchStarted
                            || dispatched.Outcome == DispatchMarchOutcome.AlreadyMarching);
                    if (!marchStarted)
                        return CompleteAttempt(result, attempt, attemptWatch,
                            ResourceFarmFallbackOutcome.DispatchFailed, watch,
                            dispatched.Message, dispatched.ErrorMessage);

                    attempt.Message = dispatched.Message; attempt.Duration = attemptWatch.Elapsed;
                    result.DispatchedResource = resource;
                    result.DispatchedTeam = dispatched.DispatchedTeam ?? selected.SelectedTeam;
                    return Complete(result, ResourceFarmFallbackOutcome.MarchStarted, watch,
                        $"{resource} march start was verified.", null);
                    }

                    bool allStorageFull = resourceOrder.All(storageFull.Contains);
                    if (allStorageFull)
                        return Complete(result, ResourceFarmFallbackOutcome.AllCandidateStoragesFull, watch,
                            "Storage is full for every candidate resource.", null);

                    bool completeSweepFailed = failedResourcesInCurrentArea.Count == resourceOrder.Length;
                    if (!completeSweepFailed)
                        return Complete(result, ResourceFarmFallbackOutcome.ResourcePlanExhausted, watch,
                            "The resource plan ended before every resource received a terminal search result.", null);

                    if (repositionCount >= 1)
                    {
                        LogAreaSweepCompleted(runId, deviceName, areaEpoch, repositionCount,
                            resourceOrder, attemptedResourcesInCurrentArea,
                            failedResourcesInCurrentArea);
                        return Complete(result, ResourceFarmFallbackOutcome.SearchAreaExhausted, watch,
                            "NoResourceAfterReposition: the complete resource sweep failed after one verified map reposition.",
                            $"AreaEpoch={areaEpoch}; ResourceOrder={string.Join(",", resourceOrder)}; "
                            + $"Attempted={string.Join(",", attemptedResourcesInCurrentArea)}; "
                            + $"Failed={string.Join(",", failedResourcesInCurrentArea)}; RepositionCount={repositionCount}");
                    }

                    LogSearchAreaRecoveryDecision(runId, deviceName, areaEpoch, repositionCount,
                        resourceOrder, attemptedResourcesInCurrentArea,
                        failedResourcesInCurrentArea, completeSweepFailed);

                    NavigationResult ensuredWorldMap = await navigation.EnsureWorldMapAsync(
                        deviceName, cancellationToken);
                    result.FinalState = ensuredWorldMap.FinalState;
                    if (!ensuredWorldMap.Success || ensuredWorldMap.FinalState != GameState.WorldMap)
                        return Complete(result, ResourceFarmFallbackOutcome.SearchAreaRecoveryFailed, watch,
                            "Không thể trở về bản đồ thế giới trước khi chuyển khu vực tìm tài nguyên.",
                            ensuredWorldMap.ErrorMessage ?? ensuredWorldMap.Message);

                    if (options.RepositionCooldownMs > 0)
                        await Task.Delay(options.RepositionCooldownMs, cancellationToken);

                    Task<NavigationResult> repositionTask;
                    var progressNavigation =
                        navigation as IWorldMapNavigationProgressService;
                    if (progressNavigation != null)
                    {
                        var navigationProgress =
                            new CallbackProgress<NavigationTransition>(transition =>
                            {
                                if (transition == null
                                    || !string.Equals(transition.Operation,
                                        "TerritoryColor",
                                        StringComparison.Ordinal)
                                    || string.IsNullOrWhiteSpace(
                                        transition.Message)
                                    || transition.Message.IndexOf(
                                        "Home=", StringComparison.Ordinal) < 0)
                                    return;
                                result.TerritoryColorSummary =
                                    transition.Message;
                                ReportColor(progress, areaEpoch + 1, transition.Message);
                            });
                        repositionTask = progressNavigation
                            .RepositionToAllianceTerritoryAsync(
                                deviceName, navigationProgress,
                                cancellationToken);
                    }
                    else
                    {
                        repositionTask = navigation
                            .RepositionToAllianceTerritoryAsync(
                                deviceName, cancellationToken);
                    }
                    Task completed = await Task.WhenAny(repositionTask,
                        Task.Delay(options.RepositionTimeoutMs, cancellationToken));
                    cancellationToken.ThrowIfCancellationRequested();
                    if (completed != repositionTask)
                        return Complete(result, ResourceFarmFallbackOutcome.RepositionTimeout, watch,
                            "Chuyển khu vực bản đồ đã quá thời gian chờ cho phép.", null);
                    NavigationResult reposition = await repositionTask;
                    result.TerritoryColorSummary =
                        GetTerritoryColorSummary(reposition);
                    ReportColor(progress, areaEpoch + 1,
                        result.TerritoryColorSummary);
                    result.FinalState = reposition.FinalState;
                    result.RecoveryTransitions++;
                    LogRecovery(runId, deviceName, reposition.Success ? "Repositioned" : "Failed");
                    if (!reposition.Success || reposition.FinalState != GameState.WorldMap)
                        return Complete(result, ResourceFarmFallbackOutcome.SearchAreaRecoveryFailed, watch,
                            "Vị trí hiện tại không phù hợp để khai thác tài nguyên; đang chuyển sang khu vực bản đồ khác.",
                            reposition.ErrorMessage ?? reposition.Message);

                    int previousAreaEpoch = areaEpoch;
                    repositionCount = 1;
                    areaEpoch++;
                    resourceIndex = 0;
                    attemptedResourcesInCurrentArea.Clear();
                    failedResourcesInCurrentArea.Clear();
                    LogAreaSweepReset(runId, deviceName, previousAreaEpoch, areaEpoch,
                        repositionCount, resourceOrder);
                }
            }
            catch (OperationCanceledException)
            {
                areaLv2Recovery?.Clear(activeAreaLv2Request);
                return Complete(result, ResourceFarmFallbackOutcome.Cancelled, watch,
                    "Resource fallback was cancelled.", null);
            }
            catch (Exception exception)
            {
                areaLv2Recovery?.Clear(activeAreaLv2Request);
                logger.Error($"[Resource Farm Fallback] RunId='{runId}', DeviceName='{deviceName}', Error='{exception.Message}'", exception);
                return Complete(result, ResourceFarmFallbackOutcome.Failed, watch,
                    "Resource fallback failed.", exception.Message);
            }
        }

        private string Validate(OneShotFarmRequest request)
        {
            if (request == null) return "OneShotFarmRequest is required.";
            var policy = new ResourceFarmFallbackPolicy
            {
                ResourcePriority = request.ResourcePriority,
                LevelPriority = request.ResourceLevelPriority,
                AttemptsPerLevel = request.AttemptsPerResourceLevel,
                StorageLimitPolicy = request.StorageLimitPolicy,
                SwitchOnStorageLimit = options.SwitchOnStorageLimit,
                SwitchWhenLevelsExhausted = options.SwitchWhenLevelsExhausted,
                StopOnFirstMarchStarted = options.StopOnFirstMarchStarted
            };
            try { policy.Validate(); }
            catch (Exception exception) { return exception.Message; }
            return null;
        }

        private static ResourceFarmFallbackResult CompleteAttempt(ResourceFarmFallbackResult result,
            ResourceFarmAttemptResult attempt, Stopwatch attemptWatch,
            ResourceFarmFallbackOutcome outcome, Stopwatch watch, string message, string error)
        {
            attempt.Message = message; attempt.ErrorMessage = error; attempt.Duration = attemptWatch.Elapsed;
            return Complete(result, outcome, watch, message, error);
        }

        private static ResourceFarmFallbackResult Complete(ResourceFarmFallbackResult result,
            ResourceFarmFallbackOutcome outcome, Stopwatch watch, string message, string error)
        {
            result.Outcome = outcome; result.Success = outcome == ResourceFarmFallbackOutcome.MarchStarted;
            result.Duration = watch.Elapsed; result.Message = message; result.ErrorMessage = error;
            return result;
        }

        private static ResourceFarmFallbackResult NewResult(OneShotFarmRequest request,
            GameState initialState, IReadOnlyList<ResourceFarmAttemptResult> attempts,
            IReadOnlyList<ResourceType> attempted, IReadOnlyList<ResourceType> storage,
            IReadOnlyList<ResourceType> exhausted) => new ResourceFarmFallbackResult
        {
            RequestedResources = request?.ResourcePriority ?? new ResourceType[0],
            AttemptedResources = attempted, StorageFullResources = storage,
            LevelsExhaustedResources = exhausted, Attempts = attempts,
            InitialState = initialState, FinalState = initialState
        };

        private static void AddUnique(IList<ResourceType> items, ResourceType resource)
        {
            if (!items.Contains(resource)) items.Add(resource);
        }

        private static ResourceType[] NormalizeResourceOrder(
            IEnumerable<ResourceType> resources)
        {
            return (resources ?? Enumerable.Empty<ResourceType>())
                .Where(resource => Enum.IsDefined(typeof(ResourceType), resource))
                .Distinct()
                .ToArray();
        }

        private void LogAreaSweepStarted(string runId, string deviceName, int areaEpoch,
            int repositionCount, IReadOnlyList<ResourceType> resourceOrder)
        {
            logger.Info($"[Resource Area Sweep Started] RunId='{runId}', DeviceName='{deviceName}', "
                + $"AreaEpoch={areaEpoch}, RepositionCount={repositionCount}, "
                + $"ResourceOrder='{string.Join(",", resourceOrder)}', ResourceCount={resourceOrder.Count}, "
                + $"StartResource='{resourceOrder[0]}', Reason='OperationStart'");
        }

        private void LogAreaSweepProgress(string runId, string deviceName, int areaEpoch,
            int repositionCount, int resourceIndex, IReadOnlyList<ResourceType> resourceOrder,
            ICollection<ResourceType> attemptedResources,
            ICollection<ResourceType> failedResources, string resourceOutcome,
            bool countsTowardAreaFailure, string nextAction)
        {
            logger.Info($"[Resource Area Sweep Progress] RunId='{runId}', DeviceName='{deviceName}', "
                + $"AreaEpoch={areaEpoch}, RepositionCount={repositionCount}, ResourceIndex={resourceIndex}, "
                + $"Resource='{resourceOrder[resourceIndex]}', ResourceOrder='{string.Join(",", resourceOrder)}', "
                + $"AttemptedResources='{string.Join(",", attemptedResources)}', "
                + $"FailedResources='{string.Join(",", failedResources)}', "
                + $"DistinctFailedCount={failedResources.Count}, RequiredFailureCount={resourceOrder.Count}, "
                + $"ResourceOutcome='{resourceOutcome}', CountsTowardAreaFailure={countsTowardAreaFailure}, "
                + $"SweepComplete={failedResources.Count == resourceOrder.Count}, NextAction='{nextAction}'");
        }

        private void LogSearchAreaRecoveryDecision(string runId, string deviceName,
            int areaEpoch, int repositionCount, IReadOnlyList<ResourceType> resourceOrder,
            ICollection<ResourceType> attemptedResources, ICollection<ResourceType> failedResources,
            bool completeSweepFailed)
        {
            logger.Info($"[Search Area Recovery Decision] RunId='{runId}', DeviceName='{deviceName}', "
                + $"AreaEpoch={areaEpoch}, RepositionCount={repositionCount}, "
                + $"ResourceOrder='{string.Join(",", resourceOrder)}', "
                + $"AttemptedResources='{string.Join(",", attemptedResources)}', "
                + $"FailedResources='{string.Join(",", failedResources)}', "
                + $"DistinctFailedCount={failedResources.Count}, RequiredFailureCount={resourceOrder.Count}, "
                + $"CompleteSweepFailed={completeSweepFailed}, RecoveryAllowed={repositionCount == 0}, "
                + "Outcome='Reposition', FailureReason='CompleteResourceSweepFailed'");
        }

        private void LogAreaSweepReset(string runId, string deviceName, int previousAreaEpoch,
            int newAreaEpoch, int repositionCount, IReadOnlyList<ResourceType> resourceOrder)
        {
            logger.Info($"[Resource Area Sweep Reset] RunId='{runId}', DeviceName='{deviceName}', "
                + $"PreviousAreaEpoch={previousAreaEpoch}, NewAreaEpoch={newAreaEpoch}, "
                + $"RepositionCount={repositionCount}, ClearedAttemptedResources=true, "
                + $"ClearedFailedResources=true, RestartResourceIndex=0, "
                + $"RestartResource='{resourceOrder[0]}', ResourceOrder='{string.Join(",", resourceOrder)}'");
        }

        private void LogAreaSweepCompleted(string runId, string deviceName, int areaEpoch,
            int repositionCount, IReadOnlyList<ResourceType> resourceOrder,
            ICollection<ResourceType> attemptedResources, ICollection<ResourceType> failedResources)
        {
            logger.Info($"[Resource Area Sweep Completed] RunId='{runId}', DeviceName='{deviceName}', "
                + $"AreaEpoch={areaEpoch}, RepositionCount={repositionCount}, "
                + $"ResourceOrder='{string.Join(",", resourceOrder)}', "
                + $"AttemptedResources='{string.Join(",", attemptedResources)}', "
                + $"FailedResources='{string.Join(",", failedResources)}', "
                + "Outcome='NoResourceAfterReposition', RecoveryAttemptedAgain=false");
        }

        private static ResourceSearchFailureReason? GetSearchAreaRecoveryReason(
            ResourceLevelFallbackResult result)
        {
            if (result?.Attempts == null)
                return null;

            return result.Attempts
                .Where(attempt => attempt.FailureReason
                    == ResourceSearchFailureReason.SearchOtherRegion)
                .Select(attempt => (ResourceSearchFailureReason?)attempt.FailureReason)
                .FirstOrDefault();
        }

        private static bool HasSearchTapNotApplied(ResourceLevelFallbackResult result) =>
            result?.Attempts != null && result.Attempts.Any(attempt =>
                attempt.SearchOutcome == ResourceSearchOutcome.SearchTapNotApplied);

        private static string GetTerritoryColorSummary(NavigationResult result)
        {
            return result?.Transitions?
                .Where(transition => string.Equals(
                    transition.Operation, "TerritoryColor",
                    StringComparison.Ordinal)
                    && !string.IsNullOrWhiteSpace(transition.Message)
                    && transition.Message.IndexOf(
                        "Home=", StringComparison.Ordinal) >= 0)
                .Select(transition => transition.Message)
                .LastOrDefault();
        }

        private static void ReportColor(
            IProgress<ResourceFarmFallbackProgress> progress,
            int recoveryAttempt,
            string summary)
        {
            if (progress == null || string.IsNullOrWhiteSpace(summary)) return;
            try
            {
                progress.Report(new ResourceFarmFallbackProgress
                {
                    RecoveryAttempt = recoveryAttempt,
                    TerritoryColorSummary = summary,
                    MapRepositionState = MapRepositionState.VerifyingTerritoryColor
                });
            }
            catch
            {
                // Progress reporting must never alter device recovery.
            }
        }

        private static void ReportStep(
            IProgress<ResourceFarmFallbackProgress> progress,
            OneShotFarmStep step,
            bool clearTerritoryColor,
            ResourceType? resource = null,
            int? effectiveLevel = null)
        {
            if (progress == null) return;
            try
            {
                progress.Report(new ResourceFarmFallbackProgress
                {
                    CurrentStep = step,
                    ClearTerritoryColor = clearTerritoryColor,
                    CurrentResource = resource,
                    EffectiveLevel = effectiveLevel,
                    MapRepositionState = MapRepositionState.None
                });
            }
            catch
            {
                // Progress reporting must never alter device recovery.
            }
        }

        private void ReportToast(
            IProgress<ResourceFarmFallbackProgress> progress,
            string deviceName,
            string farmRunId,
            ResourceType resource,
            int effectiveLevel,
            string variant)
        {
            if (progress == null) return;
            try
            {
                DateTimeOffset detectedAt = DateTimeOffset.UtcNow;
                progress.Report(new ResourceFarmFallbackProgress
                {
                    CurrentStep = OneShotFarmStep.ResourceFarmFallback,
                    CurrentResource = resource,
                    EffectiveLevel = effectiveLevel > 0 ? effectiveLevel : (int?)null,
                    ResourceToastVariant = variant,
                    ResourceToastDetectedAt = detectedAt,
                    ResourceToastState = "Detected",
                    FarmRunId = farmRunId,
                    MapRepositionState = MapRepositionState.None
                });
                logger.Info($"[Device Toast Status] DeviceName='{deviceName}', FarmRunId='{farmRunId ?? string.Empty}', "
                    + $"Resource='{resource}', EffectiveLevel={effectiveLevel}, Variant='{variant}', "
                    + $"DetectedAt='{detectedAt:O}', DisplayText='NormalizedNotFoundToast', State='Detected'");
            }
            catch
            {
                // Progress reporting must never alter recovery behavior.
            }
        }

        private void Log(string runId, string device, ResourceType resource,
            int? level, string phase, string outcome) => logger.Info(
            $"[Resource Farm Fallback] RunId='{runId}', DeviceName='{device}', Resource='{resource}', Level='{level?.ToString() ?? string.Empty}', Phase='{phase}', Outcome='{outcome ?? string.Empty}', Cancellation=false");

        private void LogRecovery(string runId, string device, string outcome) => logger.Info(
            $"[Resource Farm Fallback] RunId='{runId}', DeviceName='{device}', Resource='', Level='', Phase='SearchAreaRecovery', Outcome='{outcome ?? string.Empty}', Cancellation=false");

        private sealed class CallbackProgress<T> : IProgress<T>
        {
            private readonly Action<T> callback;

            public CallbackProgress(Action<T> callback)
            {
                this.callback = callback
                    ?? throw new ArgumentNullException(nameof(callback));
            }

            public void Report(T value)
            {
                callback(value);
            }
        }
    }
}
