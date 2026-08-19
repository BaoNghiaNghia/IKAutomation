using System;
using System.Linq;
using ADB_Tool_Automation_Post_FB.Core.GameDetection;
using ADB_Tool_Automation_Post_FB.Core.Vision;

namespace ADB_Tool_Automation_Post_FB.Core.ResourceSearch
{
    public enum ResourceSearchPanelReadiness
    {
        Ready,
        Animating,
        NotReady,
        CaptureUnavailable,
        Cancelled
    }

    /// <summary>
    /// Shared interpretation of the evidence required before resource
    /// configuration or search is allowed to start.
    /// </summary>
    public sealed class ResourceSearchPanelReadinessResult
    {
        public ResourceSearchPanelReadiness Status { get; set; }
        public bool PanelAnchorFound { get; set; }
        public bool SearchButtonFound { get; set; }
        public bool StableSecondaryFound { get; set; }
        public bool SearchButtonBoundsStable { get; set; }
        public string Reason { get; set; }

        public bool IsReady => Status == ResourceSearchPanelReadiness.Ready;
    }

    public static class ResourceSearchPanelReadinessVerifier
    {
        public static ResourceSearchPanelReadinessResult Evaluate(
            GameDetectionResult detection, bool searchButtonBoundsStable = false,
            bool trustedScreenshotConfirmation = false)
        {
            if (detection == null
                || (!detection.IsSuccessful
                    && (detection.Evidence == null || detection.Evidence.Count == 0)))
                return Unavailable("CaptureUnavailable");

            bool panelAnchor = Has(detection, TemplateId.ResourceSearchPanelAnchor);
            bool searchButton = Has(detection, TemplateId.SearchButtonEnabled);
            bool stableSecondary = Has(detection, TemplateId.LevelMinusButton)
                || Has(detection, TemplateId.ResourceTabSelected)
                || Has(detection, TemplateId.ResourceTabUnselected);

            return Evaluate(panelAnchor, searchButton, stableSecondary,
                searchButtonBoundsStable, trustedScreenshotConfirmation,
                detection.State == GameState.WorldMap);
        }

        public static ResourceSearchPanelReadinessResult Evaluate(
            bool panelAnchorFound, bool searchButtonFound, bool stableSecondaryFound,
            bool searchButtonBoundsStable = false,
            bool trustedScreenshotConfirmation = false,
            bool worldMapFound = false)
        {
            var result = new ResourceSearchPanelReadinessResult
            {
                PanelAnchorFound = panelAnchorFound,
                SearchButtonFound = searchButtonFound,
                StableSecondaryFound = stableSecondaryFound,
                SearchButtonBoundsStable = searchButtonBoundsStable
            };

            if (worldMapFound && !panelAnchorFound)
                return Set(result, ResourceSearchPanelReadiness.NotReady, "WorldMapEvidencePresent");
            if (!searchButtonFound)
                return Set(result, ResourceSearchPanelReadiness.NotReady, "SearchButtonMissing");
            // The Search button inside its configured ROI is the sole authoritative
            // handoff signal for this lightweight resource-search flow.
            if (searchButtonFound)
                return Set(result, ResourceSearchPanelReadiness.Ready, string.Empty);

            return Set(result, ResourceSearchPanelReadiness.Animating,
                "SearchButtonFoundAwaitingPanelConfirmation");
        }

        private static bool Has(GameDetectionResult detection, TemplateId id) =>
            detection.Evidence != null && detection.Evidence.Any(item =>
                item.TemplateId == id && item.Found);

        private static ResourceSearchPanelReadinessResult Unavailable(string reason) =>
            new ResourceSearchPanelReadinessResult
            {
                Status = ResourceSearchPanelReadiness.CaptureUnavailable,
                Reason = reason
            };

        private static ResourceSearchPanelReadinessResult Set(
            ResourceSearchPanelReadinessResult result,
            ResourceSearchPanelReadiness status, string reason)
        {
            result.Status = status;
            result.Reason = reason;
            return result;
        }
    }
}
