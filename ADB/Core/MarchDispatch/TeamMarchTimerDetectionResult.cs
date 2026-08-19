using IK_Auto_ADB.Core.Vision;

namespace IK_Auto_ADB.Core.MarchDispatch
{
    public sealed class TeamMarchTimerDetectionResult
    {
        public bool ContentDetected { get; set; }
        public double ForegroundRatio { get; set; }
        public ImageRegion TimerRegion { get; set; }
    }
}
