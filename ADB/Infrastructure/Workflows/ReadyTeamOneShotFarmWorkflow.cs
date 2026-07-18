using ADB_Tool_Automation_Post_FB.Core.Diagnostics;
using ADB_Tool_Automation_Post_FB.Core.GameDetection;
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
            if (string.IsNullOrWhiteSpace(deviceName) || request == null
                || request.AllowedTeams == null || request.AllowedTeams.Count == 0)
                return await inner.RunAsync(deviceName, request, cancellationToken);

            var watch = Stopwatch.StartNew();
            int checks = 0;
            IReadOnlyList<TeamNumber> eligibleReadyTeams = new TeamNumber[0];
            try
            {
                while (true)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (checks > 0 && watch.ElapsedMilliseconds >= options.MaxWaitMs)
                    {
                        return Empty(deviceName, request,
                            OneShotFarmOutcome.TeamAvailabilityWaitTimeout,
                            $"No allowed team became ready within {options.MaxWaitMs} ms.",
                            null, checks, watch.Elapsed);
                    }

                    WorldMapTeamAvailabilityResult check = await availability.CheckAsync(
                        deviceName, cancellationToken);
                    checks++;
                    if (!check.Success)
                    {
                        return Empty(deviceName, request,
                            OneShotFarmOutcome.TeamAvailabilityCheckFailed,
                            check.Message, check.ErrorMessage, checks, watch.Elapsed);
                    }
                    eligibleReadyTeams = (check.ReadyTeams ?? new TeamNumber[0])
                        .Where(team => request.AllowedTeams.Contains(team))
                        .Distinct()
                        .ToArray();
                    if (eligibleReadyTeams.Count > 0) break;

                    long remainingMs = options.MaxWaitMs - watch.ElapsedMilliseconds;
                    if (remainingMs <= 0)
                    {
                        return Empty(deviceName, request,
                            OneShotFarmOutcome.TeamAvailabilityWaitTimeout,
                            $"No allowed team became ready within {options.MaxWaitMs} ms.",
                            null, checks, watch.Elapsed);
                    }

                    logger.Info($"[Ready Team Gate] DeviceName='{deviceName}', Check={checks}, "
                        + $"ReadyTeams='{string.Join(",", check.ReadyTeams ?? new TeamNumber[0])}', "
                        + $"AllowedTeams='{string.Join(",", request.AllowedTeams)}', EligibleReady=false, "
                        + $"NextCheckInMs={Math.Min(options.CheckIntervalMs, remainingMs)}, Cancellation=false");
                    await Task.Delay((int)Math.Min(options.CheckIntervalMs, remainingMs),
                        cancellationToken);
                }

                OneShotFarmResult result = await inner.RunAsync(
                    deviceName, request, cancellationToken);
                result.TeamAvailabilityChecks = checks;
                result.ReadyTeamObserved = true;
                result.ReadyTeams = eligibleReadyTeams;
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
                ReadyTeams = new TeamNumber[0],
                Duration = duration,
                Message = message,
                ErrorMessage = error,
                Steps = new OneShotFarmStepResult[0]
            };
    }
}
