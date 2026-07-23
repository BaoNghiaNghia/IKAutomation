using ADB_Tool_Automation_Post_FB.Core.Abstractions;
using ADB_Tool_Automation_Post_FB.Core.Concurrency;
using ADB_Tool_Automation_Post_FB.Core.Diagnostics;
using ADB_Tool_Automation_Post_FB.Core.GameDetection;
using ADB_Tool_Automation_Post_FB.Core.Navigation;
using ADB_Tool_Automation_Post_FB.Core.Vision;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ADB_Tool_Automation_Post_FB.Infrastructure.Concurrency;

namespace ADB_Tool_Automation_Post_FB.Infrastructure.Navigation
{
    public sealed class WorldMapNavigationService : IWorldMapNavigationService
    {
        private const int ExpectedScreenshotWidth = 1280;
        private const int ExpectedScreenshotHeight = 720;
        private const int MaxTerritoryMarkerDistanceFromViewportCenterPx = 360;
        private readonly ILdPlayerClient ldPlayerClient;
        private readonly IGameStateDetector detector;
        private readonly WorldMapNavigationOptions options;
        private readonly IDiagnosticLogger logger;
        private readonly IDeviceOperationLock operationLock;

        public WorldMapNavigationService(ILdPlayerClient ldPlayerClient, IGameStateDetector detector,
            WorldMapNavigationOptions options, IDiagnosticLogger logger)
            : this(ldPlayerClient, detector, options, logger, DeviceOperationLock.Shared)
        {
        }

