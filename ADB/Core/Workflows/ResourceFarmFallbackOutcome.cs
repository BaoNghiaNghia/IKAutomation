namespace IK_Auto_ADB.Core.Workflows
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
