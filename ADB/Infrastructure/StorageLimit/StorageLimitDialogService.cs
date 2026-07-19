using ADB_Tool_Automation_Post_FB.Core.Abstractions;
using ADB_Tool_Automation_Post_FB.Core.Diagnostics;
using ADB_Tool_Automation_Post_FB.Core.GameDetection;
using ADB_Tool_Automation_Post_FB.Core.StorageLimit;
using ADB_Tool_Automation_Post_FB.Core.Vision;
using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace ADB_Tool_Automation_Post_FB.Infrastructure.StorageLimit
{
    public sealed class StorageLimitDialogService : IStorageLimitDialogService
    {
        private readonly ILdPlayerClient client;
        private readonly IGameStateDetector detector;
        private readonly ITemplateRegistry registry;
        private readonly IImageMatcher matcher;
        private readonly StorageLimitDialogOptions options;
        private readonly IDiagnosticLogger logger;

        public StorageLimitDialogService(ILdPlayerClient client, IGameStateDetector detector,
            ITemplateRegistry registry, IImageMatcher matcher,
            StorageLimitDialogOptions options, IDiagnosticLogger logger)
        {
            this.client = client ?? throw new ArgumentNullException(nameof(client));
            this.detector = detector ?? throw new ArgumentNullException(nameof(detector));
            this.registry = registry ?? throw new ArgumentNullException(nameof(registry));
            this.matcher = matcher ?? throw new ArgumentNullException(nameof(matcher));
            this.options = options ?? throw new ArgumentNullException(nameof(options));
            this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
            options.Validate();
        }

        public async Task<StorageLimitDialogResult> HandleAsync(string deviceName,
            StorageLimitPolicy policy, CancellationToken cancellationToken)
        {
            return await HandleCoreAsync(deviceName, policy,
                TemplateId.StorageLimitDialogAnchor, GameState.StorageLimitDialog,
                "Storage limit", cancellationToken);
        }

        public async Task<StorageLimitDialogResult> HandleResourceExpiryAsync(
            string deviceName, CancellationToken cancellationToken)
        {
            return await HandleCoreAsync(deviceName, StorageLimitPolicy.CancelAndSwitchResource,
                TemplateId.ResourceExpiryDialogAnchor, GameState.ResourceExpiryDialog,
                "Resource expiry", cancellationToken);
        }

        private async Task<StorageLimitDialogResult> HandleCoreAsync(string deviceName,
            StorageLimitPolicy policy, TemplateId dialogTemplate, GameState dialogState,
            string logName, CancellationToken cancellationToken)
        {
            var watch = Stopwatch.StartNew();
            var result = NewResult(policy);
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                string missing = MissingTemplate(dialogTemplate)
                    ?? MissingTemplate(TemplateId.StorageLimitCancelButton);
                if (missing != null)
                    return Complete(result, StorageLimitDialogOutcome.ActionButtonUnavailable,
                        $"{logName} Cancel templates are incomplete; no input was sent.", missing, watch);
                if (policy != StorageLimitPolicy.CancelAndSwitchResource)
                    return Complete(result, StorageLimitDialogOutcome.Failed,
                        $"Storage-limit policy '{policy}' is not handled by the cancel-and-switch workflow.", null, watch);

                for (int attempt = 0; attempt < options.MaxCancelAttempts; attempt++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    byte[] fresh = await client.CaptureScreenshotPngAsync(deviceName, cancellationToken);
                    GameDetectionEvidence dialog = Match(fresh, dialogTemplate);
                    GameDetectionEvidence cancel = Match(fresh, TemplateId.StorageLimitCancelButton);
                    result.Evidence = new[] { dialog, cancel };
                    result.DialogVerified = dialog.Found;
                    result.CancelButtonVerified = HasBounds(cancel.MatchResult);
                    result.InitialState = result.DialogVerified
                        ? dialogState : GameState.Unknown;
                    if (!result.DialogVerified)
                        return Complete(result, StorageLimitDialogOutcome.DialogNotVerified,
                            $"{dialogState} was not present in the fresh frame; no input was sent.", null, watch);
                    if (!result.CancelButtonVerified)
                        return Complete(result, StorageLimitDialogOutcome.ActionButtonUnavailable,
                            "StorageLimitCancelButton had no valid fresh bounds; no input was sent.",
                            $"TemplateId '{TemplateId.StorageLimitCancelButton}' at '{registry.GetPath(TemplateId.StorageLimitCancelButton)}'.", watch);

                    await client.TapAsync(deviceName, cancel.MatchResult.CenterX,
                        cancel.MatchResult.CenterY, cancellationToken);
                    result.ActionTapCount++;
                    logger.Info($"[{logName}] DeviceName='{deviceName}', Policy='{policy}', CancelBounds=({cancel.MatchResult.X},{cancel.MatchResult.Y},{cancel.MatchResult.Width},{cancel.MatchResult.Height}), Tap=({cancel.MatchResult.CenterX},{cancel.MatchResult.CenterY}), Attempt={result.ActionTapCount}");

                    StorageLimitDialogResult recovered = await RecoverAfterCancelAsync(
                        deviceName, result, dialogState, logName, cancellationToken, watch);
                    if (recovered != null) return recovered;
                    if (attempt + 1 < options.MaxCancelAttempts)
                        await Task.Delay(options.CancelRetryDelayMs, cancellationToken);
                }

                return Complete(result, result.ModalClosed
                    ? StorageLimitDialogOutcome.RecoveryFailed
                    : StorageLimitDialogOutcome.TransitionTimeout,
                    result.ModalClosed
                        ? $"{dialogState} closed but WorldMap recovery was not verified."
                        : $"{dialogState} did not close before timeout.", null, watch);
            }
            catch (OperationCanceledException)
            {
                return Complete(result, StorageLimitDialogOutcome.Cancelled,
                    $"{logName} handling was cancelled.", null, watch);
            }
            catch (Exception exception)
            {
                logger.Error($"[{logName}] DeviceName='{deviceName}', Error='{exception.Message}'", exception);
                return Complete(result, StorageLimitDialogOutcome.Failed,
                    $"{logName} handling failed.", exception.Message, watch);
            }
        }

        private async Task<StorageLimitDialogResult> RecoverAfterCancelAsync(string deviceName,
            StorageLimitDialogResult result, GameState dialogState, string logName,
            CancellationToken cancellationToken, Stopwatch watch)
        {
            DateTimeOffset deadline = DateTimeOffset.UtcNow.AddSeconds(options.TransitionTimeoutSeconds);
            int unknownFrames = 0;
            while (DateTimeOffset.UtcNow < deadline)
            {
                await Task.Delay(options.PollIntervalMs, cancellationToken);
                GameDetectionResult state = await detector.DetectAsync(deviceName, cancellationToken);
                if (!state.IsSuccessful || state.State == GameState.Unknown)
                {
                    unknownFrames++;
                    if (unknownFrames > options.MaxTransientUnknownFrames) return null;
                    continue;
                }
                unknownFrames = 0;
                result.StateAfterCancel = state.State;
                result.StateAfterConfirmation = state.State;
                result.FinalState = state.State;
                if (state.State == dialogState) return null;

                result.ModalClosed = true;
                if (state.State == GameState.WorldMap)
                {
                    result.ReturnedToWorldMap = true;
                    return Complete(result, StorageLimitDialogOutcome.CancelledForResourceSwitch,
                        "Resource-switch warning was cancelled and WorldMap was verified.", null, watch);
                }
                if (state.State == GameState.ResourceSearchPanel)
                {
                    result.ReturnedToSearchPanel = true;
                    return Complete(result, StorageLimitDialogOutcome.CancelledForResourceSwitch,
                        "Resource-switch warning was cancelled and ResourceSearchPanel is ready.", null, watch);
                }
                if (state.State != GameState.TeamSelection) continue;

                result.ReturnedToTeamSelection = true;
                if (result.BackCount >= options.MaxBackAttempts) return null;
                cancellationToken.ThrowIfCancellationRequested();
                await client.BackAsync(deviceName, cancellationToken);
                result.BackSent = true;
                result.BackCount++;
                result.RecoveryTransitions++;
                logger.Info($"[{logName}] DeviceName='{deviceName}', TeamSelectionVerified=true, BackSent=true, BackCount={result.BackCount}");
                return await VerifyAfterBackAsync(deviceName, result, dialogState,
                    cancellationToken, watch);
            }
            return null;
        }

        private async Task<StorageLimitDialogResult> VerifyAfterBackAsync(string deviceName,
            StorageLimitDialogResult result, GameState dialogState,
            CancellationToken cancellationToken, Stopwatch watch)
        {
            DateTimeOffset deadline = DateTimeOffset.UtcNow.AddSeconds(options.TransitionTimeoutSeconds);
            int unknownFrames = 0;
            bool postBackCancelTapped = false;
            while (DateTimeOffset.UtcNow < deadline)
            {
                await Task.Delay(options.PollIntervalMs, cancellationToken);

                // A Back from TeamSelection can expose the game's exit confirmation
                // instead of returning directly to WorldMap.  This recovery runs only
                // after that one verified Back.  Rematch the safe Cancel action on a
                // fresh frame and never retry it blindly.
                if (!postBackCancelTapped)
                {
                    byte[] fresh = await client.CaptureScreenshotPngAsync(
                        deviceName, cancellationToken);
                    GameDetectionEvidence cancel = Match(
                        fresh, TemplateId.StorageLimitCancelButton);
                    if (HasBounds(cancel.MatchResult))
                    {
                        result.Evidence = new[] { cancel };
                        await client.TapAsync(deviceName, cancel.MatchResult.CenterX,
                            cancel.MatchResult.CenterY, cancellationToken);
                        result.ActionTapCount++;
                        result.PostBackConfirmationCancelled = true;
                        postBackCancelTapped = true;
                        result.RecoveryTransitions++;
                        logger.Info($"[PostBackRecovery] DeviceName='{deviceName}', CancelBounds=({cancel.MatchResult.X},{cancel.MatchResult.Y},{cancel.MatchResult.Width},{cancel.MatchResult.Height}), Tap=({cancel.MatchResult.CenterX},{cancel.MatchResult.CenterY}), BackCount={result.BackCount}");
                        continue;
                    }
                }

                GameDetectionResult state = await detector.DetectAsync(deviceName, cancellationToken);
                if (!state.IsSuccessful || state.State == GameState.Unknown)
                {
                    unknownFrames++;
                    if (unknownFrames > options.MaxTransientUnknownFrames) break;
                    continue;
                }
                unknownFrames = 0;
                result.FinalState = state.State;
                result.StateAfterConfirmation = state.State;
                if (state.State == GameState.WorldMap)
                {
                    result.ReturnedToWorldMap = true;
                    return Complete(result, StorageLimitDialogOutcome.CancelledForResourceSwitch,
                        result.PostBackConfirmationCancelled
                            ? "Resource-switch warning and post-Back confirmation were cancelled; WorldMap was verified."
                            : "Resource-switch warning was cancelled; TeamSelection closed and WorldMap was verified.", null, watch);
                }
                if (state.State == GameState.ResourceSearchPanel)
                {
                    result.ReturnedToSearchPanel = true;
                    return Complete(result, StorageLimitDialogOutcome.CancelledForResourceSwitch,
                        "Resource-switch warning was cancelled; ResourceSearchPanel is ready.", null, watch);
                }
                if (state.State == dialogState) break;
                // Do not send another Back for Unknown or any unverified transition.
            }
            return Complete(result, StorageLimitDialogOutcome.RecoveryFailed,
                "Back was sent once, but WorldMap was not verified before timeout.", null, watch);
        }

        private GameDetectionEvidence Match(byte[] frame, TemplateId id)
        {
            ImageMatchResult match = matcher.Find(frame, registry.LoadBytes(id), options.DialogRegion);
            return new GameDetectionEvidence
            {
                TemplateId = id, TemplateExists = true, Found = match != null && match.Found,
                MatchResult = match, Confidence = match?.Confidence, SearchRegion = options.DialogRegion,
                Message = match != null && match.Found ? $"Template '{id}' matched." : $"Template '{id}' did not match."
            };
        }

        private string MissingTemplate(TemplateId id) => registry.Exists(id)
            ? null : $"Required template '{id}' was not found at '{registry.GetPath(id)}'.";
        private static bool HasBounds(ImageMatchResult match) =>
            match != null && match.Found && match.Width > 0 && match.Height > 0;
        private static StorageLimitDialogResult NewResult(StorageLimitPolicy policy) =>
            new StorageLimitDialogResult
            {
                Policy = policy, InitialState = GameState.Unknown,
                StateAfterCancel = GameState.Unknown, StateAfterConfirmation = GameState.Unknown,
                FinalState = GameState.Unknown, Evidence = new GameDetectionEvidence[0]
            };
        private static StorageLimitDialogResult Complete(StorageLimitDialogResult result,
            StorageLimitDialogOutcome outcome, string message, string error, Stopwatch watch)
        {
            result.Outcome = outcome;
            result.Success = outcome == StorageLimitDialogOutcome.CancelledForResourceSwitch;
            result.Message = message;
            result.ErrorMessage = error;
            result.Duration = watch.Elapsed;
            return result;
        }
    }
}
