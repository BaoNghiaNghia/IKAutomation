using IK_Auto_ADB.Core.Vision;

namespace IK_Auto_ADB.Core.MarchDispatch
{
    public sealed class TeamMarchTimerProgressionResult
    {
        public bool PreviousContentDetected { get; set; }
        public bool CurrentContentDetected { get; set; }
        public bool ProgressionDetected { get; set; }
        public double PreviousForegroundRatio { get; set; }
        public double CurrentForegroundRatio { get; set; }
        public double DifferenceRatio { get; set; }
        public ImageRegion TimerRegion { get; set; }
    }
}
