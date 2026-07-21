using ADB_Tool_Automation_Post_FB.Core.Abstractions;
using ADB_Tool_Automation_Post_FB.Core.Concurrency;
using ADB_Tool_Automation_Post_FB.Core.Diagnostics;
using ADB_Tool_Automation_Post_FB.Core.GameDetection;
using ADB_Tool_Automation_Post_FB.Core.ResourceSearch;
using ADB_Tool_Automation_Post_FB.Core.Vision;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace ADB_Tool_Automation_Post_FB.Infrastructure.ResourceSearch
{
    public sealed class ResourceLevelFallbackService : IResourceLevelFallbackService
    {
        private static readonly TemplateId[] ToastTemplates =
        {
            TemplateId.ResourceNotFoundToastAnchor,
            TemplateId.ResourceNotFoundToastActionAnchor,
            TemplateId.ResourceNotFoundToastShortAnchor,
            TemplateId.ResourceNotFoundToastOtherRegionAnchor
        };

        private readonly IResourceSearchConfigurationService configuration;
        private readonly IResourceSearchExecutionService search;
        private readonly IGameStateDetector detector;
        private readonly ILdPlayerClient client;
        private readonly ITemplateRegistry registry;
        private readonly IImageMatcher matcher;
        private readonly IDeviceOperationLock operationLock;
        private readonly ResourceSearchConfigurationOptions configurationOptions;
        private readonly ResourceLevelFallbackOptions options;
        private readonly IResourceLevelFallbackDiagnosticStore diagnostics;
        private readonly IDiagnosticLogger logger;
        private readonly object ceilingSync = new object();
        private readonly Dictionary<string, RunLevelCeiling> ceilingsByDevice =
            new Dictionary<string, RunLevelCeiling>(StringComparer.OrdinalIgnoreCase);

        public ResourceLevelFallbackService(IResourceSearchConfigurationService configuration,
            IResourceSearchExecutionService search, IGameStateDetector detector,
            ILdPlayerClient client, ITemplateRegistry registry, IImageMatcher matcher,
            IDeviceOperationLock operationLock, ResourceSearchConfigurationOptions configurationOptions,
            ResourceLevelFallbackOptions options, IResourceLevelFallbackDiagnosticStore diagnostics,
            IDiagnosticLogger logger)
        {
            this.configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
            this.search = search ?? throw new ArgumentNullException(nameof(search));
            this.detector = detector ?? throw new ArgumentNullException(nameof(detector));
            this.client = client ?? throw new ArgumentNullException(nameof(client));
            this.registry = registry ?? throw new ArgumentNullException(nameof(registry));
            this.matcher = matcher ?? throw new ArgumentNullException(nameof(matcher));
            this.operationLock = operationLock ?? throw new ArgumentNullException(nameof(operationLock));
            this.configurationOptions = configurationOptions ?? throw new ArgumentNullException(nameof(configurationOptions));
            this.options = options ?? throw new ArgumentNullException(nameof(options));
            this.diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
            this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public Task<ResourceLevelFallbackResult> SearchAsync(string deviceName,
            ResourceType resourceType, ResourceLevelFallbackPolicy policy,
            bool unoccupiedOnly, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(deviceName)) throw new ArgumentException("LDPlayer device name is required.", nameof(deviceName));
            string validation = Validate(resourceType, policy);
            if (validation != null) return Task.FromResult(Invalid(resourceType, policy, validation));
            return operationLock.RunAsync(deviceName.Trim(), token => SearchCoreAsync(
                deviceName.Trim(), resourceType, policy, unoccupiedOnly, token), cancellationToken);
        }

        private async Task<ResourceLevelFallbackResult> SearchCoreAsync(string deviceName,
            ResourceType resourceType, ResourceLevelFallbackPolicy policy,
            bool unoccupiedOnly, CancellationToken token)
        {
            var watch = Stopwatch.StartNew();
            var attempts = new List<ResourceLevelAttemptResult>();
            var result = NewResult(resourceType, policy, attempts);
            string runId = string.IsNullOrWhiteSpace(policy.RunId) ? Guid.NewGuid().ToString() : policy.RunId;
            try
            {
                token.ThrowIfCancellationRequested();
                GameDetectionResult initial = await detector.DetectAsync(deviceName, token);
                result.InitialState = initial.State; result.FinalState = initial.State;
                if (!IsPanel(initial))
                    return await CompleteAsync(deviceName, runId, result, ResourceLevelFallbackOutcome.PanelUnavailable,
                        "ResourceSearchPanel is not open; no input was sent.", initial.ErrorMessage,
                        watch, "panel-unavailable", token, true);

                bool needsToastClear = false;
                int? knownCeiling = GetKnownCeiling(deviceName, runId);
                foreach (int level in policy.Levels)
                {
                    if (knownCeiling.HasValue && level > knownCeiling.Value)
                    {
                        logger.Info($"[Resource Level Fallback] RunId='{runId}', DeviceName='{deviceName}', "
                            + $"Level='{level}', Skipped=true, KnownAccountCeiling='{knownCeiling.Value}'");
                        continue;
                    }
                    for (int attemptNumber = 1; attemptNumber <= policy.AttemptsPerLevel; attemptNumber++)
                    {
                        token.ThrowIfCancellationRequested();
                        var attemptWatch = Stopwatch.StartNew();
                        var attempt = new ResourceLevelAttemptResult { Level = level, AttemptNumber = attemptNumber };
                        attempts.Add(attempt); result.LastAttemptedLevel = level;

                        if (needsToastClear && policy.WaitForToastClearBetweenAttempts)
                        {
                            ToastClearResult clear = await WaitForToastClearAsync(deviceName, runId, token);
                            attempt.ToastClearResult = clear;
                            attempt.ToastClearVerifiedBeforeAttempt = clear.Cleared;
                            if (!clear.Cleared)
                            {
                                attempt.Duration = attemptWatch.Elapsed;
                                attempt.Message = clear.Message; attempt.ErrorMessage = clear.ErrorMessage;
                                ResourceLevelFallbackOutcome outcome = clear.PanelVerified
                                    ? ResourceLevelFallbackOutcome.SearchFailed
                                    : ResourceLevelFallbackOutcome.PanelUnavailable;
                                return await CompleteAsync(deviceName, runId, result, outcome, clear.Message,
                                    clear.ErrorMessage, watch, $"level-{level}_{outcome.ToString().ToLowerInvariant()}", token, true);
                            }
                        }

                        var request = new ResourceSearchConfigurationRequest
                        {
                            ResourceType = resourceType, TargetLevel = level, UnoccupiedOnly = unoccupiedOnly
                        };
                        ResourceSearchConfigurationResult configured = await configuration.ConfigureAsync(deviceName, request, token);
                        attempt.ConfigurationResult = configured;
                        attempt.ConfigurationSucceeded = configured.Success && configured.ResourceVerified
                            && configured.LevelVerified && configured.FilterVerified;
                        result.FinalState = configured.FinalState;
                        if (!attempt.ConfigurationSucceeded)
                        {
                            attempt.Duration = attemptWatch.Elapsed; attempt.Message = configured.Message;
                            attempt.ErrorMessage = configured.ErrorMessage;
                            if (configured.ObservedLevel.HasValue
                                && configured.ObservedLevel.Value < level)
                            {
                                knownCeiling = RememberCeiling(deviceName, runId,
                                    configured.ObservedLevel.Value);
                                attempt.Message = $"Requested level {level} is unavailable; "
                                    + $"level {configured.ObservedLevel.Value} was verified. "
                                    + "Continuing with the next configured level.";
                                attempt.ErrorMessage = null;
                                LogAttempt(deviceName, runId, attempt);
                                break;
                            }
                            NotFoundToastObservation notFound = await ObserveNotFoundToastAsync(
                                deviceName, token);
                            if (notFound.Verified)
                            {
                                attempt.SearchOutcome = ResourceSearchOutcome.ResourceNotFound;
                                attempt.MatchedNotFoundVariant = notFound.Variant;
                                attempt.Message = $"ResourceNotFound toast variant '{notFound.Variant}' "
                                    + "was verified while configuring the next level.";
                                attempt.ErrorMessage = null;
                                LogAttempt(deviceName, runId, attempt);
                                return await CompleteAsync(deviceName, runId, result,
                                    ResourceLevelFallbackOutcome.ResourceLevelsExhausted,
                                    attempt.Message + " Switching to the next resource.", null,
                                    watch, "resource-not-found-during-configuration", token,
                                    options.SaveExhaustedScreenshot);
                            }
                            return await CompleteAsync(deviceName, runId, result, ResourceLevelFallbackOutcome.ConfigurationFailed,
                                configured.Message, configured.ErrorMessage, watch,
                                $"level-{level}_configurationfailed", token, true);
                        }

                        ResourceSearchExecutionResult searched = await search.ExecuteAsync(deviceName,
                            new ResourceSearchExecutionRequest { Configuration = request, ConfigureBeforeSearch = false }, token);
                        attempt.SearchResult = searched; attempt.SearchOutcome = searched.Outcome;
                        attempt.MatchedNotFoundVariant = searched.MatchedNotFoundVariant;
                        attempt.Duration = attemptWatch.Elapsed; attempt.Message = searched.Message;
                        attempt.ErrorMessage = searched.ErrorMessage; result.FinalState = searched.FinalState;
                        LogAttempt(deviceName, runId, attempt);

                        if (searched.Outcome == ResourceSearchOutcome.Cancelled) throw new OperationCanceledException(token);
                        if (searched.Success && searched.Outcome == ResourceSearchOutcome.ResourceLocated)
                        {
                            result.LocatedLevel = level;
                            return await CompleteAsync(deviceName, runId, result, ResourceLevelFallbackOutcome.ResourceLocated,
                                $"Iron level {level} was located.", null, watch, null, token, false);
                        }
                        if (searched.Outcome != ResourceSearchOutcome.ResourceNotFound)
                            return await CompleteAsync(deviceName, runId, result, ResourceLevelFallbackOutcome.SearchFailed,
                                searched.Message, searched.ErrorMessage, watch,
                                $"level-{level}_searchfailed", token, true);

                        needsToastClear = true;
                    }
                }

                return await CompleteAsync(deviceName, runId, result, ResourceLevelFallbackOutcome.ResourceLevelsExhausted,
                    "No unoccupied Iron target was found at any requested level.", null,
                    watch, "levels-exhausted", token, options.SaveExhaustedScreenshot);
            }
            catch (OperationCanceledException)
            {
                result.Outcome = ResourceLevelFallbackOutcome.Cancelled; result.Success = false;
                result.Duration = watch.Elapsed; result.Message = "Resource level fallback was cancelled.";
                logger.Info($"[Resource Level Fallback] RunId='{runId}', DeviceName='{deviceName}', "
                    + $"Outcome='Cancelled', Cancellation=true, DurationMs={result.Duration.TotalMilliseconds:F0}");
                return result;
            }
            catch (Exception exception)
            {
                logger.Error($"[Resource Level Fallback] RunId='{runId}', DeviceName='{deviceName}', Error='{exception.Message}'", exception);
                return await CompleteAsync(deviceName, runId, result, ResourceLevelFallbackOutcome.Failed,
                    "Resource level fallback failed.", exception.Message, watch,
                    "fallback-failed", token, true);
            }
        }

        private async Task<NotFoundToastObservation> ObserveNotFoundToastAsync(
            string deviceName, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            byte[] screenshot = await client.CaptureScreenshotPngAsync(deviceName, token);
            bool panel = MatchRequired(screenshot, TemplateId.SearchButtonEnabled, null)
                && (MatchRequired(screenshot, TemplateId.ResourceSearchPanelAnchor, null)
                    || MatchRequired(screenshot, TemplateId.LevelMinusButton, null)
                    || MatchRequired(screenshot, TemplateId.ResourceTabSelected, null)
                    || MatchRequired(screenshot, TemplateId.ResourceTabUnselected, null));
            if (!panel) return NotFoundToastObservation.NotVerified;

            ImageMatchResult legacyStart = MatchOptionalResult(screenshot,
                TemplateId.ResourceNotFoundToastAnchor, options.ToastRegion);
            ImageMatchResult legacyEnd = MatchOptionalResult(screenshot,
                TemplateId.ResourceNotFoundToastActionAnchor, options.ToastRegion);
            if (IsToastPair(legacyStart, legacyEnd))
                return new NotFoundToastObservation(true, "LegacyMoveArea");

            ImageMatchResult shortAnchor = MatchOptionalResult(screenshot,
                TemplateId.ResourceNotFoundToastShortAnchor, options.ToastRegion);
            ImageMatchResult otherRegion = MatchOptionalResult(screenshot,
                TemplateId.ResourceNotFoundToastOtherRegionAnchor, options.ToastRegion);
            return IsToastPair(shortAnchor, otherRegion)
                ? new NotFoundToastObservation(true, "SearchOtherRegion")
                : NotFoundToastObservation.NotVerified;
        }

        private int? GetKnownCeiling(string deviceName, string runId)
        {
            lock (ceilingSync)
            {
                RunLevelCeiling value;
                if (!ceilingsByDevice.TryGetValue(deviceName, out value)
                    || !string.Equals(value.RunId, runId, StringComparison.Ordinal))
                {
                    ceilingsByDevice[deviceName] = new RunLevelCeiling(runId, null);
                    return null;
                }
                return value.Level;
            }
        }

        private int RememberCeiling(string deviceName, string runId, int level)
        {
            lock (ceilingSync)
            {
                RunLevelCeiling current;
                int ceiling = level;
                if (ceilingsByDevice.TryGetValue(deviceName, out current)
                    && string.Equals(current.RunId, runId, StringComparison.Ordinal)
                    && current.Level.HasValue)
                    ceiling = Math.Min(current.Level.Value, level);
                ceilingsByDevice[deviceName] = new RunLevelCeiling(runId, ceiling);
                return ceiling;
            }
        }

        private sealed class RunLevelCeiling
        {
            public RunLevelCeiling(string runId, int? level)
            {
                RunId = runId;
                Level = level;
            }

            public string RunId { get; }
            public int? Level { get; }
        }

        private bool IsToastPair(ImageMatchResult first, ImageMatchResult second) =>
            first != null && first.Found && first.Width > 0 && first.Height > 0
            && second != null && second.Found && second.Width > 0 && second.Height > 0
            && Math.Abs(first.CenterY - second.CenterY)
                <= options.MaxToastAnchorVerticalDistancePx;

        private async Task<ToastClearResult> WaitForToastClearAsync(string deviceName, string runId, CancellationToken token)
        {
            var watch = Stopwatch.StartNew(); int observed = 0; int consecutive = 0;
            try
            {
                while (watch.Elapsed < TimeSpan.FromSeconds(options.ToastClearTimeoutSeconds))
                {
                    token.ThrowIfCancellationRequested();
                    byte[] screenshot = await client.CaptureScreenshotPngAsync(deviceName, token);
                    observed++;
                    bool panel = MatchRequired(screenshot, TemplateId.SearchButtonEnabled, null)
                        && (MatchRequired(screenshot, TemplateId.ResourceSearchPanelAnchor, null)
                            || MatchRequired(screenshot, TemplateId.LevelMinusButton, null)
                            || MatchRequired(screenshot, TemplateId.ResourceTabSelected, null)
                            || MatchRequired(screenshot, TemplateId.ResourceTabUnselected, null));
                    if (!panel)
                        return ToastClear(false, false, observed, consecutive, watch,
                            "ResourceSearchPanel closed while waiting for the previous toast to clear.", null);

                    bool toastFound = ToastTemplates.Any(id => MatchOptional(screenshot, id, options.ToastRegion));
                    consecutive = toastFound ? 0 : consecutive + 1;
                    logger.Info($"[Resource Level Fallback] RunId='{runId}', DeviceName='{deviceName}', Phase='ToastClear', "
                        + $"ObservedFrames={observed}, ConsecutiveClearFrames={consecutive}, ToastFound={toastFound}, PanelVerified={panel}");
                    if (consecutive >= options.RequiredConsecutiveToastClearFrames)
                        return ToastClear(true, true, observed, consecutive, watch,
                            "Previous ResourceNotFound toast cleared while ResourceSearchPanel remained open.", null);
                    await Task.Delay(options.ToastClearPollIntervalMs, token);
                }
                return ToastClear(false, true, observed, consecutive, watch,
                    "Timed out waiting for the previous ResourceNotFound toast to clear.", null);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception exception)
            {
                return ToastClear(false, false, observed, consecutive, watch,
                    "Toast-clear verification failed.", exception.Message);
            }
        }

        private bool MatchRequired(byte[] screenshot, TemplateId id, ImageRegion? region)
        {
            ImageMatchResult match = matcher.Find(screenshot, registry.LoadBytes(id), region);
            return match != null && match.Found;
        }

        private bool MatchOptional(byte[] screenshot, TemplateId id, ImageRegion region)
        {
            ImageMatchResult match = MatchOptionalResult(screenshot, id, region);
            return match != null && match.Found;
        }

        private ImageMatchResult MatchOptionalResult(byte[] screenshot, TemplateId id,
            ImageRegion region)
        {
            try
            {
                if (!registry.Exists(id)) return ImageMatchResult.NotFound();
                return matcher.Find(screenshot, registry.LoadBytes(id), region)
                    ?? ImageMatchResult.NotFound();
            }
            catch (System.IO.FileNotFoundException) { return ImageMatchResult.NotFound(); }
            catch (KeyNotFoundException) { return ImageMatchResult.NotFound(); }
        }

        private sealed class NotFoundToastObservation
        {
            public static readonly NotFoundToastObservation NotVerified =
                new NotFoundToastObservation(false, null);

            public NotFoundToastObservation(bool verified, string variant)
            {
                Verified = verified;
                Variant = variant;
            }

            public bool Verified { get; }
            public string Variant { get; }
        }

        private async Task<ResourceLevelFallbackResult> CompleteAsync(string deviceName, string runId,
            ResourceLevelFallbackResult result, ResourceLevelFallbackOutcome outcome,
            string message, string error, Stopwatch watch, string diagnosticSuffix,
            CancellationToken token, bool saveDiagnostic)
        {
            result.Outcome = outcome; result.Success = outcome == ResourceLevelFallbackOutcome.ResourceLocated;
            result.Message = message; result.ErrorMessage = error; result.Duration = watch.Elapsed;
            if (saveDiagnostic && diagnosticSuffix != null)
            {
                try
                {
                    byte[] png = await client.CaptureScreenshotPngAsync(deviceName, token);
                    result.DiagnosticScreenshotPath = await diagnostics.SaveAsync(deviceName, diagnosticSuffix, png, token);
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception exception)
                {
                    logger.Error($"[Resource Level Fallback] RunId='{runId}', DiagnosticError='{exception.Message}'", exception);
                }
            }
            logger.Info($"[Resource Level Fallback] RunId='{runId}', DeviceName='{deviceName}', Outcome='{outcome}', "
                + $"LocatedLevel='{result.LocatedLevel}', LastAttemptedLevel='{result.LastAttemptedLevel}', "
                + $"Attempts={result.Attempts.Count}, DurationMs={result.Duration.TotalMilliseconds:F0}, Error='{error ?? string.Empty}'");
            return result;
        }

        private string Validate(ResourceType resourceType, ResourceLevelFallbackPolicy policy)
        {
            if (!Enum.IsDefined(typeof(ResourceType), resourceType)) return $"Resource '{resourceType}' is not supported.";
            if (policy == null) return "ResourceLevelFallbackPolicy is required.";
            return policy.Validate(configurationOptions.MinimumLevel, configurationOptions.MaximumLevel);
        }

        private static bool IsPanel(GameDetectionResult detection) => detection != null
            && detection.IsSuccessful && detection.State == GameState.ResourceSearchPanel;
        private static ToastClearResult ToastClear(bool cleared, bool panel, int observed,
            int consecutive, Stopwatch watch, string message, string error) => new ToastClearResult
            { Cleared = cleared, PanelVerified = panel, ObservedFrames = observed,
                ConsecutiveClearFrames = consecutive, Duration = watch.Elapsed,
                Message = message, ErrorMessage = error };
        private static ResourceLevelFallbackResult NewResult(ResourceType type,
            ResourceLevelFallbackPolicy policy, IReadOnlyList<ResourceLevelAttemptResult> attempts) =>
            new ResourceLevelFallbackResult { ResourceType = type,
                RequestedLevels = policy.Levels.ToArray(), Attempts = attempts,
                InitialState = GameState.Unknown, FinalState = GameState.Unknown };
        private static ResourceLevelFallbackResult Invalid(ResourceType type,
            ResourceLevelFallbackPolicy policy, string error) => new ResourceLevelFallbackResult
            { Outcome = ResourceLevelFallbackOutcome.Failed, Success = false, ResourceType = type,
                RequestedLevels = policy?.Levels?.ToArray() ?? new int[0],
                Attempts = new ResourceLevelAttemptResult[0], InitialState = GameState.Unknown,
                FinalState = GameState.Unknown, Message = "Fallback request was rejected before input.", ErrorMessage = error };
        private void LogAttempt(string deviceName, string runId, ResourceLevelAttemptResult attempt) =>
            logger.Info($"[Resource Level Fallback] RunId='{runId}', DeviceName='{deviceName}', Level={attempt.Level}, "
                + $"AttemptNumber={attempt.AttemptNumber}, ConfigureSuccess={attempt.ConfigurationSucceeded}, "
                + $"SearchOutcome='{attempt.SearchOutcome}', MatchedNotFoundVariant='{attempt.MatchedNotFoundVariant}', "
                + $"ToastClearFrames={attempt.ToastClearResult?.ConsecutiveClearFrames ?? 0}, "
                + $"DurationMs={attempt.Duration.TotalMilliseconds:F0}, Error='{attempt.ErrorMessage ?? string.Empty}'");
    }
}
