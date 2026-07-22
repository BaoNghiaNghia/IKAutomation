using ADB_Tool_Automation_Post_FB.Core.Workflows;
using ADB_Tool_Automation_Post_FB.Core.TeamSelection;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace ADB_Tool_Automation_Post_FB.Infrastructure.Workflows
{
    public sealed class MultiDeviceOneShotFarmRunner : IMultiDeviceOneShotFarmRunner
    {
        public const int MaximumSupportedConcurrency = 20;

        private readonly Func<IOneShotFarmWorkflow> workflowFactory;
        private readonly Func<IWorldMapTeamAvailabilityService> availabilityFactory;
        private readonly int maximumConcurrency;
        private readonly SemaphoreSlim executionGate;

        public MultiDeviceOneShotFarmRunner(Func<IOneShotFarmWorkflow> workflowFactory,
            int maximumConcurrency = MaximumSupportedConcurrency)
            : this(workflowFactory, null, maximumConcurrency)
        {
        }

        public MultiDeviceOneShotFarmRunner(Func<IOneShotFarmWorkflow> workflowFactory,
            Func<IWorldMapTeamAvailabilityService> availabilityFactory,
            int maximumConcurrency = MaximumSupportedConcurrency)
        {
            this.workflowFactory = workflowFactory
                ?? throw new ArgumentNullException(nameof(workflowFactory));
            if (maximumConcurrency < 1 || maximumConcurrency > MaximumSupportedConcurrency)
                throw new ArgumentOutOfRangeException(nameof(maximumConcurrency));
            this.maximumConcurrency = maximumConcurrency;
            this.availabilityFactory = availabilityFactory;
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
                throw new ArgumentException("At least one LDPlayer device must be selected.",
                    nameof(deviceNames));

            PreflightResult[] preflights;
            if (availabilityFactory == null)
            {
                preflights = devices.Select(device => new PreflightResult
                {
                    DeviceName = device,
                    Success = true
                }).ToArray();
            }
            else
            {
                Task<PreflightResult>[] preflightTasks = devices.Select(device =>
                    Task.Run(() => RunPreflightAsync(device, request, progress,
                        cancellationToken))).ToArray();
                preflights = await Task.WhenAll(preflightTasks);
            }

            Task<MultiDeviceOneShotFarmItemResult>[] tasks = preflights
                .Where(item => item.Success)
                .Select(item => Task.Run(() => RunDeviceAsync(item.DeviceName,
                    CreatePreflightRequest(request, item.Availability), executionGate,
                    progress, cancellationToken)))
                .ToArray();
            MultiDeviceOneShotFarmItemResult[] workflowResults = await Task.WhenAll(tasks);
            MultiDeviceOneShotFarmItemResult[] results = preflights
                .Where(item => !item.Success)
                .Select(item => item.ItemResult)
                .Concat(workflowResults)
                .OrderBy(item => Array.FindIndex(devices, device =>
                    string.Equals(device, item.DeviceName, StringComparison.OrdinalIgnoreCase)))
                .ToArray();
            return new MultiDeviceOneShotFarmResult
            {
                Devices = results,
                MaximumConcurrency = maximumConcurrency,
                WasCancelled = cancellationToken.IsCancellationRequested
            };
        }

        private async Task<PreflightResult> RunPreflightAsync(string deviceName,
            OneShotFarmRequest request, IProgress<MultiDeviceOneShotFarmProgress> progress,
            CancellationToken cancellationToken)
        {
            Report(progress, deviceName, MultiDeviceOneShotFarmStage.Preflight,
                null, "Checking WorldMap, screenshot, roster and eligible teams.");
            bool entered = false;
            try
            {
                await executionGate.WaitAsync(cancellationToken);
                entered = true;
                IWorldMapTeamAvailabilityService service = availabilityFactory();
                if (service == null)
                    throw new InvalidOperationException("The preflight factory returned null.");
                WorldMapTeamAvailabilityResult availability = await service.CheckAsync(
                    deviceName, cancellationToken);
                if (availability == null || !availability.Success)
                {
                    string message = availability?.Message
                        ?? "Multi-device preflight returned no result.";
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
                    ? $"Preflight passed; eligible ready teams: {string.Join(", ", eligible)}."
                    : "Preflight passed; no allowed team is ready, waiting is required.";
                Report(progress, deviceName, stage, null, status);
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
                    null, "Multi-device preflight cancelled.");
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
                Report(progress, deviceName, MultiDeviceOneShotFarmStage.Failed,
                    null, exception.Message);
                return FailedPreflight(deviceName, exception.Message, exception.Message);
            }
            finally
            {
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
                null, "Waiting for an execution slot.");
            bool entered = false;
            try
            {
                await gate.WaitAsync(cancellationToken);
                entered = true;
                cancellationToken.ThrowIfCancellationRequested();
                Report(progress, deviceName, MultiDeviceOneShotFarmStage.Running,
                    null, "One-Shot Farm started.");

                IOneShotFarmWorkflow workflow = workflowFactory();
                if (workflow == null)
                    throw new InvalidOperationException("The workflow factory returned null.");
                var deviceProgress = new Progress<OneShotFarmProgress>(value =>
                    Report(progress, deviceName, MultiDeviceOneShotFarmStage.Running,
                        value, value?.Message));
                OneShotFarmResult result = await workflow.RunAsync(deviceName,
                    CloneRequest(sourceRequest), deviceProgress, cancellationToken);
                MultiDeviceOneShotFarmStage stage = result != null && result.Success
                    ? MultiDeviceOneShotFarmStage.Completed
                    : result != null && result.Outcome == OneShotFarmOutcome.Cancelled
                        ? MultiDeviceOneShotFarmStage.Cancelled
                        : MultiDeviceOneShotFarmStage.Failed;
                string message = result?.Message ?? result?.ErrorMessage
                    ?? "One-Shot Farm returned no result.";
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
                    null, "One-Shot Farm cancelled.");
                return new MultiDeviceOneShotFarmItemResult
                {
                    DeviceName = deviceName,
                    Stage = MultiDeviceOneShotFarmStage.Cancelled
                };
            }
            catch (Exception exception)
            {
                Report(progress, deviceName, MultiDeviceOneShotFarmStage.Failed,
                    null, exception.Message);
                return new MultiDeviceOneShotFarmItemResult
                {
                    DeviceName = deviceName,
                    Stage = MultiDeviceOneShotFarmStage.Failed,
                    ErrorMessage = exception.Message
                };
            }
            finally
            {
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

        private static void Report(IProgress<MultiDeviceOneShotFarmProgress> progress,
            string deviceName, MultiDeviceOneShotFarmStage stage,
            OneShotFarmProgress deviceProgress, string message) =>
            progress?.Report(new MultiDeviceOneShotFarmProgress
            {
                DeviceName = deviceName,
                Stage = stage,
                DeviceProgress = deviceProgress,
                Message = message
            });
    }
}
