using ADB_Tool_Automation_Post_FB.Core.Abstractions;
using ADB_Tool_Automation_Post_FB.Core.Concurrency;
using ADB_Tool_Automation_Post_FB.Core.Diagnostics;
using ADB_Tool_Automation_Post_FB.Core.GameDetection;
using ADB_Tool_Automation_Post_FB.Core.Navigation;
using ADB_Tool_Automation_Post_FB.Core.Vision;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using ADB_Tool_Automation_Post_FB.Infrastructure.Concurrency;

namespace ADB_Tool_Automation_Post_FB.Infrastructure.Navigation
{
    public sealed class WorldMapNavigationService :
        IWorldMapNavigationService,
        IWorldMapNavigationProgressService
    {
        private const int ExpectedScreenshotWidth = 1280;
        private const int ExpectedScreenshotHeight = 720;
        private const int MaxTerritoryMarkerDistanceFromViewportCenterPx = 360;
        private const int MaxSearchTargetDistanceFromHomePinPx = 260;
        private const int NearbyPinObservationAttempts = 8;
        private const int CoordinateTerritoryAttempts = 5;
        private const int DestinationPinSearchLeftPx = 220;
        private const int DestinationPinSearchTopPx = 80;
        private const int DestinationPinSearchRightMarginPx = 100;
        private const int DestinationPinSearchBottomMarginPx = 80;
        private const int MinimumDestinationPinPixels = 45;
        private const int MaximumDestinationPinPixels = 500;
        private const int MinimumDestinationPinWidthPx = 9;
        private const int MaximumDestinationPinWidthPx = 30;
        private const int MinimumDestinationPinHeightPx = 18;
        private const int MaximumDestinationPinHeightPx = 42;
        private const int PinBackgroundSideGapPx = 4;
        private const int PinBackgroundSideWidthPx = 12;
        private const int PinBackgroundHeightPx = 12;
        private const double MinimumTerritoryPixelSaturation = 0.16;
        private const double MinimumTerritoryPixelValue = 0.12;
        private const double MaximumTerritoryPixelValue = 0.98;
        private const double MinimumClassifiablePixelRatio = 0.35;
        private const double MinimumWinningGroupRatio = 0.58;
        private const double MinimumWinningMarginRatio = 0.18;
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
            return RepositionToAllianceTerritoryAsync(
                deviceName, null, cancellationToken);
        }

        public Task<NavigationResult> RepositionToAllianceTerritoryAsync(
            string deviceName,
            IProgress<NavigationTransition> progress,
            CancellationToken cancellationToken)
        {
            return WithDeviceLockAsync(deviceName, "RepositionToAllianceTerritory",
                token => RepositionToAllianceTerritoryCoreAsync(
                    deviceName, progress, token), cancellationToken);
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
            if (initial.State == GameState.Unknown)
            {
                GameDetectionResult recovered = await TryCancelWorldMapBlockingDialogAsync(
                    deviceName, initial, transitions, cancellationToken);
                if (recovered != null)
                    return recovered.IsSuccessful
                        && recovered.State == GameState.WorldMap
                        ? Result(true, initial, recovered, 1, watch,
                            "WorldMap verified after cancelling its blocking dialog.",
                            null, transitions)
                        : Result(false, initial, recovered, 1, watch,
                            "The verified WorldMap blocking dialog was cancelled, but "
                            + "WorldMap was not verified before timeout.",
                            recovered.ErrorMessage, transitions);

                return Result(false, initial, initial, 0, watch,
                    "Unknown state; no blind input was sent.", null, transitions);
            }
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
            if (initial.State == GameState.TeamSelection
                || initial.State == GameState.ResourcePopup)
            {
                int maximumBackAttempts = initial.State == GameState.TeamSelection ? 2 : 1;
                GameDetectionResult current = initial;
                for (int attempt = 1; attempt <= maximumBackAttempts; attempt++)
                {
                    if (attempt > 1 && (!current.IsSuccessful
                        || current.State != GameState.TeamSelection))
                        break;

                    await ldPlayerClient.BackAsync(deviceName, cancellationToken);
                    AddTransition(transitions, "Back",
                        initial.State == GameState.TeamSelection
                            ? "Sent a bounded Back command to close TeamSelection."
                            : "Sent one Back command to close ResourcePopup.");
                    current = await PollAsync(deviceName, GameState.WorldMap,
                        transitions, cancellationToken);
                    if (current.IsSuccessful && current.State == GameState.WorldMap)
                        return Result(true, initial, current, attempt, watch,
                            "WorldMap verified after closing the active overlay.", null,
                            transitions);
                }

                return Result(false, initial, current, maximumBackAttempts, watch,
                    initial.State == GameState.TeamSelection
                        ? "TeamSelection could not be closed to reach WorldMap."
                        : "ResourcePopup could not be closed to reach WorldMap.",
                    current?.ErrorMessage, transitions);
            }
            if (initial.State != GameState.ResourceSearchPanel && initial.State != GameState.ContinentMap)
                return Result(false, initial, initial, 0, watch, "Unsupported initial state.", null, transitions);

            await ldPlayerClient.BackAsync(deviceName, cancellationToken);
            AddTransition(transitions, "Back", "Sent one Back command to return to WorldMap.");
            GameDetectionResult final = await PollAsync(deviceName, GameState.WorldMap, transitions, cancellationToken);
            if (final.IsSuccessful && final.State == GameState.WorldMap)
                return Result(true, initial, final, 1, watch,
                    "WorldMap verified after Back.", null, transitions);

            GameDetectionResult recoveredAfterBack =
                await TryCancelWorldMapBlockingDialogAsync(
                    deviceName, final, transitions, cancellationToken);
            if (recoveredAfterBack != null)
                return recoveredAfterBack.IsSuccessful
                    && recoveredAfterBack.State == GameState.WorldMap
                    ? Result(true, initial, recoveredAfterBack, 2, watch,
                        "WorldMap verified after cancelling the blocking dialog "
                        + "opened by Back.", null, transitions)
                    : Result(false, initial, recoveredAfterBack, 2, watch,
                        "The blocking dialog opened by Back was cancelled, but "
                        + "WorldMap was not verified before timeout.",
                        recoveredAfterBack.ErrorMessage, transitions);

            return Result(false, initial, final, 1, watch,
                "Back did not reach WorldMap before timeout.",
                final.ErrorMessage, transitions);
        }

