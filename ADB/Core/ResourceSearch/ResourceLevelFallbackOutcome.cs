namespace ADB_Tool_Automation_Post_FB.Core.ResourceSearch
{
    public enum ResourceLevelFallbackOutcome
    {
        ResourceLocated,
        ResourceAreaLv2Redirect,
        ResourceAreaLv2PointAttemptsExhausted,
        ResourceLevelsExhausted,
        ConfigurationFailed,
        SearchFailed,
        PanelUnavailable,
        Failed,
        Cancelled
    }
}
