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
        private readonly IPreflightConcurrencyGate preflightConcurrencyGate;
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
            IDeviceAutomationOwnershipService ownershipService = null,
            IPreflightConcurrencyGate preflightConcurrencyGate = null)
        {
            this.workflowFactory = workflowFactory
                ?? throw new ArgumentNullException(nameof(workflowFactory));
            if (maximumConcurrency < 1 || maximumConcurrency > MaximumSupportedConcurrency)
                throw new ArgumentOutOfRangeException(nameof(maximumConcurrency));
            this.maximumConcurrency = maximumConcurrency;
            this.availabilityFactory = availabilityFactory;
            this.adaptiveConcurrencyGate = adaptiveConcurrencyGate;
            this.preflightConcurrencyGate = preflightConcurrencyGate
                ?? new PreflightConcurrencyGate(new PreflightConcurrencyOptions());
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

            var performance = new FarmRunPerformance();
            var wallClock = Stopwatch.StartNew();
            RuntimePressureSnapshot runtimeStart = RuntimePressureMetrics.GetSnapshot();

            // Each device is admitted as soon as its own preflight finishes. This avoids
            // a slow/offline device holding the entire selected set behind Task.WhenAll.
            Task<MultiDeviceOneShotFarmItemResult>[] tasks = devices.Select((device, index) =>
                RunDevicePipelineAsync(device, index, request, progress,
                    performance, cancellationToken)).ToArray();
            MultiDeviceOneShotFarmItemResult[] results = await Task.WhenAll(tasks);
            wallClock.Stop();
            results = results
                .OrderBy(item => Array.FindIndex(devices, device =>
                    string.Equals(device, item.DeviceName, StringComparison.OrdinalIgnoreCase)))
                .ToArray();
            AdaptiveConcurrencySnapshot finalConcurrency = GetConcurrencySnapshot();
            LogPerformance(devices.Length, results, performance, finalConcurrency,
                runtimeStart, wallClock.ElapsedMilliseconds);
            return new MultiDeviceOneShotFarmResult
            {
                Devices = results,
                MaximumConcurrency = maximumConcurrency,
                AdaptiveConcurrencyEnabled = adaptiveConcurrencyGate != null,
                FinalConcurrencyLimit = finalConcurrency.CurrentLimit,
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
            FarmRunPerformance performance,
            CancellationToken cancellationToken)
        {
            // RunAsync creates every selected device pipeline on its calling thread. Several
            // preflight adapters have a sizeable synchronous prefix before their first
            // incomplete await (template loading/native vision). Without an immediate yield,
            // that prefix delays creation of the remaining pipelines and makes devices appear
            // to advance in waves even though the concurrency gates have free capacity.
            await Task.Yield();
            cancellationToken.ThrowIfCancellationRequested();
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
                        deviceName, deviceIndex, quantumRequest, progress, performance,
                        cancellationToken);
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
            FarmRunPerformance performance,
            CancellationToken cancellationToken)
        {
            IAdaptiveConcurrencyLease adaptiveLease = null;
            IDeviceAutomationLease automationLease = null;
            bool entered = false;
            int resolvedDeviceIndex = ResolveDeviceIndex(deviceName, deviceIndex);
            Stopwatch leaseWait = null;
            Stopwatch leaseHeld = null;
            try
            {
                PreflightResult preflight;
                using (ScreenshotCaptureContext.Push("Preflight"))
                    preflight = availabilityFactory == null
                        ? new PreflightResult { DeviceName = deviceName, Success = true }
                        : await RunPreflightAsync(deviceName, request, progress, performance,
                            cancellationToken);
                if (!preflight.Success)
                {
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
                    waiting.PreflightDurationMs = preflight.DurationMs;
                    return waiting;
                }

                Report(progress, deviceName, MultiDeviceOneShotFarmStage.Queued, null,
                    VietnameseUserMessageLocalizer.Default.Get(UiMessageKey.WaitingForExecutionSlot));
                leaseWait = Stopwatch.StartNew();
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
                            // Device pipelines are already isolated per device and shared
                            // screenshot/vision work has its own bounded gates. A global
                            // startup stagger made a 20-device run advance device-by-device.
                            ApplyStartupStagger = false,
                            StaggerKey = request.RunId,
                            DeviceIndex = resolvedDeviceIndex,
                            ExecutionPhase = AdaptiveExecutionPhase.Gameplay
                        }, cancellationToken);
                }
                else
                {
                    adaptiveLease = await adaptiveConcurrencyGate.AcquireAsync(deviceName,
                        AdaptiveOperationKind.Automation, cancellationToken);
                }
                leaseWait.Stop();
                RuntimePressureMetrics.ReportGameplayLeaseWait(leaseWait.ElapsedMilliseconds);
                performance.RecordFarm(GetConcurrencySnapshot(), leaseWait.ElapsedMilliseconds);
                leaseHeld = Stopwatch.StartNew();

                // The adaptive gate shapes admission only. Holding this lease for the whole
                // farm workflow made device 9+ wait minutes for an earlier device to finish,
                // even though screenshot and vision operations already have their own bounded
                // gates. Release immediately after admission so every selected device can
                // advance through its state machine independently.
                if (adaptiveLease != null)
                {
                    adaptiveLease.Dispose();
                    adaptiveLease = null;
                    leaseHeld.Stop();
                }

                infoLogger($"[Adaptive Admission] DeviceName='{deviceName}', "
                    + $"DeviceIndex={resolvedDeviceIndex}, ExecutionPhase='Gameplay', "
                    + "LeaseScope='AdmissionOnly', StartupStagger=false");
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
                leaseWait?.Stop();
                leaseHeld?.Stop();
                automationLease?.Dispose();
                adaptiveLease?.Dispose();
                if (entered) executionGate.Release();
                infoLogger($"[Farm Scheduling] DeviceName='{deviceName}', "
                    + $"GameplayLeaseWaitMs={leaseWait?.ElapsedMilliseconds ?? 0}, "
                    + $"GameplayLeaseHeldMs={leaseHeld?.ElapsedMilliseconds ?? 0}");
            }
        }

        private async Task<PreflightResult> RunPreflightAsync(string deviceName,
            OneShotFarmRequest request, IProgress<MultiDeviceOneShotFarmProgress> progress,
            FarmRunPerformance performance,
            CancellationToken cancellationToken)
        {
            bool succeeded = false;
            bool technicalFailure = false;
            var queueWait = Stopwatch.StartNew();
            Stopwatch execution = null;
            CancellationTokenSource timeoutSource = null;
            IPreflightConcurrencyLease preflightLease = null;
            try
            {
                Report(progress, deviceName, MultiDeviceOneShotFarmStage.PreflightQueued,
                    null, "Đang chờ lượt kiểm tra thiết bị.");
                preflightLease = await preflightConcurrencyGate.AcquireAsync(deviceName,
                    cancellationToken);
                queueWait.Stop();
                PreflightConcurrencySnapshot enteredSnapshot = preflightConcurrencyGate
                    .GetSnapshot();
                performance.RecordPreflight(enteredSnapshot, queueWait.ElapsedMilliseconds);
                infoLogger($"[Preflight Concurrency] DeviceName='{deviceName}', "
                    + $"Action='Entered', Active={enteredSnapshot.Active}, "
                    + $"Queued={enteredSnapshot.Queued}, Limit={enteredSnapshot.Limit}, "
                    + $"QueueWaitMs={queueWait.ElapsedMilliseconds}");
                Report(progress, deviceName, MultiDeviceOneShotFarmStage.Preflight,
                    null, VietnameseUserMessageLocalizer.Default.Get(UiMessageKey.CheckingPreflight),
                    null, queueWait.ElapsedMilliseconds, 0);
                execution = Stopwatch.StartNew();
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
                    failed.DurationMs = execution.ElapsedMilliseconds;
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
                Report(progress, deviceName, stage, null, status, null,
                    queueWait.ElapsedMilliseconds, execution.ElapsedMilliseconds);
                succeeded = true;
                return new PreflightResult
                {
                    DeviceName = deviceName,
                    Success = true,
                    Availability = availability,
                    DurationMs = execution.ElapsedMilliseconds
                };
            }
            catch (OperationCanceledException exception)
                when (!cancellationToken.IsCancellationRequested
                    && timeoutSource != null && timeoutSource.IsCancellationRequested)
            {
                technicalFailure = true;
                const string message = "Kiểm tra thiết bị quá thời gian cho phép; thiết bị này đã nhường lượt.";
                infoLogger($"[Farm Preflight] DeviceName='{deviceName}', TimedOut=true, "
                    + $"DurationMs={execution?.ElapsedMilliseconds ?? 0}, Error='{exception.Message}'");
                Report(progress, deviceName, MultiDeviceOneShotFarmStage.PreflightFailed,
                    null, message);
                PreflightResult failed = FailedPreflight(deviceName, message,
                    exception.ToString());
                failed.TimedOut = true;
                failed.DurationMs = execution?.ElapsedMilliseconds ?? 0;
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
                    DurationMs = execution?.ElapsedMilliseconds ?? 0,
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
                failed.DurationMs = execution?.ElapsedMilliseconds ?? 0;
                return failed;
            }
            finally
            {
                timeoutSource?.Dispose();
                queueWait.Stop();
                execution?.Stop();
                if (preflightLease != null)
                {
                    long executionMs = execution?.ElapsedMilliseconds ?? 0;
                    preflightLease.Dispose();
                    PreflightConcurrencySnapshot releasedSnapshot = preflightConcurrencyGate
                        .GetSnapshot();
                    infoLogger($"[Preflight Concurrency] DeviceName='{deviceName}', "
                        + $"Action='Released', Active={releasedSnapshot.Active}, "
                        + $"Queued={releasedSnapshot.Queued}, Limit={releasedSnapshot.Limit}, "
                        + $"ExecutionMs={executionMs}");
                }
                adaptiveConcurrencyGate?.Report(new AdaptiveConcurrencyObservation
                {
                    DeviceName = deviceName,
                    Success = succeeded,
                    TechnicalFailure = technicalFailure,
                    DurationMs = execution?.ElapsedMilliseconds ?? 0,
                    UseDurationAsPressure = false
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
            // Pass the concrete observation forward as a short-lived candidate.
            // ReadyTeamOneShotFarmWorkflow validates its age and confidence before
            // using it, so a device that waited for the execution gate is rescanned.
            request.InitialTeamAvailability = availability;
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

        private void LogPerformance(int selectedDevices,
            IReadOnlyList<MultiDeviceOneShotFarmItemResult> results,
            FarmRunPerformance performance, AdaptiveConcurrencySnapshot finalConcurrency,
            RuntimePressureSnapshot runtimeStart,
            long totalWallClockMs)
        {
            RuntimePressureSnapshot runtimeEnd = RuntimePressureMetrics.GetSnapshot();
            long screenshotCaptures = Math.Max(0, runtimeEnd.ScreenshotOperationCount
                - runtimeStart.ScreenshotOperationCount);
            long screenshotWait = Math.Max(0, runtimeEnd.ScreenshotTotalQueueWaitMs
                - runtimeStart.ScreenshotTotalQueueWaitMs);
            long visionOperations = Math.Max(0, runtimeEnd.VisionOperationCount
                - runtimeStart.VisionOperationCount);
            long visionWait = Math.Max(0, runtimeEnd.VisionTotalQueueWaitMs
                - runtimeStart.VisionTotalQueueWaitMs);
            int completed = results.Count(item => item.Stage
                == MultiDeviceOneShotFarmStage.Completed);
            int failed = results.Count(item => item.Stage
                == MultiDeviceOneShotFarmStage.Failed);
            int cancelled = results.Count(item => item.Stage
                == MultiDeviceOneShotFarmStage.Cancelled);
            infoLogger("[MultiDevice Farm Performance] "
                + $"SelectedDevices={selectedDevices}, "
                + $"PeakConcurrentPreflight={performance.PeakPreflightActive}, "
                + $"PeakConcurrentFarm={performance.PeakFarmActive}, "
                + $"FarmAdaptiveMinimum={finalConcurrency.MinimumLimit}, "
                + $"FarmAdaptiveInitial={finalConcurrency.InitialLimit}, "
                + $"FarmAdaptiveMaximum={finalConcurrency.MaximumLimit}, "
                + $"FarmAdaptiveFinal={finalConcurrency.CurrentLimit}, "
                + $"PeakConcurrentScreenshots={runtimeEnd.PeakActiveScreenshotOperations}, "
                + $"ScreenshotLimit={runtimeEnd.ScreenshotConcurrencyLimit}, "
                + $"AverageScreenshotQueueWaitMs={Average(screenshotWait, screenshotCaptures)}, "
                + $"MaxScreenshotQueueWaitMs={runtimeEnd.MaxScreenshotQueueWaitMs}, "
                + $"PeakConcurrentVision={runtimeEnd.PeakActiveVisionOperations}, "
                + $"VisionLimit={runtimeEnd.VisionConcurrencyLimit}, "
                + $"AverageVisionQueueWaitMs={Average(visionWait, visionOperations)}, "
                + $"MaxVisionQueueWaitMs={runtimeEnd.MaxVisionQueueWaitMs}, "
                + $"AverageFarmAdmissionWaitMs={performance.AverageFarmWaitMs}, "
                + $"MaxFarmAdmissionWaitMs={performance.MaxFarmWaitMs}, "
                + $"ADBFailures='IncludedInScreenshotFailureRate', "
                + $"ScreenshotFailures={runtimeEnd.ScreenshotFailureRate:F3}, "
                + $"VisionFailures={runtimeEnd.VisionFailureRate:F3}, "
                + $"Completed={completed}, Failed={failed}, Cancelled={cancelled}, "
                + $"TotalWallClockMs={totalWallClockMs}");
        }

        private static long Average(long total, long count) => count <= 0 ? 0 : total / count;

        private sealed class FarmRunPerformance
        {
            private readonly object sync = new object();
            private long totalFarmWaitMs;
            private int farmAdmissions;

            public int PeakPreflightActive { get; private set; }
            public int PeakFarmActive { get; private set; }
            public long MaxFarmWaitMs { get; private set; }
            public long AverageFarmWaitMs
            {
                get { lock (sync) return farmAdmissions == 0 ? 0 : totalFarmWaitMs / farmAdmissions; }
            }

            public void RecordPreflight(PreflightConcurrencySnapshot snapshot, long queueWaitMs)
            {
                lock (sync) PeakPreflightActive = Math.Max(PeakPreflightActive,
                    snapshot?.Active ?? 0);
            }

            public void RecordFarm(AdaptiveConcurrencySnapshot snapshot, long queueWaitMs)
            {
                lock (sync)
                {
                    PeakFarmActive = Math.Max(PeakFarmActive, snapshot?.ActiveExecutions ?? 0);
                    totalFarmWaitMs += Math.Max(0, queueWaitMs);
                    farmAdmissions++;
                    MaxFarmWaitMs = Math.Max(MaxFarmWaitMs, Math.Max(0, queueWaitMs));
                }
            }
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
                MinimumLimit = maximumConcurrency,
                InitialLimit = maximumConcurrency,
                MaximumLimit = maximumConcurrency,
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
            DeviceExecutionState state = null, long preflightQueueWaitMs = 0,
            long preflightExecutionMs = 0)
        {
            AdaptiveConcurrencySnapshot concurrency = GetConcurrencySnapshot();
            PreflightConcurrencySnapshot preflight = preflightConcurrencyGate.GetSnapshot();
            progress?.Report(new MultiDeviceOneShotFarmProgress
            {
                DeviceName = deviceName,
                Stage = stage,
                DeviceProgress = deviceProgress,
                Message = message,
                ConcurrencyLimit = concurrency.CurrentLimit,
                ConcurrencyMaximum = concurrency.MaximumLimit,
                QueuedExecutions = concurrency.QueuedExecutions,
                ActiveExecutions = concurrency.ActiveExecutions,
                PreflightActive = preflight.Active,
                PreflightQueued = preflight.Queued,
                PreflightLimit = preflight.Limit,
                PreflightQueueWaitMs = preflightQueueWaitMs,
                PreflightExecutionMs = preflightExecutionMs,
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
