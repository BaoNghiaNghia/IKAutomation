using ADB_Tool_Automation_Post_FB.Core.GameDetection;
using ADB_Tool_Automation_Post_FB.Core.Vision;
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
        public bool ExpectedTeamReadyBeforeDispatch { get; set; }
        public bool ExpectedTeamReadyAfterDispatch { get; set; }
        public bool ReadyAnchorDisappeared { get; set; }
        public bool TimerContentDetected { get; set; }
        public bool TimerProgressionDetected { get; set; }
        public double TimerForegroundRatio { get; set; }
        public double TimerDifferenceRatio { get; set; }
        public ImageRegion TimerRegion { get; set; }
        public MarchVerificationMode VerificationMode { get; set; }
        public bool DirectSuccessRuleMatched { get; set; }
        public bool StructuralSuccessRuleMatched { get; set; }
        public double? TeamRegionDifference { get; set; }
        public bool TeamRegionChanged { get; set; }
        public bool SuccessRuleMatched { get; set; }
        public string Message { get; set; }
    }
}
