using ADB_Tool_Automation_Post_FB.Core.Abstractions;
using ADB_Tool_Automation_Post_FB.Core.Diagnostics;
using ADB_Tool_Automation_Post_FB.Core.GameDetection;
using ADB_Tool_Automation_Post_FB.Core.StorageLimit;
using ADB_Tool_Automation_Post_FB.Core.Vision;
using System;
using System.Collections.Generic;
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
            var result = NewResult(policy);
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                string missing = MissingTemplate(TemplateId.StorageLimitDialogAnchor)
                    ?? MissingTemplate(TemplateId.StorageLimitConfirmButton);
                if (missing != null)
                    return Complete(result, StorageLimitDialogOutcome.ActionButtonUnavailable,
                        "Storage-limit action templates are incomplete; no input was sent.", missing);
                if (policy != StorageLimitPolicy.ConfirmAndSwitchResource)
                    return Complete(result, StorageLimitDialogOutcome.Failed,
                        $"Storage-limit policy '{policy}' is not handled by this workflow.", null);

                for (int attempt = 0; attempt < options.MaxActionAttempts; attempt++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    byte[] fresh = await client.CaptureScreenshotPngAsync(deviceName, cancellationToken);
                    GameDetectionEvidence dialog = Match(fresh, TemplateId.StorageLimitDialogAnchor);
                    GameDetectionEvidence confirm = Match(fresh, TemplateId.StorageLimitConfirmButton);
                    result.Evidence = new[] { dialog, confirm };
                    result.DialogVerified = dialog.Found;
                    result.ConfirmButtonVerified = HasBounds(confirm.MatchResult);
                    if (!result.DialogVerified)
                        return Complete(result, StorageLimitDialogOutcome.DialogNotVerified,
                            "StorageLimitDialog was not present in the fresh frame; no input was sent.", null);
                    if (!result.ConfirmButtonVerified)
                        return Complete(result, StorageLimitDialogOutcome.ActionButtonUnavailable,
                            "StorageLimitConfirmButton had no valid fresh bounds; no input was sent.",
                            $"TemplateId '{TemplateId.StorageLimitConfirmButton}' at '{registry.GetPath(TemplateId.StorageLimitConfirmButton)}'.");

                    await client.TapAsync(deviceName, confirm.MatchResult.CenterX,
                        confirm.MatchResult.CenterY, cancellationToken);
                    result.ActionTapCount++;
                    logger.Info($"[Storage Limit] DeviceName='{deviceName}', Policy='{policy}', ConfirmBounds=({confirm.MatchResult.X},{confirm.MatchResult.Y},{confirm.MatchResult.Width},{confirm.MatchResult.Height}), Tap=({confirm.MatchResult.CenterX},{confirm.MatchResult.CenterY}), Attempt={result.ActionTapCount}");

                    DateTimeOffset deadline = DateTimeOffset.UtcNow.AddSeconds(options.TransitionTimeoutSeconds);
                    int recoveryTransitions = 0;
                    while (DateTimeOffset.UtcNow < deadline)
                    {
                        await Task.Delay(options.PollIntervalMs, cancellationToken);
                        GameDetectionResult state = await detector.DetectAsync(deviceName, cancellationToken);
                        result.StateAfterConfirmation = state.State;
                        if (state.State == GameState.StorageLimitDialog) break;
                        result.ModalClosed = true;
                        if (state.State == GameState.ResourceSearchPanel)
                        {
                            result.ReturnedToSearchPanel = true;
                            return Complete(result, StorageLimitDialogOutcome.ConfirmedForResourceSwitch,
                                "Storage limit was confirmed; ResourceSearchPanel is ready for the next resource.", null);
                        }
                        if (state.State == GameState.WorldMap || state.State == GameState.ContinentMap)
                        {
                            result.ReturnedToWorldMap = state.State == GameState.WorldMap;
                            return Complete(result, StorageLimitDialogOutcome.ConfirmedForResourceSwitch,
                                "Storage limit was confirmed; map state is ready for resource recovery.", null);
                        }
                        if ((state.State == GameState.TeamSelection || state.State == GameState.ResourcePopup)
                            && recoveryTransitions < 3)
                        {
                            await client.BackAsync(deviceName, cancellationToken);
                            recoveryTransitions++;
                            result.RecoveryTransitions = recoveryTransitions;
                            continue;
                        }
                        // Unknown is transient: deliberately poll without blind input.
                    }

                    if (attempt + 1 < options.MaxActionAttempts)
                        await Task.Delay(options.ActionRetryDelayMs, cancellationToken);
                }
                return Complete(result, result.ModalClosed
                    ? StorageLimitDialogOutcome.RecoveryFailed
                    : StorageLimitDialogOutcome.TransitionTimeout,
                    result.ModalClosed
                        ? "StorageLimitDialog closed but no recoverable state was verified."
                        : "StorageLimitDialog did not close before timeout.", null);
            }
            catch (OperationCanceledException)
            {
                return Complete(result, StorageLimitDialogOutcome.Cancelled,
                    "Storage-limit handling was cancelled.", null);
            }
            catch (Exception exception)
            {
                logger.Error($"[Storage Limit] DeviceName='{deviceName}', Error='{exception.Message}'", exception);
                return Complete(result, StorageLimitDialogOutcome.Failed,
                    "Storage-limit handling failed.", exception.Message);
            }
        }

        private GameDetectionEvidence Match(byte[] frame, TemplateId id)
        {
            ImageMatchResult match = matcher.Find(frame, registry.LoadBytes(id), null);
            return new GameDetectionEvidence
            {
                TemplateId = id, TemplateExists = true, Found = match != null && match.Found,
                MatchResult = match, Confidence = match?.Confidence,
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
                Policy = policy, StateAfterConfirmation = GameState.Unknown,
                Evidence = new GameDetectionEvidence[0]
            };
        private static StorageLimitDialogResult Complete(StorageLimitDialogResult result,
            StorageLimitDialogOutcome outcome, string message, string error)
        {
            result.Outcome = outcome; result.Message = message; result.ErrorMessage = error;
            return result;
        }
    }
}
