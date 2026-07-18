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

        public Task<OneShotFarmResult> RunAsync(string deviceName,
            OneShotFarmRequest request, CancellationToken cancellationToken) =>
            RunAsync(deviceName, request, null, cancellationToken);

        public async Task<OneShotFarmResult> RunAsync(string deviceName,
            OneShotFarmRequest request, IProgress<OneShotFarmProgress> progress,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(deviceName) || request == null
                || request.AllowedTeams == null || request.AllowedTeams.Count == 0)
                return await inner.RunAsync(deviceName, request, progress, cancellationToken);

            var watch = Stopwatch.StartNew();
            ReadyTeamGateRunOptions runOptions = request.ReadyTeamOptions;
            int checkIntervalMs = runOptions?.CheckIntervalMs ?? options.CheckIntervalMs;
            int maxWaitMs = runOptions?.MaxWaitMs ?? options.MaxWaitMs;
            DateTimeOffset waitStartedAt = DateTimeOffset.UtcNow;
            DateTimeOffset waitDeadline = waitStartedAt.AddMilliseconds(maxWaitMs);
            int checks = 0;
            IReadOnlyList<TeamNumber> eligibleReadyTeams = new TeamNumber[0];
            try
            {
                while (true)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (checks > 0 && watch.ElapsedMilliseconds >= maxWaitMs)
                    {
                        Report(progress, Terminal(OneShotFarmProgressStage.Failed,
                            request, checks, "Maximum ready-team wait time elapsed."));
                        return Empty(deviceName, request,
                            OneShotFarmOutcome.TeamAvailabilityWaitTimeout,
                            $"No allowed team became ready within {maxWaitMs} ms.",
                            null, checks, watch.Elapsed);
                    }

                    Report(progress, new OneShotFarmProgress
                    {
                        Stage = OneShotFarmProgressStage.CheckingTeamAvailability,
                        ReportedAt = DateTimeOffset.UtcNow,
                        TeamAvailabilityChecks = checks + 1,
                        AllowedTeams = request.AllowedTeams,
                        WaitDeadline = waitDeadline,
                        Message = $"Checking allowed teams (attempt {checks + 1})."
                    });
                    WorldMapTeamAvailabilityResult check = await availability.CheckAsync(
                        deviceName, cancellationToken);
                    checks++;
                    if (!check.Success)
                    {
                        Report(progress, Terminal(OneShotFarmProgressStage.Failed,
                            request, checks, check.Message));
                        return Empty(deviceName, request,
                            OneShotFarmOutcome.TeamAvailabilityCheckFailed,
                            check.Message, check.ErrorMessage, checks, watch.Elapsed);
                    }
                    eligibleReadyTeams = (check.ReadyTeams ?? new TeamNumber[0])
                        .Where(team => request.AllowedTeams.Contains(team))
                        .Distinct()
                        .ToArray();
                    if (eligibleReadyTeams.Count > 0)
                    {
                        Report(progress, new OneShotFarmProgress
                        {
                            Stage = OneShotFarmProgressStage.ReadyTeamFound,
                            ReportedAt = DateTimeOffset.UtcNow,
                            TeamAvailabilityChecks = checks,
                            AllowedTeams = request.AllowedTeams,
                            ReadyTeams = check.ReadyTeams ?? new TeamNumber[0],
                            EligibleReadyTeams = eligibleReadyTeams,
                            WaitDeadline = waitDeadline,
                            Message = $"Ready allowed team(s): {string.Join(", ", eligibleReadyTeams)}."
                        });
                        break;
                    }

                    long remainingMs = maxWaitMs - watch.ElapsedMilliseconds;
                    if (remainingMs <= 0)
                    {
                        Report(progress, Terminal(OneShotFarmProgressStage.Failed,
                            request, checks, "Maximum ready-team wait time elapsed."));
                        return Empty(deviceName, request,
                            OneShotFarmOutcome.TeamAvailabilityWaitTimeout,
                            $"No allowed team became ready within {maxWaitMs} ms.",
                            null, checks, watch.Elapsed);
                    }

                    int delayMs = (int)Math.Min(checkIntervalMs, remainingMs);
                    DateTimeOffset nextCheckAt = DateTimeOffset.UtcNow.AddMilliseconds(delayMs);
                    Report(progress, new OneShotFarmProgress
                    {
                        Stage = OneShotFarmProgressStage.WaitingForReadyTeam,
                        ReportedAt = DateTimeOffset.UtcNow,
                        TeamAvailabilityChecks = checks,
                        AllowedTeams = request.AllowedTeams,
                        ReadyTeams = check.ReadyTeams ?? new TeamNumber[0],
                        EligibleReadyTeams = new TeamNumber[0],
                        NextCheckAt = nextCheckAt,
                        WaitDeadline = waitDeadline,
                        Message = "No allowed team is ready; waiting before the next check."
                    });
                    logger.Info($"[Ready Team Gate] DeviceName='{deviceName}', Check={checks}, "
                        + $"ReadyTeams='{string.Join(",", check.ReadyTeams ?? new TeamNumber[0])}', "
                        + $"AllowedTeams='{string.Join(",", request.AllowedTeams)}', EligibleReady=false, "
                        + $"NextCheckInMs={delayMs}, Cancellation=false");
                    await Task.Delay(delayMs, cancellationToken);
                }

                OneShotFarmResult result = await inner.RunAsync(
                    deviceName, request, progress, cancellationToken);
                result.TeamAvailabilityChecks = checks;
                result.ReadyTeamObserved = true;
                result.ReadyTeams = eligibleReadyTeams;
                result.Duration = watch.Elapsed;
                return result;
            }
            catch (OperationCanceledException)
            {
                Report(progress, Terminal(OneShotFarmProgressStage.Cancelled,
                    request, checks, "One-shot farm was cancelled."));
                return Empty(deviceName, request, OneShotFarmOutcome.Cancelled,
                    "One-shot farm readiness waiting was cancelled.", null,
                    checks, watch.Elapsed);
            }
        }

        private OneShotFarmProgress Terminal(OneShotFarmProgressStage stage,
            OneShotFarmRequest request, int checks, string message) =>
            new OneShotFarmProgress
            {
                Stage = stage,
                ReportedAt = DateTimeOffset.UtcNow,
                TeamAvailabilityChecks = checks,
                AllowedTeams = request?.AllowedTeams ?? new TeamNumber[0],
                Message = message
            };

        private void Report(IProgress<OneShotFarmProgress> progress,
            OneShotFarmProgress value)
        {
            if (progress == null) return;
            try { progress.Report(value); }
            catch (Exception exception)
            {
                logger.Error("[Ready Team Gate] Progress callback failed; workflow continues.",
                    exception);
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
