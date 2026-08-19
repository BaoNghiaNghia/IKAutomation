namespace IK_Auto_ADB.Core.Notifications
{
    public sealed class AutomationNotificationDeliveryResult
    {
        public bool Attempted { get; set; }
        public bool Success { get; set; }
        public string Message { get; set; }
    }
}
