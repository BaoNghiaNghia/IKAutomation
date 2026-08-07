namespace ADB_Tool_Automation_Post_FB.Core.ResourceSearch
{
    public sealed class ResourceSearchExecutionRequest
    {
        public string RunId { get; set; }
        public int? EffectiveLevel { get; set; }
        public bool LevelCapped { get; set; }
        public int AreaEpoch { get; set; }
        public ResourceSearchExecutionRequest()
        {
            ConfigureBeforeSearch = true;
        }

        public ResourceSearchConfigurationRequest Configuration { get; set; }
        public bool ConfigureBeforeSearch { get; set; }
    }
}
