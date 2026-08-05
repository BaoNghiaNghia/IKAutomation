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
            cancellationToken.ThrowIfCancellationRequested();
            var result = new ResourceAreaLv2RecoveryResult
            { MaxAttempts = ResourceAreaLv2PointSelector.MaxResourceAreaLv2PointAttempts };

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

            NavigationResult tapped = await navigation.TapWorldMapPointAsync(
                request.DeviceName, point.ScaledPoint.X, point.ScaledPoint.Y,
                cancellationToken);
            result.PointTapResult = tapped;
            result.PointTapVerified = tapped != null && tapped.Success
                && tapped.FinalState == GameState.WorldMap;
            if (!result.PointTapVerified)
            {
                result.FailureReason = tapped?.FailureReason ?? "WorldMapVerificationFailed";
                Log(request, result, point, tapped, true, false, "Failed");
                return result;
            }

            NavigationResult panel = await navigation.OpenResourceSearchPanelAsync(
                request.DeviceName, cancellationToken);
            result.SearchPanelResult = panel;
            result.SearchPanelReopened = panel != null && panel.Success
                && panel.FinalState == GameState.ResourceSearchPanel;
            result.Success = result.SearchPanelReopened;
            if (!result.SearchPanelReopened)
                result.FailureReason = "ResourceSearchPanelReopenFailed";
            Log(request, result, point, tapped, true, result.SearchPanelReopened,
                result.Success ? "Success" : "Failed");
            return result;
        }

        private void Log(ResourceAreaLv2RecoveryRequest request,
            ResourceAreaLv2RecoveryResult result, ResourceAreaLv2PointSelection point,
            NavigationResult tapped, bool tapSent, bool reopened, string outcome)
        {
            logger.Info("[Resource Area Lv2 Navigation Recovery] "
                + $"RunId='{request.RunId}', DeviceName='{request.DeviceName}', "
                + $"Resource='{request.Resource}', Level={request.Level}, AreaEpoch={request.AreaEpoch}, "
                + $"Attempt={result.Attempt}, MaxAttempts={result.MaxAttempts}, "
                + $"BasePoint=({result.BasePoint.X},{result.BasePoint.Y}), "
                + $"ScaledPoint=({result.ScaledPoint.X},{result.ScaledPoint.Y}), "
                + $"ActualResolution='{(point == null ? string.Empty : point.ActualResolution.ToString())}', "
                + $"RemainingPointCount={result.RemainingPointCount}, "
                + $"EnsureWorldMapSuccess={result.WorldMapVerifiedBeforeTap}, "
                + $"PointInsideBounds={tapSent}, TapCommandSent={tapSent}, "
                + $"WorldMapVerifiedAfterTap={result.PointTapVerified}, "
                + $"SearchPanelReopened={reopened}, Outcome='{outcome}', "
                + $"FailureReason='{result.FailureReason ?? string.Empty}'");
        }
    }
}
