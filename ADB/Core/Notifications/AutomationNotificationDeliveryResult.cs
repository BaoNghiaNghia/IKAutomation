namespace ADB_Tool_Automation_Post_FB.Core.Notifications
{
    public sealed class AutomationNotificationDeliveryResult
    {
        public bool Attempted { get; set; }
        public bool Success { get; set; }
        public string Message { get; set; }
    }
}
