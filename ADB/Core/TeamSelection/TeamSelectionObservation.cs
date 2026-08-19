using ADB_Tool_Automation_Post_FB.Core.GameDetection;
using System;

namespace ADB_Tool_Automation_Post_FB.Core.TeamSelection
{
    public sealed class TeamSelectionObservation
    {
        public DateTimeOffset Timestamp { get; set; }
        public GameState State { get; set; }
        public bool PanelAnchorFound { get; set; }
        public bool AdjustFormationButtonFound { get; set; }
        public bool TeamActionButtonFound { get; set; }
        public bool TeamSelectionConfirmed { get; set; }
        public bool TeamSelectionReady { get; set; }
        public string Message { get; set; }
    }
}