        private async Task<GameDetectionResult> TryCancelWorldMapBlockingDialogAsync(
            string deviceName,
            GameDetectionResult detection,
            IList<NavigationTransition> transitions,
            CancellationToken cancellationToken)
        {
            if (detection == null
                || !detection.IsSuccessful
                || detection.State != GameState.Unknown)
                return null;

            GameDetectionEvidence cancel = FindFreshEvidence(
                detection, TemplateId.StorageLimitCancelButton);
            GameDetectionEvidence underlyingWorldMap = FindFreshEvidence(
                detection, TemplateId.WorldMapPinButton);
            if (cancel == null || underlyingWorldMap == null)
                return null;

            AddTransition(transitions, "Detect",
                "Verified a blocking Cancel button over WorldMap from fresh bounds.");
            await TapEvidenceAsync(deviceName, cancel,
                "WorldMapBlockingDialogCancelButton", transitions,
                cancellationToken);
            return await PollAsync(
                deviceName, GameState.WorldMap, transitions, cancellationToken);
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
            string deviceName,
            IProgress<NavigationTransition> progress,
            CancellationToken cancellationToken)
        {
            var watch = Stopwatch.StartNew();
            var transitions = new List<NavigationTransition>();
            GameDetectionResult initial = await DetectAsync(deviceName, transitions, cancellationToken);
            if (!initial.IsSuccessful)
                return Result(false, initial, initial, 0, watch, "State detection failed.", initial.ErrorMessage, transitions);
            NavigationResult ensured = await EnsureWorldMapCoreAsync(deviceName, initial, cancellationToken);
            foreach (NavigationTransition transition in ensured.Transitions) transitions.Add(transition);
            if (!ensured.Success)
                return Result(false, initial, DetectionFrom(ensured),
                    ensured.Attempts, watch,
                    "Could not ensure WorldMap before territory reposition.",
                    ensured.ErrorMessage, transitions);

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
                TerritoryValidation nearbyValidation =
                    await ValidateNearbyPinTerritoriesAsync(
                        deviceName, nearbyPins.Home, transitions,
                        cancellationToken);
                current = nearbyValidation.Latest ?? current;
                if (!nearbyValidation.Allowed)
                    return Result(false, initial, current,
                        ensured.Attempts + 1, watch,
                        nearbyValidation.Message, null, transitions);

                await TapEvidenceAsync(deviceName,
                    nearbyValidation.Destination,
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
                GameDetectionEvidence homePin = pinObservation.Home
                    ?? FindFreshEvidence(
                        current, TemplateId.ContinentMapHomeTerritoryAnchor);
                NavigationResult coordinateFallback = await TryCoordinateFallbackAsync(
                    deviceName, initial, current, homePin,
                    ensured.Attempts, watch, transitions, progress,
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
            GameDetectionEvidence homePin,
            int priorAttempts,
            Stopwatch watch,
            IList<NavigationTransition> transitions,
            IProgress<NavigationTransition> progress,
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

            TerritoryColorGroup homeTerritory =
                await ClassifyPinTerritoryAsync(
                    deviceName, homePin, "green home pin",
                    transitions, cancellationToken);
            if (homeTerritory == TerritoryColorGroup.Unknown)
                return Result(false, initial, current, priorAttempts + 1, watch,
                    "Coordinate fallback stopped because the green home-pin territory "
                    + "color could not be classified confidently; no coordinate input "
                    + "or move Tap was sent.", null, transitions);

            GameDetectionEvidence coordinatePin = initialPin;
            string lastValidationMessage = null;
            for (int attempt = 1; attempt <= CoordinateTerritoryAttempts; attempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                AddTransition(transitions, "CoordinateAttempt",
                    $"Trying fallback X/Y candidate {attempt}/"
                    + $"{CoordinateTerritoryAttempts}.");

                CoordinateEdit xEdit = await AddCoordinateOffsetAsync(
                    deviceName,
                    coordinatePin.MatchResult.CenterX
                        + CoordinateXOffsetFromPinCenterPx,
                    coordinatePin.MatchResult.CenterY,
                    "X",
                    transitions,
                    cancellationToken);

                current = await DetectContinentMapAfterCoordinateEditAsync(
                    deviceName, "X", transitions, cancellationToken);
                GameDetectionEvidence pinAfterX = FindFreshEvidence(
                    current, TemplateId.ContinentMapPinButton);
                if (pinAfterX == null)
                    return Result(false, initial, current,
                        priorAttempts + (attempt * 2), watch,
                        "Coordinate X was changed, but ContinentMap pin bounds could not "
                        + "be refreshed before editing Y.", current?.ErrorMessage, transitions);

                CoordinateEdit yEdit = await AddCoordinateOffsetAsync(
                    deviceName,
                    pinAfterX.MatchResult.CenterX
                        + CoordinateYOffsetFromPinCenterPx,
                    pinAfterX.MatchResult.CenterY,
                    "Y",
                    transitions,
                    cancellationToken);

                current = await DetectContinentMapAfterCoordinateEditAsync(
                    deviceName, "Y", transitions, cancellationToken);
                GameDetectionEvidence movePin = FindFreshEvidence(
                    current, TemplateId.ContinentMapPinButton);
                if (movePin == null)
                    return Result(false, initial, current,
                        priorAttempts + (attempt * 2) + 1, watch,
                        "Coordinate Y was changed, but the move-to-coordinate pin had no "
                        + "valid fresh bounds; no move Tap was sent.",
                        current?.ErrorMessage, transitions);

                TerritoryValidation coordinateValidation =
                    await ValidateCoordinateDestinationAsync(
                        deviceName, homeTerritory, attempt,
                        xEdit.TargetValue, yEdit.TargetValue,
                        transitions, progress, cancellationToken);
                current = coordinateValidation.Latest ?? current;
                if (coordinateValidation.Allowed)
                {
                    await TapEvidenceAsync(deviceName,
                        coordinateValidation.Destination,
                        "ContinentMapPinButtonAfterCoordinateChange", transitions,
                        cancellationToken);
                    GameDetectionResult final = await PollAsync(
                        deviceName, GameState.WorldMap, transitions,
                        cancellationToken);
                    return final.IsSuccessful && final.State == GameState.WorldMap
                        ? Result(true, initial, final,
                            priorAttempts + (attempt * 2) + 2, watch,
                            "WorldMap verified after selecting an X/Y candidate with "
                            + "a territory tone matching the home pin.", null, transitions)
                        : Result(false, initial, final,
                            priorAttempts + (attempt * 2) + 2, watch,
                            "Coordinate fallback was submitted, but WorldMap was not "
                            + "verified before timeout.", final.ErrorMessage, transitions);
                }

                lastValidationMessage = coordinateValidation.Message;
                AddTransition(transitions, "TerritoryColor",
                    $"Candidate {attempt} was rejected. Restoring X={xEdit.OriginalValue} "
                    + $"and Y={yEdit.OriginalValue} before retry.");
                current = await RestoreCoordinatesAsync(
                    deviceName, movePin, xEdit, yEdit, transitions,
                    cancellationToken);
                coordinatePin = FindFreshEvidence(
                    current, TemplateId.ContinentMapPinButton);
                if (coordinatePin == null)
                    return Result(false, initial, current,
                        priorAttempts + (attempt * 4), watch,
                        "The rejected X/Y candidate was rolled back, but fresh coordinate "
                        + "controls could not be verified; no move Tap was sent.",
                        current?.ErrorMessage, transitions);
            }

            return Result(false, initial, current,
                priorAttempts + (CoordinateTerritoryAttempts * 4), watch,
                $"No matching territory tone was found after "
                + $"{CoordinateTerritoryAttempts} bounded X/Y candidates. Original "
                + $"coordinates were restored; no move Tap was sent. "
                + $"{lastValidationMessage}", null, transitions);
        }

        private async Task<CoordinateEdit> AddCoordinateOffsetAsync(
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
            await ReplaceFocusedCoordinateAsync(
                deviceName, currentValue, targetValue, cancellationToken);
            AddTransition(transitions, "Input",
                $"Set coordinate {axis}: {currentValue} + {offset} = {targetValue}, "
                + "then confirmed with Enter.");
            await Task.Delay(options.StatePollIntervalMs, cancellationToken);
            AddTransition(transitions, "Wait",
                $"Waited {options.StatePollIntervalMs} ms after confirming coordinate {axis}.");
            return new CoordinateEdit(axis, currentValue, targetValue);
        }

        private async Task<GameDetectionResult> RestoreCoordinatesAsync(
            string deviceName,
            GameDetectionEvidence pin,
            CoordinateEdit xEdit,
            CoordinateEdit yEdit,
            IList<NavigationTransition> transitions,
            CancellationToken cancellationToken)
        {
            await SetCoordinateValueAsync(
                deviceName,
                pin.MatchResult.CenterX + CoordinateXOffsetFromPinCenterPx,
                pin.MatchResult.CenterY,
                xEdit,
                transitions,
                cancellationToken);
            GameDetectionResult current =
                await DetectContinentMapAfterCoordinateEditAsync(
                    deviceName, "X rollback", transitions, cancellationToken);
            GameDetectionEvidence pinAfterX = FindFreshEvidence(
                current, TemplateId.ContinentMapPinButton);
            if (pinAfterX == null) return current;

            await SetCoordinateValueAsync(
                deviceName,
                pinAfterX.MatchResult.CenterX
                    + CoordinateYOffsetFromPinCenterPx,
                pinAfterX.MatchResult.CenterY,
                yEdit,
                transitions,
                cancellationToken);
            return await DetectContinentMapAfterCoordinateEditAsync(
                deviceName, "Y rollback", transitions, cancellationToken);
        }

        private async Task SetCoordinateValueAsync(
            string deviceName,
            int x,
            int y,
            CoordinateEdit edit,
            IList<NavigationTransition> transitions,
            CancellationToken cancellationToken)
        {
            if (x < 0 || x >= ExpectedScreenshotWidth
                || y < 0 || y >= ExpectedScreenshotHeight)
                throw new InvalidOperationException(
                    $"Derived ContinentMap coordinate {edit.Axis} field is outside "
                    + "the supported viewport.");

            await ldPlayerClient.TapAsync(deviceName, x, y, cancellationToken);
            await ReplaceFocusedCoordinateAsync(
                deviceName, edit.TargetValue, edit.OriginalValue,
                cancellationToken);
            AddTransition(transitions, "Rollback",
                $"Restored coordinate {edit.Axis} from {edit.TargetValue} "
                + $"to {edit.OriginalValue}.");
            await Task.Delay(options.StatePollIntervalMs, cancellationToken);
        }

        private async Task ReplaceFocusedCoordinateAsync(
            string deviceName,
            int oldValue,
            int newValue,
            CancellationToken cancellationToken)
        {
            string oldText = oldValue.ToString(CultureInfo.InvariantCulture);
            string newText = newValue.ToString(CultureInfo.InvariantCulture);
            for (int index = 0; index < oldText.Length; index++)
                await ldPlayerClient.PressKeyAsync(
                    deviceName, AndroidKeyCode.Delete, cancellationToken);
            await ldPlayerClient.InputTextAsync(
                deviceName, newText, cancellationToken);
            await ldPlayerClient.PressKeyAsync(
                deviceName, AndroidKeyCode.Enter, cancellationToken);
        }

        private async Task<TerritoryValidation> ValidateNearbyPinTerritoriesAsync(
            string deviceName,
            GameDetectionEvidence recentlyObservedHome,
            IList<NavigationTransition> transitions,
            CancellationToken cancellationToken)
        {
            byte[] screenshot = await ldPlayerClient.CaptureScreenshotPngAsync(
                deviceName, cancellationToken);
            GameDetectionResult latest = detector.Detect(screenshot);
            AddTransition(transitions, "Detect",
                "Rematched ContinentMap pins in the screenshot captured immediately "
                + "before selecting the yellow destination.");

            PinPair freshPair = FindNearbyPinPair(
                latest, recentlyObservedHome);
            if (freshPair == null)
                return TerritoryValidation.Blocked(latest,
                    "Territory validation stopped because fresh green and yellow pin "
                    + "bounds were not both available; no destination Tap was sent.");

            TerritoryColorGroup home;
            TerritoryColorGroup destination;
            if (!TryClassifyPinTerritory(screenshot, freshPair.Home, out home)
                || !TryClassifyPinTerritory(
                    screenshot, freshPair.SearchTarget, out destination))
            {
                AddTransition(transitions, "TerritoryColor",
                    "Home=Unknown; Destination=Unknown; Result=Blocked; "
                    + "Source=FreshPins; Reason=LowConfidence.");
                return TerritoryValidation.Blocked(latest,
                    "Territory validation stopped because the green home or yellow "
                    + "destination territory color could not be classified confidently; "
                    + "no destination Tap was sent.");
            }

            AddTransition(transitions, "TerritoryColor",
                $"Home={home}; Destination={destination}; "
                + $"Result={(home == destination ? "Match" : "Different")}; "
                + "Source=FreshPins.");
            if (home != destination)
                return TerritoryValidation.Blocked(latest,
                    $"Territory validation stopped because the green home territory "
                    + $"group ({home}) differs from the yellow destination group "
                    + $"({destination}); no destination Tap was sent.");

            return TerritoryValidation.Permitted(
                latest, freshPair.SearchTarget,
                $"Both pin-base backgrounds belong to the {home} territory group.");
        }

        private async Task<TerritoryColorGroup> ClassifyPinTerritoryAsync(
            string deviceName,
            GameDetectionEvidence pin,
            string label,
            IList<NavigationTransition> transitions,
            CancellationToken cancellationToken)
        {
            if (!HasValidBounds(pin))
            {
                AddTransition(transitions, "TerritoryColor",
                    $"Could not classify {label}: no valid pin bounds were available.");
                return TerritoryColorGroup.Unknown;
            }

            byte[] screenshot = await ldPlayerClient.CaptureScreenshotPngAsync(
                deviceName, cancellationToken);
            TerritoryColorGroup group;
            bool classified = TryClassifyPinTerritory(screenshot, pin, out group);
            AddTransition(transitions, "TerritoryColor",
                classified
                    ? $"Classified the background below the {label} as {group}."
                    : $"Could not classify the background below the {label} confidently.");
            return classified ? group : TerritoryColorGroup.Unknown;
        }

        private async Task<TerritoryValidation> ValidateCoordinateDestinationAsync(
            string deviceName,
            TerritoryColorGroup homeTerritory,
            int candidate,
            int destinationX,
            int destinationY,
            IList<NavigationTransition> transitions,
            IProgress<NavigationTransition> progress,
            CancellationToken cancellationToken)
        {
            byte[] screenshot = await ldPlayerClient.CaptureScreenshotPngAsync(
                deviceName, cancellationToken);
            GameDetectionResult latest = detector.Detect(screenshot);
            GameDetectionEvidence movePin = FindFreshEvidence(
                latest, TemplateId.ContinentMapPinButton);
            GameDetectionEvidence destinationPin;
            bool destinationLocated = TryLocateYellowDestinationPin(
                screenshot, out destinationPin);
            string source = "YellowPinPixels";
            AddTransition(transitions, "Detect",
                "Rematched the coordinate move button and searched the fresh map "
                + "screenshot for the yellow X/Y destination pin.");
            if (movePin == null)
                return TerritoryValidation.Blocked(latest,
                    "Coordinate fallback stopped because the move button could not be "
                    + "rematched immediately before movement; no move Tap was sent.");

            if (!destinationLocated)
            {
                AddTerritoryColorProgress(transitions, progress,
                    $"Candidate={candidate}/{CoordinateTerritoryAttempts}; "
                    + $"X={destinationX}; Y={destinationY}; Home={homeTerritory}; "
                    + "Destination=Unknown; Result=Blocked; "
                    + $"Source={source}; Reason=PinNotLocatedConfidently.");
                return TerritoryValidation.Blocked(latest,
                    "Coordinate fallback stopped because one unique yellow destination "
                    + "pin could not be located confidently; no move Tap was sent.");
            }

            TerritoryColorGroup destination;
            if (!TryClassifyPinTerritory(
                    screenshot, destinationPin, out destination))
            {
                AddTerritoryColorProgress(transitions, progress,
                    $"Candidate={candidate}/{CoordinateTerritoryAttempts}; "
                    + $"X={destinationX}; Y={destinationY}; Home={homeTerritory}; "
                    + $"Destination=Unknown; Result=Blocked; Source={source}; "
                    + "Reason=LowConfidence.");
                return TerritoryValidation.Blocked(latest,
                    "Coordinate fallback stopped because the destination territory color "
                    + "could not be classified confidently; no move Tap was sent.");
            }

            AddTerritoryColorProgress(transitions, progress,
                $"Candidate={candidate}/{CoordinateTerritoryAttempts}; "
                + $"X={destinationX}; Y={destinationY}; Home={homeTerritory}; "
                + $"Destination={destination}; "
                + $"Result={(homeTerritory == destination ? "Match" : "Different")}; "
                + $"Source={source}.");
            if (homeTerritory != destination)
                return TerritoryValidation.Blocked(latest,
                    $"Coordinate fallback stopped because the green home territory group "
                    + $"({homeTerritory}) differs from the X/Y destination group "
                    + $"({destination}); no move Tap was sent.");

            return TerritoryValidation.Permitted(latest, movePin,
                $"The fallback X/Y destination belongs to the {homeTerritory} territory group.");
        }

        private static bool TryLocateYellowDestinationPin(
            byte[] screenshotPng,
            out GameDetectionEvidence destinationPin)
        {
            destinationPin = null;
            if (screenshotPng == null || screenshotPng.Length == 0)
                return false;

            try
            {
                using (var stream = new MemoryStream(screenshotPng, false))
                using (var bitmap = new Bitmap(stream))
                {
                    int left = Math.Min(
                        DestinationPinSearchLeftPx, bitmap.Width);
                    int top = Math.Min(
                        DestinationPinSearchTopPx, bitmap.Height);
                    int right = Math.Max(left, bitmap.Width
                        - DestinationPinSearchRightMarginPx);
                    int bottom = Math.Max(top, bitmap.Height
                        - DestinationPinSearchBottomMarginPx);
                    var visited = new bool[bitmap.Width * bitmap.Height];
                    var candidates = new List<YellowPinCandidate>();

                    for (int y = top; y < bottom; y++)
                    {
                        for (int x = left; x < right; x++)
                        {
                            int index = (y * bitmap.Width) + x;
                            if (visited[index]
                                || !IsYellowDestinationPinPixel(
                                    bitmap.GetPixel(x, y)))
                                continue;

                            var queue = new Queue<int>();
                            queue.Enqueue(index);
                            visited[index] = true;
                            int pixels = 0;
                            int minimumX = x;
                            int maximumX = x;
                            int minimumY = y;
                            int maximumY = y;

                            while (queue.Count > 0)
                            {
                                int current = queue.Dequeue();
                                int currentX = current % bitmap.Width;
                                int currentY = current / bitmap.Width;
                                pixels++;
                                minimumX = Math.Min(minimumX, currentX);
                                maximumX = Math.Max(maximumX, currentX);
                                minimumY = Math.Min(minimumY, currentY);
                                maximumY = Math.Max(maximumY, currentY);

                                for (int offsetY = -1;
                                    offsetY <= 1; offsetY++)
                                {
                                    for (int offsetX = -1;
                                        offsetX <= 1; offsetX++)
                                    {
                                        if (offsetX == 0 && offsetY == 0)
                                            continue;
                                        int nextX = currentX + offsetX;
                                        int nextY = currentY + offsetY;
                                        if (nextX < left || nextX >= right
                                            || nextY < top || nextY >= bottom)
                                            continue;
                                        int next = (nextY * bitmap.Width)
                                            + nextX;
                                        if (visited[next]
                                            || !IsYellowDestinationPinPixel(
                                                bitmap.GetPixel(
                                                    nextX, nextY)))
                                            continue;
                                        visited[next] = true;
                                        queue.Enqueue(next);
                                    }
                                }
                            }

                            int width = maximumX - minimumX + 1;
                            int height = maximumY - minimumY + 1;
                            if (pixels < MinimumDestinationPinPixels
                                || pixels > MaximumDestinationPinPixels
                                || width < MinimumDestinationPinWidthPx
                                || width > MaximumDestinationPinWidthPx
                                || height < MinimumDestinationPinHeightPx
                                || height > MaximumDestinationPinHeightPx
                                || height <= width)
                                continue;
                            candidates.Add(new YellowPinCandidate(
                                minimumX, minimumY, width, height, pixels));
                        }
                    }

                    if (candidates.Count == 0) return false;
                    candidates.Sort((first, second) =>
                        second.PixelCount.CompareTo(first.PixelCount));
                    YellowPinCandidate best = candidates[0];
                    if (candidates.Count > 1
                        && candidates[1].PixelCount
                            >= best.PixelCount * 0.8)
                        return false;

                    destinationPin = new GameDetectionEvidence
                    {
                        TemplateId =
                            TemplateId.ContinentMapSearchTargetPin,
                        TemplateExists = true,
                        Found = true,
                        MatchResult = ImageMatchResult.FoundAt(
                            best.X, best.Y, best.Width, best.Height),
                        Message = "Yellow X/Y destination pin located "
                            + "from foreground pixels."
                    };
                    return true;
                }
            }
            catch (ArgumentException)
            {
                return false;
            }
            catch (ExternalException)
            {
                return false;
            }
        }

        private static bool IsYellowDestinationPinPixel(Color pixel)
        {
            return pixel.R >= 225
                && pixel.G >= 160
                && pixel.B <= 195
                && pixel.R - pixel.G >= 15
                && pixel.G - pixel.B >= 15;
        }

        private static void AddTerritoryColorProgress(
            IList<NavigationTransition> transitions,
            IProgress<NavigationTransition> progress,
            string message)
        {
            var transition = new NavigationTransition
            {
                Operation = "TerritoryColor",
                Message = message
            };
            transitions.Add(transition);
            if (progress == null) return;
            try
            {
                progress.Report(transition);
            }
            catch
            {
                // Display callbacks must never alter safe navigation behavior.
            }
        }

        private static bool TryClassifyPinTerritory(
            byte[] screenshotPng,
            GameDetectionEvidence pin,
            out TerritoryColorGroup group)
        {
            group = TerritoryColorGroup.Unknown;
            if (!HasValidBounds(pin)) return false;

            ImageMatchResult bounds = pin.MatchResult;
            int sampleY = bounds.Y + bounds.Height
                - PinBackgroundHeightPx;
            var regions = new[]
            {
                new ImageRegion(
                    bounds.X - PinBackgroundSideGapPx
                        - PinBackgroundSideWidthPx,
                    sampleY,
                    PinBackgroundSideWidthPx,
                    PinBackgroundHeightPx),
                new ImageRegion(
                    bounds.X + bounds.Width + PinBackgroundSideGapPx,
                    sampleY,
                    PinBackgroundSideWidthPx,
                    PinBackgroundHeightPx)
            };
            return TryClassifyTerritoryRegions(
                screenshotPng, regions, out group);
        }

        private static bool TryClassifyTerritoryRegions(
            byte[] screenshotPng,
            IEnumerable<ImageRegion> regions,
            out TerritoryColorGroup group)
        {
            group = TerritoryColorGroup.Unknown;
            if (screenshotPng == null || screenshotPng.Length == 0)
                return false;

            try
            {
                using (var stream = new MemoryStream(screenshotPng, false))
                using (var bitmap = new Bitmap(stream))
                {
                    int sampled = 0;
                    int classifiable = 0;
                    var counts = new int[5];
                    foreach (ImageRegion region in regions)
                    {
                        int left = Math.Max(0, region.X);
                        int top = Math.Max(0, region.Y);
                        int right = Math.Min(
                            bitmap.Width, region.X + region.Width);
                        int bottom = Math.Min(
                            bitmap.Height, region.Y + region.Height);
                        if (right <= left || bottom <= top) continue;

                        for (int y = top; y < bottom; y++)
                        {
                            for (int x = left; x < right; x++)
                            {
                                sampled++;
                                Color pixel = bitmap.GetPixel(x, y);
                                double hue;
                                double saturation;
                                double value;
                                ToHsv(pixel, out hue, out saturation, out value);
                                if (saturation < MinimumTerritoryPixelSaturation
                                    || value < MinimumTerritoryPixelValue
                                    || value > MaximumTerritoryPixelValue)
                                    continue;

                                TerritoryColorGroup pixelGroup = GroupForHue(hue);
                                counts[(int)pixelGroup - 1]++;
                                classifiable++;
                            }
                        }
                    }

                    if (sampled == 0
                        || classifiable < sampled * MinimumClassifiablePixelRatio)
                        return false;

                    int winnerIndex = 0;
                    int runnerUp = 0;
                    for (int index = 1; index < counts.Length; index++)
                    {
                        if (counts[index] > counts[winnerIndex])
                        {
                            runnerUp = counts[winnerIndex];
                            winnerIndex = index;
                        }
                        else if (counts[index] > runnerUp)
                        {
                            runnerUp = counts[index];
                        }
                    }

                    int winner = counts[winnerIndex];
                    if (winner < classifiable * MinimumWinningGroupRatio
                        || winner - runnerUp
                            < classifiable * MinimumWinningMarginRatio)
                        return false;

                    group = (TerritoryColorGroup)(winnerIndex + 1);
                    return true;
                }
            }
            catch (ArgumentException)
            {
                return false;
            }
            catch (ExternalException)
            {
                return false;
            }
        }

        private static TerritoryColorGroup GroupForHue(double hue)
        {
            if (hue < 25 || hue >= 335) return TerritoryColorGroup.Red;
            if (hue < 75) return TerritoryColorGroup.Gold;
            if (hue < 165) return TerritoryColorGroup.Green;
            if (hue < 260) return TerritoryColorGroup.Blue;
            return TerritoryColorGroup.Purple;
        }

        private static void ToHsv(
            Color color,
            out double hue,
            out double saturation,
            out double value)
        {
            double red = color.R / 255.0;
            double green = color.G / 255.0;
            double blue = color.B / 255.0;
            double maximum = Math.Max(red, Math.Max(green, blue));
            double minimum = Math.Min(red, Math.Min(green, blue));
            double delta = maximum - minimum;

            value = maximum;
            saturation = maximum <= 0 ? 0 : delta / maximum;
            if (delta <= 0)
            {
                hue = 0;
                return;
            }

            if (maximum == red)
                hue = 60 * (((green - blue) / delta) % 6);
            else if (maximum == green)
                hue = 60 * (((blue - red) / delta) + 2);
            else
                hue = 60 * (((red - green) / delta) + 4);
            if (hue < 0) hue += 360;
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
                        return new PinObservation(
                            fresh, freshPair, freshPair.Home);
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
                    return new PinObservation(current, null, latestHome);
                }

                if (!detectorSupportsPins)
                    return new PinObservation(current, null, latestHome);

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

            return new PinObservation(current, null, latestHome);
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
            public PinObservation(
                GameDetectionResult latest,
                PinPair pair,
                GameDetectionEvidence home)
            {
                Latest = latest;
                Pair = pair;
                Home = home;
            }

            public GameDetectionResult Latest { get; }
            public PinPair Pair { get; }
            public GameDetectionEvidence Home { get; }
        }

        private sealed class YellowPinCandidate
        {
            public YellowPinCandidate(
                int x, int y, int width, int height, int pixelCount)
            {
                X = x;
                Y = y;
                Width = width;
                Height = height;
                PixelCount = pixelCount;
            }

            public int X { get; }
            public int Y { get; }
            public int Width { get; }
            public int Height { get; }
            public int PixelCount { get; }
        }

        private enum TerritoryColorGroup
        {
            Unknown = 0,
            Red = 1,
            Gold = 2,
            Green = 3,
            Blue = 4,
            Purple = 5
        }

        private sealed class CoordinateEdit
        {
            public CoordinateEdit(
                string axis, int originalValue, int targetValue)
            {
                Axis = axis;
                OriginalValue = originalValue;
                TargetValue = targetValue;
            }

            public string Axis { get; }
            public int OriginalValue { get; }
            public int TargetValue { get; }
        }

        private sealed class TerritoryValidation
        {
            private TerritoryValidation(
                bool allowed,
                GameDetectionResult latest,
                GameDetectionEvidence destination,
                string message)
            {
                Allowed = allowed;
                Latest = latest;
                Destination = destination;
                Message = message;
            }

            public bool Allowed { get; }
            public GameDetectionResult Latest { get; }
            public GameDetectionEvidence Destination { get; }
            public string Message { get; }

            public static TerritoryValidation Blocked(
                GameDetectionResult latest,
                string message) =>
                new TerritoryValidation(false, latest, null, message);

            public static TerritoryValidation Permitted(
                GameDetectionResult latest,
                GameDetectionEvidence destination,
                string message) =>
                new TerritoryValidation(true, latest, destination, message);
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
