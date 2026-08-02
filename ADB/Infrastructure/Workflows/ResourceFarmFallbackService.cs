using ADB_Tool_Automation_Post_FB.Core.Diagnostics;
using ADB_Tool_Automation_Post_FB.Core.GameDetection;
using ADB_Tool_Automation_Post_FB.Core.MarchDispatch;
using ADB_Tool_Automation_Post_FB.Core.Navigation;
using ADB_Tool_Automation_Post_FB.Core.ResourcePopup;
using ADB_Tool_Automation_Post_FB.Core.ResourceSearch;
using ADB_Tool_Automation_Post_FB.Core.TeamSelection;
using ADB_Tool_Automation_Post_FB.Core.Workflows;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace ADB_Tool_Automation_Post_FB.Infrastructure.Workflows
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

        public ResourceFarmFallbackService(IWorldMapNavigationService navigation,
            IResourceLevelFallbackService levelFallback,
            IResourceAwarePopupVerificationService popup,
            IOpenTeamSelectionService openTeam, ISelectFarmTeamService selectTeam,
            IDispatchSelectedTeamService dispatch, IResourceTemplateProfileProvider profiles,
            ResourceFarmFallbackOptions options, IDiagnosticLogger logger)
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
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                string validation = Validate(request);
                if (validation != null) return Complete(result, ResourceFarmFallbackOutcome.Failed,
                    watch, "Resource fallback request is invalid.", validation);

                var repositionsByResource = new Dictionary<ResourceType, int>();
                int totalRepositions = 0;
                ResourceType? retryResourceAfterReposition = null;
                for (int searchAreaAttempt = 0; ; searchAreaAttempt++)
                {
                    var attemptedThisPass = new HashSet<ResourceType>();
                    int exhaustedResources = 0;
                    int searchTapNotAppliedResources = 0;
                    bool searchAreaRecoveryRequested = false;
                    ResourceType? preferredRetryResource = retryResourceAfterReposition;
                    IEnumerable<ResourceType> passResources = preferredRetryResource.HasValue
                        ? new[] { preferredRetryResource.Value }.Concat(request.ResourcePriority
                            .Where(resource => resource != preferredRetryResource.Value))
                        : request.ResourcePriority;
                    retryResourceAfterReposition = null;
                    foreach (ResourceType resource in passResources)
                    {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (attemptedThisPass.Contains(resource) || storageFull.Contains(resource)) continue;
                    attemptedThisPass.Add(resource);
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

                    NavigationResult panel = await navigation.OpenResourceSearchPanelAsync(
                        deviceName, cancellationToken);
                    result.FinalState = panel.FinalState;
                    if (!panel.Success || panel.FinalState != GameState.ResourceSearchPanel)
                    {
                        attempt.Message = panel.Message; attempt.ErrorMessage = panel.ErrorMessage;
                        attempt.Duration = attemptWatch.Elapsed;
                        return Complete(result, ResourceFarmFallbackOutcome.RecoveryFailed, watch,
                            "ResourceSearchPanel could not be prepared for the next resource.", panel.ErrorMessage);
                    }

                    var levelPolicy = new ResourceLevelFallbackPolicy
                    {
                        Levels = request.ResourceLevelPriority,
                        AttemptsPerLevel = request.AttemptsPerResourceLevel,
                        StopOnFirstLocated = true,
                        WaitForToastClearBetweenAttempts = true,
                        RunId = runId
                    };
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
                    if (level.Outcome == ResourceLevelFallbackOutcome.ResourceLevelsExhausted)
                    {
                        attempt.SearchLevelsExhausted = true;
                        AddUnique(exhausted, resource);
                        attempt.Message = level.Message; attempt.Duration = attemptWatch.Elapsed;
                        ResourceSearchFailureReason? searchAreaReason =
                            GetSearchAreaRecoveryReason(level);
                        if (searchAreaReason.HasValue)
                        {
                            searchAreaRecoveryRequested = true;
                            retryResourceAfterReposition = resource;
                            Log(runId, deviceName, resource, level.LocatedLevel,
                                "SearchAreaRecovery", searchAreaReason.Value.ToString());
                            break;
                        }
                        if (HasSearchTapNotApplied(level))
                        {
                            searchTapNotAppliedResources++;
                            if (searchTapNotAppliedResources
                                >= options.SearchTapNotAppliedResourcesBeforeReposition)
                            {
                                searchAreaRecoveryRequested = true;
                                retryResourceAfterReposition = resource;
                                Log(runId, deviceName, resource, level.LocatedLevel,
                                    "SearchAreaRecovery",
                                    $"SearchTapNotAppliedResources={searchTapNotAppliedResources}");
                                break;
                            }
                        }
                        exhaustedResources++;
                        if (exhaustedResources
                            >= options.ExhaustedResourcesBeforeReposition)
                        {
                            searchAreaRecoveryRequested = true;
                            retryResourceAfterReposition = resource;
                            Log(runId, deviceName, resource, level.LocatedLevel,
                                "SearchAreaRecovery",
                                $"ExhaustedResources={exhaustedResources}");
                            break;
                        }
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
                            Priority = request.TeamPriority, AllowTeam1 = request.AllowTeam1,
                            RunId = runId }, cancellationToken);
                    attempt.SelectTeamResult = selected; result.FinalState = selected.FinalState;
                    if (selected.Outcome == SelectFarmTeamOutcome.Cancelled)
                        throw new OperationCanceledException(cancellationToken);
                    if (selected.Outcome == SelectFarmTeamOutcome.NoEligibleTeam)
                        return CompleteAttempt(result, attempt, attemptWatch,
                            ResourceFarmFallbackOutcome.NoEligibleTeam, watch, selected.Message, selected.ErrorMessage);
                    if (!selected.Success || !selected.SelectedTeam.HasValue || !selected.SelectedStateVerified)
                        return CompleteAttempt(result, attempt, attemptWatch,
                            ResourceFarmFallbackOutcome.TeamSelectionFailed, watch, selected.Message, selected.ErrorMessage);

                    DispatchMarchResult dispatched = await dispatch.DispatchAsync(deviceName,
                        new DispatchMarchRequest { ExpectedTeam = selected.SelectedTeam.Value,
                            RequireExpectedTeamSelected = true,
                            AllowActionReadyFallback = selected.ActionReadyFallbackAccepted,
                            TeamTapCount = selected.TeamTapCount,
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

                    bool allStorageFull = storageFull.Count == request.ResourcePriority.Count;
                    if (allStorageFull)
                        return Complete(result, ResourceFarmFallbackOutcome.AllCandidateStoragesFull, watch,
                            "Storage is full for every candidate resource.", null);

                    if (searchAreaRecoveryRequested
                        && (totalRepositions >= options.MaxAreaRepositionsPerFarmRun
                            || (retryResourceAfterReposition.HasValue
                                && repositionsByResource.TryGetValue(
                                    retryResourceAfterReposition.Value, out int resourceRepositions)
                                && resourceRepositions >= options.MaxAreaRepositionsPerResource)))
                        return Complete(result, ResourceFarmFallbackOutcome.SearchAreaExhausted, watch,
                            "Đã thử số lần chuyển khu vực tối đa nhưng vẫn chưa tìm thấy tài nguyên.", null);

                    if (searchAreaAttempt >= options.MaxSearchAreaRecoveryAttempts)
                        return Complete(result, ResourceFarmFallbackOutcome.ResourcePlanExhausted, watch,
                            searchAreaRecoveryRequested
                                ? "Đã thử số lần chuyển khu vực tối đa nhưng vẫn chưa tìm thấy tài nguyên."
                                : "The four-resource plan was exhausted without a march.", null);

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
                                ReportColor(progress,
                                    searchAreaAttempt + 1,
                                    transition.Message);
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
                    ReportColor(progress, searchAreaAttempt + 1,
                        result.TerritoryColorSummary);
                    result.FinalState = reposition.FinalState;
                    result.RecoveryTransitions++;
                    totalRepositions++;
                    if (retryResourceAfterReposition.HasValue)
                    {
                        int count;
                        repositionsByResource.TryGetValue(retryResourceAfterReposition.Value, out count);
                        repositionsByResource[retryResourceAfterReposition.Value] = count + 1;
                    }
                    LogRecovery(runId, deviceName, reposition.Success ? "Repositioned" : "Failed");
                    if (!reposition.Success || reposition.FinalState != GameState.WorldMap)
                        return Complete(result, ResourceFarmFallbackOutcome.SearchAreaRecoveryFailed, watch,
                            "Vị trí hiện tại không phù hợp để khai thác tài nguyên; đang chuyển sang khu vực bản đồ khác.",
                            reposition.ErrorMessage ?? reposition.Message);
                }
            }
            catch (OperationCanceledException)
            {
                return Complete(result, ResourceFarmFallbackOutcome.Cancelled, watch,
                    "Resource fallback was cancelled.", null);
            }
            catch (Exception exception)
            {
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
            bool clearTerritoryColor)
        {
            if (progress == null) return;
            try
            {
                progress.Report(new ResourceFarmFallbackProgress
                {
                    CurrentStep = step,
                    ClearTerritoryColor = clearTerritoryColor,
                    MapRepositionState = MapRepositionState.None
                });
            }
            catch
            {
                // Progress reporting must never alter device recovery.
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
