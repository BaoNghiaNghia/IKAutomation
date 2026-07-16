using ADB_Tool_Automation_Post_FB.Core.GameDetection;
using System;

namespace ADB_Tool_Automation_Post_FB.Core.MarchDispatch
{
    public sealed class MarchDispatchObservation
    {
        public DateTimeOffset Timestamp { get; set; }
        public GameState State { get; set; }
        public bool TeamSelectionFound { get; set; }
        public bool WorldMapFound { get; set; }
        public bool ExpectedTeamBadgeFound { get; set; }
        public bool SelectedBorderFound { get; set; }
        public bool BusyStatusFound { get; set; }
        public bool MarchTimerFound { get; set; }
        public double? TeamRegionDifference { get; set; }
        public bool TeamRegionChanged { get; set; }
        public bool SuccessRuleMatched { get; set; }
        public string Message { get; set; }
    }
}
