using IK_Auto_ADB.Core.Vision;

namespace IK_Auto_ADB.Core.TeamSelection
{
    public enum TeamRowState
    {
        Missing,
        Ready,
        Busy,
        Locked,
        Unknown
    }

    public sealed class TeamRowObservation
    {
        public TeamNumber Team { get; set; }
        public bool BadgeFound { get; set; }
        public ImageRegion BadgeBounds { get; set; }
        public ImageRegion ReadyBounds { get; set; }
        public ImageRegion RowBounds { get; set; }
        public bool Exists { get; set; }
        public bool IsVisible { get; set; }
        public bool IsReady { get; set; }
        public bool IsBusy { get; set; }
        public bool IsLocked { get; set; }
        public bool IsSelected { get; set; }
        public TeamRowState State { get; set; }
        public string EvidenceSource { get; set; }
        public string EvidenceStrength { get; set; }
    }
}
