using IK_Auto_ADB.Core.Abstractions;
using IK_Auto_ADB.Core.Concurrency;
using IK_Auto_ADB.Core.Diagnostics;
using IK_Auto_ADB.Core.GameDetection;
using IK_Auto_ADB.Core.ResourcePopup;
using IK_Auto_ADB.Core.ResourceSearch;
using IK_Auto_ADB.Core.TeamSelection;
using IK_Auto_ADB.Core.Vision;
using IK_Auto_ADB.Infrastructure.ResourcePopup;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace IK_Auto_ADB.Infrastructure.TeamSelection
{
    public sealed class OpenTeamSelectionService : IResourceAwareOpenTeamSelectionService
    {
        private const int MinimumPostGatherFrames = 2;
        private const int MaximumPostGatherFrames = 3;
        private static readonly TemplateId[] RequiredTemplates =
        {
            TemplateId.ResourcePopupInfoAnchor, TemplateId.GatherButtonEnabled,
            TemplateId.TeamSelectionPanelAnchor,
            TemplateId.TeamAdjustFormationButton, TemplateId.TeamActionButtonEnabled
        };

        private readonly IResourcePopupVerificationService popupVerifier;
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
            if (detector == null) throw new ArgumentNullException(nameof(detector));
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
            return await OpenAsync(deviceName, ResourceType.Iron, cancellationToken);
        }

        public async Task<OpenTeamSelectionResult> OpenAsync(string deviceName,
            ResourceType resourceType, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(deviceName))
                return Empty(OpenTeamSelectionOutcome.Failed, "LDPlayer device name is required.");
            string templateError = ValidateTemplates(resourceType);
            if (templateError != null)
                return Empty(OpenTeamSelectionOutcome.Failed, templateError);
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                return await operationLock.RunAsync(deviceName.Trim(),
                    token => OpenCoreAsync(deviceName.Trim(), resourceType, token), cancellationToken);
            }
            catch (OperationCanceledException)
            {
                return Empty(OpenTeamSelectionOutcome.Cancelled,
                    "Open Team Selection was cancelled while waiting for the device operation lock.");
            }
        }

        private async Task<OpenTeamSelectionResult> OpenCoreAsync(
            string deviceName, ResourceType expectedResource, CancellationToken cancellationToken)
        {
            var watch = Stopwatch.StartNew();
            var observations = new List<TeamSelectionObservation>();
            var result = NewResult(observations);
            byte[] lastFrame = null;
            try
            {
                // This workflow can only safely start from two focused overlays:
                // TeamSelection or ResourcePopup.  A global state pass here scans
                // unrelated City/WorldMap/Search templates and used to dominate the
                // normal Gather -> TeamSelection transition under multi-device load.
                lastFrame = await client.CaptureScreenshotPngAsync(deviceName, cancellationToken);
                TeamMatch initialTeam = MatchTeam(lastFrame);
                result.InitialState = initialTeam.Confirmed
                    ? GameState.TeamSelection : GameState.Unknown;
                result.FinalState = result.InitialState;
                result.FinalEvidence = initialTeam.Evidence;
                ApplyTeamMatch(result, initialTeam);
                if (initialTeam.Confirmed)
                    return Complete(result, OpenTeamSelectionOutcome.AlreadyOpen,
                        "Team Selection is already open; no Tap was sent.", null, watch);

                ResourcePopupVerificationResult popup;
                if (popupVerifier is IResourceAwarePopupVerificationService resourceAware)
                    popup = await resourceAware.VerifyAsync(deviceName, expectedResource, cancellationToken);
                else if (expectedResource == ResourceType.Iron)
                    popup = await popupVerifier.VerifyAsync(deviceName, cancellationToken);
                else
                    return await CompleteAsync(deviceName, result, OpenTeamSelectionOutcome.ResourcePopupNotReady,
                        $"The popup verifier is not resource-aware for {expectedResource}; no Tap was sent.", null,
                        lastFrame, watch, cancellationToken);
                result.ResourcePopupVerified = popup.Outcome == ResourcePopupOutcome.ResourcePopupReady
                    && popup.PopupAnchorVerified && popup.GatherButtonVerified
                    && (popup.ExpectedResourceVerified || popup.ResourceVerified
                        || (expectedResource == ResourceType.Iron && popup.IronResourceVerified))
                    && HasBounds(popup.GatherButtonMatch);
                if (!result.ResourcePopupVerified)
                {
                    lastFrame = await TryCaptureDiagnosticFrameAsync(deviceName, cancellationToken);
                    return await CompleteAsync(deviceName, result, OpenTeamSelectionOutcome.ResourcePopupNotReady,
                        "Resource Popup is not ready; Gather was not tapped.", popup.ErrorMessage,
                        lastFrame, watch, cancellationToken);
                }

                lastFrame = await client.CaptureScreenshotPngAsync(deviceName, cancellationToken);
                PopupMatch freshPopup = MatchPopup(lastFrame, expectedResource);
                result.FinalState = freshPopup.Anchor.Found
                    ? GameState.ResourcePopup : GameState.Unknown;
                result.FinalEvidence = freshPopup.Evidence;
                // The production tap target is Gather itself.  Rematching that popup-only
                // control on the fresh frame is the authoritative pre-tap check; requiring the
                // older title crop here would reintroduce the false negative already resolved
                // by ResourcePopupVerificationService.
                if (!freshPopup.Gather.Found)
                    return await CompleteAsync(deviceName, result, OpenTeamSelectionOutcome.ResourcePopupNotReady,
                        "Resource Popup disappeared before Gather could be tapped.", null,
                        lastFrame, watch, cancellationToken);
                if (!HasBounds(freshPopup.Gather.MatchResult))
                    return await CompleteAsync(deviceName, result, OpenTeamSelectionOutcome.GatherButtonNotAvailable,
                        "Fresh Gather button bounds are unavailable; no Tap was sent.", null,
                        lastFrame, watch, cancellationToken);

                await TapGatherAsync(deviceName, freshPopup.Gather.MatchResult, result, cancellationToken);
                DateTimeOffset transitionDeadline = DateTimeOffset.UtcNow.AddSeconds(
                    options.TransitionTimeoutSeconds);
                int framesSinceLastGatherTap = 0;
                bool confirmedObserved = false;
                // Under multi-device load the first useful capture/match can finish after the
                // wall-clock deadline. That first frame is commonly the panel animation (only
                // Adjust is visible). Always inspect a bounded second frame, while retaining a
                // hard frame cap so queue pressure cannot make this an unbounded operation.
                while (framesSinceLastGatherTap < MaximumPostGatherFrames
                    && (DateTimeOffset.UtcNow < transitionDeadline
                        || framesSinceLastGatherTap < MinimumPostGatherFrames))
                {
                    await Task.Delay(options.PollIntervalMs, cancellationToken);
                    lastFrame = await client.CaptureScreenshotPngAsync(deviceName, cancellationToken);
                    TeamMatch team = MatchTeam(lastFrame);
                    framesSinceLastGatherTap++;
                    result.ObservedFrameCount++;
                    result.FinalEvidence = team.Evidence;
                    ApplyTeamMatch(result, team);
                    if (team.Confirmed)
                    {
                        result.FinalState = GameState.TeamSelection;
                        confirmedObserved = true;
                        result.TransientUnknownFrameCount = 0;
                        observations.Add(new TeamSelectionObservation
                        {
                            Timestamp = DateTimeOffset.Now,
                            State = GameState.TeamSelection,
                            PanelAnchorFound = team.Panel.Found,
                            AdjustFormationButtonFound = team.Adjust.Found,
                            TeamActionButtonFound = team.Action.Found,
                            TeamSelectionConfirmed = true,
                            TeamSelectionReady = team.Ready,
                            Message = team.Ready ? "All Team Selection signals matched."
                                : "Team Selection confirmed but not ready."
                        });
                        LogObservation(deviceName, result, team, GameState.TeamSelection);
                        if (team.Ready || !options.RequireReadyForSuccess)
                            return Complete(result, OpenTeamSelectionOutcome.TeamSelectionOpened,
                                team.Ready ? "Team Selection opened and is ready."
                                    : "Team Selection opened with the required confirmation signals.", null, watch);
                        continue;
                    }

                    PopupMatch visiblePopup = MatchPopup(lastFrame, expectedResource);
                    result.FinalState = visiblePopup.Anchor.Found
                        ? GameState.ResourcePopup : GameState.Unknown;
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
                    LogObservation(deviceName, result, team, result.FinalState);

                    if (result.FinalState == GameState.Unknown)
                    {
                        result.TransientUnknownFrameCount++;
                        if (result.TransientUnknownFrameCount > options.MaxTransientUnknownFrames)
                            return await CompleteAsync(deviceName, result, OpenTeamSelectionOutcome.TransitionTimeout,
                                "Transient Unknown frame limit was exceeded.", null,
                                lastFrame, watch, cancellationToken);
                        continue;
                    }
                    result.TransientUnknownFrameCount = 0;

                    if (visiblePopup.Gather.Found
                        && result.GatherTapCount < options.MaxGatherTapAttempts)
                    {
                        lastFrame = await client.CaptureScreenshotPngAsync(deviceName, cancellationToken);
                        PopupMatch retryPopup = MatchPopup(lastFrame, expectedResource);
                        if (!retryPopup.Ready || !HasBounds(retryPopup.Gather.MatchResult))
                            return await CompleteAsync(deviceName, result, OpenTeamSelectionOutcome.TransitionTimeout,
                                "Gather retry was unsafe because Resource Popup was no longer ready.",
                                null, lastFrame, watch, cancellationToken);
                        await Task.Delay(options.GatherTapRetryDelayMs, cancellationToken);
                        await TapGatherAsync(deviceName, retryPopup.Gather.MatchResult, result, cancellationToken);
                        // A busy multi-device screenshot queue can consume the original
                        // transition window before this bounded retry is sent. The retry is a
                        // new accepted Gather input, so give only that attempt its own bounded
                        // visual-confirmation window. MaxGatherTapAttempts still caps input.
                        transitionDeadline = DateTimeOffset.UtcNow.AddSeconds(
                            options.TransitionTimeoutSeconds);
                        framesSinceLastGatherTap = 0;
                    }
                    else if (visiblePopup.Gather.Found)
                    {
                        // The fresh popup remains visible but the bounded Gather retry count is
                        // exhausted. Keep observing until the transition deadline; do not mistake
                        // the underlying WorldMap classification for a vanished popup.
                        continue;
                    }
                    else if (result.FinalState != GameState.ResourcePopup)
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

        private PopupMatch MatchPopup(byte[] frame, ResourceType expectedResource)
        {
            GameDetectionEvidence anchor = Match(frame, TemplateId.ResourcePopupInfoAnchor, options.ResourcePopupRegion);
            GameDetectionEvidence resource = MatchPopupTitle(frame,
                ResourceTemplateMap.PopupTitle(expectedResource));
            GameDetectionEvidence gather = Match(frame, TemplateId.GatherButtonEnabled, options.ResourcePopupRegion);
            return new PopupMatch(anchor, resource, gather, gather.Found);
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

        private GameDetectionEvidence MatchPopupTitle(byte[] frame, TemplateId id)
        {
            GameDetectionEvidence direct = Match(frame, id, options.ResourcePopupRegion);
            if (direct.Found) return direct;

            byte[] stableTitle = ResourcePopupTitleTemplateCropper.TryCreateStableTitle(
                registry.LoadBytes(id));
            if (stableTitle == null) return direct;

            ImageMatchResult match = matcher.Find(frame, stableTitle, options.ResourcePopupRegion);
            return new GameDetectionEvidence
            {
                TemplateId = id,
                TemplateExists = true,
                Found = match != null && match.Found,
                MatchResult = match,
                Confidence = match?.Confidence,
                SearchRegion = options.ResourcePopupRegion,
                Message = match != null && match.Found
                    ? $"Template '{id}' matched by its stable title-only region inside configured ROI."
                    : $"Template '{id}' did not match directly or by its stable title-only region inside configured ROI."
            };
        }

        private static bool HasBounds(ImageMatchResult match) =>
            match != null && match.Found && match.Width > 0 && match.Height > 0;

        private static void ApplyTeamMatch(OpenTeamSelectionResult result, TeamMatch match)
        {
            result.PanelAnchorVerified = match.Panel.Found;
            result.AdjustFormationButtonVerified = match.Adjust.Found;
            result.TeamActionButtonVerified = match.Action.Found;
            result.TeamSelectionVerified = match.Confirmed;
            result.TeamSelectionReady = match.Ready;
        }

        private string ValidateTemplates(ResourceType expectedResource)
        {
            TemplateId[] templates;
            try
            {
                templates = RequiredTemplates.Concat(new[]
                    { ResourceTemplateMap.PopupTitle(expectedResource) }).ToArray();
            }
            catch (ArgumentOutOfRangeException exception)
            {
                return exception.Message;
            }
            foreach (TemplateId id in templates)
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
            public PopupMatch(GameDetectionEvidence anchor, GameDetectionEvidence resource,
                GameDetectionEvidence gather, bool ready)
            { Anchor = anchor; Resource = resource; Gather = gather; Ready = ready; Evidence = new[] { anchor, resource, gather }; }
            public GameDetectionEvidence Anchor { get; }
            public GameDetectionEvidence Resource { get; }
            public GameDetectionEvidence Gather { get; }
            public bool Ready { get; }
            public IReadOnlyList<GameDetectionEvidence> Evidence { get; }
        }
    }
}
