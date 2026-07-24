namespace ADB_Tool_Automation_Post_FB.Core.ResourceSearch
{
    public sealed class ResourceSearchExecutionRequest
    {
        public ResourceSearchExecutionRequest()
        {
            ConfigureBeforeSearch = true;
        }

        public ResourceSearchConfigurationRequest Configuration { get; set; }
        public bool ConfigureBeforeSearch { get; set; }
    }
}
