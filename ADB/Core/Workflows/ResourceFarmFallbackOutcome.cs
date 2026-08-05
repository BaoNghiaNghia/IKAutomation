namespace ADB_Tool_Automation_Post_FB.Core.Workflows
{
    public enum ResourceFarmFallbackOutcome
    {
        MarchStarted, ResourceAreaLv2Redirect, ResourceAreaLv2PointAttemptsExhausted,
        ResourcePlanExhausted, AllCandidateStoragesFull,
        NoEligibleTeam, SearchFailed, PopupFailed, TeamSelectionFailed,
        DispatchFailed, RecoveryFailed, SearchAreaExhausted, RepositionTimeout,
        SearchAreaRecoveryFailed, Failed, Cancelled
    }
}
