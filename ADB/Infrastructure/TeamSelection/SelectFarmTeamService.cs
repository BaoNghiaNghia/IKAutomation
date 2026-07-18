using ADB_Tool_Automation_Post_FB.Core.Abstractions;
using ADB_Tool_Automation_Post_FB.Core.Concurrency;
using ADB_Tool_Automation_Post_FB.Core.Diagnostics;
using ADB_Tool_Automation_Post_FB.Core.GameDetection;
using ADB_Tool_Automation_Post_FB.Core.TeamSelection;
using ADB_Tool_Automation_Post_FB.Core.Vision;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace ADB_Tool_Automation_Post_FB.Infrastructure.TeamSelection
{
    public sealed class SelectFarmTeamService : ISelectFarmTeamService
    {
        private static readonly TemplateId[] ReadyTemplates =
        {
            TemplateId.TeamSelectionPanelAnchor,
            TemplateId.TeamAdjustFormationButton,
            TemplateId.TeamActionButtonEnabled
        };

        private readonly IGameStateDetector detector;
        private readonly ILdPlayerClient client;
        private readonly ITemplateRegistry registry;
        private readonly IImageMatcher matcher;
        private readonly IDeviceOperationLock operationLock;
        private readonly FarmTeamSelectionOptions options;
        private readonly ISelectFarmTeamDiagnosticStore diagnosticStore;
        private readonly IDiagnosticLogger logger;

        public SelectFarmTeamService(IGameStateDetector detector, ILdPlayerClient client,
            ITemplateRegistry registry, IImageMatcher matcher,
            IDeviceOperationLock operationLock, FarmTeamSelectionOptions options,
            ISelectFarmTeamDiagnosticStore diagnosticStore, IDiagnosticLogger logger)
        {
            this.detector = detector ?? throw new ArgumentNullException(nameof(detector));
            this.client = client ?? throw new ArgumentNullException(nameof(client));
            this.registry = registry ?? throw new ArgumentNullException(nameof(registry));
            this.matcher = matcher ?? throw new ArgumentNullException(nameof(matcher));
            this.operationLock = operationLock ?? throw new ArgumentNullException(nameof(operationLock));
            this.options = options ?? throw new ArgumentNullException(nameof(options));
            this.diagnosticStore = diagnosticStore ?? throw new ArgumentNullException(nameof(diagnosticStore));
            this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<SelectFarmTeamResult> SelectAsync(string deviceName,
            TeamSelectionRequest request, CancellationToken cancellationToken)
        {
            string validationError = ValidateRequest(deviceName, request);
            if (validationError != null) return Empty(SelectFarmTeamOutcome.Failed, validationError);
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                return await operationLock.RunAsync(deviceName.Trim(),
                    token => SelectCoreAsync(deviceName.Trim(), request, token), cancellationToken);
            }
            catch (OperationCanceledException)
            {
                return Empty(SelectFarmTeamOutcome.Cancelled,
                    "Farm team selection was cancelled while waiting for the device lock.");
            }
        }

        private async Task<SelectFarmTeamResult> SelectCoreAsync(string deviceName,
            TeamSelectionRequest request, CancellationToken cancellationToken)
        {
            var watch = Stopwatch.StartNew();
            var attempts = new List<TeamSelectionAttempt>();
            var attemptedTeams = new List<TeamNumber>();
            var result = NewResult(attempts, attemptedTeams);
            byte[] lastFrame = null;
            try
            {
                logger.Info($"[Farm Team Selection] DeviceName='{deviceName}', Allowed='{Join(request.AllowedTeams)}', Priority='{Join(request.Priority)}', Cancellation=false, Phase='Starting'");
                if (!RequiredScreenTemplatesExist(out string screenTemplateError)
                    || !registry.Exists(TemplateId.TeamSelectedBorderAnchor))
                {
                    string error = screenTemplateError ?? $"Required template '{TemplateId.TeamSelectedBorderAnchor}' was not found at '{registry.GetPath(TemplateId.TeamSelectedBorderAnchor)}'.";
                    return Complete(result, SelectFarmTeamOutcome.Failed,
                        "Farm team selection templates are incomplete; no Tap was sent.", error, watch);
                }

                GameDetectionResult initial = await detector.DetectAsync(deviceName, cancellationToken);
                result.InitialState = initial.State;
                result.FinalState = initial.State;
                if (!IsSelectionScreen(initial))
                {
                    lastFrame = await TryCaptureAsync(deviceName, cancellationToken);
                    return await CompleteAsync(deviceName, result,
                        SelectFarmTeamOutcome.TeamSelectionNotReady,
                        "Team Selection is not ready; no Tap was sent.",
                        initial.ErrorMessage, lastFrame, watch, cancellationToken);
                }
                result.TeamSelectionScreenVerified = true;

                lastFrame = await client.CaptureScreenshotPngAsync(deviceName, cancellationToken);
                GameDetectionResult freshState = detector.Detect(lastFrame);
                if (!IsSelectionScreen(freshState))
                    return await CompleteAsync(deviceName, result,
                        SelectFarmTeamOutcome.TeamSelectionNotReady,
                        "Team Selection was not ready on the fresh screenshot; no Tap was sent.",
                        freshState.ErrorMessage, lastFrame, watch, cancellationToken);

                SelectedScan selected = ScanSelected(lastFrame);
                if (selected.IsAmbiguous)
                    return await CompleteAsync(deviceName, result, SelectFarmTeamOutcome.Failed,
                        "Selected border appeared in multiple team ROIs; no Tap was sent.",
                        "Ambiguous selected-team evidence.", lastFrame, watch, cancellationToken);

                TeamNumber? allowedSelected = FirstByPriority(selected.Teams,
                    request.Priority, request.AllowedTeams, request.AllowTeam1);
                if (allowedSelected.HasValue && HasEnabledAction(freshState))
                {
                    result.SelectedTeam = allowedSelected;
                    result.SelectedStateVerified = true;
                    attempts.Add(new TeamSelectionAttempt
                    {
                        TeamNumber = allowedSelected.Value,
                        AlreadySelected = true,
                        SelectedVerified = true,
                        SelectedBorderMatch = selected.Matches[allowedSelected.Value],
                        Message = "Allowed team was already selected; no Tap was sent."
                    });
                    return Complete(result, SelectFarmTeamOutcome.AlreadySelected,
                        $"{allowedSelected.Value} was already selected.", null, watch);
                }

                DateTimeOffset selectionDeadline = DateTimeOffset.UtcNow.AddSeconds(
                    options.SelectionTimeoutSeconds);
                bool continueAfterConfirmedUnavailable = false;
                foreach (TeamNumber team in request.Priority)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (!request.AllowedTeams.Contains(team) || (!request.AllowTeam1 && team == TeamNumber.Team1))
                        continue;
                    attemptedTeams.Add(team);
                    TemplateId? badgeIdValue = BadgeId(team);
                    if (!badgeIdValue.HasValue)
                    {
                        attempts.Add(new TeamSelectionAttempt
                        {
                            TeamNumber = team,
                            Message = "Team1 selection is outside the MVP because no Team1 badge template is registered; no Tap was sent."
                        });
                        continue;
                    }
                    TemplateId badgeId = badgeIdValue.Value;
                    if (!registry.Exists(badgeId))
                    {
                        attempts.Add(new TeamSelectionAttempt
                        {
                            TeamNumber = team,
                            Message = $"Badge template '{badgeId}' was not found at '{registry.GetPath(badgeId)}'; no Tap was sent."
                        });
                        continue;
                    }

                    ImageRegion teamRegion = options.TeamRegions[team];
                    ImageMatchResult preliminaryBadge = Match(lastFrame, badgeId, teamRegion);
                    bool preliminaryDisabled = IsDisabled(lastFrame, teamRegion);
                    if (!HasBounds(preliminaryBadge) || preliminaryDisabled)
                    {
                        attempts.Add(new TeamSelectionAttempt
                        {
                            TeamNumber = team,
                            BadgeFound = HasBounds(preliminaryBadge),
                            BadgeMatch = preliminaryBadge,
                            DisabledDetected = preliminaryDisabled,
                            Message = preliminaryDisabled
                                ? "Disabled team evidence was found during candidate inspection; no Tap was sent."
                                : "Team badge was not found during candidate inspection; no Tap was sent."
                        });
                        continue;
                    }

                    for (int attemptNumber = 1;
                        attemptNumber <= options.MaxSelectionAttemptsPerTeam;
                        attemptNumber++)
                    {
                        bool selectedButUnavailable = false;
                        cancellationToken.ThrowIfCancellationRequested();
                        if (DateTimeOffset.UtcNow >= selectionDeadline
                            && !continueAfterConfirmedUnavailable)
                            return await CompleteAsync(deviceName, result,
                                SelectFarmTeamOutcome.SelectionTimeout,
                                "Farm team selection timed out before another safe attempt.",
                                null, lastFrame, watch, cancellationToken);
                        continueAfterConfirmedUnavailable = false;

                        lastFrame = await client.CaptureScreenshotPngAsync(deviceName, cancellationToken);
                        GameDetectionResult state = detector.Detect(lastFrame);
                        if (!IsSelectionScreen(state))
                            return await CompleteAsync(deviceName, result,
                                SelectFarmTeamOutcome.TeamSelectionNotReady,
                                "Team Selection stopped being ready; no further Tap was sent.",
                                state.ErrorMessage, lastFrame, watch, cancellationToken);

                        ImageRegion region = teamRegion;
                        ImageMatchResult badge = Match(lastFrame, badgeId, region);
                        bool disabled = IsDisabled(lastFrame, region);
                        var attempt = new TeamSelectionAttempt
                        {
                            TeamNumber = team,
                            BadgeFound = HasBounds(badge),
                            BadgeMatch = badge,
                            DisabledDetected = disabled
                        };
                        attempts.Add(attempt);
                        LogMatch(deviceName, team, attemptNumber, region, badge, disabled);
                        if (!HasBounds(badge))
                        {
                            attempt.Message = "Team badge was not found with valid bounds; no Tap was sent.";
                            break;
                        }
                        if (disabled)
                        {
                            attempt.Message = "Disabled team evidence was found; no Tap was sent.";
                            break;
                        }

                        await client.TapAsync(deviceName, badge.CenterX, badge.CenterY, cancellationToken);
                        result.TeamTapCount++;
                        attempt.TapSent = true;
                        logger.Info($"[Farm Team Selection] DeviceName='{deviceName}', Team='{team}', Attempt={attemptNumber}, BadgeBounds=({badge.X},{badge.Y},{badge.Width},{badge.Height}), Tap=({badge.CenterX},{badge.CenterY}), Cancellation=false");

                        DateTimeOffset observationDeadline = DateTimeOffset.UtcNow.AddMilliseconds(
                            Math.Max(options.TapRetryDelayMs, options.PollIntervalMs * 2));
                        bool firstPostTapObservation = true;
                        while (firstPostTapObservation
                            || (DateTimeOffset.UtcNow < observationDeadline
                                && DateTimeOffset.UtcNow < selectionDeadline))
                        {
                            firstPostTapObservation = false;
                            await Task.Delay(options.PollIntervalMs, cancellationToken);
                            lastFrame = await client.CaptureScreenshotPngAsync(deviceName, cancellationToken);
                            GameDetectionResult observedState = detector.Detect(lastFrame);
                            if (!IsSelectionScreen(observedState)) continue;
                            SelectedScan observed = ScanSelected(lastFrame);
                            if (observed.IsAmbiguous)
                                return await CompleteAsync(deviceName, result,
                                    SelectFarmTeamOutcome.Failed,
                                    "Selected border appeared in multiple team ROIs after Tap.",
                                    "Ambiguous selected-team evidence.", lastFrame, watch, cancellationToken);
                            if (observed.Teams.Count == 1 && observed.Teams[0] == team)
                            {
                                if (!HasEnabledAction(observedState))
                                {
                                    attempt.SelectedVerified = true;
                                    attempt.SelectedBorderMatch = observed.Matches[team];
                                    attempt.Message = "Team was selected but has no enabled farm action; trying the next eligible team.";
                                    selectedButUnavailable = true;
                                    break;
                                }
                                attempt.SelectedVerified = true;
                                attempt.SelectedBorderMatch = observed.Matches[team];
                                attempt.Message = "Team selected border was verified in the target ROI.";
                                result.SelectedTeam = team;
                                result.SelectedStateVerified = true;
                                result.FinalState = GameState.TeamSelection;
                                return Complete(result, SelectFarmTeamOutcome.TeamSelected,
                                    $"{team} was selected and verified.", null, watch);
                            }
                        }

                        if (selectedButUnavailable)
                        {
                            continueAfterConfirmedUnavailable = true;
                            break;
                        }
                        attempt.Message = "Tap was sent but selected border was not verified in the target ROI.";
                        if (attemptNumber < options.MaxSelectionAttemptsPerTeam
                            && DateTimeOffset.UtcNow < selectionDeadline)
                            await Task.Delay(options.TapRetryDelayMs, cancellationToken);
                    }
                }

                SelectFarmTeamOutcome outcome = DateTimeOffset.UtcNow >= selectionDeadline
                    ? SelectFarmTeamOutcome.SelectionTimeout
                    : SelectFarmTeamOutcome.NoEligibleTeam;
                return await CompleteAsync(deviceName, result, outcome,
                    outcome == SelectFarmTeamOutcome.SelectionTimeout
                        ? "Farm team selection timed out without a verified team."
                        : "No eligible team could be selected and verified.",
                    null, lastFrame, watch, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                return Complete(result, SelectFarmTeamOutcome.Cancelled,
                    "Farm team selection was cancelled.", null, watch);
            }
            catch (Exception exception)
            {
                logger.Error($"[Farm Team Selection] DeviceName='{deviceName}', Error='{exception.Message}'", exception);
                return await CompleteAsync(deviceName, result, SelectFarmTeamOutcome.Failed,
                    "Farm team selection failed.", exception.Message,
                    lastFrame, watch, cancellationToken);
            }
        }

        private SelectedScan ScanSelected(byte[] frame)
        {
            var matches = new Dictionary<TeamNumber, ImageMatchResult>();
            foreach (KeyValuePair<TeamNumber, ImageRegion> item in options.TeamRegions)
            {
                ImageMatchResult match = Match(frame,
                    TemplateId.TeamSelectedBorderAnchor, item.Value);
                if (match != null && match.Found) matches[item.Key] = match;
            }
            return new SelectedScan(matches);
        }

        private ImageMatchResult Match(byte[] frame, TemplateId id, ImageRegion region) =>
            matcher.Find(frame, registry.LoadBytes(id), region) ?? ImageMatchResult.NotFound();

        private bool IsDisabled(byte[] frame, ImageRegion region) =>
            registry.Exists(TemplateId.TeamDisabledAnchor)
            && Match(frame, TemplateId.TeamDisabledAnchor, region).Found;

        private bool RequiredScreenTemplatesExist(out string error)
        {
            foreach (TemplateId id in ReadyTemplates)
            {
                if (!registry.Exists(id))
                {
                    error = $"Required template '{id}' was not found at '{registry.GetPath(id)}'.";
                    return false;
                }
            }
            error = null;
            return true;
        }

        private static bool IsSelectionScreen(GameDetectionResult state) =>
            state != null && state.IsSuccessful && state.State == GameState.TeamSelection
            && Found(state, TemplateId.TeamSelectionPanelAnchor)
            && Found(state, TemplateId.TeamAdjustFormationButton);

        private static bool HasEnabledAction(GameDetectionResult state) =>
            Found(state, TemplateId.TeamActionButtonEnabled);

        private static bool Found(GameDetectionResult state, TemplateId id) =>
            state != null && state.Evidence != null
            && state.Evidence.Any(item => item.TemplateId == id && item.Found);

        private static TeamNumber? FirstByPriority(IReadOnlyList<TeamNumber> selected,
            IReadOnlyList<TeamNumber> priority, IReadOnlyList<TeamNumber> allowed, bool allowTeam1)
        {
            foreach (TeamNumber team in priority)
                if (selected.Contains(team) && allowed.Contains(team)
                    && (allowTeam1 || team != TeamNumber.Team1)) return team;
            return null;
        }

        private static TemplateId? BadgeId(TeamNumber team)
        {
            switch (team)
            {
                case TeamNumber.Team2: return TemplateId.Team2Badge;
                case TeamNumber.Team3: return TemplateId.Team3Badge;
                case TeamNumber.Team4: return TemplateId.Team4Badge;
                default: return null;
            }
        }

        private static string ValidateRequest(string deviceName, TeamSelectionRequest request)
        {
            if (string.IsNullOrWhiteSpace(deviceName)) return "LDPlayer device name is required.";
            if (request == null) return "Team selection request is required.";
            if (request.AllowedTeams == null || request.AllowedTeams.Count == 0) return "AllowedTeams cannot be empty.";
            if (request.Priority == null || request.Priority.Count == 0) return "Priority cannot be empty.";
            if (request.AllowedTeams.Distinct().Count() != request.AllowedTeams.Count) return "AllowedTeams cannot contain duplicates.";
            if (request.Priority.Distinct().Count() != request.Priority.Count) return "Priority cannot contain duplicates.";
            if (request.Priority.Any(team => !request.AllowedTeams.Contains(team))) return "Priority can only contain allowed teams.";
            if (!request.AllowTeam1 && (request.AllowedTeams.Contains(TeamNumber.Team1)
                || request.Priority.Contains(TeamNumber.Team1))) return "Team1 is not allowed when AllowTeam1 is false.";
            return null;
        }

        private async Task<byte[]> TryCaptureAsync(string deviceName, CancellationToken token)
        {
            if (!options.SaveFailureScreenshots) return null;
            try { return await client.CaptureScreenshotPngAsync(deviceName, token); }
            catch (OperationCanceledException) { throw; }
            catch (Exception exception)
            {
                logger.Error($"[Farm Team Selection] DiagnosticCaptureError='{exception.Message}'", exception);
                return null;
            }
        }

        private async Task<SelectFarmTeamResult> CompleteAsync(string deviceName,
            SelectFarmTeamResult result, SelectFarmTeamOutcome outcome,
            string message, string error, byte[] frame, Stopwatch watch, CancellationToken token)
        {
            Complete(result, outcome, message, error, watch);
            if (options.SaveFailureScreenshots && frame != null
                && outcome != SelectFarmTeamOutcome.AlreadySelected
                && outcome != SelectFarmTeamOutcome.TeamSelected
                && outcome != SelectFarmTeamOutcome.Cancelled)
            {
                try { result.DiagnosticScreenshotPath = await diagnosticStore.SaveAsync(deviceName, outcome, frame, token); }
                catch (OperationCanceledException) { throw; }
                catch (Exception exception) { logger.Error($"[Farm Team Selection] DiagnosticSaveError='{exception.Message}'", exception); }
            }
            return result;
        }

        private SelectFarmTeamResult Complete(SelectFarmTeamResult result,
            SelectFarmTeamOutcome outcome, string message, string error, Stopwatch watch)
        {
            result.Outcome = outcome;
            result.Success = outcome == SelectFarmTeamOutcome.AlreadySelected
                || outcome == SelectFarmTeamOutcome.TeamSelected;
            result.Duration = watch.Elapsed;
            result.Message = message;
            result.ErrorMessage = error;
            logger.Info($"[Farm Team Selection] InitialState='{result.InitialState}', FinalState='{result.FinalState}', TeamTapCount={result.TeamTapCount}, SelectedTeam='{result.SelectedTeam}', SelectedVerified={result.SelectedStateVerified}, Outcome='{outcome}', DurationMs={result.Duration.TotalMilliseconds:F0}, Cancellation={outcome == SelectFarmTeamOutcome.Cancelled}, Error='{error ?? string.Empty}'");
            return result;
        }

        private static SelectFarmTeamResult NewResult(
            IReadOnlyList<TeamSelectionAttempt> attempts,
            IReadOnlyList<TeamNumber> attemptedTeams) => new SelectFarmTeamResult
            {
                Outcome = SelectFarmTeamOutcome.Failed,
                InitialState = GameState.Unknown,
                FinalState = GameState.Unknown,
                Attempts = attempts,
                AttemptedTeams = attemptedTeams
            };

        private static SelectFarmTeamResult Empty(SelectFarmTeamOutcome outcome, string error) =>
            new SelectFarmTeamResult
            {
                Outcome = outcome,
                Success = false,
                InitialState = GameState.Unknown,
                FinalState = GameState.Unknown,
                Message = error,
                ErrorMessage = error,
                Attempts = new TeamSelectionAttempt[0],
                AttemptedTeams = new TeamNumber[0]
            };

        private static bool HasBounds(ImageMatchResult match) =>
            match != null && match.Found && match.Width > 0 && match.Height > 0;
        private static string Join(IEnumerable<TeamNumber> teams) =>
            string.Join(",", teams.Select(team => ((int)team).ToString()));

        private void LogMatch(string deviceName, TeamNumber team, int attempt,
            ImageRegion region, ImageMatchResult badge, bool disabled)
        {
            string bounds = HasBounds(badge)
                ? $"({badge.X},{badge.Y},{badge.Width},{badge.Height})" : string.Empty;
            logger.Info($"[Farm Team Selection] DeviceName='{deviceName}', Team='{team}', Attempt={attempt}, ROI=({region.X},{region.Y},{region.Width},{region.Height}), BadgeFound={HasBounds(badge)}, BadgeBounds={bounds}, Disabled={disabled}");
        }

        private sealed class SelectedScan
        {
            public SelectedScan(Dictionary<TeamNumber, ImageMatchResult> matches)
            {
                Matches = matches;
                Teams = matches.Keys.OrderBy(team => (int)team).ToArray();
            }
            public IReadOnlyDictionary<TeamNumber, ImageMatchResult> Matches { get; }
            public IReadOnlyList<TeamNumber> Teams { get; }
            public bool IsAmbiguous => Teams.Count > 1;
        }
    }
}
