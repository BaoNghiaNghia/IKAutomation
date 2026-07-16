using ADB_Tool_Automation_Post_FB.Core.Abstractions;
using ADB_Tool_Automation_Post_FB.Core.Concurrency;
using ADB_Tool_Automation_Post_FB.Core.Diagnostics;
using ADB_Tool_Automation_Post_FB.Core.GameDetection;
using ADB_Tool_Automation_Post_FB.Core.ResourceSearch;
using ADB_Tool_Automation_Post_FB.Core.ResourcePopup;
using ADB_Tool_Automation_Post_FB.Core.Vision;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace ADB_Tool_Automation_Post_FB.Infrastructure.ResourceSearch
{
    public sealed class ResourceSearchExecutionService : IResourceSearchExecutionService
    {
        private static readonly TemplateId[] RequiredTemplates =
        {
            TemplateId.SearchButtonEnabled,
            TemplateId.ResourceNotFoundToastAnchor,
            TemplateId.ResourceNotFoundToastActionAnchor,
            TemplateId.ResourcePopupInfoAnchor,
            TemplateId.ResourcePopupIronTitle,
            TemplateId.GatherButtonEnabled
        };

        private readonly IResourceSearchConfigurationService configurationService;
        private readonly IGameStateDetector detector;
        private readonly ILdPlayerClient ldPlayerClient;
        private readonly ITemplateRegistry templateRegistry;
        private readonly IImageMatcher imageMatcher;
        private readonly IFrameStabilityDetector stabilityDetector;
        private readonly IDeviceOperationLock operationLock;
        private readonly ResourceSearchExecutionOptions options;
        private readonly IResourceSearchDiagnosticStore diagnosticStore;
        private readonly IDiagnosticLogger logger;
        private readonly IResourcePopupVerificationService popupVerificationService;

        public ResourceSearchExecutionService(
            IResourceSearchConfigurationService configurationService,
            IGameStateDetector detector,
            ILdPlayerClient ldPlayerClient,
            ITemplateRegistry templateRegistry,
            IImageMatcher imageMatcher,
            IFrameStabilityDetector stabilityDetector,
            IDeviceOperationLock operationLock,
            ResourceSearchExecutionOptions options,
            IResourceSearchDiagnosticStore diagnosticStore,
            IDiagnosticLogger logger,
            IResourcePopupVerificationService popupVerificationService = null)
        {
            this.configurationService = configurationService ?? throw new ArgumentNullException(nameof(configurationService));
            this.detector = detector ?? throw new ArgumentNullException(nameof(detector));
            this.ldPlayerClient = ldPlayerClient ?? throw new ArgumentNullException(nameof(ldPlayerClient));
            this.templateRegistry = templateRegistry ?? throw new ArgumentNullException(nameof(templateRegistry));
            this.imageMatcher = imageMatcher ?? throw new ArgumentNullException(nameof(imageMatcher));
            this.stabilityDetector = stabilityDetector ?? throw new ArgumentNullException(nameof(stabilityDetector));
            this.operationLock = operationLock ?? throw new ArgumentNullException(nameof(operationLock));
            this.options = options ?? throw new ArgumentNullException(nameof(options));
            this.diagnosticStore = diagnosticStore ?? throw new ArgumentNullException(nameof(diagnosticStore));
            this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
            this.popupVerificationService = popupVerificationService;
        }

        public async Task<ResourceSearchExecutionResult> ExecuteAsync(string deviceName,
            ResourceSearchExecutionRequest request, CancellationToken cancellationToken)
        {
            var watch = Stopwatch.StartNew();
            if (string.IsNullOrWhiteSpace(deviceName))
                return EmptyResult(ResourceSearchOutcome.Failed, watch, "LDPlayer device name is required.");
            string validationError = ValidateRequest(request);
            if (validationError != null)
                return EmptyResult(ResourceSearchOutcome.Failed, watch, validationError);
            string templateError = ValidateTemplates();
            if (templateError != null)
                return EmptyResult(ResourceSearchOutcome.Failed, watch, templateError);

            try
            {
                return await operationLock.RunAsync(deviceName.Trim(),
                    token => ExecuteCoreAsync(deviceName.Trim(), request, watch, token), cancellationToken);
            }
            catch (OperationCanceledException)
            {
                logger.Info($"[Resource Search Execution] DeviceName='{deviceName}', Outcome='Cancelled', "
                    + $"DurationMs={watch.Elapsed.TotalMilliseconds:F0}, Cancellation=true");
                return EmptyResult(ResourceSearchOutcome.Cancelled, watch, "Resource search was cancelled.");
            }
        }

        private async Task<ResourceSearchExecutionResult> ExecuteCoreAsync(string deviceName,
            ResourceSearchExecutionRequest request, Stopwatch watch, CancellationToken cancellationToken)
        {
            var observations = new List<ResourceSearchObservation>();
            var result = NewResult(observations);
            var context = new ObservationContext { BurstTimestamp = DateTimeOffset.Now };
            try
            {
                LogStart(deviceName, request);
                if (request.ConfigureBeforeSearch)
                {
                    result.ConfigurationResult = await configurationService.ConfigureAsync(
                        deviceName, request.Configuration, cancellationToken);
                    result.InitialState = result.ConfigurationResult.InitialState;
                    result.FinalState = result.ConfigurationResult.FinalState;
                    if (!result.ConfigurationResult.Success)
                        return await CompleteAsync(deviceName, result, context, ResourceSearchOutcome.Failed,
                            "Search configuration failed; Search was not tapped.",
                            result.ConfigurationResult.ErrorMessage ?? result.ConfigurationResult.Message, watch, cancellationToken);
                }
                else
                {
                    GameDetectionResult current = await detector.DetectAsync(deviceName, cancellationToken);
                    result.InitialState = current.State;
                    result.FinalState = current.State;
                    if (!current.IsSuccessful || !IsPanelConfirmed(current))
                        return await CompleteAsync(deviceName, result, context, ResourceSearchOutcome.Failed,
                            "Current screen is not a verified ResourceSearchPanel; Search was not tapped.",
                            current.ErrorMessage, watch, cancellationToken);
                }

                Stopwatch searchResultWatch = null;
                for (int attempt = 1; attempt <= options.MaxSearchTapAttempts; attempt++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    byte[] beforeTap = await ldPlayerClient.CaptureScreenshotPngAsync(deviceName, cancellationToken);
                    string resolutionError = ValidateResolution(beforeTap);
                    if (resolutionError != null)
                        return await CompleteAsync(deviceName, result, context, ResourceSearchOutcome.Failed,
                            "Search screenshot resolution is invalid.", resolutionError, watch, cancellationToken);

                    ImageMatchResult button = Match(beforeTap, TemplateId.SearchButtonEnabled, null);
                    if (!HasBounds(button))
                        return await CompleteAsync(deviceName, result, context, ResourceSearchOutcome.Failed,
                            "SearchButtonEnabled was not found with valid bounds; no Tap was sent.", null,
                            watch, cancellationToken);

                    GameDetectionResult beforeTapState = detector.Detect(beforeTap);
                    if (!beforeTapState.IsSuccessful || !IsPanelConfirmed(beforeTapState))
                        return await CompleteAsync(deviceName, result, context, ResourceSearchOutcome.Failed,
                            "ResourceSearchPanel was not verified on the fresh pre-Tap screenshot.",
                            beforeTapState.ErrorMessage, watch, cancellationToken);

                    result.SearchButtonVerified = true;
                    int tapX = button.CenterX;
                    int tapY = button.CenterY;
                    logger.Info($"[Resource Search Execution] DeviceName='{deviceName}', Attempt={attempt}, "
                        + $"SearchButtonBounds=({button.X},{button.Y},{button.Width},{button.Height}), "
                        + $"SearchTap=({tapX},{tapY}), SearchTapCount={result.SearchTapCount + 1}");
                    await ldPlayerClient.TapAsync(deviceName, tapX, tapY, cancellationToken);
                    result.SearchTapCount++;
                    if (searchResultWatch == null)
                        searchResultWatch = Stopwatch.StartNew();
                    context.PreviousFrame = beforeTap;
                    context.PreviousPanelConfirmed = true;
                    context.LastPanelConfirmed = true;

                    int fastWindowMs = Math.Min(options.NotFoundObservationWindowMs,
                        options.SearchTapVerificationTimeoutSeconds * 1000);
                    var fastWatch = Stopwatch.StartNew();
                    while (fastWatch.ElapsedMilliseconds < fastWindowMs
                        && searchResultWatch.Elapsed < TimeSpan.FromSeconds(options.SearchResultTimeoutSeconds))
                    {
                        ObservationDecision decision = await ObserveFrameAsync(
                            deviceName, result, observations, context, cancellationToken);
                        if (decision.HasOutcome)
                            return await CompleteAsync(deviceName, result, context, decision.Outcome,
                                decision.Message, decision.ErrorMessage, watch, cancellationToken);
                        await Task.Delay(options.NotFoundFastPollIntervalMs, cancellationToken);
                    }

                    if (context.LastPanelConfirmed && !result.CameraMovementObserved)
                    {
                        if (attempt < options.MaxSearchTapAttempts)
                            continue;
                        return await CompleteAsync(deviceName, result, context, ResourceSearchOutcome.Timeout,
                            "Search result was indeterminate: panel remained open and no transient toast was captured.",
                            null, watch, cancellationToken);
                    }

                    while (searchResultWatch.Elapsed < TimeSpan.FromSeconds(options.SearchResultTimeoutSeconds))
                    {
                        ObservationDecision decision = await ObserveFrameAsync(
                            deviceName, result, observations, context, cancellationToken);
                        if (decision.HasOutcome)
                            return await CompleteAsync(deviceName, result, context, decision.Outcome,
                                decision.Message, decision.ErrorMessage, watch, cancellationToken);
                        await Task.Delay(options.NormalPollIntervalMs, cancellationToken);
                    }
                    break;
                }

                string timeoutMessage = result.PopupVerificationResult != null
                    && result.PopupVerificationResult.Outcome == ResourcePopupOutcome.ResourcePopupDetectedButNotReady
                    ? "Resource popup was detected but did not become ready before timeout."
                    : result.PanelClosed && !result.CameraMovementObserved
                        ? "WorldMap was observed after the panel closed, but camera movement was not verified."
                        : "Resource search result observation timed out.";
                return await CompleteAsync(deviceName, result, context, ResourceSearchOutcome.Timeout,
                    timeoutMessage, null, watch, cancellationToken);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception exception)
            {
                logger.Error($"[Resource Search Execution] DeviceName='{deviceName}', Error='{exception.Message}', "
                    + $"SearchTapCount={result.SearchTapCount}, DurationMs={watch.Elapsed.TotalMilliseconds:F0}", exception);
                return await CompleteAsync(deviceName, result, context, ResourceSearchOutcome.Failed,
                    "Resource search execution failed.", exception.Message, watch, cancellationToken);
            }
        }

        private async Task<ObservationDecision> ObserveFrameAsync(string deviceName,
            ResourceSearchExecutionResult result, IList<ResourceSearchObservation> observations,
            ObservationContext context, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            byte[] frame = await ldPlayerClient.CaptureScreenshotPngAsync(deviceName, cancellationToken);
            context.LastFrame = frame;
            string resolutionError = ValidateResolution(frame);
            if (resolutionError != null)
                return ObservationDecision.Decided(ResourceSearchOutcome.Failed,
                    "Observation frame resolution is invalid.", resolutionError);

            await TrySaveBurstFrameAsync(deviceName, frame, context, cancellationToken);
            GameDetectionResult detection = detector.Detect(frame);
            cancellationToken.ThrowIfCancellationRequested();
            if (detection == null || !detection.IsSuccessful)
                return ObservationDecision.Decided(ResourceSearchOutcome.Failed,
                    "Game state detection failed during search observation.", detection?.ErrorMessage);

            ImageMatchResult toastAnchor = Match(frame, TemplateId.ResourceNotFoundToastAnchor, options.ToastRegion);
            ImageMatchResult actionAnchor = Match(frame, TemplateId.ResourceNotFoundToastActionAnchor, options.ToastRegion);
            bool panelConfirmed = IsPanelConfirmed(detection);
            double? difference = null;
            bool stable = false;
            if (context.PreviousFrame != null)
            {
                FrameComparisonResult comparison = stabilityDetector.Compare(
                    context.PreviousFrame, frame, options.MapRegion);
                difference = comparison.DifferenceRatio;
                stable = comparison.DifferenceRatio <= options.CameraStableThreshold;
                if (comparison.DifferenceRatio > options.CameraMovementThreshold)
                {
                    result.CameraMovementObserved = true;
                    context.StableFrameCount = 0;
                }
            }

            result.PanelClosed = !panelConfirmed;
            result.FinalState = detection.State;
            bool anchorsClose = HasBounds(toastAnchor) && HasBounds(actionAnchor)
                && Math.Abs(toastAnchor.CenterY - actionAnchor.CenterY)
                    <= options.MaxToastAnchorVerticalDistancePx;
            bool toastVerified = anchorsClose && (panelConfirmed || context.PreviousPanelConfirmed);

            if (result.CameraMovementObserved && !panelConfirmed
                && detection.State == GameState.WorldMap && stable)
                context.StableFrameCount++;
            else if (result.CameraMovementObserved && !stable)
                context.StableFrameCount = 0;
            result.CameraStabilityVerified = context.StableFrameCount >= options.RequiredStableFrames;

            string observationMessage = HasBounds(toastAnchor) && HasBounds(actionAnchor) && !anchorsClose
                ? "Toast anchors were ambiguous because their vertical distance exceeded the configured maximum."
                : toastVerified ? "Both not-found toast anchors matched and were latched."
                : "No conclusive search outcome in this frame.";
            var observation = new ResourceSearchObservation
            {
                Timestamp = DateTimeOffset.Now,
                State = detection.State,
                ToastAnchorFound = HasBounds(toastAnchor),
                ToastActionAnchorFound = HasBounds(actionAnchor),
                SearchPanelConfirmed = panelConfirmed,
                FrameDifference = difference,
                IsStable = stable,
                Message = observationMessage
            };
            observations.Add(observation);
            result.ObservedFrameCount = observations.Count;
            LogObservation(deviceName, result, context, observation, toastAnchor, actionAnchor);

            if (toastVerified)
            {
                result.NotFoundObserved = true;
                result.NotFoundToastVerified = true;
                return ObservationDecision.Decided(ResourceSearchOutcome.ResourceNotFound,
                    "ResourceNotFound toast was verified in one observation frame.", null);
            }
            if (!result.NotFoundObserved && detection.State == GameState.ResourcePopup)
            {
                if (popupVerificationService == null)
                    return ObservationDecision.Decided(ResourceSearchOutcome.Failed,
                        "ResourcePopup was detected but no popup verification service is configured.", null);
                ResourcePopupVerificationResult popup = await popupVerificationService.VerifyAsync(
                    deviceName, cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                result.PopupVerificationResult = popup;
                observation.PopupOutcome = popup.Outcome;
                observation.Message = "ResourcePopup verification outcome: " + popup.Outcome + ". " + popup.Message;
                if (popup.Outcome == ResourcePopupOutcome.ResourcePopupReady)
                {
                    result.PanelClosed = true;
                    result.FinalState = GameState.ResourcePopup;
                    return ObservationDecision.Decided(ResourceSearchOutcome.ResourceLocated,
                        "Iron ResourcePopup and enabled Gather button were verified; popup evidence superseded camera stability.", null);
                }
                if (popup.Outcome == ResourcePopupOutcome.Failed)
                    return ObservationDecision.Decided(ResourceSearchOutcome.Failed,
                        "ResourcePopup verification failed.", popup.ErrorMessage);
                if (popup.Outcome == ResourcePopupOutcome.Cancelled)
                    throw new OperationCanceledException(cancellationToken);
            }
            if (!result.NotFoundObserved && result.PanelClosed
                && detection.State == GameState.WorldMap && result.CameraMovementObserved
                && result.CameraStabilityVerified)
                return ObservationDecision.Decided(ResourceSearchOutcome.ResourceLocated,
                    "Search panel closed and WorldMap camera movement stabilized.", null);

            if (detection.State == GameState.Unknown)
            {
                context.UnknownFrameCount++;
                if (context.UnknownFrameCount > options.MaxTransientUnknownFrames)
                    return ObservationDecision.Decided(ResourceSearchOutcome.Failed,
                        "Transient Unknown frame limit was exceeded.", null);
            }
            else
                context.UnknownFrameCount = 0;

            context.PreviousPanelConfirmed = panelConfirmed;
            context.LastPanelConfirmed = panelConfirmed;
            context.PreviousFrame = frame;
            return ObservationDecision.Pending();
        }

        private async Task<ResourceSearchExecutionResult> CompleteAsync(string deviceName,
            ResourceSearchExecutionResult result, ObservationContext context,
            ResourceSearchOutcome outcome, string message, string error,
            Stopwatch watch, CancellationToken cancellationToken)
        {
            result.Outcome = outcome;
            result.Success = outcome == ResourceSearchOutcome.ResourceLocated;
            result.Message = message;
            result.ErrorMessage = error;
            result.Duration = watch.Elapsed;
            if (options.SaveResultScreenshots && context.LastFrame != null
                && outcome != ResourceSearchOutcome.Cancelled)
            {
                try
                {
                    result.DiagnosticScreenshotPath = await diagnosticStore.SaveResultAsync(
                        deviceName, outcome, context.LastFrame, cancellationToken);
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception exception)
                {
                    logger.Error($"[Resource Search Execution] DeviceName='{deviceName}', "
                        + $"Outcome='{outcome}', DiagnosticSaveError='{exception.Message}'", exception);
                }
            }
            logger.Info($"[Resource Search Execution] DeviceName='{deviceName}', Outcome='{outcome}', "
                + $"InitialState='{result.InitialState}', FinalState='{result.FinalState}', "
                + $"SearchTapCount={result.SearchTapCount}, ObservedFrameCount={result.ObservedFrameCount}, "
                + $"PanelClosed={result.PanelClosed}, Movement={result.CameraMovementObserved}, "
                + $"Stable={result.CameraStabilityVerified}, NotFoundLatch={result.NotFoundObserved}, "
                + $"DurationMs={result.Duration.TotalMilliseconds:F0}, Cancellation=false, Error='{error ?? string.Empty}'");
            return result;
        }

        private async Task TrySaveBurstFrameAsync(string deviceName, byte[] frame,
            ObservationContext context, CancellationToken cancellationToken)
        {
            if (!options.SaveObservationBurst || context.BurstFrameCount >= options.MaxObservationBurstFrames)
                return;
            context.BurstFrameCount++;
            try
            {
                await diagnosticStore.SaveObservationAsync(deviceName, context.BurstTimestamp,
                    context.BurstFrameCount, frame, cancellationToken);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception exception)
            {
                logger.Error($"[Resource Search Execution] DeviceName='{deviceName}', "
                    + $"ObservationFrame={context.BurstFrameCount}, BurstSaveError='{exception.Message}'", exception);
            }
        }

        private ImageMatchResult Match(byte[] screenshot, TemplateId id, ImageRegion? region)
        {
            return imageMatcher.Find(screenshot, templateRegistry.LoadBytes(id), region);
        }

        private string ValidateTemplates()
        {
            foreach (TemplateId id in RequiredTemplates)
            {
                try
                {
                    string path = templateRegistry.GetPath(id);
                    if (!templateRegistry.Exists(id))
                        return $"Required template '{id}' was not found at '{path}'.";
                }
                catch (Exception exception)
                {
                    return $"Required template '{id}' could not be resolved: {exception.Message}";
                }
            }
            return null;
        }

        private static string ValidateRequest(ResourceSearchExecutionRequest request)
        {
            if (request == null) return "ResourceSearchExecutionRequest is required.";
            if (request.ConfigureBeforeSearch && request.Configuration == null)
                return "Configuration is required when ConfigureBeforeSearch is true.";
            return null;
        }

        private string ValidateResolution(byte[] png)
        {
            try
            {
                using (var stream = new MemoryStream(png, writable: false))
                using (Image image = Image.FromStream(stream, false, true))
                    return image.Width == options.ExpectedWidth && image.Height == options.ExpectedHeight
                        ? null
                        : $"Expected {options.ExpectedWidth}x{options.ExpectedHeight}, actual {image.Width}x{image.Height}.";
            }
            catch (Exception exception)
            {
                return "Screenshot could not be decoded: " + exception.Message;
            }
        }

        private static bool IsPanelConfirmed(GameDetectionResult result)
        {
            if (result == null || result.State != GameState.ResourceSearchPanel || result.Evidence == null)
                return false;
            bool anchor = result.Evidence.Any(item =>
                (item.TemplateId == TemplateId.ResourceSearchPanelAnchor
                    || item.TemplateId == TemplateId.LevelMinusButton
                    || item.TemplateId == TemplateId.ResourceTabSelected
                    || item.TemplateId == TemplateId.ResourceTabUnselected) && item.Found);
            bool search = result.Evidence.Any(item => item.TemplateId == TemplateId.SearchButtonEnabled && item.Found);
            return anchor && search;
        }

        private static bool HasBounds(ImageMatchResult match) =>
            match != null && match.Found && match.Width > 0 && match.Height > 0;

        private static ResourceSearchExecutionResult NewResult(List<ResourceSearchObservation> observations) =>
            new ResourceSearchExecutionResult
            {
                Outcome = ResourceSearchOutcome.Failed,
                InitialState = GameState.Unknown,
                FinalState = GameState.Unknown,
                Observations = observations.AsReadOnly()
            };

        private static ResourceSearchExecutionResult EmptyResult(ResourceSearchOutcome outcome,
            Stopwatch watch, string message) => new ResourceSearchExecutionResult
            {
                Outcome = outcome,
                Success = false,
                InitialState = GameState.Unknown,
                FinalState = GameState.Unknown,
                Duration = watch.Elapsed,
                Message = message,
                ErrorMessage = outcome == ResourceSearchOutcome.Failed ? message : null,
                Observations = new ResourceSearchObservation[0]
            };

        private void LogStart(string deviceName, ResourceSearchExecutionRequest request)
        {
            ResourceSearchConfigurationRequest configuration = request.Configuration;
            logger.Info($"[Resource Search Execution] DeviceName='{deviceName}', "
                + $"ConfigureBeforeSearch={request.ConfigureBeforeSearch}, "
                + $"Resource='{configuration?.ResourceType.ToString() ?? string.Empty}', "
                + $"Level={configuration?.TargetLevel ?? 0}, Filter={configuration?.UnoccupiedOnly ?? false}, "
                + "Cancellation=false, Phase='Starting'");
        }

        private void LogObservation(string deviceName, ResourceSearchExecutionResult result,
            ObservationContext context, ResourceSearchObservation observation,
            ImageMatchResult toast, ImageMatchResult action)
        {
            string toastBounds = HasBounds(toast) ? $"({toast.X},{toast.Y},{toast.Width},{toast.Height})" : string.Empty;
            string actionBounds = HasBounds(action) ? $"({action.X},{action.Y},{action.Width},{action.Height})" : string.Empty;
            logger.Info($"[Resource Search Observation] DeviceName='{deviceName}', Index={result.ObservedFrameCount}, "
                + $"State='{observation.State}', ToastAnchorFound={observation.ToastAnchorFound}, "
                + $"ToastActionAnchorFound={observation.ToastActionAnchorFound}, ToastBounds='{toastBounds}', "
                + $"ActionBounds='{actionBounds}', SearchPanelConfirmed={observation.SearchPanelConfirmed}, "
                + $"FrameDifference={observation.FrameDifference?.ToString("F4") ?? "n/a"}, "
                + $"Movement={result.CameraMovementObserved}, StableFrameCount={context.StableFrameCount}, "
                + $"UnknownFrameCount={context.UnknownFrameCount}, NotFoundLatch={result.NotFoundObserved}");
        }

        private sealed class ObservationContext
        {
            public byte[] PreviousFrame;
            public byte[] LastFrame;
            public bool PreviousPanelConfirmed;
            public bool LastPanelConfirmed;
            public int StableFrameCount;
            public int UnknownFrameCount;
            public int BurstFrameCount;
            public DateTimeOffset BurstTimestamp;
        }

        private sealed class ObservationDecision
        {
            public bool HasOutcome;
            public ResourceSearchOutcome Outcome;
            public string Message;
            public string ErrorMessage;
            public static ObservationDecision Pending() => new ObservationDecision();
            public static ObservationDecision Decided(ResourceSearchOutcome outcome, string message, string error) =>
                new ObservationDecision { HasOutcome = true, Outcome = outcome, Message = message, ErrorMessage = error };
        }
    }
}
