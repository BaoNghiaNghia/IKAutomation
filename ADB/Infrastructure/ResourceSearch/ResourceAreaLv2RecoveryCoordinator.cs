using ADB_Tool_Automation_Post_FB.Core.Abstractions;
using ADB_Tool_Automation_Post_FB.Core.Diagnostics;
using ADB_Tool_Automation_Post_FB.Core.GameDetection;
using ADB_Tool_Automation_Post_FB.Core.Navigation;
using ADB_Tool_Automation_Post_FB.Core.ResourceSearch;
using ADB_Tool_Automation_Post_FB.Core.Vision;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace ADB_Tool_Automation_Post_FB.Infrastructure.ResourceSearch
{
    public sealed class ResourceAreaLv2RecoveryCoordinator : IResourceAreaLv2RecoveryCoordinator
    {
        private readonly IWorldMapNavigationService navigation;
        private readonly ResourceAreaLv2PointSelector selector;
        private readonly IFrameCapturingLdPlayerClient frameClient;
        private readonly IDiagnosticLogger logger;

        public ResourceAreaLv2RecoveryCoordinator(
            IWorldMapNavigationService navigation,
            ResourceAreaLv2PointSelector selector,
            IFrameCapturingLdPlayerClient frameClient,
            IDiagnosticLogger logger)
        {
            this.navigation = navigation ?? throw new ArgumentNullException(nameof(navigation));
            this.selector = selector ?? throw new ArgumentNullException(nameof(selector));
            this.frameClient = frameClient ?? throw new ArgumentNullException(nameof(frameClient));
            this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public void Clear(ResourceAreaLv2RecoveryRequest request)
        {
            if (request == null) return;
            selector.Clear(request.RunId, request.DeviceName, request.Resource,
                request.Level, request.AreaEpoch);
        }

        public async Task<ResourceAreaLv2RecoveryResult> RecoverAsync(
            ResourceAreaLv2RecoveryRequest request,
            CancellationToken cancellationToken)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            LogCancellationTrace(request, "RecoveryStart", cancellationToken,
                "FarmOperationToken", "ResourceAreaLv2RecoveryCoordinator");
            cancellationToken.ThrowIfCancellationRequested();
            var result = new ResourceAreaLv2RecoveryResult
            { MaxAttempts = ResourceAreaLv2PointSelector.MaxResourceAreaLv2PointAttempts };

            logger.Info($"[Resource Area Lv2 Point Flow Started] RunId='{request.RunId ?? string.Empty}', DeviceName='{request.DeviceName}', Resource='{request.Resource}', EffectiveLevel={request.Level}, AreaEpoch={request.AreaEpoch}, ExpectedTeam='{request.ExpectedTeam?.ToString() ?? string.Empty}', Strategy='PredefinedMapCoordinateEntry', CoordinateInputInvoked=true, TerritoryColorScanInvoked=false, SpecialAttemptNumber=1, RemainingUnusedPoints={ResourceAreaLv2PointSelector.Points1280x720.Count}, OperationTokenCancelled={cancellationToken.IsCancellationRequested}, NextAction='EnsureWorldMap'");
            NavigationResult ensured = await navigation.EnsureWorldMapAsync(
                request.DeviceName, cancellationToken);
            result.EnsureWorldMapResult = ensured;
            result.WorldMapVerifiedBeforeTap = ensured != null && ensured.Success
                && ensured.FinalState == GameState.WorldMap;
            if (!result.WorldMapVerifiedBeforeTap)
            {
                result.FailureReason = "WorldMapUnavailable";
                Log(request, result, null, null, false, false, "Failed");
                return result;
            }

            ResourceAreaLv2PointSelection point;
            using (CapturedFrame frame = await frameClient.CaptureFrameAsync(
                request.DeviceName, cancellationToken))
            {
                point = selector.Next(request.RunId, request.DeviceName, request.Resource,
                    request.Level, request.AreaEpoch, frame.Width, frame.Height);
            }
            result.Attempt = point.Attempt;
            result.MaxAttempts = point.MaxAttempts;
            result.BasePoint = point.BasePoint;
            result.ScaledPoint = point.ScaledPoint;
            result.RemainingPointCount = point.RemainingPointCount;
            result.Exhausted = point.Exhausted && point.Attempt == 0;
            if (result.Exhausted)
            {
                result.FailureReason = "PointPoolExhausted";
                Log(request, result, point, null, false, false, "Exhausted");
                return result;
            }

            logger.Info($"[Resource Area Lv2 Point Attempt] RunId='{request.RunId ?? string.Empty}', DeviceName='{request.DeviceName}', Resource='{request.Resource}', EffectiveLevel={request.Level}, AreaEpoch={request.AreaEpoch}, Attempt={point.Attempt}, MaxAttempts={point.MaxAttempts}, RemainingPointCount={point.RemainingPointCount}, MapCoordinate=({point.BasePoint.X},{point.BasePoint.Y}), CoordinateSequence='FocusX-ClearX-InputX-FocusY-ClearY-InputY-Pin', PanelClosed={result.WorldMapVerifiedBeforeTap}, WorldMapVerifiedBeforeTap={result.WorldMapVerifiedBeforeTap}, PointTapSent=false, OperationTokenCancelled={cancellationToken.IsCancellationRequested}, NextAction='EnterPredefinedCoordinates'");
            LogCancellationTrace(request, "BeforePointTap", cancellationToken,
                "FarmOperationToken", "ResourceAreaLv2RecoveryCoordinator");
            var mapPointNavigation = navigation as IResourceAreaMapPointNavigationService;
            if (mapPointNavigation == null)
            {
                result.FailureReason = "ResourceAreaMapPointNavigationUnavailable";
                Log(request, result, point, null, false, false, "Failed");
                return result;
            }
            NavigationResult tapped = await mapPointNavigation.OpenMapAndEnterCoordinatesAsync(
                request.DeviceName, point.BasePoint.X, point.BasePoint.Y,
                cancellationToken);
            result.PointTapResult = tapped;
            // A point can open a terrain/object popup while the WorldMap anchor
            // remains visible. EnsureWorldMap is the bounded popup-dismissal and
            // clean-map verification step before reopening the search panel.
            NavigationResult cleanedAfterTap = tapped != null && tapped.Success
                ? await navigation.EnsureWorldMapAsync(request.DeviceName, cancellationToken)
                : null;
            if (cleanedAfterTap != null)
                result.PointTapResult = cleanedAfterTap;
            result.PointTapVerified = cleanedAfterTap != null && cleanedAfterTap.Success
                && cleanedAfterTap.FinalState == GameState.WorldMap;
            if (!result.PointTapVerified)
            {
                result.FailureReason = tapped?.FailureReason ?? "WorldMapVerificationFailed";
                Log(request, result, point, tapped, true, false, "Failed");
                return result;
            }

            NavigationResult panel = await navigation.OpenResourceSearchPanelAsync(
                request.DeviceName, cancellationToken);
            result.SearchPanelResult = panel;
            // OpenResourceSearchPanelAsync now treats a freshly matched Search
            // button in its configured ROI as authoritative. Requiring the broad
            // GameState result here could reject the same verified panel when the
            // detector reports Unknown and stop the 25-point retry loop early.
            result.SearchPanelReopened = panel != null && panel.Success;
            result.Success = result.SearchPanelReopened;
            if (!result.SearchPanelReopened)
                result.FailureReason = "ResourceSearchPanelReopenFailed";
            Log(request, result, point, tapped, true, result.SearchPanelReopened,
                result.Success ? "Success" : "Failed");
            return result;
        }

        private void LogCancellationTrace(ResourceAreaLv2RecoveryRequest request,
            string stage, CancellationToken token, string tokenName, string sourceScope)
        {
            logger.Info($"[Resource Area Lv2 Cancellation Trace] RunId='{request.RunId ?? string.Empty}', DeviceName='{request.DeviceName}', Stage='{stage}', TokenName='{tokenName}', IsCancellationRequested={token.IsCancellationRequested}, SourceScope='{sourceScope}', SourceDeadline='', RemainingMs='', ParentOperationCancelled={token.IsCancellationRequested}, SearchAttemptCancelled=false, ToastWatchCancelled=false, Reason='{(token.IsCancellationRequested ? "FarmOperationTokenCancelled" : string.Empty)}'");
        }

        private void Log(ResourceAreaLv2RecoveryRequest request,
            ResourceAreaLv2RecoveryResult result, ResourceAreaLv2PointSelection point,
            NavigationResult tapped, bool tapSent, bool reopened, string outcome)
        {
            logger.Info("[Resource Area Lv2 Point Attempt] "
                + $"RunId='{request.RunId}', DeviceName='{request.DeviceName}', "
                + $"Resource='{request.Resource}', Level={request.Level}, AreaEpoch={request.AreaEpoch}, "
                + $"Attempt={result.Attempt}, MaxAttempts={result.MaxAttempts}, "
                + $"BasePoint=({result.BasePoint.X},{result.BasePoint.Y}), "
                + $"ScaledPoint=({result.ScaledPoint.X},{result.ScaledPoint.Y}), "
                + $"ActualResolution='{(point == null ? string.Empty : point.ActualResolution.ToString())}', "
                + $"RemainingPointCount={result.RemainingPointCount}, "
                + $"PanelClosed={result.WorldMapVerifiedBeforeTap}, "
                + $"WorldMapVerifiedBeforeTap={result.WorldMapVerifiedBeforeTap}, "
                + $"PointTapSent={tapSent}, "
                + $"WorldMapVerifiedAfterTap={result.PointTapVerified}, "
                + $"NextAction='{(reopened ? "RestoreResourceConfiguration" : outcome)}', "
                + $"SearchPanelReopened={reopened}, Outcome='{outcome}', "
                + $"FailureReason='{result.FailureReason ?? string.Empty}'");
        }
    }
}
