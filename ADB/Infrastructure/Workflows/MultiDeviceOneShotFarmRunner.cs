using ADB_Tool_Automation_Post_FB.Core.Workflows;
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
        private readonly int maximumConcurrency;
        private readonly SemaphoreSlim executionGate;

        public MultiDeviceOneShotFarmRunner(Func<IOneShotFarmWorkflow> workflowFactory,
            int maximumConcurrency = MaximumSupportedConcurrency)
        {
            this.workflowFactory = workflowFactory
                ?? throw new ArgumentNullException(nameof(workflowFactory));
            if (maximumConcurrency < 1 || maximumConcurrency > MaximumSupportedConcurrency)
                throw new ArgumentOutOfRangeException(nameof(maximumConcurrency));
            this.maximumConcurrency = maximumConcurrency;
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

            Task<MultiDeviceOneShotFarmItemResult>[] tasks = devices
                .Select(device => Task.Run(() => RunDeviceAsync(device, request,
                    executionGate, progress, cancellationToken)))
                .ToArray();
            MultiDeviceOneShotFarmItemResult[] results = await Task.WhenAll(tasks);
            return new MultiDeviceOneShotFarmResult
            {
                Devices = results,
                MaximumConcurrency = maximumConcurrency,
                WasCancelled = cancellationToken.IsCancellationRequested
            };
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
                RunId = Guid.NewGuid().ToString()
            };

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
