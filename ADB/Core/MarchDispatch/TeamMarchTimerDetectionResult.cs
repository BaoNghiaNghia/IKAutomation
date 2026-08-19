using ADB_Tool_Automation_Post_FB.Core.Vision;

namespace ADB_Tool_Automation_Post_FB.Core.MarchDispatch
{
    public sealed class TeamMarchTimerDetectionResult
    {
        public bool ContentDetected { get; set; }
        public double ForegroundRatio { get; set; }
        public ImageRegion TimerRegion { get; set; }
    }
}
