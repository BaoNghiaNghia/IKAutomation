using IK_Auto_ADB.Core.Vision;

namespace IK_Auto_ADB.Core.TeamSelection
{
    public sealed class TeamSelectionAttempt
    {
        public TeamNumber TeamNumber { get; set; }
        public bool BadgeFound { get; set; }
        public bool DisabledDetected { get; set; }
        public bool AlreadySelected { get; set; }
        public bool TapSent { get; set; }
        public bool SelectedVerified { get; set; }
        public ImageMatchResult BadgeMatch { get; set; }
        public ImageMatchResult SelectedBorderMatch { get; set; }
        public ImageRegion RowBounds { get; set; }
        public int ScrollAttempt { get; set; }
        public int TapAttempt { get; set; }
        public TeamNumber? SelectedBefore { get; set; }
        public TeamNumber? SelectedAfter { get; set; }
        public string Message { get; set; }
    }
}
