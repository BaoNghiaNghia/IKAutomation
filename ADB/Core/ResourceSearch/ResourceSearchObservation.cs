using ADB_Tool_Automation_Post_FB.Core.GameDetection;
using ADB_Tool_Automation_Post_FB.Core.ResourcePopup;
using System;

namespace ADB_Tool_Automation_Post_FB.Core.ResourceSearch
{
    public sealed class ResourceSearchObservation
    {
        public DateTimeOffset Timestamp { get; set; }
        public GameState State { get; set; }
        public bool ToastAnchorFound { get; set; }
        public bool ToastActionAnchorFound { get; set; }
        public bool SearchPanelConfirmed { get; set; }
        public double? FrameDifference { get; set; }
        public bool IsStable { get; set; }
        public ResourcePopupOutcome? PopupOutcome { get; set; }
        public string Message { get; set; }
    }
}
