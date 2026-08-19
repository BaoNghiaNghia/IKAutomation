using System;

namespace ADB_Tool_Automation_Post_FB.Core.ResourceSearch
{
    public sealed class ToastClearResult
    {
        public bool Cleared { get; set; }
        public bool PanelVerified { get; set; }
        public int ObservedFrames { get; set; }
        public int ConsecutiveClearFrames { get; set; }
        public TimeSpan Duration { get; set; }
        public string Message { get; set; }
        public string ErrorMessage { get; set; }
    }
}
