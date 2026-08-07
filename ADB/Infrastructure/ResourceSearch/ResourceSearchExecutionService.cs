using ADB_Tool_Automation_Post_FB.Core.Abstractions;
using ADB_Tool_Automation_Post_FB.Core.Concurrency;
using ADB_Tool_Automation_Post_FB.Core.Diagnostics;
using ADB_Tool_Automation_Post_FB.Core.GameDetection;
using ADB_Tool_Automation_Post_FB.Core.ResourceSearch;
using ADB_Tool_Automation_Post_FB.Core.ResourcePopup;
using ADB_Tool_Automation_Post_FB.Core.Vision;
using System;
using System.Collections.Concurrent;
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
        private const string LegacyMoveAreaVariant = "LegacyMoveArea";
        private const string SearchOtherRegionVariant = "SearchOtherRegion";
        private const string TargetLevelTooLowVariant = "TargetLevelTooLow";

        private static readonly TemplateId[] RequiredTemplates =
        {
            TemplateId.SearchButtonEnabled,
            TemplateId.ResourceNotFoundToastAnchor,
            TemplateId.ResourceNotFoundToastActionAnchor
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
        private readonly ConcurrentDictionary<string, GameState> lastKnownStates =
            new ConcurrentDictionary<string, GameState>(StringComparer.OrdinalIgnoreCase);

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
            GameState knownState;
            if (lastKnownStates.TryGetValue(deviceName, out knownState))
                context.LastKnownState = knownState;
            try
            {
                LogStart(deviceName, request);
                if (request.ConfigureBeforeSearch)
                {
                    result.ConfigurationResult = await configurationService.ConfigureAsync(
                        deviceName, request.Configuration, cancellationToken);
                    result.InitialState = result.ConfigurationResult.InitialState;
                    result.FinalState = result.ConfigurationResult.FinalState;
                    RememberKnownState(deviceName, result.FinalState, context);
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
                    RememberKnownState(deviceName, current.State, context);
                    if (!current.IsSuccessful || !IsPanelConfirmed(current))
                        return await CompleteAsync(deviceName, result, context, ResourceSearchOutcome.Failed,
                            "Current screen is not a verified ResourceSearchPanel; Search was not tapped.",
                            current.ErrorMessage, watch, cancellationToken);
                }

                int maxSearchTapAttempts = request.ExecutionMode == ResourceSearchExecutionMode.ResourceAreaLv2PointRetry
                    ? 1
                    : options.MaxSearchTapAttempts;
                for (int attempt = 1; attempt <= maxSearchTapAttempts; attempt++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (attempt > 1)
                    {
                        int retryDelayMs = 600 + ((attempt * 73) % 151);
                        await Task.Delay(retryDelayMs, cancellationToken);
                    }
                    context.ResetToastEvidence();
                    Stopwatch searchResultWatch = null;
                    CapturedFrame beforeTap = await CaptureFrameAsync(deviceName, cancellationToken);
                    try
                    {
                    string resolutionError = ValidateResolution(beforeTap);
                    if (resolutionError != null)
                    {
                        return await CompleteAsync(deviceName, result, context, ResourceSearchOutcome.Failed,
                            "Search screenshot resolution is invalid.", resolutionError, watch, cancellationToken);
                    }

                    ImageMatchResult button = Match(beforeTap, TemplateId.SearchButtonEnabled, null);
                    if (!HasBounds(button))
                    {
                        // The button briefly disappears while the panel applies the
                        // previous resource/level input.  Treat this as an unchanged
                        // panel observation and use the next bounded fresh frame,
                        // rather than failing a device that has not left the panel.
                        if (attempt < maxSearchTapAttempts)
                        {
                            logger.Info($"[Resource Search Execution] DeviceName='{deviceName}', Attempt={attempt}, SearchButtonAvailable=false, Decision='RetryFreshFrame'");
                            continue;
                        }
                        result.FailureReason = ResourceSearchFailureReason.SearchButtonUnavailable;
                        return await CompleteAsync(deviceName, result, context, ResourceSearchOutcome.SearchButtonUnavailable,
                            "SearchButtonEnabled was not found with valid bounds after fresh retries; no Tap was sent.", null,
                            watch, cancellationToken);
                    }

                    GameDetectionResult beforeTapState = Detect(beforeTap, deviceName,
                        new GameStateDetectionContext(GameState.ResourceSearchPanel,
                            context.LastKnownState));
                    if (!beforeTapState.IsSuccessful || !IsPanelConfirmed(beforeTapState))
                    {
                        return await CompleteAsync(deviceName, result, context, ResourceSearchOutcome.Failed,
                            "ResourceSearchPanel was not verified on the fresh pre-Tap screenshot.",
                            beforeTapState.ErrorMessage, watch, cancellationToken);
                    }

                    result.SearchButtonVerified = true;
                    int tapX = button.CenterX;
                    int tapY = button.CenterY;
                    // Arm the lightweight frame request before the input so the
                    // first post-tap frame can already be in flight.
                    Task<CapturedFrame> armedToastFrame = CaptureFrameAsync(deviceName, cancellationToken);
                    logger.Info($"[Resource Search Execution] DeviceName='{deviceName}', Attempt={attempt}, "
                        + $"SearchButtonBounds=({button.X},{button.Y},{button.Width},{button.Height}), "
                        + $"SearchTap=({tapX},{tapY}), SearchTapCount={result.SearchTapCount + 1}");
                    await ldPlayerClient.TapAsync(deviceName, tapX, tapY, cancellationToken);
                    result.SearchTapCount++;
                    DateTimeOffset searchTapCompletedAt = DateTimeOffset.UtcNow;
                    ImmediateToastProbeResult immediateProbe = await ProbeImmediateToastAsync(
                        deviceName, request, result, result.SearchTapCount,
                        searchTapCompletedAt, armedToastFrame, cancellationToken);
                    if (immediateProbe.Outcome.HasValue)
                    {
                        result.FailureReason = immediateProbe.FailureReason;
                        return await CompleteAsync(deviceName, result, context,
                            immediateProbe.Outcome.Value, immediateProbe.Message,
                            immediateProbe.ErrorMessage, watch, cancellationToken);
                    }
                    // Each freshly rematched Tap gets its own bounded verification
                    // window. A slow/Unknown observation after the first Tap must
                    // not consume the verification budget of later retries.
                    searchResultWatch = Stopwatch.StartNew();
                    context.ReplacePreviousFrame(beforeTap);
                    beforeTap = null;
                    context.PreviousPanelConfirmed = true;
                    context.LastPanelConfirmed = true;
                    }
                    finally
                    {
                        if (beforeTap != null) beforeTap.Dispose();
                    }

                    int fastWindowMs = Math.Min(options.NotFoundObservationWindowMs,
                        options.SearchTapVerificationTimeoutSeconds * 1000);
                    var fastWatch = Stopwatch.StartNew();
                    while (fastWatch.ElapsedMilliseconds < fastWindowMs
                        && searchResultWatch.Elapsed < TimeSpan.FromSeconds(options.SearchResultTimeoutSeconds))
                    {
                        ObservationDecision decision = await ObserveFrameAsync(
                            deviceName, request.Configuration.ResourceType, result, observations,
                            context, cancellationToken);
                        if (decision.HasOutcome)
                            return await CompleteAsync(deviceName, result, context, decision.Outcome,
                                decision.Message, decision.ErrorMessage, watch, cancellationToken);
                        await Task.Delay(options.NotFoundFastPollIntervalMs, cancellationToken);
                    }

                    if (!IsUnchangedSearchPanel(result, context))
                    {
                        while (searchResultWatch.Elapsed < TimeSpan.FromSeconds(
                            options.SearchResultTimeoutSeconds))
                        {
                            ObservationDecision decision = await ObserveFrameAsync(
                                deviceName, request.Configuration.ResourceType, result, observations,
                                context, cancellationToken);
                            if (decision.HasOutcome)
                                return await CompleteAsync(deviceName, result, context, decision.Outcome,
                                    decision.Message, decision.ErrorMessage, watch, cancellationToken);
                            await Task.Delay(options.NormalPollIntervalMs, cancellationToken);
                        }
                    }

                    // The first post-Tap frame can be transient Unknown. If later
                    // frames recover to the unchanged panel, retry with fresh bounds
                    // instead of falling through to SearchTransitionTimeout.
                    if (IsUnchangedSearchPanel(result, context))
                    {
                        if (context.HasPartialToastEvidence)
                        {
                            result.FailureReason = ResourceSearchFailureReason.ResourceToastUnclassified;
                            result.ShouldRetrySearch = false;
                            return await CompleteAsync(deviceName, result, context,
                                ResourceSearchOutcome.ResourceToastUnclassified,
                                "Phát hiện toast chưa phân loại; dừng Search để tránh gửi lặp.",
                                null, watch, cancellationToken);
                        }
                        if (attempt < maxSearchTapAttempts)
                            continue;
                        result.FailureReason = ResourceSearchFailureReason
                            .SearchButtonStillVisibleAfterMaxAttempts;
                        result.ShouldRetrySearch = true;
                        return await CompleteAsync(deviceName, result, context,
                            ResourceSearchOutcome.SearchTapNotApplied,
                            $"Nút Tìm kiếm vẫn hiển thị sau {maxSearchTapAttempts} lần thử; "
                                + "chuyển sang tài nguyên khác.",
                            null, watch, cancellationToken);
                    }
                    break;
                }

                ObservationDecision finalPopupDecision = await VerifyPopupAsync(
                    deviceName, request.Configuration.ResourceType, result, cancellationToken);
                if (finalPopupDecision.HasOutcome)
                    return await CompleteAsync(deviceName, result, context, finalPopupDecision.Outcome,
                        finalPopupDecision.Message, finalPopupDecision.ErrorMessage, watch, cancellationToken);

                string timeoutMessage = result.PopupVerificationResult != null
                    && result.PopupVerificationResult.Outcome == ResourcePopupOutcome.ResourcePopupDetectedButNotReady
                    ? "Resource popup was detected but did not become ready before timeout."
                    : result.PanelClosed && !result.CameraMovementObserved
                        ? "WorldMap was observed after the panel closed, but camera movement was not verified."
                        : "Resource search result observation timed out.";
                return await CompleteAsync(deviceName, result, context,
                    ResourceSearchOutcome.SearchTransitionTimeout,
                    timeoutMessage, null, watch, cancellationToken);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception exception)
            {
                logger.Error($"[Resource Search Execution] DeviceName='{deviceName}', Error='{exception.Message}', "
                    + $"SearchTapCount={result.SearchTapCount}, DurationMs={watch.Elapsed.TotalMilliseconds:F0}", exception);
                return await CompleteAsync(deviceName, result, context,
                    ResourceSearchOutcome.TechnicalFailure,
                    "Resource search execution failed.", exception.Message, watch, cancellationToken);
            }
            finally
            {
                context.Dispose();
            }
        }

        private async Task<ImmediateToastProbeResult> ProbeImmediateToastAsync(
            string deviceName, ResourceSearchExecutionRequest request,
            ResourceSearchExecutionResult result, int searchTapCount,
            DateTimeOffset searchTapCompletedAt, Task<CapturedFrame> armedToastFrame,
            CancellationToken cancellationToken)
        {
            int settleMs = 350;
            await Task.Delay(settleMs, cancellationToken);
            DateTimeOffset captureStarted = DateTimeOffset.UtcNow;
            CapturedFrame frame;
            try
            {
                frame = armedToastFrame == null
                    ? await CaptureFrameAsync(deviceName, cancellationToken)
                    : await armedToastFrame;
                // An armed request can complete just before the input. Never use a
                // pre-tap frame as the post-tap Search-button gate.
                if (frame.CapturedAt <= searchTapCompletedAt)
                {
                    frame.Dispose();
                    frame = await CaptureFrameAsync(deviceName, cancellationToken);
                }
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception exception)
            {
                logger.Error($"[Resource Area Lv2 Toast Burst] RunId='{request.RunId ?? string.Empty}', DeviceName='{deviceName}', Resource='{request.Configuration?.ResourceType}', SearchTapCount={searchTapCount}, FramesReceived=0, FramesInspected=0, TemplateFound=false, NextAction='RetrySearchWithinExistingLimit', FailureReason='CaptureUnavailable: {exception.Message}'", exception);
                return ImmediateToastProbeResult.None;
            }
            try
            {
                // The pre-armed capture is the operation-local one-frame rolling buffer.
                // Only a fresh Search-button-positive frame activates toast matching.
                ImageRegion toastRoi = ScaleRegion(
                    new ImageRegion(190, 105, 900, 180), frame.Width, frame.Height);

                // Gate the transient watcher on the fresh post-tap Search button.
                // If it disappeared, this frame belongs to the normal result flow.
                ImageRegion searchButtonRoi = ScaleRegion(options.SearchButtonRegion, frame.Width, frame.Height);
                ImageMatchResult postTapSearchButton;
                try
                {
                    postTapSearchButton = Match(frame, TemplateId.SearchButtonEnabled, searchButtonRoi);
                }
                catch (Exception exception)
                {
                    logger.Info($"[Resource Area Lv2 Toast Burst] RunId='{request.RunId ?? string.Empty}', DeviceName='{deviceName}', Resource='{request.Configuration?.ResourceType}', SearchTapCount={searchTapCount}, SearchButtonState='Unknown', FramesReceived=1, FramesInspected=0, TemplateFound=false, NextAction='RetrySearchWithinExistingLimit', FailureReason='SearchButtonProbeFailed: {exception.Message}'");
                    return ImmediateToastProbeResult.None;
                }

                bool searchButtonStillVisible = HasBounds(postTapSearchButton)
                    && IsInside(postTapSearchButton, searchButtonRoi);
                if (!searchButtonStillVisible)
                {
                    logger.Info($"[Resource Area Lv2 Toast Burst] RunId='{request.RunId ?? string.Empty}', DeviceName='{deviceName}', Resource='{request.Configuration?.ResourceType}', SearchTapCount={searchTapCount}, SearchButtonState='Disappeared', FramesReceived=1, FramesInspected=0, FirstFrameAfterTapMs={(frame.CapturedAt - searchTapCompletedAt).TotalMilliseconds:F0}, LastFrameAfterTapMs={(frame.CapturedAt - searchTapCompletedAt).TotalMilliseconds:F0}, TemplateFound=false, NextAction='ContinueNormalSearchObservation', FailureReason=''");
                    return ImmediateToastProbeResult.None;
                }

                logger.Info($"[Resource Area Lv2 Toast Burst] RunId='{request.RunId ?? string.Empty}', DeviceName='{deviceName}', Resource='{request.Configuration?.ResourceType}', SearchTapCount={searchTapCount}, SearchButtonState='StillVisible', FramesReceived=1, FramesInspected=0, TemplateFound=false, NextAction='InspectToastBurst', FailureReason=''");

                ResourceAreaLv2TemplateStatus phraseStatus = ValidateResourceAreaLv2Template(
                    TemplateId.ResourceAreaPhraseAnchor, toastRoi);
                ResourceAreaLv2TemplateStatus levelStatus = ValidateResourceAreaLv2Template(
                    TemplateId.ResourceAreaLv2EndingAnchor, toastRoi);
                LogResourceAreaLv2TemplateStatus(deviceName, request.RunId, toastRoi,
                    "ResourceAreaPhraseAnchor", phraseStatus);
                LogResourceAreaLv2TemplateStatus(deviceName, request.RunId, toastRoi,
                    "ResourceAreaLv2EndingAnchor", levelStatus);
                ResourceAreaLv2TemplateStatus unavailableStatus = !phraseStatus.Ready
                    ? phraseStatus : (!levelStatus.Ready ? levelStatus : null);
                if (unavailableStatus != null)
                    return ImmediateToastProbeResult.Decided(
                        ResourceSearchOutcome.ResourceAreaLv2TemplateUnavailable,
                        "Resource Area Lv2 toast anchor is unavailable.",
                        unavailableStatus.FailureReason);

                const int watchStartMs = 350;
                const int watchEndMs = 2100;
                const int frameIntervalTargetMs = 125;
                int framesReceived = 1;
                int framesInspected = 0;
                int bestFrameIndex = -1;
                double phraseBestScore = 0;
                double levelBestScore = 0;
                ImageMatchResult phraseBestMatch = ImageMatchResult.NotFound();
                ImageMatchResult levelBestMatch = ImageMatchResult.NotFound();
                bool templateFound = false;
                DateTimeOffset? firstFrameInsideWindow = null;
                DateTimeOffset? lastFrameInsideWindow = null;
                while ((DateTimeOffset.UtcNow - searchTapCompletedAt).TotalMilliseconds <= watchEndMs)
                {
                    double frameAfterTapMs = (frame.CapturedAt - searchTapCompletedAt).TotalMilliseconds;
                    if (frameAfterTapMs >= watchStartMs && frameAfterTapMs <= watchEndMs)
                    {
                        framesInspected++;
                        if (!firstFrameInsideWindow.HasValue)
                            firstFrameInsideWindow = frame.CapturedAt;
                        if (lastFrameInsideWindow.HasValue)
                        {
                        }
                        lastFrameInsideWindow = frame.CapturedAt;
                        IReadOnlyList<ImageMatchResult> matches;
                        try
                        {
                            matches = await MatchResourceAreaLv2AnchorsAsync(frame, toastRoi,
                                cancellationToken);
                        }
                        catch (Exception exception)
                        {
                            logger.Error($"[Resource Area Lv2 Toast Burst] DeviceName='{deviceName}', PhraseTemplatePath='{phraseStatus.ResolvedAbsolutePath}', Lv2TemplatePath='{levelStatus.ResolvedAbsolutePath}', FailureReason='MatcherExecutionFailed: {exception.Message}'", exception);
                            return ImmediateToastProbeResult.Decided(
                                ResourceSearchOutcome.ResourceAreaLv2TemplateUnavailable,
                                "Resource Area Lv2 toast matcher failed.",
                                "TemplateMatcherInitializationFailed");
                        }
                        ImageMatchResult phraseMatch = matches[0] ?? ImageMatchResult.NotFound();
                        ImageMatchResult levelMatch = matches[1] ?? ImageMatchResult.NotFound();
                        double phraseScore = phraseMatch.Confidence ?? (HasBounds(phraseMatch) ? 1.0 : 0.0);
                        double levelScore = levelMatch.Confidence ?? (HasBounds(levelMatch) ? 1.0 : 0.0);
                        if (phraseScore > phraseBestScore)
                        {
                            phraseBestScore = phraseScore;
                            phraseBestMatch = phraseMatch;
                        }
                        if (levelScore > levelBestScore)
                        {
                            levelBestScore = levelScore;
                            levelBestMatch = levelMatch;
                        }
                        bool phraseFound = HasBounds(phraseMatch);
                        bool levelFound = HasBounds(levelMatch);
                        if (phraseFound && levelFound)
                        {
                            templateFound = true;
                            bestFrameIndex = framesInspected;
                            phraseBestMatch = phraseMatch;
                            levelBestMatch = levelMatch;
                            break;
                        }
                    }
                    if ((DateTimeOffset.UtcNow - searchTapCompletedAt).TotalMilliseconds >= watchEndMs)
                        break;
                    await Task.Delay(frameIntervalTargetMs, cancellationToken);
                    CapturedFrame nextFrame;
                    try
                    {
                        nextFrame = await CaptureFrameAsync(deviceName, cancellationToken);
                    }
                    catch (OperationCanceledException) { throw; }
                    catch (Exception exception)
                    {
                        logger.Error($"[Resource Area Lv2 Toast Burst] RunId='{request.RunId ?? string.Empty}', DeviceName='{deviceName}', Resource='{request.Configuration?.ResourceType}', SearchTapCount={searchTapCount}, FramesReceived={framesReceived}, FramesInspected={framesInspected}, TemplateFound=false, NextAction='RetrySearchWithinExistingLimit', FailureReason='CaptureUnavailable: {exception.Message}'", exception);
                        return ImmediateToastProbeResult.None;
                    }
                    frame.Dispose();
                    frame = nextFrame;
                    framesReceived++;
                }

                string toastRunId = request.RunId ?? string.Empty;
                string toastNextAction = templateFound ? "StartPredefinedPointFlow" : "RetrySearchWithinExistingLimit";
                logger.Info($"[Conditional Resource Area Lv2 Toast Watch] RunId='{toastRunId}', DeviceName='{deviceName}', Resource='{request.Configuration?.ResourceType}', SearchTapCount={searchTapCount}, FramesReceived={framesReceived}, FramesInspected={framesInspected}, FirstFrameAfterTapMs={(firstFrameInsideWindow.HasValue ? (firstFrameInsideWindow.Value - searchTapCompletedAt).TotalMilliseconds : (captureStarted - searchTapCompletedAt).TotalMilliseconds):F0}, LastFrameAfterTapMs={(lastFrameInsideWindow.HasValue ? (lastFrameInsideWindow.Value - searchTapCompletedAt).TotalMilliseconds : -1):F0}, PhraseBestScore={phraseBestScore:F3}, PhraseBestBounds='{FormatBounds(phraseBestMatch)}', Lv2EndingBestScore={levelBestScore:F3}, Lv2EndingBestBounds='{FormatBounds(levelBestMatch)}', MatchingFrameIndex={bestFrameIndex}, TemplateFound={templateFound}, NextAction='{toastNextAction}', FailureReason=''");
                if (templateFound)
                {
                    result.FailureReason = ResourceSearchFailureReason.ResourceAreaLv2Redirect;
                    result.MatchedNotFoundVariant = "ResourceAreaLv2Redirect";
                    return ImmediateToastProbeResult.Decided(
                        ResourceSearchOutcome.ResourceAreaLv2Redirect,
                        "Đã phát hiện thông báo chuyển sang khu tài nguyên Lv2.", null);
                }
                return ImmediateToastProbeResult.None;

            }
            finally
            {
                frame.Dispose();
            }
        }

        private ResourceAreaLv2TemplateStatus ValidateResourceAreaLv2Template(
            TemplateId templateId, ImageRegion toastRoi)
        {
            var status = new ResourceAreaLv2TemplateStatus
            {
                AppBaseDirectory = AppContext.BaseDirectory ?? string.Empty
            };
            try
            {
                status.Definition = templateRegistry.GetDefinition(templateId);
                status.ConfiguredRelativePath = status.Definition?.RelativePath ?? string.Empty;
                status.Threshold = status.Definition?.DefaultThreshold ?? 0;
                if (string.IsNullOrWhiteSpace(status.ConfiguredRelativePath))
                {
                    status.FailureReason = "TemplatePathNotConfigured";
                    return status;
                }

                status.ResolvedAbsolutePath = templateRegistry.GetPath(templateId) ?? string.Empty;
                string platformRelativePath = status.ConfiguredRelativePath.Replace('/', Path.DirectorySeparatorChar);
                status.SourceAssetPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
                    "..", "..", "Data", "InfinityKingdom", "1280x720", "vi", platformRelativePath));
                if (string.IsNullOrWhiteSpace(status.ResolvedAbsolutePath))
                {
                    status.FailureReason = "TemplatePathNotConfigured";
                    return status;
                }

                status.Exists = File.Exists(status.ResolvedAbsolutePath);
                if (!status.Exists)
                {
                    status.FailureReason = "TemplateFileMissing";
                    return status;
                }

                status.FileLength = new FileInfo(status.ResolvedAbsolutePath).Length;
                if (status.FileLength <= 0)
                {
                    status.FailureReason = "TemplateFileEmpty";
                    return status;
                }

                status.DecodeAttempted = true;
                byte[] bytes = templateRegistry.LoadBytes(templateId);
                if (bytes == null || bytes.Length == 0)
                {
                    status.FailureReason = "TemplateFileEmpty";
                    return status;
                }

                using (var stream = new MemoryStream(bytes, false))
                using (var image = new Bitmap(stream))
                {
                    status.Width = image.Width;
                    status.Height = image.Height;
                    status.DecodeSuccess = status.Width > 0 && status.Height > 0;
                }
                if (!status.DecodeSuccess)
                {
                    status.FailureReason = "TemplateInvalidDimensions";
                    return status;
                }

                status.FitsInsideToastRoi = status.Width <= toastRoi.Width
                    && status.Height <= toastRoi.Height;
                if (!status.FitsInsideToastRoi)
                {
                    status.FailureReason = "TemplateLargerThanToastRoi";
                    return status;
                }

                status.MatcherReady = imageMatcher != null;
                if (!status.MatcherReady)
                {
                    status.FailureReason = "TemplateMatcherInitializationFailed";
                    return status;
                }

                status.Ready = true;
                return status;
            }
            catch (Exception exception)
            {
                if (string.IsNullOrWhiteSpace(status.ConfiguredRelativePath)
                    || string.IsNullOrWhiteSpace(status.ResolvedAbsolutePath))
                    status.FailureReason = "TemplatePathNotConfigured";
                else if (!status.Exists)
                    status.FailureReason = "TemplateFileMissing";
                else
                    status.FailureReason = "TemplateDecodeFailed";
                logger.Error($"[Resource Area Lv2 Toast Template Status] ValidationException='{exception.Message}', FailureReason='{status.FailureReason}'", exception);
                return status;
            }
        }

        private void LogResourceAreaLv2TemplateStatus(string deviceName, string runId,
            ImageRegion toastRoi, string templateName, ResourceAreaLv2TemplateStatus status)
        {
            logger.Info($"[Resource Area Lv2 Toast Template Status] DeviceName='{deviceName}', RunId='{runId ?? string.Empty}', TemplateName='{templateName}', AppBaseDirectory='{status.AppBaseDirectory}', ConfiguredRelativePath='{status.ConfiguredRelativePath}', ResolvedAbsolutePath='{status.ResolvedAbsolutePath}', SourceAssetPath='{status.SourceAssetPath}', Exists={status.Exists}, FileLength={status.FileLength}, DecodeAttempted={status.DecodeAttempted}, DecodeSuccess={status.DecodeSuccess}, Width={status.Width}, Height={status.Height}, ToastRoi='{FormatRegion(toastRoi)}', FitsInsideToastRoi={status.FitsInsideToastRoi}, Threshold={status.Threshold:F3}, MatcherReady={status.MatcherReady}, Ready={status.Ready}, FailureReason='{status.FailureReason}'");
        }

        private sealed class ResourceAreaLv2TemplateStatus
        {
            public TemplateDefinition Definition { get; set; }
            public string AppBaseDirectory { get; set; } = string.Empty;
            public string ConfiguredRelativePath { get; set; } = string.Empty;
            public string ResolvedAbsolutePath { get; set; } = string.Empty;
            public string SourceAssetPath { get; set; } = string.Empty;
            public bool Exists { get; set; }
            public long FileLength { get; set; }
            public bool DecodeAttempted { get; set; }
            public bool DecodeSuccess { get; set; }
            public int Width { get; set; }
            public int Height { get; set; }
            public bool FitsInsideToastRoi { get; set; }
            public double Threshold { get; set; }
            public bool MatcherReady { get; set; }
            public bool Ready { get; set; }
            public string FailureReason { get; set; } = string.Empty;
        }

        private sealed class ImmediateToastProbeResult
        {
            public ResourceSearchOutcome? Outcome { get; private set; }
            public string Message { get; private set; }
            public string ErrorMessage { get; private set; }
            public ResourceSearchFailureReason FailureReason { get; private set; }
            public static ImmediateToastProbeResult None => new ImmediateToastProbeResult();
            public static ImmediateToastProbeResult Decided(ResourceSearchOutcome outcome, string message, string error) =>
                new ImmediateToastProbeResult
                {
                    Outcome = outcome,
                    Message = message,
                    ErrorMessage = error,
                    FailureReason = outcome == ResourceSearchOutcome.ResourceAreaLv2Redirect
                        ? ResourceSearchFailureReason.ResourceAreaLv2Redirect
                        : outcome == ResourceSearchOutcome.ResourceToastUnclassified
                        ? ResourceSearchFailureReason.ResourceToastUnclassified
                        : outcome == ResourceSearchOutcome.ResourceAreaLv2TemplateUnavailable
                        ? ResourceSearchFailureReason.ResourceAreaLv2TemplateUnavailable
                        : outcome == ResourceSearchOutcome.ResourceToastCaptureUnavailable
                        ? ResourceSearchFailureReason.ResourceToastCaptureUnavailable
                        : ResourceSearchFailureReason.ResourceToastProbeLate
                };
        }

        private async Task<ObservationDecision> ObserveFrameAsync(string deviceName,
            ResourceType expectedResource,
            ResourceSearchExecutionResult result, IList<ResourceSearchObservation> observations,
            ObservationContext context, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CapturedFrame frame = await CaptureFrameAsync(deviceName, cancellationToken);
            try
            {
            string resolutionError = ValidateResolution(frame);
            if (resolutionError != null)
            {
                return ObservationDecision.Decided(ResourceSearchOutcome.Failed,
                    "Observation frame resolution is invalid.", resolutionError);
            }

            await TrySaveBurstFrameAsync(deviceName, frame, context, cancellationToken);
            GameDetectionResult detection = Detect(frame, deviceName,
                new GameStateDetectionContext(GameState.ResourceSearchPanel,
                    context.LastKnownState));
            cancellationToken.ThrowIfCancellationRequested();
            if (detection == null || !detection.IsSuccessful)
            {
                return ObservationDecision.Decided(ResourceSearchOutcome.Failed,
                    "Game state detection failed during search observation.", detection?.ErrorMessage);
            }

            IReadOnlyList<ImageMatchResult> toastMatches = await MatchToastAnchorsAsync(
                frame, cancellationToken);
            ImageMatchResult toastAnchor = toastMatches[0];
            ImageMatchResult actionAnchor = toastMatches[1];
            ImageMatchResult shortAnchor = toastMatches[2];
            ImageMatchResult otherRegionAnchor = toastMatches[3];
            ImageMatchResult targetLevelTooLowAnchor = toastMatches[4];
            ImageMatchResult seasonMapAnchor = toastMatches[5];
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
                    if (!panelConfirmed)
                    {
                        result.CameraMovementObserved = true;
                        context.StableFrameCount = 0;
                    }
                }
            }

            if (panelConfirmed)
                context.OpenPanelObservationCount++;

            result.PanelClosed = !panelConfirmed;
            result.FinalState = detection.State;
            RememberKnownState(deviceName, detection.State, context);
            bool legacyPairClose = AreToastAnchorsClose(toastAnchor, actionAnchor);
            bool alternatePairClose = AreToastAnchorsClose(shortAnchor, otherRegionAnchor);
            bool targetLevelPairClose = AreToastAnchorsClose(
                targetLevelTooLowAnchor, seasonMapAnchor);
            bool panelConfirmedNowOrAdjacent = panelConfirmed || context.PreviousPanelConfirmed;
            context.MainAnchorSeen |= HasBounds(toastAnchor);
            context.ActionAnchorSeen |= HasBounds(actionAnchor);
            context.ShortAnchorSeen |= HasBounds(shortAnchor);
            context.OtherRegionAnchorSeen |= HasBounds(otherRegionAnchor);
            context.TargetLevelTooLowSeen |= HasBounds(targetLevelTooLowAnchor);
            context.SeasonMapSeen |= HasBounds(seasonMapAnchor);
            context.AlternatePairTooFarSeen |= HasBounds(shortAnchor)
                && HasBounds(otherRegionAnchor) && !alternatePairClose;
            context.UpdateToastEvidence(result.ObservedFrameCount + 1);
            result.ToastEvidence = context.ToastEvidence;
            bool alternateConfirmed = alternatePairClose
                || (context.ShortAnchorSeen && context.OtherRegionAnchorSeen
                    && !context.AlternatePairTooFarSeen);
            // This template is also the structural companion for the target-level
            // toast. Treat it as a season restriction only when that companion is
            // absent; otherwise TargetLevelTooLow keeps its existing semantics.
            bool seasonMapRestriction = HasBounds(seasonMapAnchor)
                && !HasBounds(targetLevelTooLowAnchor);
            bool resourceAreaLv2Evidence = targetLevelPairClose && panelConfirmed;
            string matchedVariant = resourceAreaLv2Evidence
                ? "ResourceAreaLv2Redirect"
                : seasonMapRestriction
                ? "SeasonMapRestriction"
                : targetLevelPairClose ? TargetLevelTooLowVariant
                : alternateConfirmed ? SearchOtherRegionVariant
                : legacyPairClose ? LegacyMoveAreaVariant : null;
            bool toastVerified = matchedVariant != null && panelConfirmedNowOrAdjacent;

            if (result.CameraMovementObserved && !panelConfirmed
                && detection.State == GameState.WorldMap && stable)
                context.StableFrameCount++;
            else if (result.CameraMovementObserved && !stable)
                context.StableFrameCount = 0;
            result.CameraStabilityVerified = context.StableFrameCount >= options.RequiredStableFrames;

            bool legacyPairTooFar = HasBounds(toastAnchor) && HasBounds(actionAnchor) && !legacyPairClose;
            bool alternatePairTooFar = HasBounds(shortAnchor)
                && HasBounds(otherRegionAnchor) && !alternatePairClose;
            bool targetLevelPairTooFar = HasBounds(targetLevelTooLowAnchor)
                && HasBounds(seasonMapAnchor) && !targetLevelPairClose;
            string observationMessage = legacyPairTooFar || alternatePairTooFar
                || targetLevelPairTooFar
                ? "A not-found toast pair was ambiguous because its vertical distance exceeded the configured maximum."
                : toastVerified ? $"Not-found toast variant '{matchedVariant}' matched and was latched."
                : "No conclusive search outcome in this frame.";
            var observation = new ResourceSearchObservation
            {
                Timestamp = DateTimeOffset.Now,
                State = detection.State,
                ToastAnchorFound = HasBounds(toastAnchor),
                ToastActionAnchorFound = HasBounds(actionAnchor),
                ShortAnchorFound = HasBounds(shortAnchor),
                OtherRegionAnchorFound = HasBounds(otherRegionAnchor),
                TargetLevelTooLowAnchorFound = HasBounds(targetLevelTooLowAnchor),
                SeasonMapAnchorFound = HasBounds(seasonMapAnchor),
                ResourceAreaLv2EvidenceFound = resourceAreaLv2Evidence,
                MatchedNotFoundVariant = toastVerified ? matchedVariant : null,
                SearchPanelConfirmed = panelConfirmed,
                FrameDifference = difference,
                IsStable = stable,
                Message = observationMessage
            };
            observations.Add(observation);
            result.ObservedFrameCount = observations.Count;

            if (toastVerified)
            {
                result.NotFoundObserved = true;
                result.NotFoundToastVerified = true;
                result.MatchedNotFoundVariant = matchedVariant;
                result.FailureReason = matchedVariant == "ResourceAreaLv2Redirect"
                    ? ResourceSearchFailureReason.ResourceAreaLv2Redirect
                    : matchedVariant == "SeasonMapRestriction"
                    ? ResourceSearchFailureReason.SeasonMapRestriction
                    : matchedVariant == TargetLevelTooLowVariant
                        ? ResourceSearchFailureReason.TargetLevelTooLow
                        : matchedVariant == SearchOtherRegionVariant
                            ? ResourceSearchFailureReason.SearchOtherRegion
                            : ResourceSearchFailureReason.ResourceUnavailable;
                result.ShouldRepositionMap = result.FailureReason
                    == ResourceSearchFailureReason.SearchOtherRegion;
                result.ShouldTryLowerLevel = result.FailureReason
                    == ResourceSearchFailureReason.TargetLevelTooLow;
            }
            LogObservation(deviceName, result, context, observation,
                toastAnchor, actionAnchor, shortAnchor, otherRegionAnchor,
                targetLevelTooLowAnchor, seasonMapAnchor);

            context.ReplacePreviousFrame(frame);
            frame = null;

            if (toastVerified)
            {
                return ObservationDecision.Decided(matchedVariant == "ResourceAreaLv2Redirect"
                        ? ResourceSearchOutcome.ResourceAreaLv2Redirect
                        : ResourceSearchOutcome.ResourceNotFound,
                    $"ResourceNotFound toast variant '{matchedVariant}' was verified in one observation frame.", null);
            }
            if (!result.NotFoundObserved && detection.State == GameState.ResourcePopup)
            {
                if (popupVerificationService == null)
                    return ObservationDecision.Decided(ResourceSearchOutcome.Failed,
                        "ResourcePopup was detected but no popup verification service is configured.", null);
                ResourcePopupVerificationResult popup;
                if (popupVerificationService is IResourceAwarePopupVerificationService resourceAware)
                    popup = await resourceAware.VerifyAsync(deviceName, expectedResource, cancellationToken);
                else
                    popup = await popupVerificationService.VerifyAsync(deviceName, cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                result.PopupVerificationResult = popup;
                observation.PopupOutcome = popup.Outcome;
                observation.Message = "ResourcePopup verification outcome: " + popup.Outcome + ". " + popup.Message;
                if (popup.Outcome == ResourcePopupOutcome.ResourcePopupReady)
                {
                    result.PanelClosed = true;
                    result.FinalState = GameState.ResourcePopup;
                    return ObservationDecision.Decided(ResourceSearchOutcome.ResourceLocated,
                        $"{expectedResource} ResourcePopup and enabled Gather button were verified; popup evidence superseded camera stability.", null);
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
            return ObservationDecision.Pending();
            }
            finally
            {
                if (frame != null) frame.Dispose();
            }
        }

        private async Task<ObservationDecision> VerifyPopupAsync(string deviceName,
            ResourceType expectedResource, ResourceSearchExecutionResult result,
            CancellationToken cancellationToken)
        {
            if (result.NotFoundObserved || popupVerificationService == null
                || !result.PanelClosed || result.FinalState != GameState.WorldMap)
                return ObservationDecision.Pending();

            cancellationToken.ThrowIfCancellationRequested();
            ResourcePopupVerificationResult popup;
            if (popupVerificationService is IResourceAwarePopupVerificationService resourceAware)
                popup = await resourceAware.VerifyAsync(deviceName, expectedResource, cancellationToken);
            else
                popup = await popupVerificationService.VerifyAsync(deviceName, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            result.PopupVerificationResult = popup;

            if (popup.Outcome == ResourcePopupOutcome.ResourcePopupReady)
            {
                result.PanelClosed = true;
                result.FinalState = GameState.ResourcePopup;
                return ObservationDecision.Decided(ResourceSearchOutcome.ResourceLocated,
                    $"{expectedResource} ResourcePopup and enabled Gather button were verified after search observation; popup evidence superseded camera stability.", null);
            }
            if (popup.Outcome == ResourcePopupOutcome.Failed)
                return ObservationDecision.Decided(ResourceSearchOutcome.Failed,
                    "ResourcePopup verification failed.", popup.ErrorMessage);
            if (popup.Outcome == ResourcePopupOutcome.Cancelled)
                throw new OperationCanceledException(cancellationToken);

            return ObservationDecision.Pending();
        }

        private async Task<ResourceSearchExecutionResult> CompleteAsync(string deviceName,
            ResourceSearchExecutionResult result, ObservationContext context,
            ResourceSearchOutcome outcome, string message, string error,
            Stopwatch watch, CancellationToken cancellationToken)
        {
            result.Outcome = outcome;
            result.Success = outcome == ResourceSearchOutcome.ResourceLocated;
            result.PanelRemainedOpen = context.LastPanelConfirmed;
            result.MovementDetected = result.CameraMovementObserved;
            result.ShouldRetrySearch = result.ShouldRetrySearch
                || outcome == ResourceSearchOutcome.SearchTapNotApplied;
            if (result.FailureReason == ResourceSearchFailureReason.None)
            {
                result.FailureReason = outcome == ResourceSearchOutcome.SearchTapNotApplied
                    ? ResourceSearchFailureReason.SearchTapNotApplied
                    : outcome == ResourceSearchOutcome.SearchTransitionTimeout
                        ? ResourceSearchFailureReason.SearchTransitionTimeout
                        : outcome == ResourceSearchOutcome.TechnicalFailure
                            ? ResourceSearchFailureReason.TechnicalFailure
                            : ResourceSearchFailureReason.None;
            }
            result.Message = message;
            result.ErrorMessage = error;
            result.Duration = watch.Elapsed;
            if (options.SaveResultScreenshots && context.PreviousFrame != null
                && outcome != ResourceSearchOutcome.Cancelled)
            {
                try
                {
                    result.DiagnosticScreenshotPath = await diagnosticStore.SaveResultAsync(
                        deviceName, outcome, context.PreviousFrame.GetPngBytes(), cancellationToken);
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
                + $"NotFoundVariant='{result.MatchedNotFoundVariant ?? string.Empty}', "
                + $"DurationMs={result.Duration.TotalMilliseconds:F0}, Cancellation=false, Error='{error ?? string.Empty}'");
            return result;
        }

        private async Task TrySaveBurstFrameAsync(string deviceName, CapturedFrame frame,
            ObservationContext context, CancellationToken cancellationToken)
        {
            if (!options.SaveObservationBurst || context.BurstFrameCount >= options.MaxObservationBurstFrames)
                return;
            context.BurstFrameCount++;
            try
            {
                await diagnosticStore.SaveObservationAsync(deviceName, context.BurstTimestamp,
                    context.BurstFrameCount, frame.GetPngBytes(), cancellationToken);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception exception)
            {
                logger.Error($"[Resource Search Execution] DeviceName='{deviceName}', "
                    + $"ObservationFrame={context.BurstFrameCount}, BurstSaveError='{exception.Message}'", exception);
            }
        }

        private async Task<CapturedFrame> CaptureFrameAsync(string deviceName,
            CancellationToken cancellationToken)
        {
            var frameClient = ldPlayerClient as IFrameCapturingLdPlayerClient;
            if (frameClient != null)
                return await frameClient.CaptureFrameAsync(deviceName, cancellationToken);

            byte[] png = await ldPlayerClient.CaptureScreenshotPngAsync(deviceName,
                cancellationToken);
            using (var stream = new MemoryStream(png, writable: false))
            using (var source = new Bitmap(stream))
                return new CapturedFrame(new Bitmap(source), DateTimeOffset.UtcNow);
        }

        private GameDetectionResult Detect(CapturedFrame frame, string deviceName,
            GameStateDetectionContext context)
        {
            var frameDetector = detector as IFrameGameStateDetector;
            return frameDetector != null
                ? frameDetector.Detect(frame, deviceName, context)
                : detector.Detect(frame.GetPngBytes());
        }

        private void RememberKnownState(string deviceName, GameState state, ObservationContext context)
        {
            if (state == GameState.Unknown) return;
            context.LastKnownState = state;
            lastKnownStates[deviceName] = state;
        }

        private ImageMatchResult Match(CapturedFrame frame, TemplateId id, ImageRegion? region)
        {
            byte[] template = templateRegistry.LoadBytes(id);
            var frameMatcher = imageMatcher as IFrameImageMatcher;
            return frameMatcher != null
                ? frameMatcher.Find(frame, template, region)
                : imageMatcher.Find(frame.GetPngBytes(), template, region);
        }

        private async Task<IReadOnlyList<ImageMatchResult>> MatchResourceAreaLv2AnchorsAsync(
            CapturedFrame frame, ImageRegion toastRoi, CancellationToken cancellationToken)
        {
                TemplateId[] ids =
                {
                    TemplateId.ResourceAreaPhraseAnchor,
                    TemplateId.ResourceAreaLv2EndingAnchor
                };
            var requests = ids.Select(id => new ImageMatchRequest(
                templateRegistry.LoadBytes(id), toastRoi)).ToArray();
            var asyncMatcher = imageMatcher as IAsyncFrameImageMatcher;
            if (asyncMatcher != null)
                return await asyncMatcher.FindManyAsync(frame, requests, cancellationToken);
            var frameMatcher = imageMatcher as IFrameImageMatcher;
            if (frameMatcher != null)
                return frameMatcher.FindMany(frame, requests);
            var batchMatcher = imageMatcher as IBatchImageMatcher;
            if (batchMatcher != null)
                return batchMatcher.FindMany(frame.GetPngBytes(), requests);
            byte[] screenshot = frame.GetPngBytes();
            return requests.Select(request => imageMatcher.Find(screenshot,
                request.TemplatePng, request.SearchRegion)).ToArray();
        }

        private async Task<IReadOnlyList<ImageMatchResult>> MatchToastAnchorsAsync(
            CapturedFrame frame, CancellationToken cancellationToken)
        {
            TemplateId[] ids = { TemplateId.ResourceNotFoundToastAnchor,
                TemplateId.ResourceNotFoundToastActionAnchor,
                TemplateId.ResourceNotFoundToastShortAnchor,
                TemplateId.ResourceNotFoundToastOtherRegionAnchor,
                TemplateId.ResourceTargetLevelTooLowToastAnchor,
                TemplateId.ResourceTargetLevelSeasonMapToastAnchor };
            var requests = new List<ImageMatchRequest>();
            var indexes = new List<int>();
            var results = Enumerable.Repeat(ImageMatchResult.NotFound(), ids.Length).ToArray();
            for (int index = 0; index < ids.Length; index++)
            {
                if (index > 1 && !templateRegistry.Exists(ids[index])) continue;
                requests.Add(new ImageMatchRequest(templateRegistry.LoadBytes(ids[index]), options.ToastRegion));
                indexes.Add(index);
            }
            IReadOnlyList<ImageMatchResult> matched;
            var asyncMatcher = imageMatcher as IAsyncFrameImageMatcher;
            var frameMatcher = imageMatcher as IFrameImageMatcher;
            if (asyncMatcher != null)
                matched = await asyncMatcher.FindManyAsync(frame, requests, cancellationToken);
            else if (frameMatcher != null) matched = frameMatcher.FindMany(frame, requests);
            else
            {
                var batchMatcher = imageMatcher as IBatchImageMatcher;
                matched = batchMatcher != null ? batchMatcher.FindMany(frame.GetPngBytes(), requests)
                    : requests.Select(request => imageMatcher.Find(frame.GetPngBytes(), request.TemplatePng,
                        request.SearchRegion)).ToArray();
            }
            for (int index = 0; index < indexes.Count; index++)
                results[indexes[index]] = matched[index] ?? ImageMatchResult.NotFound();
            return results;
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

        private string ValidateResolution(CapturedFrame frame)
        {
            if (frame == null) return "Screenshot frame was not captured.";
            return frame.Width == options.ExpectedWidth && frame.Height == options.ExpectedHeight
                ? null : $"Expected {options.ExpectedWidth}x{options.ExpectedHeight}, actual {frame.Width}x{frame.Height}.";
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

        private static GameDetectionEvidence FindEvidence(GameDetectionResult result, TemplateId id)
        {
            return result?.Evidence?.FirstOrDefault(item => item.TemplateId == id);
        }

        private static bool IsInside(ImageMatchResult match, ImageRegion region)
        {
            return HasBounds(match)
                && match.X >= region.X
                && match.Y >= region.Y
                && match.X + match.Width <= region.X + region.Width
                && match.Y + match.Height <= region.Y + region.Height;
        }

        private static ImageRegion ScaleRegion(ImageRegion region, int width, int height)
        {
            int x = (int)Math.Round(region.X * width / 1280.0);
            int y = (int)Math.Round(region.Y * height / 720.0);
            int scaledWidth = Math.Max(1, (int)Math.Round(region.Width * width / 1280.0));
            int scaledHeight = Math.Max(1, (int)Math.Round(region.Height * height / 720.0));
            x = Math.Max(0, Math.Min(x, Math.Max(0, width - 1)));
            y = Math.Max(0, Math.Min(y, Math.Max(0, height - 1)));
            scaledWidth = Math.Min(scaledWidth, width - x);
            scaledHeight = Math.Min(scaledHeight, height - y);
            return new ImageRegion(x, y, Math.Max(1, scaledWidth), Math.Max(1, scaledHeight));
        }

        private static string FormatRegion(ImageRegion region) =>
            $"({region.X},{region.Y},{region.Width},{region.Height})";

        private static string FormatBounds(ImageMatchResult match) =>
            HasBounds(match) ? $"({match.X},{match.Y},{match.Width},{match.Height})" : string.Empty;

        private static bool IsUnchangedSearchPanel(ResourceSearchExecutionResult result,
            ObservationContext context) => !result.CameraMovementObserved
            && (context.LastPanelConfirmed
                || (!result.PanelClosed && result.FinalState == GameState.ResourceSearchPanel));

        private bool AreToastAnchorsClose(ImageMatchResult first, ImageMatchResult second) =>
            HasBounds(first) && HasBounds(second)
            && Math.Abs(first.CenterY - second.CenterY)
                <= options.MaxToastAnchorVerticalDistancePx;

        private static bool HasPartialNotFoundToast(IEnumerable<ResourceSearchObservation> observations) =>
            observations != null && observations.Any(item =>
                item.ToastAnchorFound
                || item.ToastActionAnchorFound
                || item.ShortAnchorFound
                || item.OtherRegionAnchorFound
                || item.TargetLevelTooLowAnchorFound
                || item.SeasonMapAnchorFound);

        private static ResourceSearchExecutionResult NewResult(List<ResourceSearchObservation> observations) =>
            new ResourceSearchExecutionResult
            {
                Outcome = ResourceSearchOutcome.Failed,
                InitialState = GameState.Unknown,
                FinalState = GameState.Unknown,
                ToastEvidence = new ResourceSearchToastEvidence(),
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
                FailureReason = outcome == ResourceSearchOutcome.ResourceAreaLv2TemplateUnavailable
                    ? ResourceSearchFailureReason.ResourceAreaLv2TemplateUnavailable
                    : ResourceSearchFailureReason.None,
                ToastEvidence = new ResourceSearchToastEvidence(),
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
            ImageMatchResult toast, ImageMatchResult action,
            ImageMatchResult shortAnchor, ImageMatchResult otherRegionAnchor,
            ImageMatchResult targetLevelTooLowAnchor, ImageMatchResult seasonMapAnchor)
        {
            string toastBounds = HasBounds(toast) ? $"({toast.X},{toast.Y},{toast.Width},{toast.Height})" : string.Empty;
            string actionBounds = HasBounds(action) ? $"({action.X},{action.Y},{action.Width},{action.Height})" : string.Empty;
            string shortBounds = HasBounds(shortAnchor) ? $"({shortAnchor.X},{shortAnchor.Y},{shortAnchor.Width},{shortAnchor.Height})" : string.Empty;
            string otherRegionBounds = HasBounds(otherRegionAnchor) ? $"({otherRegionAnchor.X},{otherRegionAnchor.Y},{otherRegionAnchor.Width},{otherRegionAnchor.Height})" : string.Empty;
            string targetLevelTooLowBounds = HasBounds(targetLevelTooLowAnchor) ? $"({targetLevelTooLowAnchor.X},{targetLevelTooLowAnchor.Y},{targetLevelTooLowAnchor.Width},{targetLevelTooLowAnchor.Height})" : string.Empty;
            string seasonMapBounds = HasBounds(seasonMapAnchor) ? $"({seasonMapAnchor.X},{seasonMapAnchor.Y},{seasonMapAnchor.Width},{seasonMapAnchor.Height})" : string.Empty;
            logger.Info($"[Resource Search Observation] DeviceName='{deviceName}', Index={result.ObservedFrameCount}, "
                + $"State='{observation.State}', ToastAnchorFound={observation.ToastAnchorFound}, "
                + $"ToastActionAnchorFound={observation.ToastActionAnchorFound}, ToastBounds='{toastBounds}', "
                + $"ActionBounds='{actionBounds}', ShortAnchorFound={observation.ShortAnchorFound}, "
                + $"OtherRegionAnchorFound={observation.OtherRegionAnchorFound}, ShortBounds='{shortBounds}', "
                + $"OtherRegionBounds='{otherRegionBounds}', MatchedVariant='{observation.MatchedNotFoundVariant ?? string.Empty}', "
                + $"TargetLevelTooLowAnchorFound={observation.TargetLevelTooLowAnchorFound}, "
                + $"SeasonMapAnchorFound={observation.SeasonMapAnchorFound}, "
                + $"TargetLevelTooLowBounds='{targetLevelTooLowBounds}', SeasonMapBounds='{seasonMapBounds}', "
                + $"SearchPanelConfirmed={observation.SearchPanelConfirmed}, "
                + $"FrameDifference={observation.FrameDifference?.ToString("F4") ?? "n/a"}, "
                + $"Movement={result.CameraMovementObserved}, StableFrameCount={context.StableFrameCount}, "
                + $"UnknownFrameCount={context.UnknownFrameCount}, NotFoundLatch={result.NotFoundObserved}");
        }

        private sealed class ObservationContext : IDisposable
        {
            public CapturedFrame PreviousFrame;
            public GameState LastKnownState = GameState.Unknown;
            public bool PreviousPanelConfirmed;
            public bool LastPanelConfirmed;
            public int OpenPanelObservationCount;
            public int StableFrameCount;
            public int UnknownFrameCount;
            public int BurstFrameCount;
            public DateTimeOffset BurstTimestamp;
            public bool MainAnchorSeen;
            public bool ActionAnchorSeen;
            public bool ShortAnchorSeen;
            public bool OtherRegionAnchorSeen;
            public bool TargetLevelTooLowSeen;
            public bool SeasonMapSeen;
            public bool AlternatePairTooFarSeen;
            public ResourceSearchToastEvidence ToastEvidence = new ResourceSearchToastEvidence();

            public bool HasPartialToastEvidence => MainAnchorSeen || ActionAnchorSeen
                || ShortAnchorSeen || OtherRegionAnchorSeen || TargetLevelTooLowSeen || SeasonMapSeen;

            public void ResetToastEvidence()
            {
                MainAnchorSeen = false;
                ActionAnchorSeen = false;
                ShortAnchorSeen = false;
                OtherRegionAnchorSeen = false;
                TargetLevelTooLowSeen = false;
                SeasonMapSeen = false;
                AlternatePairTooFarSeen = false;
                ToastEvidence = new ResourceSearchToastEvidence();
            }

            public void UpdateToastEvidence(int frame)
            {
                ToastEvidence.MainAnchorSeen = MainAnchorSeen;
                ToastEvidence.ActionAnchorSeen = ActionAnchorSeen;
                ToastEvidence.ShortAnchorSeen = ShortAnchorSeen;
                ToastEvidence.OtherRegionAnchorSeen = OtherRegionAnchorSeen;
                ToastEvidence.TargetLevelTooLowSeen = TargetLevelTooLowSeen;
                ToastEvidence.SeasonMapSeen = SeasonMapSeen;
                if (HasPartialToastEvidence && ToastEvidence.FirstSeenFrame == 0)
                    ToastEvidence.FirstSeenFrame = frame;
                if (HasPartialToastEvidence)
                    ToastEvidence.LastSeenFrame = frame;
            }

            public void ReplacePreviousFrame(CapturedFrame frame)
            {
                CapturedFrame previous = PreviousFrame;
                PreviousFrame = frame;
                previous?.Dispose();
            }

            public void Dispose()
            {
                CapturedFrame previous = PreviousFrame;
                PreviousFrame = null;
                previous?.Dispose();
            }
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
