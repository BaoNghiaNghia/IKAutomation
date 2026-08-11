using ADB_Tool_Automation_Post_FB.Core.Workflows;
using ADB_Tool_Automation_Post_FB.Core.TeamSelection;
using ADB_Tool_Automation_Post_FB.Core.Diagnostics;
using ADB_Tool_Automation_Post_FB.Core.Concurrency;
using ADB_Tool_Automation_Post_FB.Infrastructure.Concurrency;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace ADB_Tool_Automation_Post_FB.Infrastructure.Workflows
{
    public sealed class MultiDeviceOneShotFarmRunner : IMultiDeviceOneShotFarmRunner
    {
        public const int MaximumSupportedConcurrency = 25;

        private readonly Func<IOneShotFarmWorkflow> workflowFactory;
        private readonly Func<IWorldMapTeamAvailabilityService> availabilityFactory;
        private readonly int maximumConcurrency;
        private readonly SemaphoreSlim executionGate;
        private readonly IAdaptiveConcurrencyGate adaptiveConcurrencyGate;
        private readonly Action<string> infoLogger;
        private readonly MultiDeviceOneShotFarmRunnerOptions options;
        private readonly Func<int, CancellationToken, Task> requeueDelayAsync;
        private readonly IDeviceAutomationOwnershipService ownershipService;

        public MultiDeviceOneShotFarmRunner(Func<IOneShotFarmWorkflow> workflowFactory,
            int maximumConcurrency = MaximumSupportedConcurrency)
            : this(workflowFactory, null, maximumConcurrency)
        {
        }

        public MultiDeviceOneShotFarmRunner(Func<IOneShotFarmWorkflow> workflowFactory,
            Func<IWorldMapTeamAvailabilityService> availabilityFactory,
            int maximumConcurrency = MaximumSupportedConcurrency)
            : this(workflowFactory, availabilityFactory, maximumConcurrency, null)
        {
        }

        public MultiDeviceOneShotFarmRunner(Func<IOneShotFarmWorkflow> workflowFactory,
            Func<IWorldMapTeamAvailabilityService> availabilityFactory,
            int maximumConcurrency, IAdaptiveConcurrencyGate adaptiveConcurrencyGate,
            Action<string> infoLogger = null,
            MultiDeviceOneShotFarmRunnerOptions options = null,
            Func<int, CancellationToken, Task> requeueDelayAsync = null,
            IDeviceAutomationOwnershipService ownershipService = null)
        {
            this.workflowFactory = workflowFactory
                ?? throw new ArgumentNullException(nameof(workflowFactory));
            if (maximumConcurrency < 1 || maximumConcurrency > MaximumSupportedConcurrency)
                throw new ArgumentOutOfRangeException(nameof(maximumConcurrency));
            this.maximumConcurrency = maximumConcurrency;
            this.availabilityFactory = availabilityFactory;
            this.adaptiveConcurrencyGate = adaptiveConcurrencyGate;
            this.infoLogger = infoLogger ?? (message => Trace.TraceInformation(message));
            this.options = options ?? new MultiDeviceOneShotFarmRunnerOptions();
            this.requeueDelayAsync = requeueDelayAsync
                ?? ((delayMs, token) => Task.Delay(delayMs, token));
            this.ownershipService = ownershipService
                ?? DeviceAutomationOwnershipService.Shared;
            executionGate = new SemaphoreSlim(maximumConcurrency, maximumConcurrency);
        }

        public async Task<MultiDeviceOneShotFarmResult> RunAsync(
            IReadOnlyList<string> deviceNames, OneShotFarmRequest request,
            IProgress<MultiDeviceOneShotFarmProgress> progress,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (request == null) throw new ArgumentNullException(nameof(request));
            string[] devices = (deviceNames ?? new string[0])
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Select(name => name.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (devices.Length == 0)
                throw new ArgumentException(VietnameseUserMessageLocalizer.Default.Get(
                    UiMessageKey.NoDeviceSelected),
                    nameof(deviceNames));
            if (string.IsNullOrWhiteSpace(request.RunId))
                request.RunId = Guid.NewGuid().ToString();
            if (string.IsNullOrWhiteSpace(request.FarmRunId))
                request.FarmRunId = request.RunId;

            // Each device is admitted as soon as its own preflight finishes. This avoids
            // a slow/offline device holding the entire selected set behind Task.WhenAll.
            Task<MultiDeviceOneShotFarmItemResult>[] tasks = devices.Select((device, index) =>
                Task.Run(() => RunDevicePipelineAsync(device, index, request, progress,
                    cancellationToken))).ToArray();
            MultiDeviceOneShotFarmItemResult[] results = await Task.WhenAll(tasks);
            results = results
                .OrderBy(item => Array.FindIndex(devices, device =>
                    string.Equals(device, item.DeviceName, StringComparison.OrdinalIgnoreCase)))
                .ToArray();
            return new MultiDeviceOneShotFarmResult
            {
                Devices = results,
                MaximumConcurrency = maximumConcurrency,
                AdaptiveConcurrencyEnabled = adaptiveConcurrencyGate != null,
                FinalConcurrencyLimit = GetConcurrencySnapshot().CurrentLimit,
                TotalSelected = devices.Length,
                Active = 0,
                Queued = 0,
                WaitingForTeam = results.Count(item => item.Stage
                    == MultiDeviceOneShotFarmStage.WaitingForReadyTeam),
                ScheduledForNextCheck = results.Count(item => item.Stage
                    == MultiDeviceOneShotFarmStage.WaitingForReadyTeam),
                Failed = results.Count(item => item.Stage == MultiDeviceOneShotFarmStage.Failed),
                WasCancelled = cancellationToken.IsCancellationRequested
            };
        }

        private async Task<MultiDeviceOneShotFarmItemResult> RunDevicePipelineAsync(
            string deviceName, int deviceIndex, OneShotFarmRequest sourceRequest,
            IProgress<MultiDeviceOneShotFarmProgress> progress,
            CancellationToken cancellationToken)
        {
            var state = new DeviceExecutionState(deviceName, deviceIndex);
            try
            {
                while (true)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    state.Iterations++;
                    OneShotFarmRequest quantumRequest = CloneRequest(sourceRequest);
                    quantumRequest.RunId = state.RunId;
                    quantumRequest.CooperativeDispatch = sourceRequest.RunUntilNoReadyTeams;
                    quantumRequest.CycleDispatchedTeams = state.DispatchedTeams.ToArray();
                    quantumRequest.CycleDispatchedResources = state.DispatchedResources.ToArray();

                    MultiDeviceOneShotFarmItemResult item = await RunAfterPreflightAsync(
                        deviceName, deviceIndex, quantumRequest, progress, cancellationToken);
                    state.LastResult = item;
                    state.LastKnownGameState = item.Result?.FinalState
                        ?? ADB_Tool_Automation_Post_FB.Core.GameDetection.GameState.Unknown;
                    state.AnotherTeamMayBeReady = item.Result?.RequeueRequested == true;
                    state.PreflightDurationMs += item.PreflightDurationMs;
                    state.GameplayLeaseWaitMs += item.GameplayLeaseWaitMs;
                    state.GameplayLeaseHeldMs += item.GameplayLeaseHeldMs;
                    state.PreflightTimeoutCount += item.PreflightTimeoutCount;
                    state.PreflightFailureCount += item.PreflightFailureCount;
                    state.TeamsDispatchedPerLease = item.TeamsDispatchedPerLease;

                    int priorDispatchCount = state.DispatchedTeams.Count;
                    MergeDispatches(state, item.Result);
                    item.TeamsDispatchedPerDeviceCycle = state.DispatchedTeams.Count;
                    item.DeviceRequeueCount = state.RequeueCount;

                    bool shouldRequeue = item.Stage == MultiDeviceOneShotFarmStage.Completed
                        && item.Result?.RequeueRequested == true;
                    if (!shouldRequeue)
                        return ApplyAggregateMetrics(item, state);

                    if (state.DispatchedTeams.Count == priorDispatchCount)
                    {
                        state.ConsecutiveNoProgress++;
                        state.RetryCount++;
                    }
                    else
                        state.ConsecutiveNoProgress = 0;

                    bool safetyLimitReached = state.DispatchedTeams.Count
                            >= options.MaxTeamsPerDeviceCycle
                        || state.Iterations >= options.MaxDeviceIterationsPerCycle
                        || state.ConsecutiveNoProgress
                            >= options.MaxConsecutiveNoProgressAttempts;
                    if (safetyLimitReached)
                    {
                        item.Result.RequeueRequested = false;
                        item.Result.Message = "Đã dừng xếp lại lượt do đạt giới hạn an toàn của chu kỳ.";
                        infoLogger($"[Farm Scheduling] DeviceName='{deviceName}', "
                            + $"SafetyLimitReached=true, Iterations={state.Iterations}, "
                            + $"TeamsDispatched={state.DispatchedTeams.Count}, "
                            + $"NoProgress={state.ConsecutiveNoProgress}");
                        return ApplyAggregateMetrics(item, state);
                    }

                    state.RequeueCount++;
                    state.NextEligibleExecutionTime = DateTimeOffset.UtcNow
                        .AddMilliseconds(options.DeviceRequeueDelayMs);
                    Report(progress, deviceName, MultiDeviceOneShotFarmStage.Requeued,
                        null, "Đã điều một đội; thiết bị đã nhường lượt và sẽ tiếp tục sau.",
                        state);
                    await requeueDelayAsync(options.DeviceRequeueDelayMs, cancellationToken);
                }
            }
            catch (OperationCanceledException)
            {
                var cancelled = new MultiDeviceOneShotFarmItemResult
                {
                    DeviceName = deviceName,
                    Stage = MultiDeviceOneShotFarmStage.Cancelled,
                    Result = state.LastResult?.Result
                };
                Report(progress, deviceName, MultiDeviceOneShotFarmStage.Cancelled,
                    null, "Đã hủy lượt xử lý đang chờ.", state);
                return ApplyAggregateMetrics(cancelled, state);
            }
        }

        private static void MergeDispatches(DeviceExecutionState state,
            OneShotFarmResult result)
        {
            foreach (TeamNumber team in result?.BatchDispatchedTeams ?? new TeamNumber[0])
                if (!state.DispatchedTeams.Contains(team)) state.DispatchedTeams.Add(team);
            foreach (ADB_Tool_Automation_Post_FB.Core.ResourceSearch.ResourceType resource
                in result?.DispatchedResources
                    ?? new ADB_Tool_Automation_Post_FB.Core.ResourceSearch.ResourceType[0])
                state.DispatchedResources.Add(resource);
        }

        private static MultiDeviceOneShotFarmItemResult ApplyAggregateMetrics(
            MultiDeviceOneShotFarmItemResult item, DeviceExecutionState state)
        {
            item.PreflightDurationMs = state.PreflightDurationMs;
            item.GameplayLeaseWaitMs = state.GameplayLeaseWaitMs;
            item.GameplayLeaseHeldMs = state.GameplayLeaseHeldMs;
            item.TeamsDispatchedPerDeviceCycle = state.DispatchedTeams.Count;
            item.DeviceRequeueCount = state.RequeueCount;
            item.PreflightTimeoutCount = state.PreflightTimeoutCount;
            item.PreflightFailureCount = state.PreflightFailureCount;
            return item;
        }

        private async Task<MultiDeviceOneShotFarmItemResult> RunAfterPreflightAsync(
            string deviceName, int deviceIndex, OneShotFarmRequest request,
            IProgress<MultiDeviceOneShotFarmProgress> progress,
            CancellationToken cancellationToken)
        {
            IAdaptiveConcurrencyLease adaptiveLease = null;
            IDeviceAutomationLease automationLease = null;
            bool entered = false;
            int resolvedDeviceIndex = ResolveDeviceIndex(deviceName, deviceIndex);
            var leaseWait = Stopwatch.StartNew();
            Stopwatch leaseHeld = null;
            try
            {
                Report(progress, deviceName, MultiDeviceOneShotFarmStage.Queued, null,
                    VietnameseUserMessageLocalizer.Default.Get(UiMessageKey.WaitingForExecutionSlot));
                if (adaptiveConcurrencyGate == null)
                {
                    await executionGate.WaitAsync(cancellationToken);
                    entered = true;
                }
                else if (adaptiveConcurrencyGate is IAdaptiveConcurrencyAdmissionGate admissionGate)
                {
                    adaptiveLease = await admissionGate.AcquireAsync(deviceName,
                        AdaptiveOperationKind.Automation, new AdaptiveAdmissionRequest
                        {
                            ApplyStartupStagger = true,
                            StaggerKey = request.RunId,
                            DeviceIndex = resolvedDeviceIndex,
                            ExecutionPhase = AdaptiveExecutionPhase.Preflight
                        }, cancellationToken);
                }
                else
                {
                    adaptiveLease = await adaptiveConcurrencyGate.AcquireAsync(deviceName,
                        AdaptiveOperationKind.Automation, cancellationToken);
                }
                leaseWait.Stop();
                RuntimePressureMetrics.ReportGameplayLeaseWait(leaseWait.ElapsedMilliseconds);
                leaseHeld = Stopwatch.StartNew();

                PreflightResult preflight;
                using (ScreenshotCaptureContext.Push("Preflight"))
                    preflight = availabilityFactory == null
                        ? new PreflightResult { DeviceName = deviceName, Success = true }
                        : await RunPreflightAsync(deviceName, request, progress, cancellationToken);
                if (!preflight.Success)
                {
                    preflight.ItemResult.GameplayLeaseWaitMs = leaseWait.ElapsedMilliseconds;
                    preflight.ItemResult.GameplayLeaseHeldMs = leaseHeld.ElapsedMilliseconds;
                    preflight.ItemResult.PreflightDurationMs = preflight.DurationMs;
                    preflight.ItemResult.PreflightTimeoutCount = preflight.TimedOut ? 1 : 0;
                    preflight.ItemResult.PreflightFailureCount = preflight.TimedOut ? 0 : 1;
                    return preflight.ItemResult;
                }

                if (availabilityFactory != null
                    && request.ReadyTeamWaitMode == ReadyTeamWaitMode.YieldToSupervisor
                    && !HasEligibleReadyTeam(preflight.Availability, request))
                {
                    MultiDeviceOneShotFarmItemResult waiting = WaitingForReadyTeam(
                        preflight.DeviceName, preflight.Availability, request);
                    waiting.GameplayLeaseWaitMs = leaseWait.ElapsedMilliseconds;
                    waiting.GameplayLeaseHeldMs = leaseHeld.ElapsedMilliseconds;
                    waiting.PreflightDurationMs = preflight.DurationMs;
                    return waiting;
                }

                infoLogger($"[Adaptive Admission] DeviceName='{deviceName}', "
                    + $"DeviceIndex={resolvedDeviceIndex}, ExecutionPhase='Gameplay', LeaseReused=true");
                Report(progress, deviceName, MultiDeviceOneShotFarmStage.ReadyForGameplay,
                    null, "Thiết bị đã sẵn sàng để điều đội.");
                if (!ownershipService.TryAcquire(deviceName, DeviceAutomationOwner.Farm,
                    out automationLease))
                {
                    DeviceAutomationOwner currentOwner = ownershipService.GetOwner(deviceName);
                    string ownershipMessage = currentOwner == DeviceAutomationOwner.Fruit2048
                        ? "Thiết bị đang được sử dụng bởi Fruit 2048."
                        : "Thiết bị đang được sử dụng bởi một tác vụ khác.";
                    Report(progress, deviceName, MultiDeviceOneShotFarmStage.Failed,
                        null, ownershipMessage);
                    return new MultiDeviceOneShotFarmItemResult
                    {
                        DeviceName = deviceName,
                        Stage = MultiDeviceOneShotFarmStage.Failed,
                        ErrorMessage = ownershipMessage,
                        Result = new OneShotFarmResult
                        {
                            DeviceName = deviceName,
                            Outcome = OneShotFarmOutcome.PreconditionFailed,
                            Success = false,
                            Message = ownershipMessage,
                            ErrorMessage = ownershipMessage
                        }
                    };
                }
                MultiDeviceOneShotFarmItemResult item;
                using (ScreenshotCaptureContext.Push("Gameplay"))
                    item = await RunDeviceAsync(preflight.DeviceName,
                        CreatePreflightRequest(request, preflight.Availability), progress,
                        cancellationToken);
                item.GameplayLeaseWaitMs = leaseWait.ElapsedMilliseconds;
                item.GameplayLeaseHeldMs = leaseHeld.ElapsedMilliseconds;
                item.PreflightDurationMs = preflight.DurationMs;
                item.TeamsDispatchedPerLease = item.Result?.Success == true
                    && item.Result.DispatchedTeam.HasValue ? 1 : 0;
                infoLogger($"[Farm Scheduling] DeviceName='{deviceName}', "
                    + $"TeamsDispatchedPerLease={item.TeamsDispatchedPerLease}, "
                    + $"PreflightDurationMs={item.PreflightDurationMs}");
                return item;
            }
            finally
            {
                leaseWait.Stop();
                leaseHeld?.Stop();
                automationLease?.Dispose();
                adaptiveLease?.Dispose();
                if (entered) executionGate.Release();
                infoLogger($"[Farm Scheduling] DeviceName='{deviceName}', "
                    + $"GameplayLeaseWaitMs={leaseWait.ElapsedMilliseconds}, "
                    + $"GameplayLeaseHeldMs={leaseHeld?.ElapsedMilliseconds ?? 0}");
            }
        }

        private async Task<PreflightResult> RunPreflightAsync(string deviceName,
            OneShotFarmRequest request, IProgress<MultiDeviceOneShotFarmProgress> progress,
            CancellationToken cancellationToken)
        {
            Report(progress, deviceName, MultiDeviceOneShotFarmStage.Preflight,
                null, VietnameseUserMessageLocalizer.Default.Get(UiMessageKey.CheckingPreflight));
            bool succeeded = false;
            bool technicalFailure = false;
            var stopwatch = Stopwatch.StartNew();
            CancellationTokenSource timeoutSource = null;
            try
            {
                timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeoutSource.CancelAfter(options.PreflightTimeoutMs);
                IWorldMapTeamAvailabilityService service = availabilityFactory();
                if (service == null)
                    throw new InvalidOperationException(VietnameseUserMessageLocalizer.Default.Get(
                        UiMessageKey.PreflightFactoryReturnedNull));
                WorldMapTeamAvailabilityResult availability = await service.CheckAsync(
                    deviceName, timeoutSource.Token);
                if (availability == null || !availability.Success)
                {
                    technicalFailure = true;
                    string message = availability?.Message
                        ?? VietnameseUserMessageLocalizer.Default.Get(
                            UiMessageKey.PreflightReturnedNoResult);
                    Report(progress, deviceName, MultiDeviceOneShotFarmStage.PreflightFailed,
                        null, message);
                    PreflightResult failed = FailedPreflight(deviceName, message,
                        availability?.ErrorMessage);
                    failed.DurationMs = stopwatch.ElapsedMilliseconds;
                    return failed;
                }

                TeamNumber[] eligible = (availability.ReadyTeams ?? new TeamNumber[0])
                    .Where(team => (request.AllowedTeams ?? new TeamNumber[0]).Contains(team))
                    .Distinct().ToArray();
                MultiDeviceOneShotFarmStage stage = eligible.Length > 0
                    ? MultiDeviceOneShotFarmStage.ReadyForGameplay
                    : MultiDeviceOneShotFarmStage.WaitingForReadyTeam;
                string status = eligible.Length > 0
                    ? VietnameseUserMessageLocalizer.Default.Format(
                        UiMessageKey.PreflightEligibleTeams, string.Join(", ", eligible))
                    : VietnameseUserMessageLocalizer.Default.Get(UiMessageKey.PreflightNoReadyTeam);
                Report(progress, deviceName, stage, null, status);
                succeeded = true;
                return new PreflightResult
                {
                    DeviceName = deviceName,
                    Success = true,
                    Availability = availability,
                    DurationMs = stopwatch.ElapsedMilliseconds
                };
            }
            catch (OperationCanceledException exception)
                when (!cancellationToken.IsCancellationRequested
                    && timeoutSource != null && timeoutSource.IsCancellationRequested)
            {
                technicalFailure = true;
                const string message = "Kiểm tra thiết bị quá thời gian cho phép; thiết bị này đã nhường lượt.";
                infoLogger($"[Farm Preflight] DeviceName='{deviceName}', TimedOut=true, "
                    + $"DurationMs={stopwatch.ElapsedMilliseconds}, Error='{exception.Message}'");
                Report(progress, deviceName, MultiDeviceOneShotFarmStage.PreflightFailed,
                    null, message);
                PreflightResult failed = FailedPreflight(deviceName, message,
                    exception.ToString());
                failed.TimedOut = true;
                failed.DurationMs = stopwatch.ElapsedMilliseconds;
                return failed;
            }
            catch (OperationCanceledException)
            {
                Report(progress, deviceName, MultiDeviceOneShotFarmStage.Cancelled,
                    null, VietnameseUserMessageLocalizer.Default.Get(UiMessageKey.PreflightCancelled));
                return new PreflightResult
                {
                    DeviceName = deviceName,
                    Success = false,
                    DurationMs = stopwatch.ElapsedMilliseconds,
                    ItemResult = new MultiDeviceOneShotFarmItemResult
                    {
                        DeviceName = deviceName,
                        Stage = MultiDeviceOneShotFarmStage.Cancelled
                    }
                };
            }
            catch (Exception exception)
            {
                technicalFailure = true;
                UserErrorPresentation error = UserErrorPresenter.Present(exception);
                Report(progress, deviceName, MultiDeviceOneShotFarmStage.PreflightFailed,
                    null, error.UserMessage);
                PreflightResult failed = FailedPreflight(deviceName, error.UserMessage,
                    error.TechnicalDetails);
                failed.DurationMs = stopwatch.ElapsedMilliseconds;
                return failed;
            }
            finally
            {
                timeoutSource?.Dispose();
                stopwatch.Stop();
                adaptiveConcurrencyGate?.Report(new AdaptiveConcurrencyObservation
                {
                    DeviceName = deviceName,
                    Success = succeeded,
                    TechnicalFailure = technicalFailure,
                    DurationMs = stopwatch.ElapsedMilliseconds,
                    UseDurationAsPressure = true
                });
            }
        }

        private static PreflightResult FailedPreflight(string deviceName,
            string message, string error) => new PreflightResult
            {
                DeviceName = deviceName,
                Success = false,
                ItemResult = new MultiDeviceOneShotFarmItemResult
                {
                    DeviceName = deviceName,
                    Stage = MultiDeviceOneShotFarmStage.Failed,
                    ErrorMessage = error ?? message,
                    Result = new OneShotFarmResult
                    {
                        DeviceName = deviceName,
                        Success = false,
                        Outcome = OneShotFarmOutcome.TeamAvailabilityCheckFailed,
                        LastCompletedStep = OneShotFarmStep.Preflight,
                        Message = message,
                        ErrorMessage = error ?? message,
                        AttemptedLevels = new int[0],
                        AttemptedResources = new ADB_Tool_Automation_Post_FB.Core.ResourceSearch.ResourceType[0],
                        MissingRuntimeTemplates = new MissingRuntimeTemplate[0],
                        StorageFullResources = new ADB_Tool_Automation_Post_FB.Core.ResourceSearch.ResourceType[0],
                        LevelsExhaustedResources = new ADB_Tool_Automation_Post_FB.Core.ResourceSearch.ResourceType[0],
                        Steps = new OneShotFarmStepResult[0]
                    }
                }
            };

        private static bool HasEligibleReadyTeam(WorldMapTeamAvailabilityResult availability,
            OneShotFarmRequest request)
        {
            return availability != null && (availability.ReadyTeams ?? new TeamNumber[0])
                .Any(team => (request.AllowedTeams ?? new TeamNumber[0]).Contains(team));
        }

        private static MultiDeviceOneShotFarmItemResult WaitingForReadyTeam(string deviceName,
            WorldMapTeamAvailabilityResult availability, OneShotFarmRequest request)
        {
            int delayMs = request?.ReadyTeamOptions?.CheckIntervalMs ?? 120000;
            DateTimeOffset nextCheckAt = DateTimeOffset.UtcNow.AddMilliseconds(delayMs);
            string message = VietnameseUserMessageLocalizer.Default.Get(
                UiMessageKey.YieldedUntilScheduledCheck);
            return new MultiDeviceOneShotFarmItemResult
            {
                DeviceName = deviceName,
                Stage = MultiDeviceOneShotFarmStage.WaitingForReadyTeam,
                Result = new OneShotFarmResult
                {
                    DeviceName = deviceName,
                    Success = false,
                    Outcome = OneShotFarmOutcome.WaitingForReadyTeam,
                    Message = message,
                    NextCheckAt = nextCheckAt,
                    DetectedTeams = availability?.AvailableTeams ?? new TeamNumber[0],
                    ReadyTeams = availability?.ReadyTeams ?? new TeamNumber[0],
                    AttemptedLevels = new int[0],
                    AttemptedResources = new ADB_Tool_Automation_Post_FB.Core.ResourceSearch.ResourceType[0],
                    MissingRuntimeTemplates = new MissingRuntimeTemplate[0],
                    StorageFullResources = new ADB_Tool_Automation_Post_FB.Core.ResourceSearch.ResourceType[0],
                    LevelsExhaustedResources = new ADB_Tool_Automation_Post_FB.Core.ResourceSearch.ResourceType[0],
                    Steps = new OneShotFarmStepResult[0]
                }
            };
        }

        private static OneShotFarmRequest CreatePreflightRequest(
            OneShotFarmRequest source, WorldMapTeamAvailabilityResult availability)
        {
            OneShotFarmRequest request = CloneRequest(source);
            // Preflight intentionally does not become the operation's roster
            // source. ReadyTeamOneShotFarmWorkflow performs a new scan immediately
            // before selecting an immutable ExpectedTeam.
            request.InitialTeamAvailability = null;
            return request;
        }

        private async Task<MultiDeviceOneShotFarmItemResult> RunDeviceAsync(
            string deviceName, OneShotFarmRequest sourceRequest,
            IProgress<MultiDeviceOneShotFarmProgress> progress,
            CancellationToken cancellationToken)
        {
            bool succeeded = false;
            bool technicalFailure = false;
            var stopwatch = Stopwatch.StartNew();
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                Report(progress, deviceName, MultiDeviceOneShotFarmStage.DispatchingTeam,
                    null, "Đang điều một đội sẵn sàng.");

                IOneShotFarmWorkflow workflow = workflowFactory();
                if (workflow == null)
                    throw new InvalidOperationException(VietnameseUserMessageLocalizer.Default.Get(
                        UiMessageKey.WorkflowFactoryReturnedNull));
                var deviceProgress = new Progress<OneShotFarmProgress>(value =>
                    Report(progress, deviceName, MultiDeviceOneShotFarmStage.DispatchingTeam,
                        value, value?.Message));
                OneShotFarmResult result = await workflow.RunAsync(deviceName,
                    CloneRequest(sourceRequest), deviceProgress, cancellationToken);
                MultiDeviceOneShotFarmStage stage = result != null && result.Success
                    ? MultiDeviceOneShotFarmStage.Completed
                    : result != null && result.Outcome == OneShotFarmOutcome.Cancelled
                        ? MultiDeviceOneShotFarmStage.Cancelled
                        : result != null && result.Outcome == OneShotFarmOutcome.WaitingForReadyTeam
                            ? MultiDeviceOneShotFarmStage.WaitingForReadyTeam
                        : MultiDeviceOneShotFarmStage.Failed;
                succeeded = stage == MultiDeviceOneShotFarmStage.Completed;
                technicalFailure = IsTechnicalFailure(result, stage);
                string message = result?.Message ?? result?.ErrorMessage
                    ?? VietnameseUserMessageLocalizer.Default.Get(UiMessageKey.OneShotReturnedNoResult);
                if (result?.RequeueRequested != true)
                    Report(progress, deviceName, stage, null, message);
                return new MultiDeviceOneShotFarmItemResult
                {
                    DeviceName = deviceName,
                    Stage = stage,
                    Result = result,
                    ErrorMessage = result?.ErrorMessage
                };
            }
            catch (OperationCanceledException)
            {
                Report(progress, deviceName, MultiDeviceOneShotFarmStage.Cancelled,
                    null, VietnameseUserMessageLocalizer.Default.Get(UiMessageKey.OneShotCancelled));
                return new MultiDeviceOneShotFarmItemResult
                {
                    DeviceName = deviceName,
                    Stage = MultiDeviceOneShotFarmStage.Cancelled
                };
            }
            catch (Exception exception)
            {
                technicalFailure = true;
                UserErrorPresentation error = UserErrorPresenter.Present(exception);
                Report(progress, deviceName, MultiDeviceOneShotFarmStage.Failed,
                    null, error.UserMessage);
                return new MultiDeviceOneShotFarmItemResult
                {
                    DeviceName = deviceName,
                    Stage = MultiDeviceOneShotFarmStage.Failed,
                    ErrorMessage = error.TechnicalDetails
                };
            }
            finally
            {
                stopwatch.Stop();
                adaptiveConcurrencyGate?.Report(new AdaptiveConcurrencyObservation
                {
                    DeviceName = deviceName,
                    Success = succeeded,
                    TechnicalFailure = technicalFailure,
                    DurationMs = stopwatch.ElapsedMilliseconds,
                    UseDurationAsPressure = false
                });
            }
        }

        private static OneShotFarmRequest CloneRequest(OneShotFarmRequest source) =>
            new OneShotFarmRequest
            {
                ResourceType = source.ResourceType,
                TargetLevel = source.TargetLevel,
                UnoccupiedOnly = source.UnoccupiedOnly,
                ResourceLevelPriority = source.ResourceLevelPriority?.ToArray(),
                ResourcePriority = source.ResourcePriority?.ToArray(),
                SelectedResources = source.SelectedResources?.ToArray(),
                ShuffleResourcePriority = source.ShuffleResourcePriority,
                StorageLimitPolicy = source.StorageLimitPolicy,
                AttemptsPerResourceLevel = source.AttemptsPerResourceLevel,
                AllowedTeams = source.AllowedTeams?.ToArray(),
                TeamPriority = source.TeamPriority?.ToArray(),
                AllowTeam1 = source.AllowTeam1,
                RequireMarchVerification = source.RequireMarchVerification,
                RunUntilNoReadyTeams = source.RunUntilNoReadyTeams,
                ReadyTeamWaitMode = source.ReadyTeamWaitMode,
                ReadyTeamOptions = source.ReadyTeamOptions == null
                    ? null
                    : new ReadyTeamGateRunOptions(source.ReadyTeamOptions.CheckIntervalMs,
                        source.ReadyTeamOptions.MaxWaitMs),
                InitialTeamAvailability = source.InitialTeamAvailability,
                ExpectedTeam = source.ExpectedTeam,
                TeamOperation = source.TeamOperation,
                RunId = source.RunId,
                FarmRunId = source.FarmRunId,
                TeamOperationRunId = source.TeamOperationRunId,
                CooperativeDispatch = source.CooperativeDispatch,
                CycleDispatchedTeams = source.CycleDispatchedTeams?.ToArray(),
                CycleDispatchedResources = source.CycleDispatchedResources?.ToArray()
            };

        private static int ResolveDeviceIndex(string deviceName, int fallbackIndex)
        {
            if (string.IsNullOrWhiteSpace(deviceName)) return fallbackIndex;
            int start = deviceName.Length;
            while (start > 0 && char.IsDigit(deviceName[start - 1])) start--;
            return start < deviceName.Length
                && int.TryParse(deviceName.Substring(start), out int parsed)
                    ? parsed : fallbackIndex;
        }

        private sealed class PreflightResult
        {
            public string DeviceName { get; set; }
            public bool Success { get; set; }
            public WorldMapTeamAvailabilityResult Availability { get; set; }
            public MultiDeviceOneShotFarmItemResult ItemResult { get; set; }
            public long DurationMs { get; set; }
            public bool TimedOut { get; set; }
        }

        private sealed class DeviceExecutionState
        {
            public DeviceExecutionState(string deviceName, int deviceIndex)
            {
                DeviceName = deviceName;
                DeviceIndex = deviceIndex;
                RunId = Guid.NewGuid().ToString();
                DispatchedTeams = new List<TeamNumber>();
                DispatchedResources = new List<ADB_Tool_Automation_Post_FB.Core.ResourceSearch.ResourceType>();
            }

            public string DeviceName { get; }
            public int DeviceIndex { get; }
            public string RunId { get; }
            public int Iterations { get; set; }
            public int ConsecutiveNoProgress { get; set; }
            public int RetryCount { get; set; }
            public int RequeueCount { get; set; }
            public bool AnotherTeamMayBeReady { get; set; }
            public ADB_Tool_Automation_Post_FB.Core.GameDetection.GameState LastKnownGameState { get; set; }
            public DateTimeOffset NextEligibleExecutionTime { get; set; }
            public List<TeamNumber> DispatchedTeams { get; }
            public List<ADB_Tool_Automation_Post_FB.Core.ResourceSearch.ResourceType> DispatchedResources { get; }
            public MultiDeviceOneShotFarmItemResult LastResult { get; set; }
            public long PreflightDurationMs { get; set; }
            public long GameplayLeaseWaitMs { get; set; }
            public long GameplayLeaseHeldMs { get; set; }
            public int PreflightTimeoutCount { get; set; }
            public int PreflightFailureCount { get; set; }
            public int TeamsDispatchedPerLease { get; set; }
        }

        private AdaptiveConcurrencySnapshot GetConcurrencySnapshot() =>
            adaptiveConcurrencyGate?.GetSnapshot() ?? new AdaptiveConcurrencySnapshot
            {
                Enabled = false,
                CurrentLimit = maximumConcurrency,
                ActiveExecutions = 0
            };

        private static bool IsTechnicalFailure(OneShotFarmResult result,
            MultiDeviceOneShotFarmStage stage)
        {
            if (stage != MultiDeviceOneShotFarmStage.Failed || result == null)
                return stage == MultiDeviceOneShotFarmStage.Failed;
            switch (result.Outcome)
            {
                case OneShotFarmOutcome.ResourceNotFound:
                case OneShotFarmOutcome.ResourceLevelsExhausted:
                case OneShotFarmOutcome.NoEligibleTeam:
                case OneShotFarmOutcome.WaitingForReadyTeam:
                case OneShotFarmOutcome.AllCandidateStoragesFull:
                case OneShotFarmOutcome.ResourcePlanExhausted:
                case OneShotFarmOutcome.TeamAvailabilityWaitTimeout:
                case OneShotFarmOutcome.PreconditionFailed:
                case OneShotFarmOutcome.Cancelled:
                    return false;
                default:
                    return true;
            }
        }

        private void Report(IProgress<MultiDeviceOneShotFarmProgress> progress,
            string deviceName, MultiDeviceOneShotFarmStage stage,
            OneShotFarmProgress deviceProgress, string message,
            DeviceExecutionState state = null)
        {
            AdaptiveConcurrencySnapshot concurrency = GetConcurrencySnapshot();
            progress?.Report(new MultiDeviceOneShotFarmProgress
            {
                DeviceName = deviceName,
                Stage = stage,
                DeviceProgress = deviceProgress,
                Message = message,
                ConcurrencyLimit = concurrency.CurrentLimit,
                ActiveExecutions = concurrency.ActiveExecutions,
                PreflightDurationMs = state?.PreflightDurationMs ?? 0,
                GameplayLeaseWaitMs = state?.GameplayLeaseWaitMs ?? 0,
                GameplayLeaseHeldMs = state?.GameplayLeaseHeldMs ?? 0,
                TeamsDispatchedPerDeviceCycle = state?.DispatchedTeams.Count ?? 0,
                DeviceRequeueCount = state?.RequeueCount ?? 0,
                PreflightTimeoutCount = state?.PreflightTimeoutCount ?? 0,
                PreflightFailureCount = state?.PreflightFailureCount ?? 0,
                TeamsDispatchedPerLease = state?.TeamsDispatchedPerLease ?? 0
            });
        }
    }
}
