using ADB_Tool_Automation_Post_FB.Core.Vision;

namespace ADB_Tool_Automation_Post_FB.Core.TeamSelection
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
        public string Message { get; set; }
    }
}
