namespace ADB_Tool_Automation_Post_FB.Core.ResourceSearch
{
    public sealed class ResourceSearchConfigurationRequest
    {
        public ResourceType ResourceType { get; set; }
        public int TargetLevel { get; set; }
        public bool UnoccupiedOnly { get; set; }
    }
}
