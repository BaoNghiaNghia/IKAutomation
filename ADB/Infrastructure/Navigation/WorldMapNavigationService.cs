using ADB_Tool_Automation_Post_FB.Core.Abstractions;
using ADB_Tool_Automation_Post_FB.Core.Concurrency;
using ADB_Tool_Automation_Post_FB.Core.Diagnostics;
using ADB_Tool_Automation_Post_FB.Core.GameDetection;
using ADB_Tool_Automation_Post_FB.Core.Navigation;
using ADB_Tool_Automation_Post_FB.Core.Vision;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
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
        private const int MaxSearchTargetDistanceFromHomePinPx = 260;
        private const int NearbyPinObservationAttempts = 8;
        // The coordinate fields are stable members of the same top-left toolbar as
        // ContinentMapPinButton. Deriving their centers from that freshly matched
        // button keeps the fallback resolution-independent within the supported
        // 1280x720 layout and avoids blind absolute taps.
        private const int CoordinateXOffsetFromPinCenterPx = -134;
        private const int CoordinateYOffsetFromPinCenterPx = -54;
        private const int MaximumCoordinateOffset = 100;
        private static readonly object CoordinateOffsetRandomLock = new object();
        private static readonly Random CoordinateOffsetRandom = new Random();
        private readonly ILdPlayerClient ldPlayerClient;
        private readonly IFocusedInputValueReader focusedInputValueReader;
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
            focusedInputValueReader = ldPlayerClient as IFocusedInputValueReader;
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
            {
                GameDetectionResult blocked = DetectionFrom(ensured);
                GameDetectionEvidence cancel = FindFreshEvidence(
                    blocked, TemplateId.StorageLimitCancelButton);
                if (cancel == null)
                    return Result(false, initial, blocked, ensured.Attempts, watch,
                        "Could not ensure WorldMap before territory reposition.",
                        ensured.ErrorMessage, transitions);

                // Back can expose the generic exit/leave confirmation used by the
                // game. Its stable Cancel button is already part of detector
                // evidence. Cancel only this freshly verified overlay, then resume
                // recovery after WorldMap itself has been verified.
                await TapEvidenceAsync(deviceName, cancel,
                    "BlockingDialogCancelButton", transitions, cancellationToken);
                GameDetectionResult recovered = await PollAsync(
                    deviceName, GameState.WorldMap, transitions, cancellationToken);
                if (!recovered.IsSuccessful || recovered.State != GameState.WorldMap)
                    return Result(false, initial, recovered, ensured.Attempts + 1, watch,
                        "Blocking dialog was cancelled but WorldMap was not verified "
                        + "before territory reposition.", recovered.ErrorMessage, transitions);

                ensured = Result(true, initial, recovered, ensured.Attempts + 1, watch,
                    "WorldMap verified after cancelling the blocking dialog.",
                    null, transitions);
            }

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

            PinObservation pinObservation = await ObserveNearbyPinPairAsync(
                deviceName, current, transitions, cancellationToken);
            current = pinObservation.Latest;
            PinPair nearbyPins = pinObservation.Pair;
            if (nearbyPins != null)
            {
                await TapEvidenceAsync(deviceName, nearbyPins.SearchTarget,
                    "ContinentMapSearchTargetPin", transitions, cancellationToken);
                await Task.Delay(options.StatePollIntervalMs, cancellationToken);
                AddTransition(transitions, "Wait",
                    $"Waited {options.StatePollIntervalMs} ms after selecting the nearby yellow search pin.");
                current = await DetectAsync(deviceName, transitions, cancellationToken);
                if (current.IsSuccessful && current.State == GameState.WorldMap)
                    return Result(true, initial, current, ensured.Attempts + 2, watch,
                        "WorldMap verified immediately after selecting the nearby yellow search pin.",
                        null, transitions);

                if (current.IsSuccessful && current.State == GameState.Unknown
                    && IsVerifiedContinentMapEvidence(current))
                {
                    current.State = GameState.ContinentMap;
                    AddTransition(transitions, "Detect",
                        "Normalized Unknown to ContinentMap after selecting the nearby yellow search pin.");
                }

                if (!current.IsSuccessful || current.State != GameState.ContinentMap)
                    return Result(false, initial, current, ensured.Attempts + 2, watch,
                        "Nearby yellow search pin was selected, but ContinentMap was not "
                        + "verified before confirming the move.",
                        current?.ErrorMessage, transitions);

                GameDetectionEvidence movePin = FindFreshEvidence(
                    current, TemplateId.ContinentMapPinButton);
                if (movePin == null)
                    return Result(false, initial, current, ensured.Attempts + 2, watch,
                        "Nearby yellow search pin was selected, but the fresh move-to-coordinate "
                        + "pin had no valid bounds; no move Tap was sent.",
                        current.ErrorMessage, transitions);

                await TapEvidenceAsync(deviceName, movePin,
                    "ContinentMapPinButtonAfterNearbyTargetSelection", transitions,
                    cancellationToken);
                GameDetectionResult nearbyFinal = await PollAsync(
                    deviceName, GameState.WorldMap, transitions, cancellationToken);
                return nearbyFinal.IsSuccessful && nearbyFinal.State == GameState.WorldMap
                    ? Result(true, initial, nearbyFinal, ensured.Attempts + 3, watch,
                        "WorldMap verified after selecting the nearby yellow search pin "
                        + "and tapping the fresh move-to-coordinate pin.",
                        null, transitions)
                    : Result(false, initial, nearbyFinal, ensured.Attempts + 3, watch,
                        "Nearby target move was confirmed, but WorldMap was not verified before timeout.",
                        nearbyFinal.ErrorMessage, transitions);
            }

            if (AnimatedPinPairWasCheckedButUnavailable(current))
            {
                NavigationResult coordinateFallback = await TryCoordinateFallbackAsync(
                    deviceName, initial, current, ensured.Attempts, watch, transitions,
                    cancellationToken);
                if (coordinateFallback != null)
                    return coordinateFallback;
            }

            TerritoryObservation territoryObservation =
                await ObserveTerritoryMarkerAsync(deviceName, current, transitions,
                    cancellationToken);
            current = territoryObservation.Latest;
            GameDetectionEvidence territory = territoryObservation.Marker;
            if (territory == null)
                return Result(false, initial, current, ensured.Attempts + 1, watch,
                    "Neither a nearby yellow search pin nor an alliance territory marker "
                    + "had valid fresh bounds; no Tap was sent.", null, transitions);

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

        private async Task<NavigationResult> TryCoordinateFallbackAsync(
            string deviceName,
            GameDetectionResult initial,
            GameDetectionResult current,
            int priorAttempts,
            Stopwatch watch,
            IList<NavigationTransition> transitions,
            CancellationToken cancellationToken)
        {
            GameDetectionEvidence initialPin = FindFreshEvidence(
                current, TemplateId.ContinentMapPinButton);
            if (initialPin == null)
                return null;
            if (focusedInputValueReader == null)
                return Result(false, initial, current, priorAttempts + 1, watch,
                    "Coordinate fallback requires a focused numeric input reader; "
                    + "no coordinate input was sent.", null, transitions);

            await AddCoordinateOffsetAsync(
                deviceName,
                initialPin.MatchResult.CenterX + CoordinateXOffsetFromPinCenterPx,
                initialPin.MatchResult.CenterY,
                "X",
                transitions,
                cancellationToken);

            current = await DetectContinentMapAfterCoordinateEditAsync(
                deviceName, "X", transitions, cancellationToken);
            GameDetectionEvidence pinAfterX = FindFreshEvidence(
                current, TemplateId.ContinentMapPinButton);
            if (pinAfterX == null)
                return Result(false, initial, current, priorAttempts + 2, watch,
                    "Coordinate X was changed, but ContinentMap pin bounds could not "
                    + "be refreshed before editing Y.", current?.ErrorMessage, transitions);

            await AddCoordinateOffsetAsync(
                deviceName,
                pinAfterX.MatchResult.CenterX + CoordinateYOffsetFromPinCenterPx,
                pinAfterX.MatchResult.CenterY,
                "Y",
                transitions,
                cancellationToken);

            current = await DetectContinentMapAfterCoordinateEditAsync(
                deviceName, "Y", transitions, cancellationToken);
            GameDetectionEvidence movePin = FindFreshEvidence(
                current, TemplateId.ContinentMapPinButton);
            if (movePin == null)
                return Result(false, initial, current, priorAttempts + 3, watch,
                    "Coordinate Y was changed, but the move-to-coordinate pin had no "
                    + "valid fresh bounds; no move Tap was sent.",
                    current?.ErrorMessage, transitions);

            await TapEvidenceAsync(deviceName, movePin,
                "ContinentMapPinButtonAfterCoordinateChange", transitions,
                cancellationToken);
            GameDetectionResult final = await PollAsync(
                deviceName, GameState.WorldMap, transitions, cancellationToken);
            return final.IsSuccessful && final.State == GameState.WorldMap
                ? Result(true, initial, final, priorAttempts + 4, watch,
                    "WorldMap verified after adding a bounded 1-100 offset to the "
                    + "current X/Y coordinates and tapping the fresh move pin.",
                    null, transitions)
                : Result(false, initial, final, priorAttempts + 4, watch,
                    "Coordinate fallback was submitted, but WorldMap was not verified "
                    + "before timeout.", final.ErrorMessage, transitions);
        }

        private async Task AddCoordinateOffsetAsync(
            string deviceName,
            int x,
            int y,
            string axis,
            IList<NavigationTransition> transitions,
            CancellationToken cancellationToken)
        {
            if (x < 0 || x >= ExpectedScreenshotWidth
                || y < 0 || y >= ExpectedScreenshotHeight)
                throw new InvalidOperationException(
                    $"Derived ContinentMap coordinate {axis} field is outside the supported viewport.");

            await ldPlayerClient.TapAsync(deviceName, x, y, cancellationToken);
            AddTransition(transitions, "Tap",
                $"Tapped coordinate {axis} field derived from fresh coordinate-pin bounds ({x},{y}).");
            int currentValue = await focusedInputValueReader.ReadFocusedIntegerAsync(
                deviceName, cancellationToken);
            int offset = NextCoordinateOffset(currentValue);
            int targetValue = checked(currentValue + offset);
            string currentText = currentValue.ToString(CultureInfo.InvariantCulture);
            string targetText = targetValue.ToString(CultureInfo.InvariantCulture);

            for (int index = 0; index < currentText.Length; index++)
                await ldPlayerClient.PressKeyAsync(
                    deviceName, AndroidKeyCode.Delete, cancellationToken);
            await ldPlayerClient.InputTextAsync(
                deviceName, targetText, cancellationToken);
            await ldPlayerClient.PressKeyAsync(
                deviceName, AndroidKeyCode.Enter, cancellationToken);
            AddTransition(transitions, "Input",
                $"Set coordinate {axis}: {currentValue} + {offset} = {targetValue}, "
                + "then confirmed with Enter.");
            await Task.Delay(options.StatePollIntervalMs, cancellationToken);
            AddTransition(transitions, "Wait",
                $"Waited {options.StatePollIntervalMs} ms after confirming coordinate {axis}.");
        }

        private static int NextCoordinateOffset(int currentValue)
        {
            lock (CoordinateOffsetRandomLock)
            {
                int magnitude = CoordinateOffsetRandom.Next(
                    1, MaximumCoordinateOffset + 1);
                bool canSubtract = currentValue > magnitude;
                bool subtract = canSubtract && CoordinateOffsetRandom.Next(0, 2) == 0;
                return subtract ? -magnitude : magnitude;
            }
        }

        private async Task<GameDetectionResult> DetectContinentMapAfterCoordinateEditAsync(
            string deviceName,
            string axis,
            IList<NavigationTransition> transitions,
            CancellationToken cancellationToken)
        {
            GameDetectionResult result = await DetectAsync(
                deviceName, transitions, cancellationToken);
            if (result != null && result.IsSuccessful
                && result.State == GameState.Unknown
                && IsVerifiedContinentMapEvidence(result))
            {
                result.State = GameState.ContinentMap;
                AddTransition(transitions, "Detect",
                    $"Normalized Unknown to ContinentMap after coordinate {axis} edit.");
            }
            return result;
        }

        private async Task<PinObservation> ObserveNearbyPinPairAsync(
            string deviceName,
            GameDetectionResult initial,
            IList<NavigationTransition> transitions,
            CancellationToken cancellationToken)
        {
            GameDetectionResult current = initial;
            bool detectorSupportsPins = ContainsPinEvidence(current);
            GameDetectionEvidence latestHome = FindFreshEvidence(
                current, TemplateId.ContinentMapHomeLocationPin);
            for (int attempt = 1; attempt <= NearbyPinObservationAttempts; attempt++)
            {
                GameDetectionEvidence currentHome = FindFreshEvidence(
                    current, TemplateId.ContinentMapHomeLocationPin);
                if (currentHome != null)
                    latestHome = currentHome;

                PinPair pair = FindNearbyPinPair(current, latestHome);
                if (pair != null)
                {
                    // The icons bounce and can obscure one another. Re-detect the
                    // yellow target immediately before the production Tap, while
                    // allowing the cyan home pin to be latched from the adjacent frame.
                    GameDetectionResult fresh = await DetectAsync(
                        deviceName, transitions, cancellationToken);
                    GameDetectionEvidence freshHome = FindFreshEvidence(
                        fresh, TemplateId.ContinentMapHomeLocationPin);
                    if (freshHome != null)
                        latestHome = freshHome;

                    PinPair freshPair = FindNearbyPinPair(fresh, latestHome);
                    if (freshPair != null)
                    {
                        AddTransition(transitions, "Match",
                            $"Matched cyan home pin ({freshPair.Home.MatchResult.CenterX},"
                            + $"{freshPair.Home.MatchResult.CenterY}) and nearby yellow search pin "
                            + $"({freshPair.SearchTarget.MatchResult.CenterX},"
                            + $"{freshPair.SearchTarget.MatchResult.CenterY}); "
                            + "the yellow target bounds were refreshed immediately before Tap.");
                        return new PinObservation(fresh, freshPair);
                    }

                    current = fresh;
                    detectorSupportsPins = detectorSupportsPins
                        || ContainsPinEvidence(fresh);
                }

                if (FindFreshEvidenceNearViewportCenter(current,
                        TemplateId.ContinentMapHomeTerritoryAnchor,
                        MaxTerritoryMarkerDistanceFromViewportCenterPx) != null
                    && !HasFreshPinEvidence(current))
                {
                    AddTransition(transitions, "Detect",
                        "No animated location pin was visible, but a fresh alliance "
                        + "territory marker is available; using the bounded territory fallback.");
                    return new PinObservation(current, null);
                }

                if (!detectorSupportsPins)
                    return new PinObservation(current, null);

                if (attempt < NearbyPinObservationAttempts)
                {
                    await Task.Delay(options.StatePollIntervalMs, cancellationToken);
                    AddTransition(transitions, "Wait",
                        $"Waited {options.StatePollIntervalMs} ms for animated ContinentMap pins.");
                    current = await DetectAsync(deviceName, transitions, cancellationToken);
                    detectorSupportsPins = detectorSupportsPins
                        || ContainsPinEvidence(current);
                }
            }

            return new PinObservation(current, null);
        }

        private async Task<TerritoryObservation> ObserveTerritoryMarkerAsync(
            string deviceName,
            GameDetectionResult initial,
            IList<NavigationTransition> transitions,
            CancellationToken cancellationToken)
        {
            GameDetectionResult current = initial;
            for (int attempt = 1; attempt <= NearbyPinObservationAttempts; attempt++)
            {
                GameDetectionEvidence marker = FindFreshEvidenceNearViewportCenter(
                    current, TemplateId.ContinentMapHomeTerritoryAnchor,
                    MaxTerritoryMarkerDistanceFromViewportCenterPx);
                if (marker != null)
                    return new TerritoryObservation(current, marker);

                if (attempt < NearbyPinObservationAttempts)
                {
                    await Task.Delay(options.StatePollIntervalMs, cancellationToken);
                    AddTransition(transitions, "Wait",
                        $"Waited {options.StatePollIntervalMs} ms for the animated "
                        + "alliance territory marker.");
                    current = await DetectAsync(deviceName, transitions, cancellationToken);
                    if (!current.IsSuccessful
                        || (current.State != GameState.ContinentMap
                            && !IsVerifiedContinentMapEvidence(current)))
                        break;
                }
            }

            return new TerritoryObservation(current, null);
        }

        private static PinPair FindNearbyPinPair(
            GameDetectionResult result,
            GameDetectionEvidence fallbackHome)
        {
            GameDetectionEvidence home = FindFreshEvidence(
                result, TemplateId.ContinentMapHomeLocationPin) ?? fallbackHome;
            GameDetectionEvidence target = FindFreshEvidence(
                result, TemplateId.ContinentMapSearchTargetPin);
            if (home == null || target == null) return null;

            double dx = target.MatchResult.CenterX - home.MatchResult.CenterX;
            double dy = target.MatchResult.CenterY - home.MatchResult.CenterY;
            double distance = Math.Sqrt((dx * dx) + (dy * dy));
            return distance <= MaxSearchTargetDistanceFromHomePinPx
                ? new PinPair(home, target)
                : null;
        }

        private static bool ContainsPinEvidence(GameDetectionResult result) =>
            result?.Evidence != null && result.Evidence.Any(item =>
                item.TemplateId == TemplateId.ContinentMapHomeLocationPin
                || item.TemplateId == TemplateId.ContinentMapSearchTargetPin);

        private static bool HasFreshPinEvidence(GameDetectionResult result) =>
            FindFreshEvidence(result, TemplateId.ContinentMapHomeLocationPin) != null
            || FindFreshEvidence(result, TemplateId.ContinentMapSearchTargetPin) != null;

        private static bool AnimatedPinPairWasCheckedButUnavailable(
            GameDetectionResult result)
        {
            if (result?.Evidence == null) return false;
            bool homeChecked = result.Evidence.Any(item =>
                item.TemplateId == TemplateId.ContinentMapHomeLocationPin
                && item.TemplateExists);
            bool targetChecked = result.Evidence.Any(item =>
                item.TemplateId == TemplateId.ContinentMapSearchTargetPin
                && item.TemplateExists);
            bool pairAvailable =
                FindFreshEvidence(result, TemplateId.ContinentMapHomeLocationPin) != null
                && FindFreshEvidence(result, TemplateId.ContinentMapSearchTargetPin) != null;
            return homeChecked && targetChecked && !pairAvailable;
        }

        private sealed class PinObservation
        {
            public PinObservation(GameDetectionResult latest, PinPair pair)
            {
                Latest = latest;
                Pair = pair;
            }

            public GameDetectionResult Latest { get; }
            public PinPair Pair { get; }
        }

        private sealed class TerritoryObservation
        {
            public TerritoryObservation(GameDetectionResult latest,
                GameDetectionEvidence marker)
            {
                Latest = latest;
                Marker = marker;
            }

            public GameDetectionResult Latest { get; }
            public GameDetectionEvidence Marker { get; }
        }

        private sealed class PinPair
        {
            public PinPair(GameDetectionEvidence home, GameDetectionEvidence searchTarget)
            {
                Home = home;
                SearchTarget = searchTarget;
            }

            public GameDetectionEvidence Home { get; }
            public GameDetectionEvidence SearchTarget { get; }
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
                if (target == GameState.ContinentMap
                    && IsVerifiedContinentMapEvidence(last))
                {
                    last.State = GameState.ContinentMap;
                    AddTransition(transitions, "Detect",
                        "Normalized Unknown to ContinentMap from fresh continent-specific evidence.");
                    return last;
                }
                if (!last.IsSuccessful || IsVerifiedTarget(last, target)
                    || (target == GameState.WorldMap
                        && last.State == GameState.Unknown
                        && FindFreshEvidence(last,
                            TemplateId.StorageLimitCancelButton) != null))
                    return last;
            }
            return last ?? await DetectAsync(deviceName, transitions, cancellationToken);
        }

        private static bool IsVerifiedTarget(GameDetectionResult result, GameState target)
        {
            return target == GameState.ResourceSearchPanel
                ? IsVerifiedResourceSearchPanel(result)
                : result.State == target;
        }

        private static bool IsVerifiedContinentMapEvidence(GameDetectionResult result)
        {
            if (result == null || !result.IsSuccessful
                || result.State != GameState.Unknown)
                return false;

            return FindFreshEvidence(result, TemplateId.ContinentMapTitle) != null
                || FindFreshEvidence(result,
                    TemplateId.ContinentMapPinButton) != null
                || FindFreshEvidence(result,
                    TemplateId.ContinentMapHomeLocationPin) != null
                || FindFreshEvidence(result,
                    TemplateId.ContinentMapSearchTargetPin) != null;
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
