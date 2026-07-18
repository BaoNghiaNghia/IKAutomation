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
            OneShotFarmRequest request, GameState initialState, CancellationToken cancellationToken)
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

                foreach (ResourceType resource in request.ResourcePriority)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (attempted.Contains(resource) || storageFull.Contains(resource)) continue;
                    attempted.Add(resource);
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
                        exhausted.Add(resource);
                        attempt.Message = level.Message; attempt.Duration = attemptWatch.Elapsed;
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

                    SelectFarmTeamResult selected = await selectTeam.SelectAsync(deviceName,
                        new TeamSelectionRequest { AllowedTeams = request.AllowedTeams,
                            Priority = request.TeamPriority, AllowTeam1 = request.AllowTeam1 }, cancellationToken);
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
                            AllowStructuralVerificationFallback = true, CurrentResource = resource },
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
                        storageFull.Add(resource);
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
                return Complete(result, allStorageFull
                    ? ResourceFarmFallbackOutcome.AllCandidateStoragesFull
                    : ResourceFarmFallbackOutcome.ResourcePlanExhausted, watch,
                    allStorageFull ? "Storage is full for every candidate resource."
                        : "The four-resource plan was exhausted without a march.", null);
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

        private void Log(string runId, string device, ResourceType resource,
            int? level, string phase, string outcome) => logger.Info(
            $"[Resource Farm Fallback] RunId='{runId}', DeviceName='{device}', Resource='{resource}', Level='{level?.ToString() ?? string.Empty}', Phase='{phase}', Outcome='{outcome ?? string.Empty}', Cancellation=false");
    }
}
