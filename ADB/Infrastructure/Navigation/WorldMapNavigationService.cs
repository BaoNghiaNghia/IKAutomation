using ADB_Tool_Automation_Post_FB.Core.Abstractions;
using ADB_Tool_Automation_Post_FB.Core.Diagnostics;
using ADB_Tool_Automation_Post_FB.Core.GameDetection;
using ADB_Tool_Automation_Post_FB.Core.Navigation;
using ADB_Tool_Automation_Post_FB.Core.Vision;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace ADB_Tool_Automation_Post_FB.Infrastructure.Navigation
{
    public sealed class WorldMapNavigationService : IWorldMapNavigationService
    {
        private static readonly ConcurrentDictionary<string, SemaphoreSlim> DeviceLocks =
            new ConcurrentDictionary<string, SemaphoreSlim>(StringComparer.OrdinalIgnoreCase);

        private readonly ILdPlayerClient ldPlayerClient;
        private readonly IGameStateDetector detector;
        private readonly WorldMapNavigationOptions options;
        private readonly IDiagnosticLogger logger;

        public WorldMapNavigationService(ILdPlayerClient ldPlayerClient, IGameStateDetector detector,
            WorldMapNavigationOptions options, IDiagnosticLogger logger)
        {
            this.ldPlayerClient = ldPlayerClient ?? throw new ArgumentNullException(nameof(ldPlayerClient));
            this.detector = detector ?? throw new ArgumentNullException(nameof(detector));
            this.options = options ?? throw new ArgumentNullException(nameof(options));
            this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public Task<NavigationResult> EnsureWorldMapAsync(string deviceName, CancellationToken cancellationToken)
        {
            return WithDeviceLockAsync(deviceName, "EnsureWorldMap",
                token => EnsureWorldMapCoreAsync(deviceName, null, token), cancellationToken);
        }

        public Task<NavigationResult> OpenResourceSearchPanelAsync(string deviceName, CancellationToken cancellationToken)
        {
            return WithDeviceLockAsync(deviceName, "OpenResourceSearchPanel",
                token => OpenResourceSearchPanelCoreAsync(deviceName, token), cancellationToken);
        }

        private async Task<NavigationResult> WithDeviceLockAsync(string deviceName, string operation,
            Func<CancellationToken, Task<NavigationResult>> action, CancellationToken cancellationToken)
        {
            ValidateDeviceName(deviceName);
            cancellationToken.ThrowIfCancellationRequested();
            var operationWatch = Stopwatch.StartNew();
            SemaphoreSlim deviceLock = DeviceLocks.GetOrAdd(deviceName.Trim(), _ => new SemaphoreSlim(1, 1));
            try
            {
                await deviceLock.WaitAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                LogInterrupted(deviceName, operation, operationWatch, true, "WaitingForDeviceLock", "Operation canceled.", null);
                throw;
            }
            try
            {
                NavigationResult result = await action(cancellationToken);
                Log(deviceName, operation, result, false);
                return result;
            }
            catch (OperationCanceledException)
            {
                LogInterrupted(deviceName, operation, operationWatch, true, "Executing", "Operation canceled.", null);
                throw;
            }
            catch (Exception exception)
            {
                LogInterrupted(deviceName, operation, operationWatch, false, "Executing", exception.Message, exception);
                throw;
            }
            finally { deviceLock.Release(); }
        }

        private async Task<NavigationResult> EnsureWorldMapCoreAsync(
            string deviceName, GameDetectionResult knownInitial, CancellationToken cancellationToken)
        {
            var watch = Stopwatch.StartNew();
            var transitions = new List<NavigationTransition>();
            GameDetectionResult initial = knownInitial ?? await DetectAsync(deviceName, transitions, cancellationToken);
            if (!initial.IsSuccessful) return Result(false, initial, initial, 0, watch, "State detection failed.", initial.ErrorMessage, transitions);
            if (initial.State == GameState.WorldMap) return Result(true, initial, initial, 0, watch, "Device is already on WorldMap.", null, transitions);
            if (initial.State == GameState.Unknown) return Result(false, initial, initial, 0, watch, "Unknown state; no blind input was sent.", null, transitions);
            if (initial.State != GameState.ResourceSearchPanel && initial.State != GameState.ContinentMap)
                return Result(false, initial, initial, 0, watch, "Unsupported initial state.", null, transitions);

            await ldPlayerClient.BackAsync(deviceName, cancellationToken);
            AddTransition(transitions, "Back", "Sent one Back command to return to WorldMap.");
            GameDetectionResult final = await PollAsync(deviceName, GameState.WorldMap, transitions, cancellationToken);
            return final.IsSuccessful && final.State == GameState.WorldMap
                ? Result(true, initial, final, 1, watch, "WorldMap verified after Back.", null, transitions)
                : Result(false, initial, final, 1, watch, "Back did not reach WorldMap before timeout.", final.ErrorMessage, transitions);
        }

        private async Task<NavigationResult> OpenResourceSearchPanelCoreAsync(
            string deviceName, CancellationToken cancellationToken)
        {
            var watch = Stopwatch.StartNew();
            var transitions = new List<NavigationTransition>();
            GameDetectionResult initial = await DetectAsync(deviceName, transitions, cancellationToken);
            if (!initial.IsSuccessful) return Result(false, initial, initial, 0, watch, "State detection failed.", initial.ErrorMessage, transitions);
            if (IsVerifiedResourceSearchPanel(initial))
                return Result(true, initial, initial, 0, watch, "ResourceSearchPanel is already open.", null, transitions);
            if (initial.State == GameState.ResourceSearchPanel)
                return Result(false, initial, initial, 0, watch,
                    "ResourceSearchPanel state was reported without both required evidence signals; no input was sent.",
                    initial.ErrorMessage, transitions);

            NavigationResult ensured = await EnsureWorldMapCoreAsync(deviceName, initial, cancellationToken);
            foreach (NavigationTransition transition in ensured.Transitions) transitions.Add(transition);
            if (!ensured.Success)
                return Result(false, initial, DetectionFrom(ensured), ensured.Attempts, watch,
                    "Could not ensure WorldMap; no Tap was sent.", ensured.ErrorMessage, transitions);

            GameDetectionResult current = DetectionFrom(ensured);
            for (int attempt = 1; attempt <= options.MaxOpenSearchAttempts; attempt++)
            {
                GameDetectionEvidence anchor = current.Evidence.FirstOrDefault(item => item.TemplateId == TemplateId.WorldMapAnchor);
                if (anchor?.MatchResult == null || !anchor.Found || anchor.MatchResult.Width <= 0 || anchor.MatchResult.Height <= 0)
                    return Result(false, initial, current, attempt - 1, watch, "WorldMapAnchor has no valid bounds; no fallback Tap was sent.", null, transitions);

                int x = anchor.MatchResult.CenterX;
                int y = anchor.MatchResult.CenterY;
                logger.Info($"[World Map Navigation] DeviceName='{deviceName}', Operation='Tap', "
                    + $"Attempt={attempt}, TapX={x}, TapY={y}, Cancellation=false, Phase='Attempting'");
                await ldPlayerClient.TapAsync(deviceName, x, y, cancellationToken);
                AddTransition(transitions, "Tap", $"Attempt {attempt}: tapped WorldMapAnchor center ({x},{y}).");
                current = await PollAsync(deviceName, GameState.ResourceSearchPanel, transitions, cancellationToken);
                if (current.IsSuccessful && IsVerifiedResourceSearchPanel(current))
                    return Result(true, initial, current, attempt, watch, "ResourceSearchPanel verified after Tap.", null, transitions);
                if (!current.IsSuccessful || current.State != GameState.WorldMap)
                    return Result(false, initial, current, attempt, watch, "Panel was not verified and retry is unsafe.", current.ErrorMessage, transitions);
            }

            return Result(false, initial, current, options.MaxOpenSearchAttempts, watch,
                "Maximum open-search attempts reached without verification.", current.ErrorMessage, transitions);
        }

        private async Task<GameDetectionResult> PollAsync(string deviceName, GameState target,
            IList<NavigationTransition> transitions, CancellationToken cancellationToken)
        {
            var watch = Stopwatch.StartNew();
            GameDetectionResult last = null;
            while (watch.Elapsed < TimeSpan.FromSeconds(options.StateTransitionTimeoutSeconds))
            {
                await Task.Delay(options.StatePollIntervalMs, cancellationToken);
                AddTransition(transitions, "Wait", $"Waited {options.StatePollIntervalMs} ms before verification.");
                last = await DetectAsync(deviceName, transitions, cancellationToken);
                // Unknown can be a transient render frame after navigation. Waiting is safe
                // because polling sends no additional input; only a verified target succeeds.
                if (!last.IsSuccessful || IsVerifiedTarget(last, target)) return last;
            }
            return last ?? await DetectAsync(deviceName, transitions, cancellationToken);
        }

        private static bool IsVerifiedTarget(GameDetectionResult result, GameState target)
        {
            return target == GameState.ResourceSearchPanel
                ? IsVerifiedResourceSearchPanel(result)
                : result.State == target;
        }

        private static bool IsVerifiedResourceSearchPanel(GameDetectionResult result)
        {
            if (result == null || result.State != GameState.ResourceSearchPanel || result.Evidence == null)
                return false;

            bool anchorFound = result.Evidence.Any(item =>
                item.TemplateId == TemplateId.ResourceSearchPanelAnchor && item.Found);
            bool searchButtonFound = result.Evidence.Any(item =>
                item.TemplateId == TemplateId.SearchButtonEnabled && item.Found);
            return anchorFound && searchButtonFound;
        }

        private async Task<GameDetectionResult> DetectAsync(string deviceName, IList<NavigationTransition> transitions, CancellationToken token)
        {
            GameDetectionResult result = await detector.DetectAsync(deviceName, token);
            AddTransition(transitions, "Detect", $"Detected {result.State}; success={result.IsSuccessful}.");
            return result;
        }

        private static GameDetectionResult DetectionFrom(NavigationResult result) => new GameDetectionResult
        { State = result.FinalState, Evidence = result.FinalEvidence, IsSuccessful = string.IsNullOrEmpty(result.ErrorMessage), ErrorMessage = result.ErrorMessage };

        private static NavigationResult Result(bool success, GameDetectionResult initial, GameDetectionResult final,
            int attempts, Stopwatch watch, string message, string error, IList<NavigationTransition> transitions) => new NavigationResult
        {
            Success = success, InitialState = initial.State, FinalState = final?.State ?? GameState.Unknown,
            Attempts = attempts, Duration = watch.Elapsed, Message = message, ErrorMessage = error,
            FinalEvidence = final?.Evidence ?? new GameDetectionEvidence[0],
            Transitions = new List<NavigationTransition>(transitions).AsReadOnly()
        };

        private static void AddTransition(IList<NavigationTransition> transitions, string operation, string message) =>
            transitions.Add(new NavigationTransition { Operation = operation, Message = message, OccurredAt = DateTimeOffset.Now });

        private void Log(string device, string operation, NavigationResult result, bool cancellation)
        {
            string transitions = string.Join("; ", result.Transitions.Select(item => item.Operation + ":" + item.Message));
            logger.Info($"[World Map Navigation] DeviceName='{device}', Operation='{operation}', InitialState='{result.InitialState}', "
                + $"FinalState='{result.FinalState}', Attempt={result.Attempts}, DurationMs={result.Duration.TotalMilliseconds:F0}, "
                + $"Success={result.Success}, Error='{result.ErrorMessage ?? string.Empty}', Cancellation={cancellation}, Transitions='{transitions}'");
        }

        private void LogInterrupted(string device, string operation, Stopwatch watch, bool cancellation,
            string phase, string error, Exception exception)
        {
            string message = $"[World Map Navigation] DeviceName='{device}', Operation='{operation}', "
                + "InitialState='Unknown', FinalState='Unknown', Attempt=0, "
                + $"DurationMs={watch.Elapsed.TotalMilliseconds:F0}, Success=false, Error='{error}', "
                + $"Cancellation={cancellation}, Phase='{phase}'";
            if (exception == null)
                logger.Info(message);
            else
                logger.Error(message, exception);
        }

        private static void ValidateDeviceName(string deviceName)
        { if (string.IsNullOrWhiteSpace(deviceName)) throw new ArgumentException("LDPlayer device name is required.", nameof(deviceName)); }
    }
}