        public WorldMapNavigationService(ILdPlayerClient ldPlayerClient, IGameStateDetector detector,
            WorldMapNavigationOptions options, IDiagnosticLogger logger, IDeviceOperationLock operationLock)
        {
            this.ldPlayerClient = ldPlayerClient ?? throw new ArgumentNullException(nameof(ldPlayerClient));
            this.detector = detector ?? throw new ArgumentNullException(nameof(detector));
            this.options = options ?? throw new ArgumentNullException(nameof(options));
            this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
            this.operationLock = operationLock ?? throw new ArgumentNullException(nameof(operationLock));
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

        public Task<NavigationResult> RepositionToAllianceTerritoryAsync(string deviceName, CancellationToken cancellationToken)
        {
            return WithDeviceLockAsync(deviceName, "RepositionToAllianceTerritory",
                token => RepositionToAllianceTerritoryCoreAsync(deviceName, token), cancellationToken);
        }

        private async Task<NavigationResult> WithDeviceLockAsync(string deviceName, string operation,
            Func<CancellationToken, Task<NavigationResult>> action, CancellationToken cancellationToken)
        {
            ValidateDeviceName(deviceName);
            cancellationToken.ThrowIfCancellationRequested();
            var operationWatch = Stopwatch.StartNew();
            try
            {
                NavigationResult result = await operationLock.RunAsync(deviceName, action, cancellationToken);
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
            if (initial.State == GameState.City)
            {
                GameDetectionEvidence mapButton = initial.Evidence?.FirstOrDefault(item =>
                    item.TemplateId == TemplateId.CityToWorldMapButton);
                if (mapButton?.MatchResult == null || !mapButton.Found
                    || mapButton.MatchResult.Width <= 0 || mapButton.MatchResult.Height <= 0)
                    return Result(false, initial, initial, 0, watch,
                        "City was detected but the World Map button had no valid fresh bounds; no Tap was sent.",
                        null, transitions);

                int x = mapButton.MatchResult.CenterX;
                int y = mapButton.MatchResult.CenterY;
                await ldPlayerClient.TapAsync(deviceName, x, y, cancellationToken);
                AddTransition(transitions, "Tap",
                    $"Tapped freshly matched CityToWorldMapButton center ({x},{y}).");
                GameDetectionResult cityFinal = await PollAsync(deviceName,
                    GameState.WorldMap, transitions, cancellationToken);
                return cityFinal.IsSuccessful && cityFinal.State == GameState.WorldMap
                    ? Result(true, initial, cityFinal, 1, watch,
                        "WorldMap verified after tapping the City navigation button.", null, transitions)
                    : Result(false, initial, cityFinal, 1, watch,
                        "City navigation button was tapped but WorldMap was not verified before timeout.",
                        cityFinal.ErrorMessage, transitions);
            }
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

        private async Task<NavigationResult> RepositionToAllianceTerritoryCoreAsync(
            string deviceName, CancellationToken cancellationToken)
        {
            var watch = Stopwatch.StartNew();
            var transitions = new List<NavigationTransition>();
            GameDetectionResult initial = await DetectAsync(deviceName, transitions, cancellationToken);
            if (!initial.IsSuccessful)
                return Result(false, initial, initial, 0, watch, "State detection failed.", initial.ErrorMessage, transitions);
            if (initial.State == GameState.Unknown)
                return Result(false, initial, initial, 0, watch, "Unknown state; no blind recovery input was sent.", null, transitions);

            NavigationResult ensured = await EnsureWorldMapCoreAsync(deviceName, initial, cancellationToken);
            foreach (NavigationTransition transition in ensured.Transitions) transitions.Add(transition);
            if (!ensured.Success)
                return Result(false, initial, DetectionFrom(ensured), ensured.Attempts, watch,
                    "Could not ensure WorldMap before territory reposition.", ensured.ErrorMessage, transitions);

            GameDetectionResult current = DetectionFrom(ensured);
            GameDetectionEvidence mapButton = FindFreshEvidence(current, TemplateId.WorldMapPinButton);
            if (mapButton == null)
                return Result(false, initial, current, ensured.Attempts, watch,
                    "WorldMap pin-map button had no valid fresh bounds; no Tap was sent.", null, transitions);

            await TapEvidenceAsync(deviceName, mapButton, "WorldMapPinButton", transitions, cancellationToken);
            current = await PollAsync(deviceName, GameState.ContinentMap, transitions, cancellationToken);
            if (!current.IsSuccessful || current.State != GameState.ContinentMap)
                return Result(false, initial, current, ensured.Attempts + 1, watch,
                    "Pin-map button was tapped but ContinentMap was not verified before timeout.",
                    current.ErrorMessage, transitions);

            GameDetectionEvidence territory = FindFreshEvidenceNearViewportCenter(current,
                TemplateId.ContinentMapHomeTerritoryAnchor, MaxTerritoryMarkerDistanceFromViewportCenterPx);
            if (territory == null)
                return Result(false, initial, current, ensured.Attempts + 1, watch,
                    "Alliance territory marker had no valid fresh near-current bounds; no Tap was sent.", null, transitions);

            await TapEvidenceAsync(deviceName, territory, "ContinentMapHomeTerritoryAnchor", transitions, cancellationToken);
            await Task.Delay(options.StatePollIntervalMs, cancellationToken);
            AddTransition(transitions, "Wait", $"Waited {options.StatePollIntervalMs} ms after selecting territory.");
            current = await DetectAsync(deviceName, transitions, cancellationToken);
            if (!current.IsSuccessful || current.State != GameState.ContinentMap)
                return Result(false, initial, current, ensured.Attempts + 2, watch,
                    "Territory marker was tapped but ContinentMap was not still verified before pin navigation.",
                    current.ErrorMessage, transitions);

            GameDetectionEvidence pin = FindFreshEvidence(current, TemplateId.ContinentMapPinButton);
            if (pin == null)
                return Result(false, initial, current, ensured.Attempts + 2, watch,
                    "ContinentMap pin button had no valid fresh bounds; no Tap was sent.", null, transitions);

            await TapEvidenceAsync(deviceName, pin, "ContinentMapPinButton", transitions, cancellationToken);
            GameDetectionResult final = await PollAsync(deviceName, GameState.WorldMap, transitions, cancellationToken);
            return final.IsSuccessful && final.State == GameState.WorldMap
                ? Result(true, initial, final, ensured.Attempts + 3, watch,
                    "WorldMap verified after selecting alliance territory and tapping the coordinate pin.", null, transitions)
                : Result(false, initial, final, ensured.Attempts + 3, watch,
                    "Territory coordinate pin was tapped but WorldMap was not verified before timeout.",
                    final.ErrorMessage, transitions);
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
            bool stableFallbackFound = result.Evidence.Any(item =>
                (item.TemplateId == TemplateId.LevelMinusButton
                    || item.TemplateId == TemplateId.ResourceTabSelected
                    || item.TemplateId == TemplateId.ResourceTabUnselected) && item.Found);
            bool searchButtonFound = result.Evidence.Any(item =>
                item.TemplateId == TemplateId.SearchButtonEnabled && item.Found);
            return (anchorFound || stableFallbackFound) && searchButtonFound;
        }

        private async Task<GameDetectionResult> DetectAsync(string deviceName, IList<NavigationTransition> transitions, CancellationToken token)
        {
            GameDetectionResult result = await detector.DetectAsync(deviceName, token);
            AddTransition(transitions, "Detect", $"Detected {result.State}; success={result.IsSuccessful}.");
            return result;
        }

        private async Task TapEvidenceAsync(string deviceName, GameDetectionEvidence evidence, string label,
            IList<NavigationTransition> transitions, CancellationToken cancellationToken)
        {
            int x = evidence.MatchResult.CenterX;
            int y = evidence.MatchResult.CenterY;
            await ldPlayerClient.TapAsync(deviceName, x, y, cancellationToken);
            AddTransition(transitions, "Tap", $"Tapped freshly matched {label} center ({x},{y}).");
        }

        private static GameDetectionEvidence FindFreshEvidence(GameDetectionResult result, TemplateId templateId)
        {
            GameDetectionEvidence evidence = result?.Evidence?.FirstOrDefault(item => item.TemplateId == templateId);
            return HasValidBounds(evidence) ? evidence : null;
        }

        private static GameDetectionEvidence FindFreshEvidenceNearViewportCenter(GameDetectionResult result,
            TemplateId templateId, int maxDistancePx)
        {
            GameDetectionEvidence evidence = FindFreshEvidence(result, templateId);
            if (evidence == null) return null;

            double dx = evidence.MatchResult.CenterX - (ExpectedScreenshotWidth / 2.0);
            double dy = evidence.MatchResult.CenterY - (ExpectedScreenshotHeight / 2.0);
            double distance = Math.Sqrt((dx * dx) + (dy * dy));
            return distance <= maxDistancePx ? evidence : null;
        }

        private static bool HasValidBounds(GameDetectionEvidence evidence) =>
            evidence != null && evidence.Found && evidence.MatchResult != null
            && evidence.MatchResult.Width > 0 && evidence.MatchResult.Height > 0;

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
