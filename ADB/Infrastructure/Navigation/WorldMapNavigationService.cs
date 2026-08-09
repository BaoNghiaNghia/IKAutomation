using ADB_Tool_Automation_Post_FB.Core.Abstractions;
using ADB_Tool_Automation_Post_FB.Core.Concurrency;
using ADB_Tool_Automation_Post_FB.Core.Diagnostics;
using ADB_Tool_Automation_Post_FB.Core.GameDetection;
using ADB_Tool_Automation_Post_FB.Core.Navigation;
using ADB_Tool_Automation_Post_FB.Core.ResourceSearch;
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
        IWorldMapNavigationProgressService,
        IResourceAreaMapPointNavigationService
    {
        private const int ExpectedScreenshotWidth = 1280;
        private const int ExpectedScreenshotHeight = 720;
        private const int MaxTerritoryMarkerDistanceFromViewportCenterPx = 360;
        private const int MaxSearchTargetDistanceFromHomePinPx = 260;
        private const int NearbyPinObservationAttempts = 8;
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
        private const int SameTerritoryPatchSizePx = 7;
        private const int SameTerritoryPatchRadiusPx = 3;
        private const int SameTerritorySampleOffsetPx = 14;
        private const int SameTerritoryMinimumVotes = 4;
        private const int SameTerritoryMaximumCandidates = 3;
        private const int SameTerritoryMinimumHomeDistancePx = 60;
        private static readonly Point[] SameTerritoryScreenOffsets =
        {
            new Point(90, 0), new Point(-90, 0), new Point(0, 90), new Point(0, -90),
            new Point(65, 65), new Point(-65, 65), new Point(65, -65), new Point(-65, -65),
            new Point(150, 0), new Point(-150, 0), new Point(0, 150), new Point(0, -150),
            new Point(105, 105), new Point(-105, 105), new Point(105, -105), new Point(-105, -105)
        };
        // The coordinate fields are stable members of the same top-left toolbar as
        // ContinentMapPinButton. Deriving their centers from that freshly matched
        // button keeps the fallback resolution-independent within the supported
        // 1280x720 layout and avoids blind absolute taps.
        private const int CoordinateXOffsetFromPinCenterPx = -134;
        private const int CoordinateYOffsetFromPinCenterPx = -54;
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

        public Task<NavigationResult> TapWorldMapPointAsync(string deviceName, int x, int y,
            CancellationToken cancellationToken)
        {
            return WithDeviceLockAsync(deviceName, "TapWorldMapPoint",
                token => TapWorldMapPointCoreAsync(deviceName, x, y, token), cancellationToken);
        }

        public Task<NavigationResult> OpenMapAndTapPointAsync(string deviceName, int x, int y,
            CancellationToken cancellationToken)
        {
            return WithDeviceLockAsync(deviceName, "OpenMapAndTapResourceAreaPoint",
                token => OpenMapAndTapPointCoreAsync(deviceName, x, y, token), cancellationToken);
        }

        private async Task<NavigationResult> OpenMapAndTapPointCoreAsync(string deviceName,
            int x, int y, CancellationToken cancellationToken)
        {
            var watch = Stopwatch.StartNew();
            var transitions = new List<NavigationTransition>();
            NavigationResult ensured = await EnsureWorldMapCoreAsync(
                deviceName, null, cancellationToken);
            if (!ensured.Success || ensured.FinalState != GameState.WorldMap)
                return ensured;

            GameDetectionResult initial = DetectionFrom(ensured);
            GameDetectionResult current = initial;
            GameDetectionEvidence mapButton = FindFreshEvidence(
                current, TemplateId.WorldMapPinButton);
            if (mapButton == null)
            {
                current = await RefreshFullFrameEvidenceAsync(
                    deviceName, transitions, cancellationToken);
                mapButton = current.IsSuccessful && current.State == GameState.WorldMap
                    ? FindFreshEvidence(current, TemplateId.WorldMapPinButton)
                    : null;
            }
            if (mapButton == null)
                return Result(false, initial, current, ensured.Attempts, watch,
                    "Không tìm thấy biểu tượng bản đồ với bounds mới; không gửi Tap.",
                    null, transitions, "WorldMapPinButtonUnavailable");

            await TapEvidenceAsync(deviceName, mapButton,
                "WorldMapPinButtonForResourceAreaLv2", transitions, cancellationToken);
            current = await PollAsync(deviceName, GameState.ContinentMap,
                transitions, cancellationToken);
            if (!current.IsSuccessful || current.State != GameState.ContinentMap)
                return Result(false, initial, current, ensured.Attempts + 1, watch,
                    "Đã bấm biểu tượng map nhưng chưa xác minh được ContinentMap.",
                    current?.ErrorMessage, transitions, "ContinentMapNotVerified");

            FrameResolutionResult resolution = await CaptureFrameResolutionAsync(
                deviceName, cancellationToken);
            if (x < 0 || y < 0 || x >= resolution.Width || y >= resolution.Height)
                return Result(false, initial, current, ensured.Attempts + 1, watch,
                    "Điểm ContinentMap nằm ngoài khung hình; không gửi Tap.", null,
                    transitions, "PointOutsideFrame", x, y, 0, false);

            await ldPlayerClient.TapAsync(deviceName, x, y, cancellationToken);
            AddTransition(transitions, "Tap",
                $"Tapped predefined ContinentMap point ({x},{y}).");
            await Task.Delay(options.StatePollIntervalMs, cancellationToken);
            current = await DetectAsync(deviceName, transitions, cancellationToken);
            if (current.IsSuccessful && current.State == GameState.Unknown
                && IsVerifiedContinentMapEvidence(current))
                current.State = GameState.ContinentMap;
            if (!current.IsSuccessful || current.State != GameState.ContinentMap)
                return Result(false, initial, current, ensured.Attempts + 2, watch,
                    "Điểm đã được chọn nhưng ContinentMap không còn được xác minh.",
                    current?.ErrorMessage, transitions, "ContinentMapPointNotVerified", x, y, 1, false);

            GameDetectionEvidence moveButton = FindFreshEvidence(
                current, TemplateId.ContinentMapPinButton);
            if (moveButton == null)
            {
                current = await RefreshFullFrameEvidenceAsync(
                    deviceName, transitions, cancellationToken);
                moveButton = FindFreshEvidence(current, TemplateId.ContinentMapPinButton);
            }
            if (moveButton == null)
                return Result(false, initial, current, ensured.Attempts + 2, watch,
                    "Không tìm thấy nút di chuyển trên ContinentMap; không gửi Tap.",
                    null, transitions, "ContinentMapPinButtonUnavailable", x, y, 1, false);

            await TapEvidenceAsync(deviceName, moveButton,
                "ContinentMapPinButtonForResourceAreaLv2", transitions, cancellationToken);
            GameDetectionResult final = await PollAsync(deviceName, GameState.WorldMap,
                transitions, cancellationToken);
            return final.IsSuccessful && final.State == GameState.WorldMap
                ? Result(true, initial, final, ensured.Attempts + 3, watch,
                    "Đã mở map, chọn điểm ngẫu nhiên và quay lại WorldMap.", null,
                    transitions, null, x, y, 2, true)
                : Result(false, initial, final, ensured.Attempts + 3, watch,
                    "Đã chọn điểm nhưng chưa xác minh quay lại WorldMap.",
                    final?.ErrorMessage, transitions, "WorldMapNotVerifiedAfterPoint", x, y, 2, false);
        }

        private async Task<NavigationResult> TapWorldMapPointCoreAsync(string deviceName,
            int x, int y, CancellationToken cancellationToken)
        {
            var watch = Stopwatch.StartNew();
            var transitions = new List<NavigationTransition>();
            NavigationResult ensured = await EnsureWorldMapCoreAsync(deviceName, null, cancellationToken);
            if (!ensured.Success || ensured.FinalState != GameState.WorldMap)
            {
                ensured.FailureReason = "WorldMapUnavailable";
                ensured.VerificationSucceeded = false;
                return ensured;
            }

            GameDetectionResult initial = DetectionFrom(ensured);
            int width;
            int height;
            try
            {
                FrameResolutionResult resolution = await CaptureFrameResolutionAsync(
                    deviceName, cancellationToken);
                width = resolution.Width;
                height = resolution.Height;
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception exception)
            {
                return Result(false, initial, initial, 0, watch,
                    "Không thể xác định độ phân giải bản đồ trước khi chạm điểm.",
                    exception.Message, transitions, "FrameResolutionUnavailable");
            }

            if (x < 0 || y < 0 || x >= width || y >= height)
                return Result(false, initial, initial, 0, watch,
                    "Điểm bản đồ nằm ngoài khung hình; không gửi Tap.", null,
                    transitions, "PointOutsideFrame", x, y, 0, false);

            await ldPlayerClient.TapAsync(deviceName, x, y, cancellationToken);
            AddTransition(transitions, "Tap", $"Tapped WorldMap point ({x},{y}).");
            await Task.Delay(options.StatePollIntervalMs, cancellationToken);
            GameDetectionResult current = await DetectAsync(deviceName, transitions, cancellationToken);
            if (current.IsSuccessful && current.State == GameState.WorldMap)
                return Result(true, initial, current, 1, watch,
                    "WorldMap remained verified after tapping the point.", null,
                    transitions, null, x, y, 1, true);

            if (current.IsSuccessful && current.State == GameState.Unknown)
            {
                GameDetectionResult recovered = await TryCancelWorldMapBlockingDialogAsync(
                    deviceName, current, transitions, cancellationToken);
                if (recovered != null && recovered.IsSuccessful
                    && recovered.State == GameState.WorldMap)
                    return Result(true, initial, recovered, 2, watch,
                        "WorldMap was re-verified after closing its blocking dialog.", null,
                        transitions, null, x, y, 1, true);
                current = recovered ?? current;
            }

            string reason = current != null && current.State == GameState.City
                ? "CityAfterTap"
                : current != null && current.State == GameState.ContinentMap
                    ? "ContinentMapAfterTap"
                    : current != null && current.State == GameState.TeamSelection
                        ? "TeamSelectionAfterTap"
                        : current != null && current.State == GameState.ResourceSearchPanel
                            ? "ResourceSearchPanelAfterTap"
                            : "WorldMapVerificationFailed";
            return Result(false, initial, current, 1, watch,
                "Điểm đã được chạm nhưng WorldMap không được xác minh an toàn.",
                current?.ErrorMessage, transitions, reason, x, y, 1, false);
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
            if (initial.State == GameState.TeamSelection)
            {
                return Result(false, initial, initial, 0, watch,
                    "TeamSelection is still open; no Android Back or navigation input was sent. "
                    + "The active or orphaned team-selection transaction requires controlled recovery.",
                    null, transitions);
            }
            if (initial.State == GameState.ResourcePopup)
            {
                int maximumBackAttempts = 1;
                GameDetectionResult current = initial;
                for (int attempt = 1; attempt <= maximumBackAttempts; attempt++)
                {
                    await ldPlayerClient.BackAsync(deviceName, cancellationToken);
                    AddTransition(transitions, "Back",
                        "Sent one Back command to close ResourcePopup.");
                    current = await PollAsync(deviceName, GameState.WorldMap,
                        transitions, cancellationToken);
                    if (current.IsSuccessful && current.State == GameState.WorldMap)
                        return Result(true, initial, current, attempt, watch,
                            "WorldMap verified after closing the active overlay.", null,
                            transitions);
                }

                return Result(false, initial, current, maximumBackAttempts, watch,
                    "ResourcePopup could not be closed to reach WorldMap.",
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
                    "Could not ensure WorldMap; no Tap was sent.",
                    ensured.ErrorMessage, transitions, ensured.FailureReason ?? "WorldMapUnavailable");

            GameDetectionResult current = DetectionFrom(ensured);
            int maxAttempts = Math.Min(options.MaxOpenSearchAttempts, 2);
            for (int attempt = 1; attempt <= maxAttempts; attempt++)
            {
                GameDetectionEvidence anchor = FindFreshEvidence(current, TemplateId.WorldMapAnchor);
                if (anchor?.MatchResult == null || !anchor.Found || anchor.MatchResult.Width <= 0 || anchor.MatchResult.Height <= 0)
                    return Result(false, initial, current, attempt - 1, watch, "WorldMapAnchor has no valid bounds; no fallback Tap was sent.", null, transitions,
                        attempt > 1 ? "WorldMapAnchorNotFoundForPanelRetry" : "ResourceSearchPanelNotOpened");

                int x = anchor.MatchResult.CenterX;
                int y = anchor.MatchResult.CenterY;
                logger.Info($"[World Map Navigation] DeviceName='{deviceName}', Operation='Tap', "
                    + $"Attempt={attempt}, TapX={x}, TapY={y}, Cancellation=false, Phase='Attempting'");
                await ldPlayerClient.TapAsync(deviceName, x, y, cancellationToken);
                AddTransition(transitions, "Tap", $"Attempt {attempt}: tapped WorldMapAnchor center ({x},{y}).");
                ResourceSearchPanelScreenshotProbe probe = await ProbeResourceSearchPanelAsync(
                    deviceName, transitions, cancellationToken);
                current = probe.LastFrame ?? current;
                if (probe.Confirmed)
                {
                    NavigationResult success = Result(true, initial, current, attempt, watch,
                        "ResourceSearchPanel confirmed by two stable SearchButton screenshots.",
                        null, transitions, null, x, y, attempt, true);
                    ApplySearchPanelProbe(success, probe, attempt);
                    // The bounded screenshot probe is the authoritative handoff
                    // signal even when the full state classifier remains Unknown.
                    success.FinalState = GameState.ResourceSearchPanel;
                    return success;
                }

                if (probe.CityDetected)
                {
                    NavigationResult cityFailure = Result(false, initial, current, attempt, watch,
                        "City was detected after opening the resource search panel; no Back was sent.",
                        current?.ErrorMessage, transitions, "CityDetectedDuringPanelOpen", x, y, attempt, false);
                    ApplySearchPanelProbe(cityFailure, probe, attempt);
                    return cityFailure;
                }

                // Panel evidence is never routed through WorldMap retry. A later
                // panel frame may be handled by the next bounded probe, but this
                // operation must not send Back or reinterpret it as a missed tap.
                if (probe.PanelEvidenceVisible)
                {
                    NavigationResult panelEvidenceFailure = Result(false, initial, current, attempt, watch,
                        "ResourceSearchPanel evidence was visible but the screenshot probe needs another positive frame.",
                        current?.ErrorMessage, transitions, probe.FailureReason ?? "ResourceSearchPanelEvidenceIncomplete", x, y, attempt, false);
                    ApplySearchPanelProbe(panelEvidenceFailure, probe, attempt);
                    if (panelEvidenceFailure.FinalState == GameState.ResourceSearchPanel)
                        panelEvidenceFailure.FinalState = GameState.Unknown;
                    return panelEvidenceFailure;
                }

                bool finalAttempt = attempt >= maxAttempts;
                if (finalAttempt)
                {
                    NavigationResult failed = Result(false, initial, current, attempt, watch,
                        "ResourceSearchPanel was not confirmed by a stable screenshot probe.",
                        current?.ErrorMessage, transitions, probe.FailureReason ?? "ResourceSearchPanelNotOpened", x, y, attempt, false);
                    ApplySearchPanelProbe(failed, probe, attempt);
                    return failed;
                }

                // Before requiring WorldMap for another tap, accept a later fresh
                // Search-button observation. The initial probe can land while the
                // panel is still animating and must not overwrite this success.
                if (!IsFreshWorldMapAnchor(current))
                {
                    current = await DetectAsync(deviceName, transitions, cancellationToken);
                    GameDetectionEvidence refreshedSearchButton = FindFreshEvidence(
                        current, TemplateId.SearchButtonEnabled);
                    bool refreshedPanelConfirmed = refreshedSearchButton?.SearchRegion != null
                        && IsInside(refreshedSearchButton.MatchResult,
                            refreshedSearchButton.SearchRegion.Value);
                    if (refreshedPanelConfirmed)
                    {
                        probe.LastFrame = current;
                        probe.Confirmed = true;
                        probe.SearchButtonVisible = true;
                        probe.ConfirmationFrames = Math.Max(probe.ConfirmationFrames, 1);
                        probe.ExpectedRegion = true;
                        probe.InsideExpectedRegion = true;
                        probe.SearchButtonBounds = refreshedSearchButton.MatchResult;
                        probe.ConfirmationMode = "FreshSearchButtonAfterProbe";
                        probe.FailureReason = null;
                        LogSearchPanelProbe(deviceName, 2,
                            HasEvidence(current, TemplateId.ResourceSearchPanelAnchor),
                            refreshedSearchButton, null, false, probe, 1,
                            probe.ConfirmationMode,
                            "ContinueResourceConfiguration", string.Empty);

                        NavigationResult success = Result(true, initial, current,
                            attempt, watch,
                            "ResourceSearchPanel confirmed by a fresh SearchButton screenshot after the initial probe.",
                            null, transitions, null, x, y, attempt, true);
                        ApplySearchPanelProbe(success, probe, attempt);
                        success.FinalState = GameState.ResourceSearchPanel;
                        return success;
                    }
                    if (!IsFreshWorldMapAnchor(current))
                    {
                        NavigationResult failed = Result(false, initial, current, attempt, watch,
                            "WorldMap was not freshly confirmed for the bounded panel retry; no Back was sent.",
                            current?.ErrorMessage, transitions, "WorldMapNotConfirmedForPanelRetry", x, y, attempt, false);
                        ApplySearchPanelProbe(failed, probe, attempt);
                        return failed;
                    }
                }
            }

            return Result(false, initial, current, maxAttempts, watch,
                "Maximum open-search attempts reached without verification.", current?.ErrorMessage,
                transitions, "ResourceSearchPanelNotOpened");
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
            {
                current = await RefreshFullFrameEvidenceAsync(
                    deviceName, transitions, cancellationToken);
                mapButton = current.IsSuccessful && current.State == GameState.WorldMap
                    ? FindFreshEvidence(current, TemplateId.WorldMapPinButton)
                    : null;
            }
            if (mapButton == null)
                return Result(false, initial, current, ensured.Attempts, watch,
                    "WorldMap pin-map button had no valid fresh bounds; no Tap was sent.", null, transitions);

            await TapEvidenceAsync(deviceName, mapButton, "WorldMapPinButton", transitions, cancellationToken);
            current = await PollAsync(deviceName, GameState.ContinentMap, transitions, cancellationToken);
            if (!current.IsSuccessful || current.State != GameState.ContinentMap)
                return Result(false, initial, current, ensured.Attempts + 1, watch,
                    "Pin-map button was tapped but ContinentMap was not verified before timeout.",
                    current.ErrorMessage, transitions);

            // Keep territory reposition deterministic: one fresh ContinentMap
            // screenshot supplies the home colour and candidate scan. The
            // selected point is rechecked on another fresh frame immediately
            // before input, then the resulting pin is verified after input.
            AddTransition(transitions, "Strategy", "FreshScreenshotColorScan");
            return await TryScreenPointFallbackAsync(deviceName, initial, current,
                ensured.Attempts, watch, transitions, progress, cancellationToken);
        }

        private async Task<HomeLocationEvidence> AcquireHomeLocationEvidenceAsync(
            string deviceName,
            GameDetectionResult initial,
            IList<NavigationTransition> transitions,
            CancellationToken cancellationToken)
        {
            GameDetectionEvidence existing = FindFreshEvidence(initial,
                TemplateId.ContinentMapHomeLocationPin)
                ?? FindFreshEvidence(initial, TemplateId.ContinentMapHomeTerritoryAnchor);
            if (existing != null)
                return new HomeLocationEvidence(initial, existing, "FreshEvidence");

            GameDetectionResult latest = initial;
            for (int attempt = 1; attempt <= options.HomePinAcquisitionAttempts; attempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                byte[] screenshot = await ldPlayerClient.CaptureScreenshotPngAsync(
                    deviceName, cancellationToken);
                GameDetectionResult detection = detector.Detect(screenshot);
                if (detection != null && detection.IsSuccessful)
                    latest = detection;
                GameDetectionEvidence detected = FindFreshEvidence(detection,
                    TemplateId.ContinentMapHomeLocationPin)
                    ?? FindFreshEvidence(detection, TemplateId.ContinentMapHomeTerritoryAnchor);
                if (detected != null)
                    return new HomeLocationEvidence(latest, detected, "FreshDetection");
                if (TryLocateHomeLocationPin(screenshot, out GameDetectionEvidence pixelPin))
                {
                    AddTransition(transitions, "HomeEvidence",
                        $"Đã tìm thấy pin nhà từ điểm ảnh cyan (lần {attempt}/{options.HomePinAcquisitionAttempts}).");
                    return new HomeLocationEvidence(latest, pixelPin, "CyanPixels");
                }
                if (attempt < options.HomePinAcquisitionAttempts)
                    await Task.Delay(options.StatePollIntervalMs, cancellationToken);
            }

            return new HomeLocationEvidence(latest, null, "Unavailable");
        }

        private async Task<NavigationResult> TryScreenPointFallbackAsync(
            string deviceName,
            GameDetectionResult initial,
            GameDetectionResult current,
            int priorAttempts,
            Stopwatch watch,
            IList<NavigationTransition> transitions,
            IProgress<NavigationTransition> progress,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            byte[] initialScreenshot = await ldPlayerClient.CaptureScreenshotPngAsync(
                deviceName, cancellationToken);
            GameDetectionResult initialMap = detector.Detect(initialScreenshot);
            if (initialMap != null && initialMap.State == GameState.Unknown
                && IsVerifiedContinentMapEvidence(initialMap))
                initialMap.State = GameState.ContinentMap;
            if (initialMap == null || initialMap.State != GameState.ContinentMap)
                return ScreenPointFailure(initial, initialMap ?? current, priorAttempts,
                    watch, transitions, "WorldMapTransitionFailed",
                    "Không thể xác nhận màn hình bản đồ lục địa trước khi quét điểm tương đối.");

            GameDetectionEvidence home = FindFreshEvidence(initialMap,
                TemplateId.ContinentMapHomeLocationPin);
            string homeSource = home == null ? string.Empty : home.TemplateId.ToString();
            using (var stream = new MemoryStream(initialScreenshot, false))
            using (var bitmap = new Bitmap(stream))
            {
                if (!HasValidBounds(home)
                    && !TryLocateHomeLocationPin(initialScreenshot, out home))
                {
                    return ScreenPointFailure(initial, initialMap, priorAttempts, watch,
                        transitions, "HomeMarkerNotFound",
                        "Không tìm thấy marker vị trí nhà trên ảnh bản đồ mới.");
                }
                if (homeSource.Length == 0)
                    homeSource = home.TemplateId.ToString();

                TerritoryColorGroup homeColor;
                if (!TryClassifyPinTerritory(bitmap, home, out homeColor)
                    || homeColor == TerritoryColorGroup.Unknown)
                {
                    return ScreenPointFailure(initial, initialMap, priorAttempts, watch,
                        transitions, "HomeTerritoryColorUnknown",
                        "Không thể phân loại màu lãnh thổ tại vị trí nhà.");
                }

                int homeX = home.MatchResult.CenterX;
                int homeY = home.MatchResult.CenterY;
                ImageRegion safeRoi = CreateSameTerritorySafeMapRoi(bitmap.Width, bitmap.Height);
                var qualified = new List<ScreenTerritoryCandidate>();
                for (int offsetIndex = 0; offsetIndex < SameTerritoryScreenOffsets.Length; offsetIndex++)
                {
                    Point offset = SameTerritoryScreenOffsets[offsetIndex];
                    int candidateX = homeX + offset.X;
                    int candidateY = homeY + offset.Y;
                    int distance = (int)Math.Round(Math.Sqrt(
                        (offset.X * offset.X) + (offset.Y * offset.Y)));
                    bool inside = ContainsPoint(safeRoi, candidateX, candidateY)
                        && distance >= SameTerritoryMinimumHomeDistancePx;
                    int votes = 0;
                    string rejection = null;
                    if (!inside)
                        rejection = distance < SameTerritoryMinimumHomeDistancePx
                            ? "TooCloseToHome" : "OutsideSafeMapRoi";
                    else
                    {
                        Point[] samples =
                        {
                            new Point(candidateX, candidateY),
                            new Point(candidateX - SameTerritorySampleOffsetPx, candidateY),
                            new Point(candidateX + SameTerritorySampleOffsetPx, candidateY),
                            new Point(candidateX, candidateY - SameTerritorySampleOffsetPx),
                            new Point(candidateX, candidateY + SameTerritorySampleOffsetPx)
                        };
                        foreach (Point sample in samples)
                        {
                            TerritoryColorGroup sampleColor;
                            if (TryClassifyTerritoryRegions(bitmap,
                                new[] { PatchAround(sample) }, out sampleColor)
                                && sampleColor == homeColor)
                                votes++;
                        }
                        if (votes < SameTerritoryMinimumVotes)
                            rejection = "InsufficientSameColorVotes";
                        else
                            qualified.Add(new ScreenTerritoryCandidate(candidateX,
                                candidateY, offsetIndex, offsetIndex < 8 ? 0 : 1,
                                votes, distance));
                    }
                    logger.Info($"[Same Territory Candidate Score] DeviceName='{deviceName}', CandidateIndex={offsetIndex + 1}, OffsetIndex={offsetIndex}, ScreenX={candidateX}, ScreenY={candidateY}, Ring={(offsetIndex < 8 ? "Near" : "Far")}, InsideSafeRoi={inside}, SameColorVotes={votes}, RequiredVotes={SameTerritoryMinimumVotes}, HomeColor={homeColor}, Qualified={votes >= SameTerritoryMinimumVotes && inside}, RejectionReason='{rejection ?? string.Empty}'");
                }

                List<ScreenTerritoryCandidate> attempts = qualified
                    .OrderByDescending(item => item.SameColorVotes)
                    .ThenBy(item => item.RingPriority)
                    .ThenBy(item => item.OffsetIndex)
                    .Take(SameTerritoryMaximumCandidates).ToList();
                logger.Info($"[Same Territory Screen Scan] DeviceName='{deviceName}', Strategy='RelativeScreenCandidates', HomePoint=({homeX},{homeY}), HomeEvidenceSource='{homeSource}', HomeColor='{homeColor}', ScreenshotWidth={bitmap.Width}, ScreenshotHeight={bitmap.Height}, SafeMapRoi=({safeRoi.X},{safeRoi.Y},{safeRoi.Width},{safeRoi.Height}), GeneratedCandidateCount={SameTerritoryScreenOffsets.Length}, InsideSafeRoiCount={CountInsideSameTerritoryRoi(homeX, homeY, safeRoi)}, QualifiedCandidateCount={qualified.Count}, MaxCandidateAttempts={SameTerritoryMaximumCandidates}, Outcome='{(attempts.Count == 0 ? "NoCandidatePassedColorVotes" : "CandidatesReady")}', FailureReason='{(attempts.Count == 0 ? "NoCandidatePassedColorVotes" : string.Empty)}'");
                if (attempts.Count == 0)
                    return ScreenPointFailure(initial, initialMap, priorAttempts, watch,
                        transitions, "NoCandidatePassedColorVotes",
                        "Không có điểm màn hình nào đạt đủ 4/5 mẫu cùng màu lãnh thổ nhà.");

                for (int attempt = 0; attempt < attempts.Count; attempt++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    ScreenTerritoryCandidate candidate = attempts[attempt];
                    byte[] preTapScreenshot = await ldPlayerClient
                        .CaptureScreenshotPngAsync(deviceName, cancellationToken);
                    GameDetectionResult preTapMap = detector.Detect(preTapScreenshot);
                    if (preTapMap != null && preTapMap.State == GameState.Unknown
                        && IsVerifiedContinentMapEvidence(preTapMap))
                        preTapMap.State = GameState.ContinentMap;
                    if (preTapMap == null || preTapMap.State != GameState.ContinentMap)
                    {
                        logger.Info($"[Same Territory Pre-Tap Check] DeviceName='{deviceName}', Attempt={attempt + 1}, FreshContinentMap=false, Qualified=false, FailureReason='ContinentMapNotVerified'");
                        break;
                    }

                    GameDetectionEvidence freshHome = FindFreshEvidence(preTapMap,
                        TemplateId.ContinentMapHomeLocationPin);
                    if (!HasValidBounds(freshHome)
                        && !TryLocateHomeLocationPin(preTapScreenshot, out freshHome))
                    {
                        logger.Info($"[Same Territory Pre-Tap Check] DeviceName='{deviceName}', Attempt={attempt + 1}, FreshContinentMap=true, Qualified=false, FailureReason='HomeMarkerNotFound'");
                        continue;
                    }

                    Point freshOffset = SameTerritoryScreenOffsets[candidate.OffsetIndex];
                    int tapX = freshHome.MatchResult.CenterX + freshOffset.X;
                    int tapY = freshHome.MatchResult.CenterY + freshOffset.Y;
                    int freshVotes = 0;
                    TerritoryColorGroup freshHomeColor = TerritoryColorGroup.Unknown;
                    bool freshQualified;
                    using (var preTapStream = new MemoryStream(preTapScreenshot, false))
                    using (var preTapBitmap = new Bitmap(preTapStream))
                    {
                        ImageRegion freshSafeRoi = CreateSameTerritorySafeMapRoi(
                            preTapBitmap.Width, preTapBitmap.Height);
                        bool homeClassified = TryClassifyPinTerritory(preTapBitmap,
                            freshHome, out freshHomeColor)
                            && freshHomeColor != TerritoryColorGroup.Unknown;
                        if (homeClassified && ContainsPoint(freshSafeRoi, tapX, tapY))
                        {
                            Point[] freshSamples =
                            {
                                new Point(tapX, tapY),
                                new Point(tapX - SameTerritorySampleOffsetPx, tapY),
                                new Point(tapX + SameTerritorySampleOffsetPx, tapY),
                                new Point(tapX, tapY - SameTerritorySampleOffsetPx),
                                new Point(tapX, tapY + SameTerritorySampleOffsetPx)
                            };
                            foreach (Point sample in freshSamples)
                            {
                                TerritoryColorGroup sampleColor;
                                if (TryClassifyTerritoryRegions(preTapBitmap,
                                    new[] { PatchAround(sample) }, out sampleColor)
                                    && sampleColor == freshHomeColor)
                                    freshVotes++;
                            }
                        }
                        freshQualified = homeClassified
                            && ContainsPoint(freshSafeRoi, tapX, tapY)
                            && freshVotes >= SameTerritoryMinimumVotes;
                    }

                    logger.Info($"[Same Territory Pre-Tap Check] DeviceName='{deviceName}', Attempt={attempt + 1}, FreshContinentMap=true, ScreenX={tapX}, ScreenY={tapY}, HomeColor='{freshHomeColor}', SameColorVotes={freshVotes}, RequiredVotes={SameTerritoryMinimumVotes}, Qualified={freshQualified}, FailureReason='{(freshQualified ? string.Empty : "FreshColorCheckFailed")}'");
                    if (!freshQualified)
                        continue;

                    await ldPlayerClient.TapAsync(deviceName, tapX, tapY,
                        cancellationToken);
                    AddTransition(transitions, "SameTerritoryCandidateTap",
                        $"Candidate {attempt + 1}/{attempts.Count}: fresh screen point ({tapX},{tapY}) tapped.");
                    await Task.Delay(options.StatePollIntervalMs, cancellationToken);
                    byte[] verificationScreenshot = await ldPlayerClient.CaptureScreenshotPngAsync(
                        deviceName, cancellationToken);
                    GameDetectionResult verification = detector.Detect(verificationScreenshot);
                    if (verification != null && verification.State == GameState.Unknown
                        && IsVerifiedContinentMapEvidence(verification))
                        verification.State = GameState.ContinentMap;
                    GameDetectionEvidence target = FindFreshEvidence(verification,
                        TemplateId.ContinentMapSearchTargetPin);
                    string targetSource = target == null ? string.Empty : "ContinentMapSearchTargetPin";
                    using (var verifyStream = new MemoryStream(verificationScreenshot, false))
                    using (var verifyBitmap = new Bitmap(verifyStream))
                    {
                        if (!HasValidBounds(target))
                        {
                            GameDetectionEvidence pixelTarget;
                            if (TryLocateYellowDestinationPin(verificationScreenshot,
                                out pixelTarget))
                            {
                                target = pixelTarget;
                                targetSource = "YellowPinPixels";
                            }
                        }
                        TerritoryColorGroup targetColor = TerritoryColorGroup.Unknown;
                        bool targetClassified = HasValidBounds(target)
                            && TryClassifyPinTerritory(verifyBitmap, target, out targetColor)
                            && targetColor != TerritoryColorGroup.Unknown;
                        if (!targetClassified) targetColor = TerritoryColorGroup.Unknown;
                        bool colorMatched = targetClassified
                            && targetColor == freshHomeColor;
                        GameDetectionEvidence moveButton = colorMatched
                            ? FindFreshEvidence(verification, TemplateId.ContinentMapPinButton)
                            : null;
                        bool moveFound = HasValidBounds(moveButton);
                        string failure = !HasValidBounds(target) ? "TargetPinNotFound"
                            : !targetClassified ? "TargetTerritoryColorUnknown"
                            : !colorMatched ? "TargetTerritoryColorMismatch"
                            : !moveFound ? "MoveButtonNotFound" : string.Empty;
                        logger.Info($"[Same Territory Candidate Verification] DeviceName='{deviceName}', Attempt={attempt + 1}, ScreenX={tapX}, ScreenY={tapY}, SameColorVotes={freshVotes}, TargetPinFound={HasValidBounds(target)}, TargetPinSource='{targetSource}', TargetPinBounds='{FormatBounds(target)}', HomeColor='{freshHomeColor}', TargetColor='{targetColor}', TargetColorClassified={targetClassified}, ColorMatched={colorMatched}, MoveButtonFound={moveFound}, WorldMapVerified=false, Outcome='{(failure.Length == 0 ? "MovePending" : "Rejected")}', FailureReason='{failure}'");
                        if (!colorMatched || !moveFound)
                            continue;

                        await TapEvidenceAsync(deviceName, moveButton,
                            "ContinentMapPinButtonAfterScreenPoint", transitions,
                            cancellationToken);
                    }

                    GameDetectionResult final = await PollAsync(deviceName,
                        GameState.WorldMap, transitions, cancellationToken);
                    bool worldMapVerified = final != null && final.IsSuccessful
                        && final.State == GameState.WorldMap;
                    logger.Info($"[Same Territory Candidate Result] DeviceName='{deviceName}', Attempt={attempt + 1}, ScreenX={tapX}, ScreenY={tapY}, SameColorVotes={freshVotes}, WorldMapVerified={worldMapVerified}, Outcome='{(worldMapVerified ? "Verified" : "Rejected")}', FailureReason='{(worldMapVerified ? string.Empty : "WorldMapTransitionFailed")}'");
                    if (worldMapVerified)
                        return Result(true, initial, final, priorAttempts + attempt + 2,
                            watch, "Đã di chuyển tới điểm cùng màu bằng chiến lược điểm màn hình.", null, transitions);
                    if (final == null || final.State != GameState.ContinentMap)
                        break;
                }
            }
            return ScreenPointFailure(initial, initialMap, priorAttempts, watch,
                transitions, "NoVerifiedSameColorScreenCandidate",
                "Không có ứng viên điểm màn hình nào được xác minh đầy đủ.");
        }

        private static NavigationResult ScreenPointFailure(
            GameDetectionResult initial, GameDetectionResult current, int attempts,
            Stopwatch watch, IList<NavigationTransition> transitions,
            string reason, string message)
        {
            AddTransition(transitions, "SameTerritoryScreenResult",
                $"Outcome=Failed; FailureReason={reason}; {message}");
            return Result(false, initial, current, attempts, watch, message, reason,
                transitions);
        }

        private static ImageRegion CreateSameTerritorySafeMapRoi(int width, int height) =>
            new ImageRegion(width * DestinationPinSearchLeftPx / ExpectedScreenshotWidth,
                height * DestinationPinSearchTopPx / ExpectedScreenshotHeight,
                Math.Max(1, width - (width * (DestinationPinSearchLeftPx
                    + DestinationPinSearchRightMarginPx) / ExpectedScreenshotWidth)),
                Math.Max(1, height - (height * (DestinationPinSearchTopPx
                    + DestinationPinSearchBottomMarginPx) / ExpectedScreenshotHeight)));

        private static int CountInsideSameTerritoryRoi(int homeX, int homeY,
            ImageRegion roi)
        {
            return SameTerritoryScreenOffsets.Count(offset =>
                ContainsPoint(roi, homeX + offset.X, homeY + offset.Y)
                && Math.Sqrt((offset.X * offset.X) + (offset.Y * offset.Y))
                    >= SameTerritoryMinimumHomeDistancePx);
        }

        private static ImageRegion PatchAround(Point point) => new ImageRegion(
            point.X - SameTerritoryPatchRadiusPx,
            point.Y - SameTerritoryPatchRadiusPx,
            SameTerritoryPatchSizePx, SameTerritoryPatchSizePx);

        private static bool ContainsPoint(ImageRegion region, int x, int y) =>
            x >= region.X && x < region.X + region.Width
            && y >= region.Y && y < region.Y + region.Height;

        private static string FormatBounds(GameDetectionEvidence evidence) =>
            HasValidBounds(evidence)
                ? $"({evidence.MatchResult.X},{evidence.MatchResult.Y},{evidence.MatchResult.Width},{evidence.MatchResult.Height})"
                : string.Empty;

        private async Task<CoordinateSearchResult> TrySameTerritoryCoordinateAsync(
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
            if (!HasValidBounds(homePin))
                return CoordinateSearchResult.Unavailable(null);

            AddTransition(transitions, "CoordinateSearch", "Đang tìm tọa độ cùng màu lãnh thổ.");
            NavigationResult result;
            try
            {
                result = await TryCoordinateFallbackAsync(deviceName, initial, current,
                    homePin, priorAttempts, watch, transitions, progress,
                    cancellationToken);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception exception)
            {
                return CoordinateSearchResult.Failed(Result(false, initial, current,
                    priorAttempts, watch, "Đã dừng để tránh di chuyển sang lãnh thổ khác.",
                    exception.Message, transitions));
            }

            if (result == null)
                return CoordinateSearchResult.Unavailable(null);
            if (result.Success)
                return CoordinateSearchResult.Succeeded(result);
            string message = result.Message ?? string.Empty;
            if (message.IndexOf("requires a focused numeric input reader", StringComparison.OrdinalIgnoreCase) >= 0)
                return CoordinateSearchResult.Unavailable(result);
            if (message.IndexOf("could not be classified", StringComparison.OrdinalIgnoreCase) >= 0
                || message.IndexOf("could not be located", StringComparison.OrdinalIgnoreCase) >= 0
                || message.IndexOf("rolled back", StringComparison.OrdinalIgnoreCase) >= 0)
                return CoordinateSearchResult.Unsafe(result);
            if (message.IndexOf("No matching territory tone", StringComparison.OrdinalIgnoreCase) >= 0
                || message.IndexOf("Không tìm thấy tọa độ cùng màu", StringComparison.OrdinalIgnoreCase) >= 0)
                return CoordinateSearchResult.NoMatchingCandidate(result);
            return CoordinateSearchResult.Failed(result);
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
                await ResolveHomeTerritoryAsync(
                    deviceName, homePin, transitions, cancellationToken);
            if (homeTerritory == TerritoryColorGroup.Unknown)
                return Result(false, initial, current, priorAttempts + 1, watch,
                    "Không thể xác định màu lãnh thổ đủ tin cậy tại vị trí nhà; "
                    + "đã dừng để tránh di chuyển sang lãnh thổ khác.", null, transitions);

            CoordinateEditTransaction transaction;
            try
            {
                transaction = await ReadCoordinateTransactionAsync(deviceName,
                    transitions, cancellationToken);
            }
            catch (Exception exception) when (!(exception is OperationCanceledException))
            {
                AddTransition(transitions, "CoordinateTransaction",
                    "Không thể đọc đủ X/Y gốc trước khi chỉnh sửa; không gửi dữ liệu tọa độ.");
                return null;
            }

            GameDetectionEvidence coordinatePin = initialPin;
            string lastValidationMessage = null;
            try
            {
                IList<CoordinateCandidate> candidates = CreateCoordinateCandidates(transaction);
                foreach (CoordinateCandidate candidate in candidates)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    AddTransition(transitions, "CoordinateCandidateRead",
                        $"Candidate {candidate.Attempt}/{candidates.Count}: X={candidate.TargetX}; Y={candidate.TargetY}; "
                        + $"Delta=({candidate.DeltaX},{candidate.DeltaY}).");

                    current = await ApplyCoordinateCandidateAsync(deviceName, transaction,
                        candidate, transitions, cancellationToken);
                    GameDetectionEvidence movePin = FindFreshEvidence(current,
                        TemplateId.ContinentMapPinButton);
                    if (movePin == null)
                        return await FailCoordinateTransactionAsync(deviceName, initial, current,
                            priorAttempts + (candidate.Attempt * 2), watch, transaction,
                            coordinatePin, "Không thể làm mới nút tọa độ sau khi chỉnh X/Y; "
                            + "đang khôi phục tọa độ ban đầu.", transitions);

                    TerritoryValidation validation = await ValidateCoordinateDestinationAsync(
                        deviceName, homeTerritory, candidate.Attempt, candidate.TargetX,
                        candidate.TargetY, transitions, progress, cancellationToken);
                    current = validation.Latest ?? current;
                    if (!validation.Allowed)
                    {
                        lastValidationMessage = validation.Message;
                        AddTransition(transitions, "CoordinateCandidateRejected",
                            $"Candidate {candidate.Attempt} bị từ chối; khôi phục X={transaction.OriginalX}; Y={transaction.OriginalY}.");
                        CoordinateRollbackResult rollback = await RollbackCoordinatesAsync(
                            deviceName, transaction, movePin, transitions, cancellationToken);
                        if (rollback.Status != CoordinateRollbackStatus.RestoredAndVerified)
                            return UnsafeRollbackResult(initial, current, priorAttempts, watch,
                                rollback, transitions);
                        current = rollback.Latest ?? current;
                        coordinatePin = FindFreshEvidence(current,
                            TemplateId.ContinentMapPinButton);
                        if (coordinatePin == null)
                            return UnsafeRollbackResult(initial, current, priorAttempts, watch,
                                CoordinateRollbackResult.VerificationUnavailable(current), transitions);
                        continue;
                    }

                    AddTransition(transitions, "CoordinateCandidateAccepted",
                        $"Candidate {candidate.Attempt} cùng màu; chỉ commit sau khi WorldMap được xác nhận.");
                    await TapEvidenceAsync(deviceName, validation.Destination,
                        "ContinentMapPinButtonAfterCoordinateChange", transitions, cancellationToken);
                    GameDetectionResult final = await PollAsync(deviceName, GameState.WorldMap,
                        transitions, cancellationToken);
                    if (final.IsSuccessful && final.State == GameState.WorldMap)
                    {
                        transaction.Commit();
                        return Result(true, initial, final, priorAttempts + (candidate.Attempt * 2) + 2,
                            watch, "WorldMap verified after selecting an X/Y candidate with a territory tone matching the home pin.", null, transitions);
                    }

                    return await FailCoordinateTransactionAsync(deviceName, initial, final,
                        priorAttempts + (candidate.Attempt * 2) + 2, watch, transaction,
                        null, "Đã gửi di chuyển nhưng không xác nhận được WorldMap; đang khôi phục tọa độ ban đầu.", transitions);
                }

                return Result(false, initial, current, priorAttempts + (options.CoordinateTerritoryAttempts * 4), watch,
                    $"Không tìm thấy tọa độ cùng màu sau {options.CoordinateTerritoryAttempts} lần thử; đã khôi phục tọa độ ban đầu và không gửi lệnh di chuyển. {lastValidationMessage}", null, transitions);
            }
            catch (OperationCanceledException)
            {
                await RollbackWithCleanupTokenAsync(deviceName, transaction, coordinatePin, transitions);
                throw;
            }
            catch (Exception exception)
            {
                CoordinateRollbackResult rollback = await RollbackWithCleanupTokenAsync(
                    deviceName, transaction, coordinatePin, transitions);
                return rollback.Status == CoordinateRollbackStatus.RestoredAndVerified
                    ? Result(false, initial, current, priorAttempts, watch,
                        "Đã xảy ra lỗi khi thử tọa độ; tọa độ ban đầu đã được khôi phục.", exception.Message, transitions)
                    : UnsafeRollbackResult(initial, current, priorAttempts, watch, rollback, transitions);
            }
        }

        // Invariant: until Commit is called after a verified WorldMap transition,
        // OriginalX/OriginalY remain authoritative and every exit restores them.
        private async Task<CoordinateEditTransaction> ReadCoordinateTransactionAsync(
            string deviceName,
            IList<NavigationTransition> transitions, CancellationToken cancellationToken)
        {
            int originalX = await ReadCoordinateValueAsync(deviceName,
                "X", transitions, cancellationToken);
            int originalY = await ReadCoordinateValueAsync(deviceName,
                "Y", transitions, cancellationToken);
            AddTransition(transitions, "CoordinateTransaction",
                $"Đã đọc X/Y gốc trước khi chỉnh sửa: X={originalX}; Y={originalY}.");
            return new CoordinateEditTransaction(originalX, originalY);
        }

        private async Task<int> ReadCoordinateValueAsync(string deviceName,
            string axis, IList<NavigationTransition> transitions, CancellationToken cancellationToken)
        {
            GameDetectionResult fresh = await DetectContinentMapAfterCoordinateEditAsync(
                deviceName, axis + " read", transitions, cancellationToken, true);
            GameDetectionEvidence pin = FindFreshEvidence(
                fresh, TemplateId.ContinentMapPinButton);
            if (pin == null)
                throw new InvalidOperationException(
                    $"Coordinate {axis} field cannot be derived without a fresh map-pin match.");
            int x = pin.MatchResult.CenterX
                + (string.Equals(axis, "X", StringComparison.Ordinal)
                    ? CoordinateXOffsetFromPinCenterPx
                    : CoordinateYOffsetFromPinCenterPx);
            int y = pin.MatchResult.CenterY;
            if (x < 0 || x >= ExpectedScreenshotWidth || y < 0 || y >= ExpectedScreenshotHeight)
                throw new InvalidOperationException($"Derived ContinentMap coordinate {axis} field is outside the supported viewport.");
            await ldPlayerClient.TapAsync(deviceName, x, y, cancellationToken);
            int value = await focusedInputValueReader.ReadFocusedIntegerAsync(deviceName, cancellationToken);
            if (value < options.MinimumWorldCoordinate || value > options.MaximumWorldCoordinate)
                throw new InvalidOperationException($"Coordinate {axis} value is outside configured world bounds.");
            return value;
        }

        private IList<CoordinateCandidate> CreateCoordinateCandidates(CoordinateEditTransaction transaction)
        {
            var candidates = new List<CoordinateCandidate>();
            int[] directionsX = { 1, -1, -1, 1 };
            int[] directionsY = { 1, 1, -1, -1 };
            int range = options.MaximumCoordinateOffset - options.MinimumCoordinateOffset;
            for (int attempt = 1; attempt <= options.CoordinateTerritoryAttempts; attempt++)
            {
                int magnitude = options.MinimumCoordinateOffset
                    + ((attempt - 1) * Math.Max(1, range) / Math.Max(1, options.CoordinateTerritoryAttempts - 1));
                int direction = (attempt - 1) % directionsX.Length;
                int targetX = transaction.OriginalX + (directionsX[direction] * magnitude);
                int targetY = transaction.OriginalY + (directionsY[direction] * magnitude);
                if (targetX < options.MinimumWorldCoordinate || targetX > options.MaximumWorldCoordinate
                    || targetY < options.MinimumWorldCoordinate || targetY > options.MaximumWorldCoordinate)
                    continue;
                if (targetX == transaction.OriginalX && targetY == transaction.OriginalY
                    || candidates.Any(c => c.TargetX == targetX && c.TargetY == targetY))
                    continue;
                candidates.Add(new CoordinateCandidate(candidates.Count + 1, targetX, targetY,
                    targetX - transaction.OriginalX, targetY - transaction.OriginalY));
            }
            return candidates;
        }

        private async Task<GameDetectionResult> ApplyCoordinateCandidateAsync(string deviceName,
            CoordinateEditTransaction transaction,
            CoordinateCandidate candidate, IList<NavigationTransition> transitions,
            CancellationToken cancellationToken)
        {
            await SetCoordinateValueVerifiedAsync(deviceName,
                "X", transaction.CurrentX, candidate.TargetX,
                transitions, cancellationToken);
            transaction.SetX(candidate.TargetX);
            await SetCoordinateValueVerifiedAsync(deviceName,
                "Y", transaction.CurrentY, candidate.TargetY,
                transitions, cancellationToken);
            transaction.SetY(candidate.TargetY);
            GameDetectionResult current = await DetectContinentMapAfterCoordinateEditAsync(deviceName, "Y", transitions,
                cancellationToken, true);
            GameDetectionEvidence submitPin = FindFreshEvidence(
                current, TemplateId.ContinentMapPinButton);
            if (submitPin == null)
                return current;

            await TapEvidenceAsync(deviceName, submitPin,
                "ContinentMapPinButtonAfterCoordinateEntry", transitions, cancellationToken);
            AddTransition(transitions, "CoordinateCandidateSubmitted",
                $"Submitted coordinate pair X={candidate.TargetX}; Y={candidate.TargetY} "
                + "after both fields were entered.");
            await Task.Delay(options.StatePollIntervalMs, cancellationToken);
            return await DetectContinentMapAfterCoordinateEditAsync(deviceName, "pin", transitions,
                cancellationToken, true);
        }

        private async Task SetCoordinateValueVerifiedAsync(string deviceName, string axis,
            int oldValue, int targetValue, IList<NavigationTransition> transitions,
            CancellationToken cancellationToken)
        {
            for (int verificationAttempt = 1; verificationAttempt <= options.CoordinateInputVerificationAttempts; verificationAttempt++)
            {
                GameDetectionResult fresh = await DetectContinentMapAfterCoordinateEditAsync(
                    deviceName, axis + " input", transitions, cancellationToken, true);
                GameDetectionEvidence pin = FindFreshEvidence(
                    fresh, TemplateId.ContinentMapPinButton);
                if (pin == null)
                    throw new InvalidOperationException(
                        $"Coordinate {axis} field cannot be derived without a fresh map-pin match.");
                int x = pin.MatchResult.CenterX
                    + (string.Equals(axis, "X", StringComparison.Ordinal)
                        ? CoordinateXOffsetFromPinCenterPx
                        : CoordinateYOffsetFromPinCenterPx);
                int y = pin.MatchResult.CenterY;
                if (x < 0 || x >= ExpectedScreenshotWidth
                    || y < 0 || y >= ExpectedScreenshotHeight)
                    throw new InvalidOperationException(
                        $"Derived ContinentMap coordinate {axis} field is outside the supported viewport.");
                await ldPlayerClient.TapAsync(deviceName, x, y, cancellationToken);
                await ReplaceFocusedCoordinateAsync(deviceName, oldValue, targetValue, cancellationToken);
                int observed = await focusedInputValueReader.ReadFocusedIntegerAsync(deviceName, cancellationToken);
                AddTransition(transitions, "CoordinateInputVerified",
                    $"Axis={axis}; ExpectedValue={targetValue}; ObservedValue={observed}; "
                    + $"VerificationAttempt={verificationAttempt}; Submission=PendingPinTap.");
                if (observed == targetValue)
                    return;
            }
            throw new InvalidOperationException($"Coordinate {axis} input could not be verified.");
        }

        private async Task<CoordinateRollbackResult> RollbackCoordinatesAsync(string deviceName,
            CoordinateEditTransaction transaction, GameDetectionEvidence pin,
            IList<NavigationTransition> transitions, CancellationToken cancellationToken)
        {
            if (!transaction.IsDirty || transaction.IsCommitted)
                return CoordinateRollbackResult.NotRequired();
            if (!HasValidBounds(pin))
                return CoordinateRollbackResult.VerificationUnavailable(null);
            var watch = Stopwatch.StartNew();
            try
            {
                GameDetectionResult current = null;
                GameDetectionEvidence currentPin = pin;
                if (transaction.XChanged)
                {
                    await SetCoordinateValueVerifiedAsync(deviceName,
                        "X", transaction.CurrentX, transaction.OriginalX, transitions, cancellationToken);
                    current = await DetectContinentMapAfterCoordinateEditAsync(
                        deviceName, "X rollback", transitions, cancellationToken, true);
                    currentPin = FindFreshEvidence(current, TemplateId.ContinentMapPinButton);
                    if (currentPin == null)
                        return CoordinateRollbackResult.VerificationUnavailable(current);
                }
                if (transaction.YChanged)
                {
                    await SetCoordinateValueVerifiedAsync(deviceName,
                        "Y", transaction.CurrentY, transaction.OriginalY, transitions, cancellationToken);
                    current = await DetectContinentMapAfterCoordinateEditAsync(
                        deviceName, "Y rollback", transitions, cancellationToken, true);
                    currentPin = FindFreshEvidence(current, TemplateId.ContinentMapPinButton);
                    if (currentPin == null)
                        return CoordinateRollbackResult.VerificationUnavailable(current);
                }

                await TapEvidenceAsync(deviceName, currentPin,
                    "ContinentMapPinButtonAfterCoordinateRollback", transitions, cancellationToken);
                await Task.Delay(options.StatePollIntervalMs, cancellationToken);
                current = await DetectContinentMapAfterCoordinateEditAsync(
                    deviceName, "rollback pin", transitions, cancellationToken, true);
                if (FindFreshEvidence(current, TemplateId.ContinentMapPinButton) == null)
                    return CoordinateRollbackResult.VerificationUnavailable(current);
                transaction.Restored();
                AddTransition(transitions, "CoordinateRollback",
                    $"RollbackStatus=RestoredAndVerified; RollbackDurationMs={watch.ElapsedMilliseconds}.");
                return CoordinateRollbackResult.Restored(current);
            }
            catch (Exception exception) when (!(exception is OperationCanceledException))
            {
                AddTransition(transitions, "CoordinateRollback", "RollbackStatus=RestorationFailed; " + exception.Message);
                return CoordinateRollbackResult.Failed(null);
            }
        }

        private async Task<CoordinateRollbackResult> RollbackWithCleanupTokenAsync(string deviceName,
            CoordinateEditTransaction transaction, GameDetectionEvidence pin,
            IList<NavigationTransition> transitions)
        {
            using (var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(options.CoordinateRollbackTimeoutSeconds)))
                return await RollbackCoordinatesAsync(deviceName, transaction, pin, transitions, cleanup.Token);
        }

        private async Task<NavigationResult> FailCoordinateTransactionAsync(string deviceName,
            GameDetectionResult initial, GameDetectionResult current, int attempts, Stopwatch watch,
            CoordinateEditTransaction transaction, GameDetectionEvidence pin, string message,
            IList<NavigationTransition> transitions)
        {
            CoordinateRollbackResult rollback = await RollbackWithCleanupTokenAsync(deviceName,
                transaction, pin, transitions);
            return rollback.Status == CoordinateRollbackStatus.RestoredAndVerified
                ? Result(false, initial, rollback.Latest ?? current, attempts, watch, message, null, transitions)
                : UnsafeRollbackResult(initial, rollback.Latest ?? current, attempts, watch, rollback, transitions);
        }

        private NavigationResult UnsafeRollbackResult(GameDetectionResult initial, GameDetectionResult current,
            int attempts, Stopwatch watch, CoordinateRollbackResult rollback,
            IList<NavigationTransition> transitions)
        {
            AddTransition(transitions, "CoordinateRollback",
                $"RollbackStatus={rollback.Status}; ExitReason=Unsafe.");
            return Result(false, initial, current, attempts, watch,
                "Không thể xác nhận đã khôi phục tọa độ ban đầu. Thiết bị được dừng để tránh di chuyển sai vị trí.",
                null, transitions);
        }

        private async Task<CoordinateEdit> AddCoordinateOffsetAsync(
            string deviceName,
            int x,
            int y,
            string axis,
            int attempt,
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
            int offset = NextCoordinateOffset(currentValue, attempt, axis);
            int targetValue = checked(currentValue + offset);
            await ReplaceFocusedCoordinateAsync(
                deviceName, currentValue, targetValue, cancellationToken);
            AddTransition(transitions, "CoordinateCandidateInput",
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
                deviceName, "Y rollback", transitions, cancellationToken, true);
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
            AddTransition(transitions, "CoordinateCandidateRollback",
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

        private async Task<TerritoryColorGroup> ResolveHomeTerritoryAsync(
            string deviceName,
            GameDetectionEvidence homePin,
            IList<NavigationTransition> transitions,
            CancellationToken cancellationToken)
        {
            for (int attempt = 1;
                attempt <= options.HomeTerritoryClassificationAttempts;
                attempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                byte[] screenshot = await ldPlayerClient.CaptureScreenshotPngAsync(
                    deviceName, cancellationToken);
                GameDetectionResult latest = detector.Detect(screenshot);
                GameDetectionEvidence resolvedPin = FindFreshEvidence(
                    latest, TemplateId.ContinentMapHomeLocationPin) ?? homePin;
                if (!HasValidBounds(resolvedPin))
                {
                    if (!TryLocateHomeLocationPin(screenshot, out resolvedPin))
                    {
                        AddTransition(transitions, "HomeTerritoryClassification",
                            $"Attempt {attempt}/{options.HomeTerritoryClassificationAttempts}: "
                            + "could not locate one unique cyan home pin from fresh pixels.");
                        if (attempt < options.HomeTerritoryClassificationAttempts)
                            await Task.Delay(options.StatePollIntervalMs, cancellationToken);
                        continue;
                    }

                    AddTransition(transitions, "Detect",
                        "Located the cyan home pin from foreground pixels after the "
                        + "background-sensitive template was unavailable.");
                }

                TerritoryColorGroup group;
                bool classified = TryClassifyPinTerritory(
                    screenshot, resolvedPin, out group);
                AddTransition(transitions, "HomeTerritoryClassification",
                    classified
                        ? $"Attempt {attempt}: classified the background near the home-pin base as {group}."
                        : $"Attempt {attempt}/{options.HomeTerritoryClassificationAttempts}: home territory remained low-confidence.");
                if (classified) return group;
                if (attempt < options.HomeTerritoryClassificationAttempts)
                    await Task.Delay(options.StatePollIntervalMs, cancellationToken);
            }

            AddTransition(transitions, "TerritoryColor",
                "Could not classify the home territory confidently after the bounded fresh-frame attempts.");
            return TerritoryColorGroup.Unknown;
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
                    $"Candidate={candidate}/{options.CoordinateTerritoryAttempts}; "
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
                    $"Candidate={candidate}/{options.CoordinateTerritoryAttempts}; "
                    + $"X={destinationX}; Y={destinationY}; Home={homeTerritory}; "
                    + $"Destination=Unknown; Result=Blocked; Source={source}; "
                    + "Reason=LowConfidence.");
                return TerritoryValidation.Blocked(latest,
                    "Coordinate fallback stopped because the destination territory color "
                    + "could not be classified confidently; no move Tap was sent.");
            }

            AddTerritoryColorProgress(transitions, progress,
                $"Candidate={candidate}/{options.CoordinateTerritoryAttempts}; "
                + $"X={destinationX}; Y={destinationY}; Home={homeTerritory}; "
                + $"Destination={destination}; "
                + $"Result={(homeTerritory == destination ? "Match" : "Different")}; "
                + $"Source={source}.");
            if (homeTerritory != destination)
                return TerritoryValidation.Blocked(latest,
                    $"Điểm mới khác màu lãnh thổ nhà ({homeTerritory}/{destination}); "
                    + "đã dừng để tránh di chuyển sang lãnh thổ khác.");

            return TerritoryValidation.Permitted(latest, movePin,
                $"The fallback X/Y destination belongs to the {homeTerritory} territory group.");
        }

        private static bool TryLocateYellowDestinationPin(
            byte[] screenshotPng,
            out GameDetectionEvidence destinationPin)
        {
            return TryLocateColoredPin(screenshotPng,
                IsYellowDestinationPinPixel,
                TemplateId.ContinentMapSearchTargetPin,
                "Yellow X/Y destination pin", out destinationPin);
        }

        private static bool TryLocateHomeLocationPin(
            byte[] screenshotPng,
            out GameDetectionEvidence homePin)
        {
            return TryLocateColoredPin(screenshotPng,
                IsCyanHomePinPixel,
                TemplateId.ContinentMapHomeLocationPin,
                "Cyan home pin", out homePin,
                minimumPixels: 14,
                minimumWidth: 5,
                minimumHeight: 12);
        }

        private static bool TryLocateColoredPin(
            byte[] screenshotPng,
            Func<Color, bool> isPinPixel,
            TemplateId templateId,
            string label,
            out GameDetectionEvidence destinationPin,
            int minimumPixels = MinimumDestinationPinPixels,
            int minimumWidth = MinimumDestinationPinWidthPx,
            int minimumHeight = MinimumDestinationPinHeightPx)
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
                                || !isPinPixel(bitmap.GetPixel(x, y)))
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
                                            || !isPinPixel(bitmap.GetPixel(
                                                nextX, nextY)))
                                            continue;
                                        visited[next] = true;
                                        queue.Enqueue(next);
                                    }
                                }
                            }

                            int width = maximumX - minimumX + 1;
                            int height = maximumY - minimumY + 1;
                            if (pixels < minimumPixels
                                || pixels > MaximumDestinationPinPixels
                                || width < minimumWidth
                                || width > MaximumDestinationPinWidthPx
                                || height < minimumHeight
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
                        TemplateId = templateId,
                        TemplateExists = true,
                        Found = true,
                        MatchResult = ImageMatchResult.FoundAt(
                            best.X, best.Y, best.Width, best.Height),
                        Message = label + " located from foreground pixels."
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

        private static bool IsCyanHomePinPixel(Color pixel)
        {
            return pixel.G >= 165
                && pixel.B >= 135
                && pixel.R <= 175
                && pixel.G - pixel.R >= 45
                && pixel.B - pixel.R >= 30
                && Math.Abs(pixel.G - pixel.B) <= 95;
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

        private static bool TryClassifyPinTerritory(
            Bitmap bitmap,
            GameDetectionEvidence pin,
            out TerritoryColorGroup group)
        {
            group = TerritoryColorGroup.Unknown;
            if (bitmap == null || !HasValidBounds(pin)) return false;

            ImageMatchResult bounds = pin.MatchResult;
            int sampleY = bounds.Y + bounds.Height - PinBackgroundHeightPx;
            var regions = new[]
            {
                new ImageRegion(bounds.X - PinBackgroundSideGapPx
                    - PinBackgroundSideWidthPx, sampleY,
                    PinBackgroundSideWidthPx, PinBackgroundHeightPx),
                new ImageRegion(bounds.X + bounds.Width
                    + PinBackgroundSideGapPx, sampleY,
                    PinBackgroundSideWidthPx, PinBackgroundHeightPx)
            };
            return TryClassifyTerritoryRegions(bitmap, regions, out group);
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

        private static bool TryClassifyTerritoryRegions(
            Bitmap bitmap,
            IEnumerable<ImageRegion> regions,
            out TerritoryColorGroup group)
        {
            group = TerritoryColorGroup.Unknown;
            if (bitmap == null || regions == null) return false;
            try
            {
                int sampled = 0;
                int classifiable = 0;
                var counts = new int[5];
                foreach (ImageRegion region in regions)
                {
                    int left = Math.Max(0, region.X);
                    int top = Math.Max(0, region.Y);
                    int right = Math.Min(bitmap.Width, region.X + region.Width);
                    int bottom = Math.Min(bitmap.Height, region.Y + region.Height);
                    if (right <= left || bottom <= top) continue;

                    for (int y = top; y < bottom; y++)
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
                        runnerUp = counts[index];
                }

                int winner = counts[winnerIndex];
                if (winner < classifiable * MinimumWinningGroupRatio
                    || winner - runnerUp < classifiable * MinimumWinningMarginRatio)
                    return false;

                group = (TerritoryColorGroup)(winnerIndex + 1);
                return true;
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

        private int NextCoordinateOffset(int currentValue, int attempt, string axis)
        {
            int range = options.MaximumCoordinateOffset
                - options.MinimumCoordinateOffset + 1;
            int axisSalt = string.Equals(axis, "Y", StringComparison.Ordinal) ? 17 : 0;
            int magnitude = options.MinimumCoordinateOffset
                + (((attempt - 1) * 37 + axisSalt) % range);
            int quadrant = (attempt - 1) % 4;
            bool subtract = string.Equals(axis, "X", StringComparison.Ordinal)
                ? quadrant == 1 || quadrant == 2
                : quadrant == 2 || quadrant == 3;
            return subtract && currentValue > magnitude ? -magnitude : magnitude;
        }

        private async Task<GameDetectionResult> DetectContinentMapAfterCoordinateEditAsync(
            string deviceName,
            string axis,
            IList<NavigationTransition> transitions,
            CancellationToken cancellationToken,
            bool waitForCoordinatePin = false)
        {
            var settleWatch = Stopwatch.StartNew();
            GameDetectionResult result;
            do
            {
                result = await DetectAsync(
                    deviceName, transitions, cancellationToken);
                if (result != null && result.IsSuccessful
                    && result.State == GameState.Unknown
                    && IsVerifiedContinentMapEvidence(result))
                {
                    result.State = GameState.ContinentMap;
                    AddTransition(transitions, "Detect",
                        $"Normalized Unknown to ContinentMap after coordinate {axis} edit.");
                }

                if (!waitForCoordinatePin
                    || result == null
                    || !result.IsSuccessful
                    || result.State != GameState.ContinentMap
                    || FindFreshEvidence(result, TemplateId.ContinentMapPinButton) != null)
                    return result;

                await Task.Delay(options.StatePollIntervalMs, cancellationToken);
            }
            while (settleWatch.ElapsedMilliseconds
                < options.CoordinateCandidateSettleTimeoutMs);

            AddTransition(transitions, "CoordinateCandidateSettling",
                $"Coordinate {axis} did not expose a fresh move pin within "
                + $"{options.CoordinateCandidateSettleTimeoutMs} ms.");
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

        private sealed class HomeLocationEvidence
        {
            public HomeLocationEvidence(GameDetectionResult latest,
                GameDetectionEvidence pin, string source)
            {
                Latest = latest;
                Pin = pin;
                Source = source;
            }

            public GameDetectionResult Latest { get; }
            public GameDetectionEvidence Pin { get; }
            public string Source { get; }
        }

        private enum CoordinateSearchStatus
        {
            Succeeded,
            NoMatchingCandidate,
            Unavailable,
            Unsafe,
            Failed
        }

        private sealed class CoordinateSearchResult
        {
            private CoordinateSearchResult(CoordinateSearchStatus status,
                NavigationResult navigation)
            {
                Status = status;
                Navigation = navigation;
            }

            public CoordinateSearchStatus Status { get; }
            public NavigationResult Navigation { get; }
            public static CoordinateSearchResult Succeeded(NavigationResult result) =>
                new CoordinateSearchResult(CoordinateSearchStatus.Succeeded, result);
            public static CoordinateSearchResult NoMatchingCandidate(NavigationResult result) =>
                new CoordinateSearchResult(CoordinateSearchStatus.NoMatchingCandidate, result);
            public static CoordinateSearchResult Unavailable(NavigationResult result) =>
                new CoordinateSearchResult(CoordinateSearchStatus.Unavailable, result);
            public static CoordinateSearchResult Unsafe(NavigationResult result) =>
                new CoordinateSearchResult(CoordinateSearchStatus.Unsafe, result);
            public static CoordinateSearchResult Failed(NavigationResult result) =>
                new CoordinateSearchResult(CoordinateSearchStatus.Failed, result);
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

        private sealed class CoordinateCandidate
        {
            public CoordinateCandidate(int attempt, int targetX, int targetY, int deltaX, int deltaY)
            {
                Attempt = attempt;
                TargetX = targetX;
                TargetY = targetY;
                DeltaX = deltaX;
                DeltaY = deltaY;
            }

            public int Attempt { get; }
            public int TargetX { get; }
            public int TargetY { get; }
            public int DeltaX { get; }
            public int DeltaY { get; }
        }

        private sealed class ScreenTerritoryCandidate
        {
            public ScreenTerritoryCandidate(int x, int y, int offsetIndex,
                int ringPriority, int sameColorVotes, int distanceFromHome)
            {
                X = x;
                Y = y;
                OffsetIndex = offsetIndex;
                RingPriority = ringPriority;
                SameColorVotes = sameColorVotes;
                DistanceFromHome = distanceFromHome;
            }

            public int X { get; }
            public int Y { get; }
            public int OffsetIndex { get; }
            public int RingPriority { get; }
            public int SameColorVotes { get; }
            public int DistanceFromHome { get; }
        }

        private sealed class CoordinateEditTransaction
        {
            public CoordinateEditTransaction(int originalX, int originalY)
            {
                OriginalX = CurrentX = originalX;
                OriginalY = CurrentY = originalY;
            }

            public int OriginalX { get; }
            public int OriginalY { get; }
            public int CurrentX { get; private set; }
            public int CurrentY { get; private set; }
            public bool XChanged { get; private set; }
            public bool YChanged { get; private set; }
            public bool IsDirty => XChanged || YChanged;
            public bool IsCommitted { get; private set; }

            public void SetX(int value) { CurrentX = value; XChanged = value != OriginalX; }
            public void SetY(int value) { CurrentY = value; YChanged = value != OriginalY; }
            public void Restored() { CurrentX = OriginalX; CurrentY = OriginalY; XChanged = YChanged = false; }
            public void Commit() { IsCommitted = true; }
        }

        private enum CoordinateRollbackStatus
        {
            NotRequired,
            RestoredAndVerified,
            RestorationFailed,
            VerificationUnavailable
        }

        private sealed class CoordinateRollbackResult
        {
            private CoordinateRollbackResult(CoordinateRollbackStatus status, GameDetectionResult latest)
            {
                Status = status;
                Latest = latest;
            }

            public CoordinateRollbackStatus Status { get; }
            public GameDetectionResult Latest { get; }
            public static CoordinateRollbackResult NotRequired() => new CoordinateRollbackResult(CoordinateRollbackStatus.NotRequired, null);
            public static CoordinateRollbackResult Restored(GameDetectionResult latest) => new CoordinateRollbackResult(CoordinateRollbackStatus.RestoredAndVerified, latest);
            public static CoordinateRollbackResult Failed(GameDetectionResult latest) => new CoordinateRollbackResult(CoordinateRollbackStatus.RestorationFailed, latest);
            public static CoordinateRollbackResult VerificationUnavailable(GameDetectionResult latest) => new CoordinateRollbackResult(CoordinateRollbackStatus.VerificationUnavailable, latest);
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

        private enum ResourceSearchPanelEvidenceState
        {
            None,
            Partial,
            Confirmed
        }

        private sealed class PanelOpenObservation
        {
            public PanelOpenObservation(GameDetectionResult lastFrame,
                ResourceSearchPanelEvidenceState evidenceState, int partialFrames,
                int confirmedFrames, int observedFrames, string failureReason)
            {
                LastFrame = lastFrame;
                EvidenceState = evidenceState;
                PartialFrames = partialFrames;
                ConfirmedFrames = confirmedFrames;
                ObservedFrames = observedFrames;
                FailureReason = failureReason;
            }

            public GameDetectionResult LastFrame { get; }
            public ResourceSearchPanelEvidenceState EvidenceState { get; }
            public int PartialFrames { get; }
            public int ConfirmedFrames { get; }
            public int ObservedFrames { get; }
            public string FailureReason { get; }
            public bool Confirmed => EvidenceState == ResourceSearchPanelEvidenceState.Confirmed;
        }

        private sealed class PanelRetryRecovery
        {
            public PanelRetryRecovery(bool success, GameDetectionResult lastFrame,
                string failureReason)
            {
                Success = success;
                LastFrame = lastFrame;
                FailureReason = failureReason;
            }

            public bool Success { get; }
            public GameDetectionResult LastFrame { get; }
            public string FailureReason { get; }
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
            ResourceSearchPanelReadinessResult readiness =
                ResourceSearchPanelReadinessVerifier.Evaluate(result);
            return readiness.IsReady;
        }

        private async Task<ResourceSearchPanelScreenshotProbe> ProbeResourceSearchPanelAsync(
            string deviceName,
            IList<NavigationTransition> transitions,
            CancellationToken cancellationToken)
        {
            ImageMatchResult previousPositiveSearchButtonBounds = null;
            int consecutivePositiveFrames = 0;
            ResourceSearchPanelScreenshotProbe probe = new ResourceSearchPanelScreenshotProbe();
            // A single fresh Search-button ROI observation is authoritative. The
            // caller owns the one bounded panel-open retry when it is absent.
            for (int frameNumber = 1; frameNumber <= 1; frameNumber++)
            {
                if (frameNumber > 1)
                    await Task.Delay(options.StatePollIntervalMs, cancellationToken);

                GameDetectionResult frame = await DetectAsync(deviceName, transitions, cancellationToken);
                probe.LastFrame = frame;
                GameDetectionEvidence search = FindFreshEvidence(frame, TemplateId.SearchButtonEnabled);
                bool cityFound = HasEvidence(frame, TemplateId.CityToWorldMapButton);
                bool anchorFound = HasEvidence(frame, TemplateId.ResourceSearchPanelAnchor);
                probe.CityDetected = cityFound;
                probe.PanelEvidenceVisible = anchorFound || search != null;

                if (cityFound && (search == null || !search.SearchRegion.HasValue
                    || !IsInside(search.MatchResult, search.SearchRegion.Value)))
                {
                    probe.FailureReason = "CityDetectedDuringPanelOpen";
                    LogSearchPanelProbe(deviceName, frameNumber, anchorFound, search,
                        previousPositiveSearchButtonBounds, false, probe, consecutivePositiveFrames,
                        "None", "Stop", probe.FailureReason);
                    return probe;
                }

                if (search == null || !search.SearchRegion.HasValue
                    || !IsInside(search.MatchResult, search.SearchRegion.Value))
                {
                    previousPositiveSearchButtonBounds = null;
                    consecutivePositiveFrames = 0;
                    probe.SearchButtonVisible = false;
                    probe.ExpectedRegion = search != null && search.SearchRegion.HasValue;
                    probe.InsideExpectedRegion = false;
                    LogSearchPanelProbe(deviceName, frameNumber, anchorFound, search,
                        previousPositiveSearchButtonBounds, false, probe, consecutivePositiveFrames,
                        "None", probe.PanelEvidenceVisible ? "Continue" : "RetryOrFail",
                        search == null ? string.Empty : "SearchButtonOutsideExpectedRegion");
                    continue;
                }

                probe.SearchButtonVisible = true;
                probe.ConfirmationFrames++;
                probe.ExpectedRegion = true;
                probe.InsideExpectedRegion = true;
                probe.SearchButtonBounds = search.MatchResult;
                bool comparisonPerformed = previousPositiveSearchButtonBounds != null;
                bool stable = comparisonPerformed
                    && AreStable(previousPositiveSearchButtonBounds, search.MatchResult);
                probe.BoundsComparisonPerformed = comparisonPerformed;
                probe.BoundsStable = stable;
                if (stable)
                    consecutivePositiveFrames++;
                else
                    consecutivePositiveFrames = 1;
                probe.ConsecutivePositiveFrames = consecutivePositiveFrames;

                probe.Confirmed = true;
                probe.ConfirmationMode = "SearchButtonOnly";
                probe.FailureReason = null;
                LogSearchPanelProbe(deviceName, frameNumber, anchorFound, search,
                    previousPositiveSearchButtonBounds, comparisonPerformed, probe,
                    consecutivePositiveFrames, probe.ConfirmationMode,
                    "ContinueResourceConfiguration", string.Empty);
                return probe;

            }

            if (probe.ConfirmationFrames == 0)
                probe.FailureReason = "SearchButtonNotFoundInExpectedRegion";
            else if (!probe.BoundsComparisonPerformed)
                probe.FailureReason = "SearchButtonNeedsSecondPositiveFrame";
            else if (!probe.BoundsStable)
                probe.FailureReason = "SearchButtonBoundsNotStable";
            return probe;
        }

        private static bool IsInside(ImageMatchResult match, ImageRegion region)
        {
            return match != null && match.Found && match.Width > 0 && match.Height > 0
                && match.X >= region.X && match.Y >= region.Y
                && match.X + match.Width <= region.X + region.Width
                && match.Y + match.Height <= region.Y + region.Height;
        }

        private static bool AreStable(ImageMatchResult left, ImageMatchResult right)
        {
            return left != null && right != null
                && Math.Abs(left.CenterX - right.CenterX) <= 8
                && Math.Abs(left.CenterY - right.CenterY) <= 8
                && Math.Abs(left.Width - right.Width) <= 10
                && Math.Abs(left.Height - right.Height) <= 10;
        }

        private void LogSearchPanelProbe(string deviceName, int screenshotIndex,
            bool anchorFound, GameDetectionEvidence search,
            ImageMatchResult previousBounds, bool comparisonPerformed,
            ResourceSearchPanelScreenshotProbe probe, int consecutivePositiveFrames,
            string confirmationMode, string nextAction, string failureReason)
        {
            string bounds = search?.MatchResult != null
                ? $"({search.MatchResult.X},{search.MatchResult.Y},{search.MatchResult.Width},{search.MatchResult.Height})"
                : string.Empty;
            string previous = previousBounds != null
                ? $"({previousBounds.X},{previousBounds.Y},{previousBounds.Width},{previousBounds.Height})"
                : string.Empty;
            logger.Info($"[Resource Search Panel Screenshot Probe] DeviceName='{deviceName}', ScreenshotIndex={screenshotIndex}, ResourceSearchPanelAnchorFound={anchorFound}, SearchButtonFound={search != null}, SearchButtonBounds='{bounds}', PreviousSearchButtonBounds='{previous}', BoundsComparisonPerformed={comparisonPerformed}, SearchButtonBoundsStable={(comparisonPerformed ? probe.BoundsStable.ToString() : string.Empty)}, ConsecutivePositiveFrames={consecutivePositiveFrames}, SearchButtonInsideExpectedRegion={probe.InsideExpectedRegion}, CityDetected={probe.CityDetected}, ConfirmationMode='{confirmationMode}', ScreenshotConfirmed={probe.Confirmed}, NextAction='{nextAction}', FailureReason='{failureReason ?? string.Empty}'");
        }

        private static void ApplySearchPanelProbe(
            NavigationResult result, ResourceSearchPanelScreenshotProbe probe, int tapCount)
        {
            result.ScreenshotConfirmed = probe.Confirmed;
            result.ConfirmationFrames = probe.ConfirmationFrames;
            result.SearchButtonBounds = probe.SearchButtonBounds;
            result.SearchButtonBoundsStable = probe.BoundsStable;
            result.SearchButtonBoundsComparisonPerformed = probe.BoundsComparisonPerformed;
            result.SearchButtonExpectedRegion = probe.ExpectedRegion;
            result.SearchButtonInsideExpectedRegion = probe.InsideExpectedRegion;
            result.ConfirmationMode = probe.ConfirmationMode;
            result.SearchIconTapCount = tapCount;
            result.BackCount = 0;
        }

        private sealed class ResourceSearchPanelScreenshotProbe
        {
            public GameDetectionResult LastFrame { get; set; }
            public ImageMatchResult SearchButtonBounds { get; set; }
            public int ConfirmationFrames { get; set; }
            public bool Confirmed { get; set; }
            public bool BoundsStable { get; set; }
            public bool BoundsComparisonPerformed { get; set; }
            public int ConsecutivePositiveFrames { get; set; }
            public bool ExpectedRegion { get; set; }
            public bool InsideExpectedRegion { get; set; }
            public bool SearchButtonVisible { get; set; }
            public bool PanelEvidenceVisible { get; set; }
            public bool CityDetected { get; set; }
            public string ConfirmationMode { get; set; }
            public string FailureReason { get; set; }
        }

        private async Task<PanelOpenObservation> ObserveResourceSearchPanelAsync(
            string deviceName, int attempt,
            IList<NavigationTransition> transitions,
            CancellationToken cancellationToken)
        {
            const int maxFrames = 4;
            GameDetectionResult last = null;
            ResourceSearchPanelEvidenceState lastState = ResourceSearchPanelEvidenceState.None;
            int partialFrames = 0;
            int confirmedFrames = 0;
            int consecutivePartialFrames = 0;
            int consecutiveConfirmedFrames = 0;
            int observedFrames = 0;
            for (int frameIndex = 1; frameIndex <= maxFrames; frameIndex++)
            {
                observedFrames = frameIndex;
                await Task.Delay(options.StatePollIntervalMs, cancellationToken);
                last = await DetectAsync(deviceName, transitions, cancellationToken);
                lastState = ClassifyResourceSearchPanelEvidence(last);
                if (lastState == ResourceSearchPanelEvidenceState.Partial)
                {
                    partialFrames++;
                    consecutivePartialFrames++;
                    consecutiveConfirmedFrames = 0;
                }
                else if (lastState == ResourceSearchPanelEvidenceState.Confirmed)
                {
                    confirmedFrames++;
                    consecutiveConfirmedFrames++;
                    consecutivePartialFrames = 0;
                }
                else
                {
                    consecutivePartialFrames = 0;
                    consecutiveConfirmedFrames = 0;
                }
                bool worldMapFound = HasEvidence(last, TemplateId.WorldMapAnchor);
                bool anchorConfirmed = HasEvidence(last, TemplateId.ResourceSearchPanelAnchor);
                bool confirmed = lastState == ResourceSearchPanelEvidenceState.Confirmed
                    && (anchorConfirmed || consecutiveConfirmedFrames >= 2);
                LogPanelOpenObservation(deviceName, attempt, frameIndex, last,
                    lastState, consecutiveConfirmedFrames, consecutivePartialFrames,
                    confirmed
                        ? "Success" : "ContinuePolling", null);
                if (confirmed)
                    return new PanelOpenObservation(last, lastState, partialFrames,
                        confirmedFrames, frameIndex, "");

                // A verified WorldMap frame means the tap did not apply. It is safe
                // to stop this observation and reacquire the map anchor if needed.
                if (worldMapFound && last.State == GameState.WorldMap)
                    break;
            }

            string reason = lastState == ResourceSearchPanelEvidenceState.Partial
                ? "ResourceSearchPanelPartialEvidenceTimeout"
                : "ResourceSearchPanelNotOpened";
            LogPanelOpenObservation(deviceName, attempt, observedFrames, last, lastState,
                consecutiveConfirmedFrames, consecutivePartialFrames, "RetryOrFail", reason);
            return new PanelOpenObservation(last, lastState, partialFrames,
                confirmedFrames, observedFrames, reason);
        }

        private async Task<PanelRetryRecovery> RecoverWorldMapForPanelRetryAsync(
            string deviceName, GameDetectionResult current,
            PanelOpenObservation observation,
            IList<NavigationTransition> transitions,
            CancellationToken cancellationToken)
        {
            GameDetectionResult latest = current;
            bool verifiedMap = IsFreshWorldMapAnchor(latest);
            bool backSent = false;
            GameDetectionResult stateAfterBack = latest;
            GameDetectionEvidence cleanupButton = null;
            bool cancelFound = HasEvidence(latest, TemplateId.StorageLimitCancelButton);
            bool resourceExpiryFound = HasEvidence(latest, TemplateId.ResourceExpiryDialogAnchor);
            bool mapPinFound = HasEvidence(latest, TemplateId.WorldMapPinButton);
            if (!verifiedMap)
            {
                // Back is safe only when the failed attempt left panel evidence. An
                // unrelated Unknown frame must not trigger blind navigation input.
                bool panelEvidence = observation != null
                    && observation.EvidenceState != ResourceSearchPanelEvidenceState.None;
                if (!panelEvidence)
                {
                    LogPanelRetryRecovery(deviceName, observation, false, latest, latest,
                        false, cancelFound, resourceExpiryFound, mapPinFound, false,
                        null, "WorldMapRecoveryBeforePanelRetryFailed");
                    return new PanelRetryRecovery(false, latest,
                        "WorldMapRecoveryBeforePanelRetryFailed");
                }

                await ldPlayerClient.BackAsync(deviceName, cancellationToken);
                backSent = true;
                AddTransition(transitions, "Back",
                    "Sent one bounded Back command before retrying ResourceSearchPanel.");
                stateAfterBack = await DetectAsync(deviceName, transitions, cancellationToken);
                cleanupButton = FindFreshEvidence(stateAfterBack,
                    TemplateId.StorageLimitCancelButton);
                // Reuse the existing lock-free core. It owns the supported
                // blocking-dialog predicate and bounded post-cleanup polling.
                NavigationResult recovery = await EnsureWorldMapCoreAsync(
                    deviceName, stateAfterBack, cancellationToken);
                foreach (NavigationTransition transition in recovery.Transitions)
                    transitions.Add(transition);
                latest = DetectionFrom(recovery);
                verifiedMap = recovery.Success && IsFreshWorldMapAnchor(latest);
                cancelFound = HasEvidence(latest, TemplateId.StorageLimitCancelButton)
                    || cancelFound;
                resourceExpiryFound = HasEvidence(latest, TemplateId.ResourceExpiryDialogAnchor)
                    || resourceExpiryFound;
                mapPinFound = HasEvidence(latest, TemplateId.WorldMapPinButton)
                    || mapPinFound;
                string recoveryFailure = recovery.FailureReason
                    ?? (verifiedMap ? null : recovery.Success
                        ? "WorldMapNotVerifiedAfterBlockingDialogCleanup"
                        : "WorldMapRecoveryBeforePanelRetryFailed");
                LogPanelRetryRecovery(deviceName, observation, backSent, stateAfterBack, latest,
                    recovery.Success, cancelFound, resourceExpiryFound, mapPinFound,
                    cleanupButton != null && mapPinFound, cleanupButton, recoveryFailure);
                if (!recovery.Success)
                    return new PanelRetryRecovery(false, latest, recoveryFailure);
            }

            if (!verifiedMap)
            {
                LogPanelRetryRecovery(deviceName, observation, backSent, stateAfterBack, latest,
                    false, cancelFound, resourceExpiryFound, mapPinFound,
                    cleanupButton != null && mapPinFound, cleanupButton,
                    "WorldMapNotVerifiedAfterBlockingDialogCleanup");
                return new PanelRetryRecovery(false, latest,
                    "WorldMapNotVerifiedAfterBlockingDialogCleanup");
            }

            return new PanelRetryRecovery(true, latest, null);
        }

        private static ResourceSearchPanelEvidenceState ClassifyResourceSearchPanelEvidence(
            GameDetectionResult result)
        {
            ResourceSearchPanelReadinessResult readiness =
                ResourceSearchPanelReadinessVerifier.Evaluate(result);
            if (readiness.Status == ResourceSearchPanelReadiness.Ready)
                return ResourceSearchPanelEvidenceState.Confirmed;
            if (readiness.Status == ResourceSearchPanelReadiness.Animating)
                return ResourceSearchPanelEvidenceState.Partial;
            return ResourceSearchPanelEvidenceState.None;
        }

        private static bool IsFreshWorldMapAnchor(GameDetectionResult result) =>
            result != null && result.IsSuccessful && result.State == GameState.WorldMap
                && HasEvidence(result, TemplateId.WorldMapAnchor);

        private static bool HasEvidence(GameDetectionResult result, TemplateId templateId) =>
            result?.Evidence != null && result.Evidence.Any(item =>
                item.TemplateId == templateId && item.Found
                && (item.MatchResult == null || (item.MatchResult.Width > 0 && item.MatchResult.Height > 0)));

        private void LogPanelOpenObservation(string deviceName, int attempt, int frameIndex,
            GameDetectionResult frame, ResourceSearchPanelEvidenceState evidenceState,
            int confirmedFrames, int partialFrames, string nextAction, string failureReason)
        {
            logger.Info($"[Resource Search Panel Open Observation] DeviceName='{deviceName}', Attempt={attempt}, FrameIndex={frameIndex}, DetectedState='{frame?.State}', ResourceSearchPanelAnchorFound={HasEvidence(frame, TemplateId.ResourceSearchPanelAnchor)}, SearchButtonFound={HasEvidence(frame, TemplateId.SearchButtonEnabled)}, LevelMinusFound={HasEvidence(frame, TemplateId.LevelMinusButton)}, ResourceTabSelectedFound={HasEvidence(frame, TemplateId.ResourceTabSelected)}, ResourceTabUnselectedFound={HasEvidence(frame, TemplateId.ResourceTabUnselected)}, WorldMapAnchorFound={HasEvidence(frame, TemplateId.WorldMapAnchor)}, EvidenceState='{evidenceState}', ConsecutiveConfirmedFrames={confirmedFrames}, ConsecutivePartialFrames={partialFrames}, NextAction='{nextAction}', FailureReason='{failureReason ?? string.Empty}'");
        }

        private void LogPanelOpenAttempt(string deviceName, int attempt, int maxAttempts,
            GameDetectionEvidence anchor, int tapX, int tapY,
            PanelOpenObservation observation, string outcome, string failureReason)
        {
            logger.Info($"[Resource Search Panel Open Attempt] DeviceName='{deviceName}', Attempt={attempt}, MaxAttempts={maxAttempts}, FreshWorldMapAnchorBounds='{FormatBounds(anchor)}', TapX={tapX}, TapY={tapY}, TapSent=true, ObservedFrames={observation?.ObservedFrames ?? 0}, PartialFrames={observation?.PartialFrames ?? 0}, ConfirmedFrames={observation?.ConfirmedFrames ?? 0}, FinalState='{observation?.LastFrame?.State}', Outcome='{outcome}', NextAction='{(outcome == "RetryRequired" ? "RecoverWorldMapAndRetry" : "Stop")}', FailureReason='{failureReason ?? string.Empty}'");
        }

        private void LogPanelRetryRecovery(string deviceName,
            PanelOpenObservation observation, bool backSent,
            GameDetectionResult stateAfterBack, GameDetectionResult recoveredState,
            bool recoverySuccess,
            bool cancelFound, bool resourceExpiryFound, bool mapPinFound,
            bool cleanupTapSent, GameDetectionEvidence cleanupButton,
            string failureReason)
        {
            logger.Info($"[Resource Search Panel Retry Recovery] DeviceName='{deviceName}', CompletedAttempt=1, NextAttempt=2, BackSent={backSent}, StateAfterBack='{stateAfterBack?.State}', StorageLimitCancelFound={cancelFound}, ResourceExpiryDialogFound={resourceExpiryFound}, WorldMapPinFound={mapPinFound}, WorldMapAnchorFound={HasEvidence(recoveredState, TemplateId.WorldMapAnchor)}, BlockingDialogRecognized={cancelFound && mapPinFound}, BlockingDialogType='{(cancelFound && mapPinFound ? "WorldMapBlockingDialog" : string.Empty)}', BlockingDialogBounds='{FormatBounds(cleanupButton)}', CleanupTapSent={cleanupTapSent}, CleanupTapX={(cleanupTapSent && cleanupButton != null ? cleanupButton.MatchResult.CenterX.ToString() : string.Empty)}, CleanupTapY={(cleanupTapSent && cleanupButton != null ? cleanupButton.MatchResult.CenterY.ToString() : string.Empty)}, WorldMapRecoverySuccess={recoverySuccess}, FreshWorldMapAnchorBounds='{FormatBounds(recoveredState?.Evidence?.FirstOrDefault(item => item.TemplateId == TemplateId.WorldMapAnchor && item.Found))}', NextAction='{(recoverySuccess ? "RetryOpenResourceSearchPanel" : "Stop")}', FailureReason='{failureReason ?? string.Empty}'");
        }

        private async Task<GameDetectionResult> DetectAsync(string deviceName, IList<NavigationTransition> transitions, CancellationToken token)
        {
            GameDetectionResult result = await detector.DetectAsync(deviceName, token);
            AddTransition(transitions, "Detect", $"Detected {result.State}; success={result.IsSuccessful}.");
            return result;
        }

        private async Task<GameDetectionResult> RefreshFullFrameEvidenceAsync(
            string deviceName,
            IList<NavigationTransition> transitions,
            CancellationToken cancellationToken)
        {
            IFrameCapturingLdPlayerClient frameClient = ldPlayerClient as IFrameCapturingLdPlayerClient;
            IFrameGameStateDetector frameDetector = detector as IFrameGameStateDetector;
            if (frameClient == null || frameDetector == null)
            {
                byte[] screenshot = await ldPlayerClient.CaptureScreenshotPngAsync(
                    deviceName, cancellationToken);
                GameDetectionResult compatibleResult = detector.Detect(screenshot);
                AddTransition(transitions, "Detect",
                    $"Performed one bounded full-scan evidence refresh; detected "
                    + $"{compatibleResult.State}; success={compatibleResult.IsSuccessful}.");
                return compatibleResult;
            }

            using (CapturedFrame frame = await frameClient.CaptureFrameAsync(
                deviceName, cancellationToken))
            {
                GameDetectionResult result = frameDetector.Detect(
                    frame, deviceName, context: null);
                AddTransition(transitions, "Detect",
                    $"Performed one bounded full-frame evidence refresh; detected "
                    + $"{result.State}; success={result.IsSuccessful}.");
                return result;
            }
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

        private async Task<FrameResolutionResult> CaptureFrameResolutionAsync(
            string deviceName, CancellationToken cancellationToken)
        {
            var frameClient = ldPlayerClient as IFrameCapturingLdPlayerClient;
            if (frameClient != null)
            {
                using (CapturedFrame frame = await frameClient.CaptureFrameAsync(
                    deviceName, cancellationToken))
                    return new FrameResolutionResult(frame.Width, frame.Height);
            }

            byte[] png = await ldPlayerClient.CaptureScreenshotPngAsync(
                deviceName, cancellationToken);
            using (var stream = new MemoryStream(png, false))
            using (var image = Image.FromStream(stream, false, true))
                return new FrameResolutionResult(image.Width, image.Height);
        }

        private sealed class FrameResolutionResult
        {
            public FrameResolutionResult(int width, int height)
            { Width = width; Height = height; }
            public int Width { get; }
            public int Height { get; }
        }

        private static NavigationResult Result(bool success, GameDetectionResult initial, GameDetectionResult final,
            int attempts, Stopwatch watch, string message, string error, IList<NavigationTransition> transitions) => new NavigationResult
        {
            Success = success, InitialState = initial.State, FinalState = final?.State ?? GameState.Unknown,
            Attempts = attempts, Duration = watch.Elapsed, Message = message, ErrorMessage = error,
            FinalEvidence = final?.Evidence ?? new GameDetectionEvidence[0],
            Transitions = new List<NavigationTransition>(transitions).AsReadOnly()
        };

        private static NavigationResult Result(bool success, GameDetectionResult initial,
            GameDetectionResult final, int attempts, Stopwatch watch, string message,
            string error, IList<NavigationTransition> transitions, string failureReason,
            int? tapX = null, int? tapY = null, int tapCount = 0,
            bool verificationSucceeded = false)
        {
            NavigationResult result = Result(success, initial, final, attempts, watch,
                message, error, transitions);
            result.FailureReason = failureReason;
            result.TapX = tapX;
            result.TapY = tapY;
            result.TapCount = tapCount;
            result.VerificationSucceeded = verificationSucceeded;
            return result;
        }

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
