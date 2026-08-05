using ADB_Tool_Automation_Post_FB.Core.Navigation;
using System.Drawing;

namespace ADB_Tool_Automation_Post_FB.Core.ResourceSearch
{
    public sealed class ResourceAreaLv2RecoveryResult
    {
        public bool Success { get; set; }
        public bool Exhausted { get; set; }
        public int Attempt { get; set; }
        public int MaxAttempts { get; set; }
        public Point BasePoint { get; set; }
        public Point ScaledPoint { get; set; }
        public int RemainingPointCount { get; set; }
        public bool WorldMapVerifiedBeforeTap { get; set; }
        public bool PointTapVerified { get; set; }
        public bool SearchPanelReopened { get; set; }
        public string FailureReason { get; set; }
        public NavigationResult EnsureWorldMapResult { get; set; }
        public NavigationResult PointTapResult { get; set; }
        public NavigationResult SearchPanelResult { get; set; }
    }
}
