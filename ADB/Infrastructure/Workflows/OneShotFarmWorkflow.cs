using ADB_Tool_Automation_Post_FB.Core.Concurrency;
using ADB_Tool_Automation_Post_FB.Core.Diagnostics;
using ADB_Tool_Automation_Post_FB.Core.GameDetection;
using ADB_Tool_Automation_Post_FB.Core.MarchDispatch;
using ADB_Tool_Automation_Post_FB.Core.Navigation;
using ADB_Tool_Automation_Post_FB.Core.ResourcePopup;
using ADB_Tool_Automation_Post_FB.Core.ResourceSearch;
using ADB_Tool_Automation_Post_FB.Core.TeamSelection;
using ADB_Tool_Automation_Post_FB.Core.Vision;
using ADB_Tool_Automation_Post_FB.Core.Workflows;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace ADB_Tool_Automation_Post_FB.Infrastructure.Workflows
{
    public sealed class OneShotFarmWorkflow : IOneShotFarmWorkflow
    {
        private readonly IWorldMapNavigationService navigation;
        private readonly IResourceSearchConfigurationService configuration;
        private readonly IResourceSearchExecutionService search;
        private readonly IResourcePopupVerificationService popup;
        private readonly IOpenTeamSelectionService openTeam;
        private readonly ISelectFarmTeamService selectTeam;
        private readonly IDispatchSelectedTeamService dispatch;
        private readonly IGameStateDetector detector;
        private readonly IDeviceOperationLock operationLock;
        private readonly ResourceSearchConfigurationOptions searchOptions;
        private readonly OneShotFarmWorkflowOptions options;
        private readonly IOneShotFarmDiagnosticService diagnostics;
        private readonly IDiagnosticLogger logger;

        public OneShotFarmWorkflow(IWorldMapNavigationService navigation,
            IResourceSearchConfigurationService configuration, IResourceSearchExecutionService search,
            IResourcePopupVerificationService popup, IOpenTeamSelectionService openTeam,
            ISelectFarmTeamService selectTeam, IDispatchSelectedTeamService dispatch,
            IGameStateDetector detector, IDeviceOperationLock operationLock,
            ResourceSearchConfigurationOptions searchOptions, OneShotFarmWorkflowOptions options,
            IOneShotFarmDiagnosticService diagnostics, IDiagnosticLogger logger)
        {
            this.navigation = navigation ?? throw new ArgumentNullException(nameof(navigation));
            this.configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
            this.search = search ?? throw new ArgumentNullException(nameof(search));
            this.popup = popup ?? throw new ArgumentNullException(nameof(popup));
            this.openTeam = openTeam ?? throw new ArgumentNullException(nameof(openTeam));
            this.selectTeam = selectTeam ?? throw new ArgumentNullException(nameof(selectTeam));
            this.dispatch = dispatch ?? throw new ArgumentNullException(nameof(dispatch));
            this.detector = detector ?? throw new ArgumentNullException(nameof(detector));
            this.operationLock = operationLock ?? throw new ArgumentNullException(nameof(operationLock));
            this.searchOptions = searchOptions ?? throw new ArgumentNullException(nameof(searchOptions));
            this.options = options ?? throw new ArgumentNullException(nameof(options));
            this.diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
            this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<OneShotFarmResult> RunAsync(string deviceName, OneShotFarmRequest request,
            CancellationToken cancellationToken)
        {
            string validation = Validate(deviceName, request);
            if (validation != null) return Empty(deviceName, request, validation);
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                return await operationLock.RunAsync(deviceName.Trim(),
                    token => RunCoreAsync(deviceName.Trim(), request, token), cancellationToken);
            }
            catch (OperationCanceledException)
            {
                return Empty(deviceName, request, "One-shot farm was cancelled while waiting for the workflow lease.", OneShotFarmOutcome.Cancelled);
            }
        }

        private async Task<OneShotFarmResult> RunCoreAsync(string deviceName,
            OneShotFarmRequest request, CancellationToken token)
        {
            var watch = Stopwatch.StartNew(); var steps = new List<OneShotFarmStepResult>();
            OneShotFarmResult result = NewResult(deviceName, request, steps);
            Guid runId = Guid.NewGuid();
            try
            {
                token.ThrowIfCancellationRequested();
                DateTimeOffset started = Start(runId, deviceName, OneShotFarmStep.Preflight);
                GameDetectionResult initial = await detector.DetectAsync(deviceName, token);
                result.InitialState = initial.State; result.FinalState = initial.State;
                if (initial.State == GameState.Unknown || initial.State == GameState.ResourcePopup
                    || initial.State == GameState.TeamSelection)
                {
                    string message = initial.State == GameState.Unknown
                        ? "Initial state is Unknown; no input was sent."
                        : "Return to WorldMap before running the one-shot workflow; no blind Back was sent.";
                    Add(steps, OneShotFarmStep.Preflight, false, started, message, initial.ErrorMessage, initial);
                    return await StopAsync(result, OneShotFarmOutcome.PreconditionFailed, message,
                        initial.ErrorMessage, OneShotFarmStep.Preflight, watch, runId, token);
                }
                AddSuccess(result, steps, OneShotFarmStep.Preflight, started,
                    $"Initial state '{initial.State}' is supported.", initial);

                token.ThrowIfCancellationRequested(); started = Start(runId, deviceName, OneShotFarmStep.EnsureWorldMap);
                NavigationResult ensure = await navigation.EnsureWorldMapAsync(deviceName, token);
                result.NavigationResult = ensure; result.FinalState = ensure.FinalState;
                if (!ensure.Success)
                {
                    Add(steps, OneShotFarmStep.EnsureWorldMap, false, started, ensure.Message, ensure.ErrorMessage, ensure);
                    return await StopAsync(result, OneShotFarmOutcome.WorldMapUnavailable, ensure.Message,
                        ensure.ErrorMessage, OneShotFarmStep.EnsureWorldMap, watch, runId, token);
                }
                GameDetectionResult world = await detector.DetectAsync(deviceName, token);
                result.FinalState = world.State;
                if (world.State != GameState.WorldMap)
                {
                    Add(steps, OneShotFarmStep.EnsureWorldMap, false, started, "WorldMap was not confirmed after navigation.", world.ErrorMessage, ensure);
                    return await StopAsync(result, OneShotFarmOutcome.WorldMapUnavailable,
                        "WorldMap was not confirmed after navigation.", world.ErrorMessage,
                        OneShotFarmStep.EnsureWorldMap, watch, runId, token);
                }
                AddSuccess(result, steps, OneShotFarmStep.EnsureWorldMap, started, ensure.Message, ensure);

                token.ThrowIfCancellationRequested(); started = Start(runId, deviceName, OneShotFarmStep.OpenSearchPanel);
                NavigationResult panel = await navigation.OpenResourceSearchPanelAsync(deviceName, token);
                result.NavigationResult = panel; result.FinalState = panel.FinalState;
                bool panelEvidence = HasResourceSearchPanelEvidence(panel.FinalEvidence);
                if (!panel.Success || panel.FinalState != GameState.ResourceSearchPanel || !panelEvidence)
                {
                    Add(steps, OneShotFarmStep.OpenSearchPanel, false, started, panel.Message, panel.ErrorMessage, panel);
                    return await StopAsync(result, OneShotFarmOutcome.SearchPanelUnavailable, panel.Message,
                        panel.ErrorMessage, OneShotFarmStep.OpenSearchPanel, watch, runId, token);
                }
                AddSuccess(result, steps, OneShotFarmStep.OpenSearchPanel, started, panel.Message, panel);

                var configRequest = new ResourceSearchConfigurationRequest { ResourceType = request.ResourceType, TargetLevel = request.TargetLevel, UnoccupiedOnly = request.UnoccupiedOnly };
                token.ThrowIfCancellationRequested(); started = Start(runId, deviceName, OneShotFarmStep.ConfigureSearch);
                ResourceSearchConfigurationResult configured = await configuration.ConfigureAsync(deviceName, configRequest, token);
                result.ConfigurationResult = configured; result.FinalState = configured.FinalState;
                if (!configured.Success || !configured.ResourceVerified || !configured.LevelVerified || !configured.FilterVerified)
                {
                    Add(steps, OneShotFarmStep.ConfigureSearch, false, started, configured.Message, configured.ErrorMessage, configured);
                    return await StopAsync(result, OneShotFarmOutcome.SearchConfigurationFailed, configured.Message,
                        configured.ErrorMessage, OneShotFarmStep.ConfigureSearch, watch, runId, token);
                }
                AddSuccess(result, steps, OneShotFarmStep.ConfigureSearch, started, configured.Message, configured);

                token.ThrowIfCancellationRequested(); started = Start(runId, deviceName, OneShotFarmStep.ExecuteSearch);
                ResourceSearchExecutionResult searched = await search.ExecuteAsync(deviceName,
                    new ResourceSearchExecutionRequest { Configuration = configRequest, ConfigureBeforeSearch = false }, token);
                result.SearchResult = searched; result.FinalState = searched.FinalState;
                if (searched.Outcome == ResourceSearchOutcome.ResourceNotFound)
                {
                    Add(steps, OneShotFarmStep.ExecuteSearch, false, started, searched.Message, searched.ErrorMessage, searched);
                    result.LastCompletedStep = OneShotFarmStep.ExecuteSearch;
                    return await StopAsync(result, OneShotFarmOutcome.ResourceNotFound, searched.Message,
                        searched.ErrorMessage, OneShotFarmStep.ExecuteSearch, watch, runId, token);
                }
                if (searched.Outcome == ResourceSearchOutcome.Cancelled) throw new OperationCanceledException(token);
                if (!searched.Success || searched.Outcome != ResourceSearchOutcome.ResourceLocated)
                {
                    Add(steps, OneShotFarmStep.ExecuteSearch, false, started, searched.Message, searched.ErrorMessage, searched);
                    return await StopAsync(result, OneShotFarmOutcome.SearchExecutionFailed, searched.Message,
                        searched.ErrorMessage, OneShotFarmStep.ExecuteSearch, watch, runId, token);
                }
                AddSuccess(result, steps, OneShotFarmStep.ExecuteSearch, started, searched.Message, searched);

                token.ThrowIfCancellationRequested(); started = Start(runId, deviceName, OneShotFarmStep.VerifyResourcePopup);
                ResourcePopupVerificationResult verifiedPopup = await popup.VerifyAsync(deviceName, token);
                result.PopupResult = verifiedPopup; result.FinalState = verifiedPopup.FinalState;
                if (verifiedPopup.Outcome == ResourcePopupOutcome.Cancelled) throw new OperationCanceledException(token);
                if (!verifiedPopup.Success || verifiedPopup.Outcome != ResourcePopupOutcome.ResourcePopupReady
                    || !verifiedPopup.PopupAnchorVerified || !verifiedPopup.IronResourceVerified || !verifiedPopup.GatherButtonVerified)
                {
                    Add(steps, OneShotFarmStep.VerifyResourcePopup, false, started, verifiedPopup.Message, verifiedPopup.ErrorMessage, verifiedPopup);
                    return await StopAsync(result, OneShotFarmOutcome.ResourcePopupNotReady, verifiedPopup.Message,
                        verifiedPopup.ErrorMessage, OneShotFarmStep.VerifyResourcePopup, watch, runId, token);
                }
                AddSuccess(result, steps, OneShotFarmStep.VerifyResourcePopup, started, verifiedPopup.Message, verifiedPopup);

                token.ThrowIfCancellationRequested(); started = Start(runId, deviceName, OneShotFarmStep.OpenTeamSelection);
                OpenTeamSelectionResult opened = await openTeam.OpenAsync(deviceName, token);
                result.OpenTeamResult = opened; result.FinalState = opened.FinalState;
                if (opened.Outcome == OpenTeamSelectionOutcome.Cancelled) throw new OperationCanceledException(token);
                if (!opened.Success || opened.FinalState != GameState.TeamSelection || !opened.TeamSelectionVerified || !opened.TeamSelectionReady)
                {
                    OneShotFarmOutcome outcome = opened.TeamSelectionVerified ? OneShotFarmOutcome.TeamSelectionNotReady : OneShotFarmOutcome.TeamSelectionFailed;
                    Add(steps, OneShotFarmStep.OpenTeamSelection, false, started, opened.Message, opened.ErrorMessage, opened);
                    return await StopAsync(result, outcome, opened.Message, opened.ErrorMessage,
                        OneShotFarmStep.OpenTeamSelection, watch, runId, token);
                }
                AddSuccess(result, steps, OneShotFarmStep.OpenTeamSelection, started, opened.Message, opened);

                token.ThrowIfCancellationRequested(); started = Start(runId, deviceName, OneShotFarmStep.SelectTeam);
                SelectFarmTeamResult selected = await selectTeam.SelectAsync(deviceName, new TeamSelectionRequest
                { AllowedTeams = request.AllowedTeams, Priority = request.TeamPriority, AllowTeam1 = request.AllowTeam1 }, token);
                result.SelectTeamResult = selected; result.FinalState = selected.FinalState;
                if (selected.Outcome == SelectFarmTeamOutcome.Cancelled) throw new OperationCanceledException(token);
                if (selected.Outcome == SelectFarmTeamOutcome.NoEligibleTeam)
                {
                    Add(steps, OneShotFarmStep.SelectTeam, false, started, selected.Message, selected.ErrorMessage, selected);
                    return await StopAsync(result, OneShotFarmOutcome.NoEligibleTeam, selected.Message,
                        selected.ErrorMessage, OneShotFarmStep.SelectTeam, watch, runId, token);
                }
                if (!selected.Success || !selected.SelectedTeam.HasValue || !selected.SelectedStateVerified)
                {
                    Add(steps, OneShotFarmStep.SelectTeam, false, started, selected.Message, selected.ErrorMessage, selected);
                    return await StopAsync(result, OneShotFarmOutcome.TeamSelectionFailed, selected.Message,
                        selected.ErrorMessage, OneShotFarmStep.SelectTeam, watch, runId, token);
                }
                result.SelectedTeam = selected.SelectedTeam;
                AddSuccess(result, steps, OneShotFarmStep.SelectTeam, started, selected.Message, selected);

                token.ThrowIfCancellationRequested(); started = Start(runId, deviceName, OneShotFarmStep.DispatchTeam);
                DispatchMarchResult dispatched = await dispatch.DispatchAsync(deviceName, new DispatchMarchRequest
                { ExpectedTeam = selected.SelectedTeam.Value, RequireExpectedTeamSelected = true, AllowStructuralVerificationFallback = true }, token);
                result.DispatchResult = dispatched; result.FinalState = dispatched.FinalState;
                if (dispatched.Outcome == DispatchMarchOutcome.Cancelled) throw new OperationCanceledException(token);
                bool dispatchSuccess = dispatched.Success && dispatched.MarchStartedVerified
                    && (dispatched.Outcome == DispatchMarchOutcome.MarchStarted || dispatched.Outcome == DispatchMarchOutcome.AlreadyMarching);
                if (!dispatchSuccess)
                {
                    Add(steps, OneShotFarmStep.DispatchTeam, false, started, dispatched.Message, dispatched.ErrorMessage, dispatched);
                    return await StopAsync(result, OneShotFarmOutcome.TeamDispatchFailed, dispatched.Message,
                        dispatched.ErrorMessage, OneShotFarmStep.DispatchTeam, watch, runId, token);
                }
                result.DispatchedTeam = dispatched.DispatchedTeam ?? selected.SelectedTeam;
                AddSuccess(result, steps, OneShotFarmStep.DispatchTeam, started, dispatched.Message, dispatched);

                token.ThrowIfCancellationRequested(); started = Start(runId, deviceName, OneShotFarmStep.FinalVerification);
                GameDetectionResult final = await detector.DetectAsync(deviceName, token);
                result.FinalState = final.State;
                AddSuccess(result, steps, OneShotFarmStep.FinalVerification, started,
                    "March was already verified by DispatchSelectedTeamService; no further input was sent.", final);
                started = Start(runId, deviceName, OneShotFarmStep.Completed);
                AddSuccess(result, steps, OneShotFarmStep.Completed, started, "One-shot farm completed.", dispatched);
                result.Outcome = OneShotFarmOutcome.MarchStarted; result.Success = true;
                result.Message = "One selected team was dispatched and march start was verified.";
                result.Duration = watch.Elapsed;
                if (options.SaveSuccessScreenshot) result.DiagnosticScreenshotPath = await TryDiagnosticAsync(deviceName, OneShotFarmStep.Completed, result.Outcome, token);
                LogEnd(runId, deviceName, result);
                return result;
            }
            catch (OperationCanceledException)
            {
                result.Outcome = OneShotFarmOutcome.Cancelled; result.Success = false;
                result.Message = "One-shot farm was cancelled."; result.Duration = watch.Elapsed;
                LogEnd(runId, deviceName, result); return result;
            }
            catch (Exception exception)
            {
                logger.Error($"[OneShotFarm] RunId='{runId}', DeviceName='{deviceName}', Error='{exception.Message}'", exception);
                result.Outcome = OneShotFarmOutcome.Failed; result.Success = false;
                result.Message = "One-shot farm failed."; result.ErrorMessage = exception.Message; result.Duration = watch.Elapsed;
                result.DiagnosticScreenshotPath = await TryDiagnosticAsync(deviceName, OneShotFarmStep.Preflight, result.Outcome, token);
                return result;
            }
        }

        private string Validate(string deviceName, OneShotFarmRequest request)
        {
            if (string.IsNullOrWhiteSpace(deviceName)) return "LDPlayer device name is required.";
            if (request == null) return "One-shot farm request is required.";
            if (request.ResourceType != ResourceType.Iron) return "Only Iron is supported by the MVP.";
            if (request.TargetLevel < searchOptions.MinimumLevel || request.TargetLevel > searchOptions.MaximumLevel) return $"TargetLevel must be between {searchOptions.MinimumLevel} and {searchOptions.MaximumLevel}.";
            if (request.AllowedTeams == null || request.AllowedTeams.Count == 0) return "AllowedTeams cannot be empty.";
            if (request.TeamPriority == null || request.TeamPriority.Count == 0) return "TeamPriority cannot be empty.";
            if (request.TeamPriority.Distinct().Count() != request.TeamPriority.Count) return "TeamPriority cannot contain duplicates.";
            if (request.TeamPriority.Any(x => !request.AllowedTeams.Contains(x))) return "TeamPriority must be a subset of AllowedTeams.";
            if (!request.AllowTeam1 && (request.AllowedTeams.Contains(TeamNumber.Team1) || request.TeamPriority.Contains(TeamNumber.Team1))) return "Team1 is not allowed.";
            return null;
        }

        private static bool HasResourceSearchPanelEvidence(
            IReadOnlyList<GameDetectionEvidence> evidence)
        {
            if (evidence == null) return false;
            bool searchButton = evidence.Any(x => x.TemplateId == TemplateId.SearchButtonEnabled && x.Found);
            bool panelChrome = evidence.Any(x => x.Found
                && (x.TemplateId == TemplateId.ResourceSearchPanelAnchor
                    || x.TemplateId == TemplateId.LevelMinusButton
                    || x.TemplateId == TemplateId.ResourceTabSelected
                    || x.TemplateId == TemplateId.ResourceTabUnselected));
            return searchButton && panelChrome;
        }

        private async Task<OneShotFarmResult> StopAsync(OneShotFarmResult result,
            OneShotFarmOutcome outcome, string message, string error, OneShotFarmStep failedStep,
            Stopwatch watch, Guid runId, CancellationToken token)
        {
            result.Outcome = outcome; result.Success = false; result.Message = message;
            result.ErrorMessage = error; result.Duration = watch.Elapsed;
            if (options.SaveStepFailureScreenshots)
            {
                string path = await TryDiagnosticAsync(result.DeviceName, failedStep, outcome, token);
                result.DiagnosticScreenshotPath = path;
                OneShotFarmStepResult step = result.Steps.LastOrDefault();
                if (step != null && step.Step == failedStep) step.DiagnosticScreenshotPath = path;
            }
            LogEnd(runId, result.DeviceName, result); return result;
        }
        private async Task<string> TryDiagnosticAsync(string device, OneShotFarmStep step, OneShotFarmOutcome outcome, CancellationToken token)
        { try { return await diagnostics.CaptureAsync(device, step, outcome, token); } catch (OperationCanceledException) { throw; } catch (Exception ex) { logger.Error($"[OneShotFarm] DiagnosticError='{ex.Message}'", ex); return null; } }
        private DateTimeOffset Start(Guid id, string device, OneShotFarmStep step) { DateTimeOffset now = DateTimeOffset.UtcNow; logger.Info($"[OneShotFarm] RunId='{id}', DeviceName='{device}', Step='{step}', StartedAt='{now:O}'"); return now; }
        private void AddSuccess(OneShotFarmResult result, List<OneShotFarmStepResult> steps, OneShotFarmStep step, DateTimeOffset started, string message, object detail) { Add(steps, step, true, started, message, null, detail); result.LastCompletedStep = step; }
        private static void Add(List<OneShotFarmStepResult> steps, OneShotFarmStep step, bool success, DateTimeOffset started, string message, string error, object detail) { DateTimeOffset end = DateTimeOffset.UtcNow; steps.Add(new OneShotFarmStepResult { Step = step, Success = success, StartedAt = started, CompletedAt = end, Duration = end - started, Message = message, ErrorMessage = error, Detail = detail }); }
        private void LogEnd(Guid id, string device, OneShotFarmResult result) => logger.Info($"[OneShotFarm] RunId='{id}', DeviceName='{device}', LastCompletedStep='{result.LastCompletedStep}', SelectedTeam='{result.SelectedTeam}', Outcome='{result.Outcome}', DurationMs={result.Duration.TotalMilliseconds:F0}, Cancellation={result.Outcome == OneShotFarmOutcome.Cancelled}, Error='{result.ErrorMessage ?? string.Empty}'");
        private static OneShotFarmResult NewResult(string device, OneShotFarmRequest request, IReadOnlyList<OneShotFarmStepResult> steps) => new OneShotFarmResult { DeviceName = device, RequestedResource = request.ResourceType, RequestedLevel = request.TargetLevel, RequestedUnoccupiedOnly = request.UnoccupiedOnly, InitialState = GameState.Unknown, FinalState = GameState.Unknown, LastCompletedStep = OneShotFarmStep.Preflight, Steps = steps };
        private static OneShotFarmResult Empty(string device, OneShotFarmRequest request, string error, OneShotFarmOutcome outcome = OneShotFarmOutcome.PreconditionFailed) => new OneShotFarmResult { Outcome = outcome, Success = false, DeviceName = device, RequestedResource = request == null ? ResourceType.Iron : request.ResourceType, RequestedLevel = request == null ? 7 : request.TargetLevel, RequestedUnoccupiedOnly = request == null || request.UnoccupiedOnly, InitialState = GameState.Unknown, FinalState = GameState.Unknown, LastCompletedStep = OneShotFarmStep.Preflight, Message = error, ErrorMessage = error, Steps = new OneShotFarmStepResult[0] };
    }
}
