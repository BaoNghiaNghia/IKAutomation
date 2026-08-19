namespace ADB_Tool_Automation_Post_FB.Core.ResourceSearch
{
    public sealed class ResourceSearchConfigurationRequest
    {
        public ResourceType ResourceType { get; set; }
        public int TargetLevel { get; set; }
        public bool UnoccupiedOnly { get; set; }
        // Set by callers that already verified the Search-button ROI. This avoids
        // reopening or re-running the global state detector during configuration.
        public bool PanelReady { get; set; }
    }
}
