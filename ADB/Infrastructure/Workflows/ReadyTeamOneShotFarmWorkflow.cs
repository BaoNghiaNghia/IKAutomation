using ADB_Tool_Automation_Post_FB.Core.Diagnostics;
using ADB_Tool_Automation_Post_FB.Core.GameDetection;
using ADB_Tool_Automation_Post_FB.Core.ResourceSearch;
using ADB_Tool_Automation_Post_FB.Core.TeamSelection;
using ADB_Tool_Automation_Post_FB.Core.Workflows;
using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace ADB_Tool_Automation_Post_FB.Infrastructure.Workflows
{
    public sealed class ReadyTeamOneShotFarmWorkflow : IOneShotFarmWorkflow
    {
        private readonly IOneShotFarmWorkflow inner;
        private readonly IWorldMapTeamAvailabilityService availability;
        private readonly ReadyTeamGateOptions options;
        private readonly IDiagnosticLogger logger;

        public ReadyTeamOneShotFarmWorkflow(IOneShotFarmWorkflow inner,
            IWorldMapTeamAvailabilityService availability,
            ReadyTeamGateOptions options, IDiagnosticLogger logger)
        {
            this.inner = inner ?? throw new ArgumentNullException(nameof(inner));
            this.availability = availability ?? throw new ArgumentNullException(nameof(availability));
            this.options = options ?? throw new ArgumentNullException(nameof(options));
            this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<OneShotFarmResult> RunAsync(string deviceName,
            OneShotFarmRequest request, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(deviceName) || request == null)
                return await inner.RunAsync(deviceName, request, cancellationToken);

            var watch = Stopwatch.StartNew();
            int checks = 0;
            try
            {
                while (true)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    WorldMapTeamAvailabilityResult check = await availability.CheckAsync(
                        deviceName, cancellationToken);
                    checks++;
                    if (!check.Success)
                    {
                        return Empty(deviceName, request,
                            OneShotFarmOutcome.TeamAvailabilityCheckFailed,
                            check.Message, check.ErrorMessage, checks, watch.Elapsed);
                    }
                    if (check.AnyReadyTeam) break;

                    logger.Info($"[Ready Team Gate] DeviceName='{deviceName}', Check={checks}, "
                        + $"Ready=false, NextCheckInMs={options.CheckIntervalMs}, Cancellation=false");
                    await Task.Delay(options.CheckIntervalMs, cancellationToken);
                }

                OneShotFarmResult result = await inner.RunAsync(
                    deviceName, request, cancellationToken);
                result.TeamAvailabilityChecks = checks;
                result.ReadyTeamObserved = true;
                result.Duration = watch.Elapsed;
                return result;
            }
            catch (OperationCanceledException)
            {
                return Empty(deviceName, request, OneShotFarmOutcome.Cancelled,
                    "One-shot farm readiness waiting was cancelled.", null,
                    checks, watch.Elapsed);
            }
        }

        private static OneShotFarmResult Empty(string deviceName,
            OneShotFarmRequest request, OneShotFarmOutcome outcome,
            string message, string error, int checks, TimeSpan duration) =>
            new OneShotFarmResult
            {
                Outcome = outcome,
                Success = false,
                DeviceName = deviceName,
                RequestedResource = request.ResourceType,
                RequestedLevel = request.TargetLevel,
                RequestedUnoccupiedOnly = request.UnoccupiedOnly,
                AttemptedLevels = new int[0],
                AttemptedResources = new ResourceType[0],
                SelectedResources = request.SelectedResources
                    ?? request.ResourcePriority ?? new ResourceType[0],
                ShuffledResourcePriority = request.ResourcePriority ?? new ResourceType[0],
                MissingRuntimeTemplates = new MissingRuntimeTemplate[0],
                StorageFullResources = new ResourceType[0],
                LevelsExhaustedResources = new ResourceType[0],
                InitialState = GameState.Unknown,
                FinalState = GameState.Unknown,
                LastCompletedStep = OneShotFarmStep.Preflight,
                TeamAvailabilityChecks = checks,
                ReadyTeamObserved = false,
                Duration = duration,
                Message = message,
                ErrorMessage = error,
                Steps = new OneShotFarmStepResult[0]
            };
    }
}
