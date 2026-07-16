using ADB_Tool_Automation_Post_FB.Core.Abstractions;
using ADB_Tool_Automation_Post_FB.Core.Concurrency;
using ADB_Tool_Automation_Post_FB.Core.Diagnostics;
using ADB_Tool_Automation_Post_FB.Core.GameDetection;
using ADB_Tool_Automation_Post_FB.Core.ResourcePopup;
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
    public sealed class OpenTeamSelectionService : IOpenTeamSelectionService
    {
        private static readonly TemplateId[] RequiredTemplates =
        {
            TemplateId.ResourcePopupInfoAnchor, TemplateId.ResourcePopupIronTitle,
            TemplateId.GatherButtonEnabled, TemplateId.TeamSelectionPanelAnchor,
            TemplateId.TeamAdjustFormationButton, TemplateId.TeamActionButtonEnabled
        };

        private readonly IResourcePopupVerificationService popupVerifier;
        private readonly IGameStateDetector detector;
        private readonly ILdPlayerClient client;
        private readonly ITemplateRegistry registry;
        private readonly IImageMatcher matcher;
        private readonly IDeviceOperationLock operationLock;
        private readonly OpenTeamSelectionOptions options;
        private readonly IOpenTeamSelectionDiagnosticStore diagnosticStore;
        private readonly IDiagnosticLogger logger;

        public OpenTeamSelectionService(IResourcePopupVerificationService popupVerifier,
            IGameStateDetector detector, ILdPlayerClient client, ITemplateRegistry registry,
            IImageMatcher matcher, IDeviceOperationLock operationLock,
            OpenTeamSelectionOptions options, IOpenTeamSelectionDiagnosticStore diagnosticStore,
            IDiagnosticLogger logger)
        {
            this.popupVerifier = popupVerifier ?? throw new ArgumentNullException(nameof(popupVerifier));
            this.detector = detector ?? throw new ArgumentNullException(nameof(detector));
            this.client = client ?? throw new ArgumentNullException(nameof(client));
            this.registry = registry ?? throw new ArgumentNullException(nameof(registry));
            this.matcher = matcher ?? throw new ArgumentNullException(nameof(matcher));
            this.operationLock = operationLock ?? throw new ArgumentNullException(nameof(operationLock));
            this.options = options ?? throw new ArgumentNullException(nameof(options));
            this.diagnosticStore = diagnosticStore ?? throw new ArgumentNullException(nameof(diagnosticStore));
            this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<OpenTeamSelectionResult> OpenAsync(string deviceName, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(deviceName))
                return Empty(OpenTeamSelectionOutcome.Failed, "LDPlayer device name is required.");
            string templateError = ValidateTemplates();
            if (templateError != null)
                return Empty(OpenTeamSelectionOutcome.Failed, templateError);
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                return await operationLock.RunAsync(deviceName.Trim(),
                    token => OpenCoreAsync(deviceName.Trim(), token), cancellationToken);
            }
            catch (OperationCanceledException)
            {
                return Empty(OpenTeamSelectionOutcome.Cancelled,
                    "Open Team Selection was cancelled while waiting for the device operation lock.");
            }
        }

        private async Task<OpenTeamSelectionResult> OpenCoreAsync(
            string deviceName, CancellationToken cancellationToken)
        {
            var watch = Stopwatch.StartNew();
            var observations = new List<TeamSelectionObservation>();
            var result = NewResult(observations);
            byte[] lastFrame = null;
            try
            {
                GameDetectionResult initial = await detector.DetectAsync(deviceName, cancellationToken);
                result.InitialState = initial.State;
                result.FinalState = initial.State;
                result.FinalEvidence = initial.Evidence ?? new GameDetectionEvidence[0];
                if (!initial.IsSuccessful)
                {
                    lastFrame = await TryCaptureDiagnosticFrameAsync(deviceName, cancellationToken);
                    return await CompleteAsync(deviceName, result, OpenTeamSelectionOutcome.Failed,
                        "Initial state detection failed.", initial.ErrorMessage, lastFrame, watch, cancellationToken);
                }

                if (initial.State == GameState.TeamSelection)
                {
                    ApplyTeamEvidence(result, initial.Evidence);
                    if (result.TeamSelectionVerified)
                        return Complete(result, OpenTeamSelectionOutcome.AlreadyOpen,
                            "Team Selection is already open; no Tap was sent.", null, watch);

                    lastFrame = await client.CaptureScreenshotPngAsync(deviceName, cancellationToken);
                    TeamMatch freshTeam = MatchTeam(lastFrame);
                    ApplyTeamMatch(result, freshTeam);
                    result.FinalEvidence = freshTeam.Evidence;
                    if (freshTeam.Confirmed)
                        return Complete(result, OpenTeamSelectionOutcome.AlreadyOpen,
                            "Team Selection was verified on a fresh screenshot; no Tap was sent.", null, watch);
                    return await CompleteAsync(deviceName, result, OpenTeamSelectionOutcome.ResourcePopupNotReady,
                        "TeamSelection state lacked required UI evidence; no Tap was sent.", null,
                        lastFrame, watch, cancellationToken);
                }

                if (initial.State != GameState.ResourcePopup)
                {
                    lastFrame = await TryCaptureDiagnosticFrameAsync(deviceName, cancellationToken);
                    return await CompleteAsync(deviceName, result, OpenTeamSelectionOutcome.ResourcePopupNotReady,
                        "Current state is not a verified ResourcePopup; no Tap was sent.", null,
                        lastFrame, watch, cancellationToken);
                }

                ResourcePopupVerificationResult popup = await popupVerifier.VerifyAsync(deviceName, cancellationToken);
                result.ResourcePopupVerified = popup.Outcome == ResourcePopupOutcome.ResourcePopupReady
                    && popup.PopupAnchorVerified && popup.IronResourceVerified && popup.GatherButtonVerified
                    && HasBounds(popup.GatherButtonMatch);
                if (!result.ResourcePopupVerified)
                {
                    lastFrame = await TryCaptureDiagnosticFrameAsync(deviceName, cancellationToken);
                    return await CompleteAsync(deviceName, result, OpenTeamSelectionOutcome.ResourcePopupNotReady,
                        "Resource Popup is not ready; Gather was not tapped.", popup.ErrorMessage,
                        lastFrame, watch, cancellationToken);
                }

                lastFrame = await client.CaptureScreenshotPngAsync(deviceName, cancellationToken);
                PopupMatch freshPopup = MatchPopup(lastFrame);
                GameDetectionResult freshState = detector.Detect(lastFrame);
                result.FinalEvidence = freshPopup.Evidence;
                if (!freshState.IsSuccessful || freshState.State != GameState.ResourcePopup)
                    return await CompleteAsync(deviceName, result, OpenTeamSelectionOutcome.ResourcePopupNotReady,
                        "Resource Popup disappeared before Gather could be tapped.", freshState.ErrorMessage,
                        lastFrame, watch, cancellationToken);
                if (!freshPopup.Ready || !HasBounds(freshPopup.Gather.MatchResult))
                    return await CompleteAsync(deviceName, result, OpenTeamSelectionOutcome.GatherButtonNotAvailable,
                        "Fresh Gather button bounds are unavailable; no Tap was sent.", null,
                        lastFrame, watch, cancellationToken);

                await TapGatherAsync(deviceName, freshPopup.Gather.MatchResult, result, cancellationToken);
                TimeSpan timeout = TimeSpan.FromSeconds(options.TransitionTimeoutSeconds);
                bool confirmedObserved = false;
                while (watch.Elapsed < timeout)
                {
                    await Task.Delay(options.PollIntervalMs, cancellationToken);
                    lastFrame = await client.CaptureScreenshotPngAsync(deviceName, cancellationToken);
                    GameDetectionResult state = detector.Detect(lastFrame);
                    if (!state.IsSuccessful)
                        return await CompleteAsync(deviceName, result, OpenTeamSelectionOutcome.Failed,
                            "State detection failed during Team Selection transition.", state.ErrorMessage,
                            lastFrame, watch, cancellationToken);

                    TeamMatch team = MatchTeam(lastFrame);
                    result.ObservedFrameCount++;
                    result.FinalState = team.Confirmed ? GameState.TeamSelection : state.State;
                    result.FinalEvidence = team.Evidence;
                    ApplyTeamMatch(result, team);
                    observations.Add(new TeamSelectionObservation
                    {
                        Timestamp = DateTimeOffset.Now, State = result.FinalState,
                        PanelAnchorFound = team.Panel.Found,
                        AdjustFormationButtonFound = team.Adjust.Found,
                        TeamActionButtonFound = team.Action.Found,
                        TeamSelectionConfirmed = team.Confirmed,
                        TeamSelectionReady = team.Ready,
                        Message = team.Ready ? "All Team Selection signals matched."
                            : team.Confirmed ? "Team Selection confirmed but not ready."
                            : "Team Selection not confirmed."
                    });
                    LogObservation(deviceName, result, team, state.State);

                    if (team.Confirmed)
                    {
                        confirmedObserved = true;
                        result.TransientUnknownFrameCount = 0;
                        if (team.Ready || !options.RequireReadyForSuccess)
                            return Complete(result, OpenTeamSelectionOutcome.TeamSelectionOpened,
                                team.Ready ? "Team Selection opened and is ready."
                                    : "Team Selection opened with the required confirmation signals.", null, watch);
                        continue;
                    }

                    if (state.State == GameState.Unknown)
                    {
                        result.TransientUnknownFrameCount++;
                        if (result.TransientUnknownFrameCount > options.MaxTransientUnknownFrames)
                            return await CompleteAsync(deviceName, result, OpenTeamSelectionOutcome.TransitionTimeout,
                                "Transient Unknown frame limit was exceeded.", null,
                                lastFrame, watch, cancellationToken);
                        continue;
                    }
                    result.TransientUnknownFrameCount = 0;

                    if (state.State == GameState.ResourcePopup
                        && result.GatherTapCount < options.MaxGatherTapAttempts)
                    {
                        lastFrame = await client.CaptureScreenshotPngAsync(deviceName, cancellationToken);
                        GameDetectionResult retryState = detector.Detect(lastFrame);
                        PopupMatch retryPopup = MatchPopup(lastFrame);
                        if (!retryState.IsSuccessful || retryState.State != GameState.ResourcePopup
                            || !retryPopup.Ready || !HasBounds(retryPopup.Gather.MatchResult))
                            return await CompleteAsync(deviceName, result, OpenTeamSelectionOutcome.TransitionTimeout,
                                "Gather retry was unsafe because Resource Popup was no longer ready.",
                                retryState.ErrorMessage, lastFrame, watch, cancellationToken);
                        await Task.Delay(options.GatherTapRetryDelayMs, cancellationToken);
                        await TapGatherAsync(deviceName, retryPopup.Gather.MatchResult, result, cancellationToken);
                    }
                    else if (state.State != GameState.ResourcePopup)
                    {
                        return await CompleteAsync(deviceName, result, OpenTeamSelectionOutcome.TransitionTimeout,
                            "Transition left Resource Popup without opening Team Selection.", null,
                            lastFrame, watch, cancellationToken);
                    }
                }

                return await CompleteAsync(deviceName, result,
                    confirmedObserved ? OpenTeamSelectionOutcome.TeamSelectionOpenedButNotReady
                        : OpenTeamSelectionOutcome.TransitionTimeout,
                    confirmedObserved ? "Team Selection opened but did not become ready before timeout."
                        : "Team Selection did not open before timeout.",
                    null, lastFrame, watch, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                return Complete(result, OpenTeamSelectionOutcome.Cancelled,
                    "Open Team Selection was cancelled.", null, watch);
            }
            catch (Exception exception)
            {
                logger.Error($"[Open Team Selection] DeviceName='{deviceName}', Error='{exception.Message}'", exception);
                return await CompleteAsync(deviceName, result, OpenTeamSelectionOutcome.Failed,
                    "Open Team Selection failed.", exception.Message, lastFrame, watch, cancellationToken);
            }
        }

        private async Task TapGatherAsync(string deviceName, ImageMatchResult match,
            OpenTeamSelectionResult result, CancellationToken token)
        {
            int x = match.CenterX;
            int y = match.CenterY;
            logger.Info($"[Open Team Selection] DeviceName='{deviceName}', GatherBounds=({match.X},{match.Y},{match.Width},{match.Height}), GatherTap=({x},{y}), Attempt={result.GatherTapCount + 1}");
            await client.TapAsync(deviceName, x, y, token);
            result.GatherButtonVerified = true;
            result.GatherButtonMatch = match;
            result.GatherTapCount++;
        }

        private async Task<byte[]> TryCaptureDiagnosticFrameAsync(
            string deviceName, CancellationToken cancellationToken)
        {
            if (!options.SaveFailureScreenshots) return null;
            try
            {
                return await client.CaptureScreenshotPngAsync(deviceName, cancellationToken);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception exception)
            {
                logger.Error($"[Open Team Selection] DeviceName='{deviceName}', DiagnosticCaptureError='{exception.Message}'", exception);
                return null;
            }
        }

        private TeamMatch MatchTeam(byte[] frame)
        {
            GameDetectionEvidence panel = Match(frame, TemplateId.TeamSelectionPanelAnchor, options.TeamSelectionRegion);
            GameDetectionEvidence adjust = Match(frame, TemplateId.TeamAdjustFormationButton, options.TeamSelectionRegion);
            GameDetectionEvidence action = Match(frame, TemplateId.TeamActionButtonEnabled, options.TeamSelectionRegion);
            int signals = (panel.Found ? 1 : 0) + (adjust.Found ? 1 : 0) + (action.Found ? 1 : 0);
            bool confirmed = panel.Found && signals >= options.RequiredTeamSelectionSignals
                && (adjust.Found || action.Found);
            return new TeamMatch(panel, adjust, action, confirmed,
                panel.Found && adjust.Found && action.Found);
        }

        private PopupMatch MatchPopup(byte[] frame)
        {
            GameDetectionEvidence anchor = Match(frame, TemplateId.ResourcePopupInfoAnchor, options.ResourcePopupRegion);
            GameDetectionEvidence iron = Match(frame, TemplateId.ResourcePopupIronTitle, options.ResourcePopupRegion);
            GameDetectionEvidence gather = Match(frame, TemplateId.GatherButtonEnabled, options.ResourcePopupRegion);
            return new PopupMatch(anchor, iron, gather, anchor.Found && iron.Found && gather.Found);
        }

        private GameDetectionEvidence Match(byte[] frame, TemplateId id, ImageRegion region)
        {
            ImageMatchResult match = matcher.Find(frame, registry.LoadBytes(id), region);
            return new GameDetectionEvidence
            {
                TemplateId = id, TemplateExists = true,
                Found = match != null && match.Found, MatchResult = match,
                Confidence = match?.Confidence, SearchRegion = region,
                Message = match != null && match.Found
                    ? $"Template '{id}' matched inside configured ROI."
                    : $"Template '{id}' did not match inside configured ROI."
            };
        }

        private static bool HasBounds(ImageMatchResult match) =>
            match != null && match.Found && match.Width > 0 && match.Height > 0;

        private static void ApplyTeamEvidence(OpenTeamSelectionResult result,
            IReadOnlyList<GameDetectionEvidence> evidence)
        {
            bool panel = Found(evidence, TemplateId.TeamSelectionPanelAnchor);
            bool adjust = Found(evidence, TemplateId.TeamAdjustFormationButton);
            bool action = Found(evidence, TemplateId.TeamActionButtonEnabled);
            result.PanelAnchorVerified = panel;
            result.AdjustFormationButtonVerified = adjust;
            result.TeamActionButtonVerified = action;
            result.TeamSelectionVerified = panel && (adjust || action);
            result.TeamSelectionReady = panel && adjust && action;
        }

        private static void ApplyTeamMatch(OpenTeamSelectionResult result, TeamMatch match)
        {
            result.PanelAnchorVerified = match.Panel.Found;
            result.AdjustFormationButtonVerified = match.Adjust.Found;
            result.TeamActionButtonVerified = match.Action.Found;
            result.TeamSelectionVerified = match.Confirmed;
            result.TeamSelectionReady = match.Ready;
        }

        private static bool Found(IReadOnlyList<GameDetectionEvidence> evidence, TemplateId id) =>
            evidence != null && evidence.Any(item => item.TemplateId == id && item.Found);

        private string ValidateTemplates()
        {
            foreach (TemplateId id in RequiredTemplates)
            {
                try
                {
                    string path = registry.GetPath(id);
                    if (!registry.Exists(id)) return $"Required template '{id}' was not found at '{path}'.";
                }
                catch (Exception exception) { return $"Required template '{id}' could not be resolved: {exception.Message}"; }
            }
            return null;
        }

        private async Task<OpenTeamSelectionResult> CompleteAsync(string deviceName,
            OpenTeamSelectionResult result, OpenTeamSelectionOutcome outcome, string message,
            string error, byte[] frame, Stopwatch watch, CancellationToken token)
        {
            Complete(result, outcome, message, error, watch);
            if (options.SaveFailureScreenshots && frame != null
                && outcome != OpenTeamSelectionOutcome.Cancelled
                && outcome != OpenTeamSelectionOutcome.AlreadyOpen
                && outcome != OpenTeamSelectionOutcome.TeamSelectionOpened)
            {
                try { result.DiagnosticScreenshotPath = await diagnosticStore.SaveAsync(deviceName, outcome, frame, token); }
                catch (OperationCanceledException) { throw; }
                catch (Exception exception) { logger.Error($"[Open Team Selection] DiagnosticSaveError='{exception.Message}'", exception); }
            }
            return result;
        }

        private OpenTeamSelectionResult Complete(OpenTeamSelectionResult result,
            OpenTeamSelectionOutcome outcome, string message, string error, Stopwatch watch)
        {
            result.Outcome = outcome;
            result.Success = outcome == OpenTeamSelectionOutcome.AlreadyOpen
                || outcome == OpenTeamSelectionOutcome.TeamSelectionOpened;
            result.Duration = watch.Elapsed;
            result.Message = message;
            result.ErrorMessage = error;
            logger.Info($"[Open Team Selection] InitialState='{result.InitialState}', FinalState='{result.FinalState}', ResourcePopupVerified={result.ResourcePopupVerified}, GatherTapCount={result.GatherTapCount}, TeamSelectionVerified={result.TeamSelectionVerified}, TeamSelectionReady={result.TeamSelectionReady}, Outcome='{outcome}', DurationMs={result.Duration.TotalMilliseconds:F0}, Cancellation={outcome == OpenTeamSelectionOutcome.Cancelled}, Error='{error ?? string.Empty}'");
            return result;
        }

        private void LogObservation(string deviceName, OpenTeamSelectionResult result,
            TeamMatch team, GameState detectedState)
        {
            logger.Info($"[Open Team Selection Observation] DeviceName='{deviceName}', Index={result.ObservedFrameCount}, DetectedState='{detectedState}', Panel={Bounds(team.Panel)}, Adjust={Bounds(team.Adjust)}, Action={Bounds(team.Action)}, Confirmed={team.Confirmed}, Ready={team.Ready}, UnknownFrames={result.TransientUnknownFrameCount}");
        }

        private static string Bounds(GameDetectionEvidence item) => item.Found && item.MatchResult != null
            ? $"true:({item.MatchResult.X},{item.MatchResult.Y},{item.MatchResult.Width},{item.MatchResult.Height})" : "false";

        private static OpenTeamSelectionResult NewResult(IReadOnlyList<TeamSelectionObservation> observations) =>
            new OpenTeamSelectionResult
            {
                Outcome = OpenTeamSelectionOutcome.Failed,
                InitialState = GameState.Unknown, FinalState = GameState.Unknown,
                FinalEvidence = new GameDetectionEvidence[0], Observations = observations
            };

        private static OpenTeamSelectionResult Empty(OpenTeamSelectionOutcome outcome, string error) =>
            new OpenTeamSelectionResult
            {
                Outcome = outcome, Success = false, InitialState = GameState.Unknown,
                FinalState = GameState.Unknown, ErrorMessage = error, Message = error,
                FinalEvidence = new GameDetectionEvidence[0], Observations = new TeamSelectionObservation[0]
            };

        private sealed class TeamMatch
        {
            public TeamMatch(GameDetectionEvidence panel, GameDetectionEvidence adjust,
                GameDetectionEvidence action, bool confirmed, bool ready)
            { Panel = panel; Adjust = adjust; Action = action; Confirmed = confirmed; Ready = ready; Evidence = new[] { panel, adjust, action }; }
            public GameDetectionEvidence Panel { get; }
            public GameDetectionEvidence Adjust { get; }
            public GameDetectionEvidence Action { get; }
            public bool Confirmed { get; }
            public bool Ready { get; }
            public IReadOnlyList<GameDetectionEvidence> Evidence { get; }
        }

        private sealed class PopupMatch
        {
            public PopupMatch(GameDetectionEvidence anchor, GameDetectionEvidence iron,
                GameDetectionEvidence gather, bool ready)
            { Anchor = anchor; Iron = iron; Gather = gather; Ready = ready; Evidence = new[] { anchor, iron, gather }; }
            public GameDetectionEvidence Anchor { get; }
            public GameDetectionEvidence Iron { get; }
            public GameDetectionEvidence Gather { get; }
            public bool Ready { get; }
            public IReadOnlyList<GameDetectionEvidence> Evidence { get; }
        }
    }
}
