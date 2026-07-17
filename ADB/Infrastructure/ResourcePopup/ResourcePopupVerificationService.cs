using ADB_Tool_Automation_Post_FB.Core.Abstractions;
using ADB_Tool_Automation_Post_FB.Core.Diagnostics;
using ADB_Tool_Automation_Post_FB.Core.GameDetection;
using ADB_Tool_Automation_Post_FB.Core.ResourcePopup;
using ADB_Tool_Automation_Post_FB.Core.Vision;
using ADB_Tool_Automation_Post_FB.Core.ResourceSearch;
using ADB_Tool_Automation_Post_FB.Infrastructure.ResourceSearch;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace ADB_Tool_Automation_Post_FB.Infrastructure.ResourcePopup
{
    public sealed class ResourcePopupVerificationService : IResourceAwarePopupVerificationService
    {
        private static readonly TemplateId[] RequiredTemplates =
        {
            TemplateId.ResourcePopupInfoAnchor,
            TemplateId.GatherButtonEnabled
        };

        private readonly IGameStateDetector detector;
        private readonly ILdPlayerClient client;
        private readonly ITemplateRegistry registry;
        private readonly IImageMatcher matcher;
        private readonly ResourcePopupVerificationOptions options;
        private readonly IResourcePopupDiagnosticStore diagnosticStore;
        private readonly IDiagnosticLogger logger;
        private readonly IResourceTemplateProfileProvider profileProvider;

        public ResourcePopupVerificationService(IGameStateDetector detector, ILdPlayerClient client,
            ITemplateRegistry registry, IImageMatcher matcher, ResourcePopupVerificationOptions options,
            IResourcePopupDiagnosticStore diagnosticStore, IDiagnosticLogger logger)
            : this(detector, client, registry, matcher, options, diagnosticStore, logger,
                new ResourceTemplateProfileProvider(registry))
        {
        }

        public ResourcePopupVerificationService(IGameStateDetector detector, ILdPlayerClient client,
            ITemplateRegistry registry, IImageMatcher matcher, ResourcePopupVerificationOptions options,
            IResourcePopupDiagnosticStore diagnosticStore, IDiagnosticLogger logger,
            IResourceTemplateProfileProvider profileProvider)
        {
            this.detector = detector ?? throw new ArgumentNullException(nameof(detector));
            this.client = client ?? throw new ArgumentNullException(nameof(client));
            this.registry = registry ?? throw new ArgumentNullException(nameof(registry));
            this.matcher = matcher ?? throw new ArgumentNullException(nameof(matcher));
            this.options = options ?? throw new ArgumentNullException(nameof(options));
            this.diagnosticStore = diagnosticStore ?? throw new ArgumentNullException(nameof(diagnosticStore));
            this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
            this.profileProvider = profileProvider ?? throw new ArgumentNullException(nameof(profileProvider));
        }

        public async Task<ResourcePopupVerificationResult> VerifyAsync(
            string deviceName, CancellationToken cancellationToken)
        {
            return await VerifyAsync(deviceName, ResourceType.Iron, cancellationToken);
        }

        public async Task<ResourcePopupVerificationResult> VerifyAsync(
            string deviceName, ResourceType resourceType, CancellationToken cancellationToken)
        {
            var watch = Stopwatch.StartNew();
            var result = NewResult(resourceType);
            byte[] lastFrame = null;
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (string.IsNullOrWhiteSpace(deviceName))
                    return Complete(result, ResourcePopupOutcome.Failed, watch,
                        "LDPlayer device name is required.", "LDPlayer device name is required.");
                string templateError = profileProvider.IsSupported(resourceType)
                    ? ValidateTemplates(resourceType)
                    : profileProvider.GetUnsupportedReason(resourceType);
                if (templateError != null)
                    return Complete(result, ResourcePopupOutcome.Failed, watch, templateError, templateError);

                int readyFrames = 0;
                bool popupObserved = false;
                var timeout = TimeSpan.FromSeconds(options.VerificationTimeoutSeconds);
                while (watch.Elapsed < timeout)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    lastFrame = await client.CaptureScreenshotPngAsync(deviceName, cancellationToken);
                    GameDetectionResult detection = detector.Detect(lastFrame);
                    if (detection == null || !detection.IsSuccessful)
                        return await CompleteWithDiagnosticAsync(deviceName, result,
                            ResourcePopupOutcome.Failed, watch, "Game state detection failed.",
                            detection?.ErrorMessage, lastFrame, cancellationToken);

                    result.ObservedFrameCount++;
                    if (result.ObservedFrameCount == 1) result.InitialState = detection.State;
                    result.FinalState = detection.State;
                    GameDetectionEvidence anchor = Match(lastFrame, TemplateId.ResourcePopupInfoAnchor);
                    ResourceTemplateProfile expectedProfile = profileProvider.Get(resourceType);
                    GameDetectionEvidence iron = Match(lastFrame, expectedProfile.PopupTitleTemplate);
                    ResourceType? mismatch = FindMismatchedResource(lastFrame, resourceType);
                    GameDetectionEvidence gather = Match(lastFrame, TemplateId.GatherButtonEnabled);
                    result.Evidence = new[] { anchor, iron, gather };
                    result.PopupAnchorVerified = anchor.Found;
                    result.IronResourceVerified = iron.Found;
                    result.ResourceVerified = iron.Found;
                    result.ExpectedResourceVerified = iron.Found;
                    result.MismatchedResource = mismatch;
                    result.GatherButtonVerified = gather.Found;
                    result.GatherButtonMatch = gather.MatchResult;

                    int signals = (anchor.Found ? 1 : 0) + (iron.Found ? 1 : 0) + (gather.Found ? 1 : 0);
                    bool detected = signals >= 2 && (anchor.Found || iron.Found);
                    popupObserved |= detected || detection.State == GameState.ResourcePopup;
                    if (!iron.Found && mismatch.HasValue && anchor.Found)
                        return Complete(result, ResourcePopupOutcome.ResourcePopupMismatch, watch,
                            $"Popup title belongs to {mismatch.Value}, not expected {resourceType}; no Gather input was sent.", null);
                    bool ready = anchor.Found && iron.Found && gather.Found;
                    readyFrames = ready ? readyFrames + 1 : 0;
                    LogFrame(deviceName, result, anchor, iron, gather);
                    if (readyFrames >= options.RequiredConsecutiveReadyFrames)
                        return Complete(result, ResourcePopupOutcome.ResourcePopupReady, watch,
                            $"{resourceType} resource popup and enabled Gather button were verified.", null);
                    await Task.Delay(options.PollIntervalMs, cancellationToken);
                }

                ResourcePopupOutcome outcome = popupObserved
                    ? ResourcePopupOutcome.ResourcePopupDetectedButNotReady
                    : ResourcePopupOutcome.ResourcePopupNotDetected;
                string message = popupObserved
                    ? $"Resource popup was detected but {resourceType}/Gather readiness was not fully verified."
                    : "Resource popup was not detected before timeout.";
                return await CompleteWithDiagnosticAsync(deviceName, result, outcome, watch,
                    message, null, lastFrame, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                return Complete(result, ResourcePopupOutcome.Cancelled, watch,
                    "Resource popup verification was cancelled.", null);
            }
            catch (Exception exception)
            {
                logger.Error($"[Resource Popup Verification] DeviceName='{deviceName}', Error='{exception.Message}'", exception);
                return await CompleteWithDiagnosticAsync(deviceName, result,
                    ResourcePopupOutcome.Failed, watch, "Resource popup verification failed.",
                    exception.Message, lastFrame, cancellationToken);
            }
        }

        private GameDetectionEvidence Match(byte[] screenshot, TemplateId id)
        {
            ImageMatchResult match = matcher.Find(screenshot, registry.LoadBytes(id), options.PopupRegion);
            return new GameDetectionEvidence
            {
                TemplateId = id, TemplateExists = true,
                Found = match != null && match.Found, MatchResult = match,
                Confidence = match?.Confidence, SearchRegion = options.PopupRegion,
                Message = match != null && match.Found
                    ? $"Template '{id}' matched inside ResourcePopup ROI."
                    : $"Template '{id}' did not match inside ResourcePopup ROI."
            };
        }

        private string ValidateTemplates(ResourceType resourceType)
        {
            foreach (TemplateId id in new List<TemplateId>(RequiredTemplates)
            {
                profileProvider.Get(resourceType).PopupTitleTemplate
            })
            {
                try
                {
                    string path = registry.GetPath(id);
                    if (!registry.Exists(id)) return $"Required template '{id}' was not found at '{path}'.";
                }
                catch (Exception exception)
                {
                    return $"Required template '{id}' could not be resolved: {exception.Message}";
                }
            }
            return null;
        }

        private async Task<ResourcePopupVerificationResult> CompleteWithDiagnosticAsync(
            string deviceName, ResourcePopupVerificationResult result, ResourcePopupOutcome outcome,
            Stopwatch watch, string message, string error, byte[] frame, CancellationToken token)
        {
            Complete(result, outcome, watch, message, error);
            if (options.SaveFailureScreenshots && frame != null && outcome != ResourcePopupOutcome.Cancelled)
            {
                try { result.DiagnosticScreenshotPath = await diagnosticStore.SaveAsync(deviceName, outcome, frame, token); }
                catch (OperationCanceledException) { throw; }
                catch (Exception exception)
                {
                    logger.Error($"[Resource Popup Verification] DeviceName='{deviceName}', DiagnosticSaveError='{exception.Message}'", exception);
                }
            }
            return result;
        }

        private ResourcePopupVerificationResult Complete(ResourcePopupVerificationResult result,
            ResourcePopupOutcome outcome, Stopwatch watch, string message, string error)
        {
            result.Outcome = outcome;
            result.Success = outcome == ResourcePopupOutcome.ResourcePopupReady;
            result.Duration = watch.Elapsed;
            result.Message = message;
            result.ErrorMessage = error;
            logger.Info($"[Resource Popup Verification] InitialState='{result.InitialState}', FinalState='{result.FinalState}', "
                + $"PopupROI=({options.PopupRegion.X},{options.PopupRegion.Y},{options.PopupRegion.Width},{options.PopupRegion.Height}), "
                + $"PopupAnchor={result.PopupAnchorVerified}, IronTitle={result.IronResourceVerified}, "
                + $"GatherButton={result.GatherButtonVerified}, Outcome='{outcome}', "
                + $"ObservedFrames={result.ObservedFrameCount}, DurationMs={result.Duration.TotalMilliseconds:F0}, "
                + $"Cancellation={outcome == ResourcePopupOutcome.Cancelled}, Error='{error ?? string.Empty}'");
            return result;
        }

        private void LogFrame(string deviceName, ResourcePopupVerificationResult result,
            GameDetectionEvidence anchor, GameDetectionEvidence iron, GameDetectionEvidence gather)
        {
            logger.Info($"[Resource Popup Observation] DeviceName='{deviceName}', Index={result.ObservedFrameCount}, "
                + $"PopupAnchor={Bounds(anchor)}, IronTitle={Bounds(iron)}, GatherButton={Bounds(gather)}");
        }

        private static string Bounds(GameDetectionEvidence evidence) => evidence.Found && evidence.MatchResult != null
            ? $"true:({evidence.MatchResult.X},{evidence.MatchResult.Y},{evidence.MatchResult.Width},{evidence.MatchResult.Height})"
            : "false";

        private ResourceType? FindMismatchedResource(byte[] screenshot, ResourceType expected)
        {
            foreach (ResourceType candidate in (ResourceType[])Enum.GetValues(typeof(ResourceType)))
            {
                if (candidate == expected) continue;
                ResourceTemplateProfile profile = profileProvider.Get(candidate);
                if (registry.Exists(profile.PopupTitleTemplate)
                    && Match(screenshot, profile.PopupTitleTemplate).Found) return candidate;
            }
            return null;
        }

        private ResourcePopupVerificationResult NewResult(ResourceType resourceType) => new ResourcePopupVerificationResult
        {
            Outcome = ResourcePopupOutcome.Failed,
            ResourceType = resourceType,
            ExpectedResource = resourceType,
            ExpectedPopupTitleTemplate = profileProvider.Get(resourceType).PopupTitleTemplate,
            InitialState = GameState.Unknown,
            FinalState = GameState.Unknown,
            Evidence = new GameDetectionEvidence[0]
        };
    }
}
