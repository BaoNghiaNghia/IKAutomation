using IK_Auto_ADB.Core.GameDetection;
using IK_Auto_ADB.Core.ResourcePopup;
using System;

namespace IK_Auto_ADB.Core.ResourceSearch
{
    public sealed class ResourceSearchObservation
    {
        public DateTimeOffset Timestamp { get; set; }
        public GameState State { get; set; }
        public bool ToastAnchorFound { get; set; }
        public bool ToastActionAnchorFound { get; set; }
        public bool ShortAnchorFound { get; set; }
        public bool OtherRegionAnchorFound { get; set; }
        public bool TargetLevelTooLowAnchorFound { get; set; }
        public bool SeasonMapAnchorFound { get; set; }
        public bool ResourceAreaLv2EvidenceFound { get; set; }
        public string MatchedNotFoundVariant { get; set; }
        public bool SearchPanelConfirmed { get; set; }
        public double? FrameDifference { get; set; }
        public bool IsStable { get; set; }
        public ResourcePopupOutcome? PopupOutcome { get; set; }
        public string Message { get; set; }
    }
}
