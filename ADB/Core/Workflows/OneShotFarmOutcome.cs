namespace ADB_Tool_Automation_Post_FB.Core.Workflows
{
    public enum OneShotFarmOutcome
    {
        MarchStarted, ResourceNotFound, ResourceLevelsExhausted, NoEligibleTeam, WorldMapUnavailable,
        SearchPanelUnavailable, SearchConfigurationFailed, SearchExecutionFailed,
        ResourceAreaLv2RedirectUnhandled,
        ResourceAreaLv2PointAttemptsExhausted,
        ResourcePopupNotReady, TeamSelectionFailed, TeamSelectionNotReady,
        TeamDispatchFailed, AllCandidateStoragesFull, ResourcePlanExhausted,
        RecoveryFailed, TeamAvailabilityCheckFailed, TeamAvailabilityWaitTimeout,
        WaitingForReadyTeam,
        PreconditionFailed, Failed, Cancelled
    }
}
