namespace IK_Auto_ADB.Core.ResourceSearch
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
