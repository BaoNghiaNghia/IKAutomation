namespace ADB_Tool_Automation_Post_FB.Core.Notifications
{
    public sealed class AutomationFailureNotification
    {
        public string DeviceName { get; set; }
        public string Outcome { get; set; }
        public string Step { get; set; }
        public string Resource { get; set; }
        public string Level { get; set; }
        public string Team { get; set; }
        public string Message { get; set; }
        public string Error { get; set; }
        public string DiagnosticPath { get; set; }
    }
}
