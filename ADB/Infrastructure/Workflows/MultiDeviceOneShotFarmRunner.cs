using ADB_Tool_Automation_Post_FB.Core.Workflows;
using ADB_Tool_Automation_Post_FB.Core.TeamSelection;
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
            int maximumConcurrency, IAdaptiveConcurrencyGate adaptiveConcurrencyGate)
        {
            this.workflowFactory = workflowFactory
                ?? throw new ArgumentNullException(nameof(workflowFactory));
            if (maximumConcurrency < 1 || maximumConcurrency > MaximumSupportedConcurrency)
                throw new ArgumentOutOfRangeException(nameof(maximumConcurrency));
            this.maximumConcurrency = maximumConcurrency;
            this.availabilityFactory = availabilityFactory;
            this.adaptiveConcurrencyGate = adaptiveConcurrencyGate;
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

            // Each device is admitted as soon as its own preflight finishes. This avoids
            // a slow/offline device holding the entire selected set behind Task.WhenAll.
            Task<MultiDeviceOneShotFarmItemResult>[] tasks = devices.Select(device =>
                Task.Run(() => RunAfterPreflightAsync(device, request, progress,
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

        private async Task<MultiDeviceOneShotFarmItemResult> RunAfterPreflightAsync(
            string deviceName, OneShotFarmRequest request,
            IProgress<MultiDeviceOneShotFarmProgress> progress,
            CancellationToken cancellationToken)
        {
            PreflightResult preflight = availabilityFactory == null
                ? new PreflightResult { DeviceName = deviceName, Success = true }
                : await RunPreflightAsync(deviceName, request, progress, cancellationToken);
            if (!preflight.Success)
                return preflight.ItemResult;

            if (availabilityFactory != null
                && request.ReadyTeamWaitMode == ReadyTeamWaitMode.YieldToSupervisor
                && !HasEligibleReadyTeam(preflight.Availability, request))
                return WaitingForReadyTeam(preflight.DeviceName, preflight.Availability,
                    request);

            return await RunDeviceAsync(preflight.DeviceName,
                CreatePreflightRequest(request, preflight.Availability), executionGate,
                progress, cancellationToken);
        }

        private async Task<PreflightResult> RunPreflightAsync(string deviceName,
            OneShotFarmRequest request, IProgress<MultiDeviceOneShotFarmProgress> progress,
            CancellationToken cancellationToken)
        {
            Report(progress, deviceName, MultiDeviceOneShotFarmStage.Preflight,
                null, VietnameseUserMessageLocalizer.Default.Get(UiMessageKey.CheckingPreflight));
            IAdaptiveConcurrencyLease adaptiveLease = null;
            bool entered = false;
            bool succeeded = false;
            bool technicalFailure = false;
            var stopwatch = Stopwatch.StartNew();
            try
            {
                if (adaptiveConcurrencyGate == null)
                {
                    await executionGate.WaitAsync(cancellationToken);
                    entered = true;
                }
                else
                {
                    adaptiveLease = await adaptiveConcurrencyGate.AcquireAsync(deviceName,
                        AdaptiveOperationKind.Automation, cancellationToken);
                }
                IWorldMapTeamAvailabilityService service = availabilityFactory();
                if (service == null)
                    throw new InvalidOperationException(VietnameseUserMessageLocalizer.Default.Get(
                        UiMessageKey.PreflightFactoryReturnedNull));
                WorldMapTeamAvailabilityResult availability = await service.CheckAsync(
                    deviceName, cancellationToken);
                if (availability == null || !availability.Success)
                {
                    technicalFailure = true;
                    string message = availability?.Message
                        ?? VietnameseUserMessageLocalizer.Default.Get(
                            UiMessageKey.PreflightReturnedNoResult);
                    Report(progress, deviceName, MultiDeviceOneShotFarmStage.Failed,
                        null, message);
                    return FailedPreflight(deviceName, message,
                        availability?.ErrorMessage);
                }

                TeamNumber[] eligible = (availability.ReadyTeams ?? new TeamNumber[0])
                    .Where(team => (request.AllowedTeams ?? new TeamNumber[0]).Contains(team))
                    .Distinct().ToArray();
                MultiDeviceOneShotFarmStage stage = eligible.Length > 0
                    ? MultiDeviceOneShotFarmStage.Queued
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
                    Availability = availability
                };
            }
            catch (OperationCanceledException)
            {
                Report(progress, deviceName, MultiDeviceOneShotFarmStage.Cancelled,
                    null, VietnameseUserMessageLocalizer.Default.Get(UiMessageKey.PreflightCancelled));
                return new PreflightResult
                {
                    DeviceName = deviceName,
                    Success = false,
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
                Report(progress, deviceName, MultiDeviceOneShotFarmStage.Failed,
                    null, error.UserMessage);
                return FailedPreflight(deviceName, error.UserMessage, error.TechnicalDetails);
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
                    UseDurationAsPressure = true
                });
                adaptiveLease?.Dispose();
                if (entered) executionGate.Release();
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
            int delayMs = request?.ReadyTeamOptions?.CheckIntervalMs ?? 600000;
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
            request.InitialTeamAvailability = availability;
            return request;
        }

        private async Task<MultiDeviceOneShotFarmItemResult> RunDeviceAsync(
            string deviceName, OneShotFarmRequest sourceRequest, SemaphoreSlim gate,
            IProgress<MultiDeviceOneShotFarmProgress> progress,
            CancellationToken cancellationToken)
        {
            Report(progress, deviceName, MultiDeviceOneShotFarmStage.Queued,
                null, VietnameseUserMessageLocalizer.Default.Get(
                    UiMessageKey.WaitingForExecutionSlot));
            IAdaptiveConcurrencyLease adaptiveLease = null;
            bool entered = false;
            bool succeeded = false;
            bool technicalFailure = false;
            var stopwatch = Stopwatch.StartNew();
            try
            {
                if (adaptiveConcurrencyGate == null)
                {
                    await gate.WaitAsync(cancellationToken);
                    entered = true;
                }
                else
                {
                    adaptiveLease = await adaptiveConcurrencyGate.AcquireAsync(deviceName,
                        AdaptiveOperationKind.Automation, cancellationToken);
                }
                cancellationToken.ThrowIfCancellationRequested();
                Report(progress, deviceName, MultiDeviceOneShotFarmStage.Running,
                    null, VietnameseUserMessageLocalizer.Default.Get(UiMessageKey.OneShotStarted));

                IOneShotFarmWorkflow workflow = workflowFactory();
                if (workflow == null)
                    throw new InvalidOperationException(VietnameseUserMessageLocalizer.Default.Get(
                        UiMessageKey.WorkflowFactoryReturnedNull));
                var deviceProgress = new Progress<OneShotFarmProgress>(value =>
                    Report(progress, deviceName, MultiDeviceOneShotFarmStage.Running,
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
                adaptiveLease?.Dispose();
                if (entered) gate.Release();
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
                RunId = Guid.NewGuid().ToString()
            };

        private sealed class PreflightResult
        {
            public string DeviceName { get; set; }
            public bool Success { get; set; }
            public WorldMapTeamAvailabilityResult Availability { get; set; }
            public MultiDeviceOneShotFarmItemResult ItemResult { get; set; }
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
            OneShotFarmProgress deviceProgress, string message)
        {
            AdaptiveConcurrencySnapshot concurrency = GetConcurrencySnapshot();
            progress?.Report(new MultiDeviceOneShotFarmProgress
            {
                DeviceName = deviceName,
                Stage = stage,
                DeviceProgress = deviceProgress,
                Message = message,
                ConcurrencyLimit = concurrency.CurrentLimit,
                ActiveExecutions = concurrency.ActiveExecutions
            });
        }
    }
}
