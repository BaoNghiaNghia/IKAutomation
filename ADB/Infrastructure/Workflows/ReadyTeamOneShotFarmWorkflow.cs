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
            IReadOnlyList<TeamNumber> detectedTeams = new TeamNumber[0];
            var dispatchedResources = new List<ResourceType>();
            var dispatchedTeams = new List<TeamNumber>();
            OneShotFarmResult lastSuccessfulResult = null;
            int consecutiveNoReadyChecks = 0;
            WorldMapTeamAvailabilityResult initialAvailability =
                request.InitialTeamAvailability;
            try
            {
                while (true)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (checks > 0 && watch.ElapsedMilliseconds >= maxWaitMs)
                    {
                        Report(progress, Terminal(OneShotFarmProgressStage.Failed,
                            request, checks, VietnameseUserMessageLocalizer.Default.Get(
                                UiMessageKey.ReadyTeamWaitTimeout)));
                        return Empty(deviceName, request,
                            OneShotFarmOutcome.TeamAvailabilityWaitTimeout,
                            VietnameseUserMessageLocalizer.Default.Get(
                                UiMessageKey.NoTeamReadyWithinWait),
                            null, checks, watch.Elapsed);
                    }

                    Report(progress, new OneShotFarmProgress
                    {
                        Stage = OneShotFarmProgressStage.CheckingTeamAvailability,
                        ReportedAt = DateTimeOffset.UtcNow,
                        TeamAvailabilityChecks = checks + 1,
                        AllowedTeams = request.AllowedTeams,
                        DetectedTeams = detectedTeams,
                        WaitDeadline = waitDeadline,
                        Message = VietnameseUserMessageLocalizer.Default.Format(
                            UiMessageKey.CheckingAllowedTeams, checks + 1)
                    });
                    WorldMapTeamAvailabilityResult check;
                    if (initialAvailability != null)
                    {
                        check = initialAvailability;
                        initialAvailability = null;
                    }
                    else
                    {
                        check = await availability.CheckAsync(deviceName, cancellationToken);
                    }
                    checks++;
                    if (!check.Success)
                    {
                        Report(progress, Terminal(OneShotFarmProgressStage.Failed,
                            request, checks, check.Message));
                        return Empty(deviceName, request,
                            OneShotFarmOutcome.TeamAvailabilityCheckFailed,
                            check.Message, check.ErrorMessage, checks, watch.Elapsed);
                    }
                    detectedTeams = check.AvailableTeams ?? new TeamNumber[0];
                    // Availability is evidence, not a rewrite of the configured
                    // policy.  A weak WorldMap frame must not permanently narrow
                    // the rows the TeamSelection screen is allowed to reconcile.
                    IReadOnlyList<TeamNumber> effectiveAllowedTeams = request.AllowedTeams
                        .Distinct().ToArray();
                    eligibleReadyTeams = (check.ReadyTeams ?? new TeamNumber[0])
                        .Where(team => effectiveAllowedTeams.Contains(team))
                        .Where(team => detectedTeams.Contains(team))
                        .Where(team => !dispatchedTeams.Contains(team))
                        .Distinct()
                        .ToArray();
                    if (eligibleReadyTeams.Count > 0)
                    {
                        consecutiveNoReadyChecks = 0;
                        TeamNumber expectedTeam = (request.TeamPriority ?? request.AllowedTeams)
                            .First(team => eligibleReadyTeams.Contains(team));
                        OneShotFarmRequest cycleRequest = CreateCycleRequest(request,
                            expectedTeam, check, dispatchedResources);
                        Report(progress, new OneShotFarmProgress
                        {
                            Stage = OneShotFarmProgressStage.ReadyTeamFound,
                            ReportedAt = DateTimeOffset.UtcNow,
                            TeamAvailabilityChecks = checks,
                            AllowedTeams = effectiveAllowedTeams,
                            DetectedTeams = detectedTeams,
                            ReadyTeams = check.ReadyTeams ?? new TeamNumber[0],
                            EligibleReadyTeams = eligibleReadyTeams,
                            CurrentExpectedTeam = expectedTeam,
                            CurrentTeam = expectedTeam,
                            ConfirmedRosterCount = check.ConfirmedRosterCount,
                            RosterConfidence = check.RosterConfidence
                                ?? (check.IsRosterUncertain ? "Uncertain" : "Confirmed"),
                            RosterSource = check.RosterSource ?? check.RosterEvidenceSource.ToString(),
                            WaitDeadline = waitDeadline,
                            Message = VietnameseUserMessageLocalizer.Default.Format(
                                UiMessageKey.ReadyAllowedTeams,
                                string.Join(", ", eligibleReadyTeams))
                        });
                        OneShotFarmResult result = await inner.RunAsync(
                            deviceName, cycleRequest, progress, cancellationToken);
                        result.TeamAvailabilityChecks = checks;
                        result.ReadyTeamObserved = true;
                        result.DetectedTeams = detectedTeams;
                        result.ReadyTeams = eligibleReadyTeams;
                        result.Duration = watch.Elapsed;
                        if (!result.Success || !request.RunUntilNoReadyTeams)
                        {
                            ApplyBatchSummary(result, dispatchedResources, dispatchedTeams);
                            return result;
                        }

                        ResourceType dispatchedResource = result.DispatchedResource
                            ?? result.LocatedResource ?? cycleRequest.ResourceType;
                        dispatchedResources.Add(dispatchedResource);
                        TeamNumber dispatchedTeam = result.DispatchedTeam
                            ?? result.SelectedTeam ?? eligibleReadyTeams[0];
                        if (!dispatchedTeams.Contains(dispatchedTeam))
                            dispatchedTeams.Add(dispatchedTeam);
                        lastSuccessfulResult = result;
                        IReadOnlyList<ResourceType> nextResourceOrder = RotateAfter(
                            request.SelectedResources ?? request.ResourcePriority,
                            dispatchedResource);
                        logger.Info($"[Ready Team Gate] DeviceName='{deviceName}', "
                            + $"CompletedDispatches={dispatchedResources.Count}, "
                            + $"DispatchedTeam='{dispatchedTeam}', "
                            + $"DispatchedResource='{dispatchedResource}', "
                            + $"NextResourceOrder='{string.Join(",", nextResourceOrder)}'");
                        continue;
                    }

                    if (request.ReadyTeamWaitMode == ReadyTeamWaitMode.YieldToSupervisor)
                    {
                        DateTimeOffset scheduledCheckAt = DateTimeOffset.UtcNow
                            .AddMilliseconds(checkIntervalMs);
                        string message = VietnameseUserMessageLocalizer.Default.Get(
                            UiMessageKey.YieldedUntilScheduledCheck);
                        Report(progress, new OneShotFarmProgress
                        {
                            Stage = OneShotFarmProgressStage.WaitingForReadyTeam,
                            ReportedAt = DateTimeOffset.UtcNow,
                            TeamAvailabilityChecks = checks,
                            AllowedTeams = effectiveAllowedTeams,
                            DetectedTeams = detectedTeams,
                            ReadyTeams = check.ReadyTeams ?? new TeamNumber[0],
                            EligibleReadyTeams = new TeamNumber[0],
                            ConfirmedRosterCount = check.ConfirmedRosterCount,
                            RosterConfidence = check.RosterConfidence
                                ?? (check.IsRosterUncertain ? "Uncertain" : "Confirmed"),
                            RosterSource = check.RosterSource ?? check.RosterEvidenceSource.ToString(),
                            NextCheckAt = scheduledCheckAt,
                            WaitDeadline = waitDeadline,
                            Message = message
                        });
                        OneShotFarmResult waiting = Empty(deviceName, request,
                            OneShotFarmOutcome.WaitingForReadyTeam, message, null,
                            checks, watch.Elapsed);
                        waiting.NextCheckAt = scheduledCheckAt;
                        waiting.DetectedTeams = detectedTeams;
                        waiting.ReadyTeams = check.ReadyTeams ?? new TeamNumber[0];
                        return waiting;
                    }

                    if (lastSuccessfulResult != null)
                    {
                        consecutiveNoReadyChecks++;
                        if (consecutiveNoReadyChecks < options.NoReadyConfirmations)
                        {
                            logger.Info($"[Ready Team Gate] DeviceName='{deviceName}', "
                                + $"PostDispatchNoReadyCheck={consecutiveNoReadyChecks}/"
                                + $"{options.NoReadyConfirmations}, RecheckInMs="
                                + $"{options.PostDispatchRecheckDelayMs}, Cancellation=false");
                            Report(progress, new OneShotFarmProgress
                            {
                                Stage = OneShotFarmProgressStage.CheckingTeamAvailability,
                                ReportedAt = DateTimeOffset.UtcNow,
                                TeamAvailabilityChecks = checks,
                                AllowedTeams = effectiveAllowedTeams,
                                DetectedTeams = detectedTeams,
                                ReadyTeams = check.ReadyTeams ?? new TeamNumber[0],
                                EligibleReadyTeams = new TeamNumber[0],
                                ConfirmedRosterCount = check.ConfirmedRosterCount,
                                RosterConfidence = check.RosterConfidence
                                    ?? (check.IsRosterUncertain ? "Uncertain" : "Confirmed"),
                                RosterSource = check.RosterSource ?? check.RosterEvidenceSource.ToString(),
                                Message = VietnameseUserMessageLocalizer.Default.Format(
                                    UiMessageKey.ConfirmingNoReadyTeam,
                                    consecutiveNoReadyChecks, options.NoReadyConfirmations)
                            });
                            await Task.Delay(options.PostDispatchRecheckDelayMs,
                                cancellationToken);
                            continue;
                        }
                        lastSuccessfulResult.TeamAvailabilityChecks = checks;
                        lastSuccessfulResult.ReadyTeamObserved = true;
                        lastSuccessfulResult.DetectedTeams = detectedTeams;
                        lastSuccessfulResult.ReadyTeams = new TeamNumber[0];
                        lastSuccessfulResult.Duration = watch.Elapsed;
                        lastSuccessfulResult.Message = VietnameseUserMessageLocalizer.Default.Format(
                            UiMessageKey.DispatchedTeamsNoReadyRemaining,
                            dispatchedResources.Count);
                        ApplyBatchSummary(lastSuccessfulResult, dispatchedResources,
                            dispatchedTeams);
                        Report(progress, Terminal(OneShotFarmProgressStage.Completed,
                            request, checks, lastSuccessfulResult.Message));
                        return lastSuccessfulResult;
                    }

                    long remainingMs = maxWaitMs - watch.ElapsedMilliseconds;
                    if (remainingMs <= 0)
                    {
                        Report(progress, Terminal(OneShotFarmProgressStage.Failed,
                            request, checks, VietnameseUserMessageLocalizer.Default.Get(
                                UiMessageKey.ReadyTeamWaitTimeout)));
                        return Empty(deviceName, request,
                            OneShotFarmOutcome.TeamAvailabilityWaitTimeout,
                            VietnameseUserMessageLocalizer.Default.Get(
                                UiMessageKey.NoTeamReadyWithinWait),
                            null, checks, watch.Elapsed);
                    }

                    int delayMs = (int)Math.Min(checkIntervalMs, remainingMs);
                    DateTimeOffset nextCheckAt = DateTimeOffset.UtcNow.AddMilliseconds(delayMs);
                    Report(progress, new OneShotFarmProgress
                    {
                        Stage = OneShotFarmProgressStage.WaitingForReadyTeam,
                        ReportedAt = DateTimeOffset.UtcNow,
                        TeamAvailabilityChecks = checks,
                        AllowedTeams = effectiveAllowedTeams,
                        DetectedTeams = detectedTeams,
                        ReadyTeams = check.ReadyTeams ?? new TeamNumber[0],
                        EligibleReadyTeams = new TeamNumber[0],
                        ConfirmedRosterCount = check.ConfirmedRosterCount,
                        RosterConfidence = check.RosterConfidence
                            ?? (check.IsRosterUncertain ? "Uncertain" : "Confirmed"),
                        RosterSource = check.RosterSource ?? check.RosterEvidenceSource.ToString(),
                        NextCheckAt = nextCheckAt,
                        WaitDeadline = waitDeadline,
                        Message = VietnameseUserMessageLocalizer.Default.Get(
                            UiMessageKey.WaitingForNextTeamCheck)
                    });
                    logger.Info($"[Ready Team Gate] DeviceName='{deviceName}', Check={checks}, "
                        + $"ReadyTeams='{string.Join(",", check.ReadyTeams ?? new TeamNumber[0])}', "
                        + $"DetectedTeams='{string.Join(",", detectedTeams)}', "
                        + $"AllowedTeams='{string.Join(",", effectiveAllowedTeams)}', EligibleReady=false, "
                        + $"NextCheckInMs={delayMs}, Cancellation=false");
                    await Task.Delay(delayMs, cancellationToken);
                }

            }
            catch (OperationCanceledException)
            {
                Report(progress, Terminal(OneShotFarmProgressStage.Cancelled,
                    request, checks, VietnameseUserMessageLocalizer.Default.Get(
                        UiMessageKey.OneShotCancelled)));
                return Empty(deviceName, request, OneShotFarmOutcome.Cancelled,
                    VietnameseUserMessageLocalizer.Default.Get(
                        UiMessageKey.ReadinessWaitCancelled), null,
                    checks, watch.Elapsed);
            }
        }

        private static OneShotFarmRequest CreateCycleRequest(OneShotFarmRequest source,
            TeamNumber expectedTeam, WorldMapTeamAvailabilityResult availability,
            IReadOnlyList<ResourceType> dispatchedResources)
        {
            IReadOnlyList<ResourceType> selected = source.SelectedResources
                ?? source.ResourcePriority ?? new ResourceType[0];
            IReadOnlyList<ResourceType> priority = dispatchedResources.Count == 0
                ? source.ResourcePriority ?? selected
                : RotateAfter(selected, dispatchedResources[dispatchedResources.Count - 1]);
            return new OneShotFarmRequest
            {
                ResourceType = priority.Count == 0 ? source.ResourceType : priority[0],
                TargetLevel = source.TargetLevel,
                UnoccupiedOnly = source.UnoccupiedOnly,
                ResourceLevelPriority = source.ResourceLevelPriority,
                ResourcePriority = priority.ToArray(),
                SelectedResources = selected.ToArray(),
                ShuffleResourcePriority = dispatchedResources.Count == 0
                    && source.ShuffleResourcePriority,
                StorageLimitPolicy = source.StorageLimitPolicy,
                AttemptsPerResourceLevel = source.AttemptsPerResourceLevel,
                AllowedTeams = (source.AllowedTeams ?? new TeamNumber[0]).Distinct().ToArray(),
                TeamPriority = (source.TeamPriority ?? source.AllowedTeams ?? new TeamNumber[0]).Distinct().ToArray(),
                ExpectedTeam = expectedTeam,
                WorldMapAvailableTeams = availability.AvailableTeams ?? new TeamNumber[0],
                WorldMapReadyTeams = availability.ReadyTeams ?? new TeamNumber[0],
                WorldMapRosterStatus = availability.RosterStatus,
                WorldMapRosterConfidence = availability.RosterConfidence,
                AllowTeam1 = source.AllowTeam1,
                RequireMarchVerification = source.RequireMarchVerification,
                RunUntilNoReadyTeams = source.RunUntilNoReadyTeams,
                ReadyTeamWaitMode = source.ReadyTeamWaitMode,
                ReadyTeamOptions = source.ReadyTeamOptions,
                RunId = source.RunId
            };
        }

        private static IReadOnlyList<ResourceType> RotateAfter(
            IReadOnlyList<ResourceType> resources, ResourceType previous)
        {
            if (resources == null || resources.Count == 0) return new ResourceType[0];
            var unique = resources.Distinct().ToArray();
            int previousIndex = Array.IndexOf(unique, previous);
            int start = previousIndex < 0 ? 0 : (previousIndex + 1) % unique.Length;
            return Enumerable.Range(0, unique.Length)
                .Select(offset => unique[(start + offset) % unique.Length]).ToArray();
        }

        private static void ApplyBatchSummary(OneShotFarmResult result,
            IReadOnlyList<ResourceType> dispatchedResources,
            IReadOnlyList<TeamNumber> dispatchedTeams)
        {
            result.CompletedDispatches = dispatchedResources.Count;
            result.DispatchedResources = dispatchedResources.ToArray();
            result.BatchDispatchedTeams = dispatchedTeams.ToArray();
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
                DetectedTeams = new TeamNumber[0],
                ReadyTeams = new TeamNumber[0],
                CompletedDispatches = 0,
                DispatchedResources = new ResourceType[0],
                BatchDispatchedTeams = new TeamNumber[0],
                Duration = duration,
                Message = message,
                ErrorMessage = error,
                Steps = new OneShotFarmStepResult[0]
            };
    }
}
