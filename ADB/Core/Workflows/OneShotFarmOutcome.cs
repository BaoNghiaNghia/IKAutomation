namespace ADB_Tool_Automation_Post_FB.Core.Workflows
{
    public enum OneShotFarmOutcome
    {
        MarchStarted, ResourceNotFound, ResourceLevelsExhausted, NoEligibleTeam, WorldMapUnavailable,
        SearchPanelUnavailable, SearchConfigurationFailed, SearchExecutionFailed,
        ResourcePopupNotReady, TeamSelectionFailed, TeamSelectionNotReady,
        TeamDispatchFailed, AllCandidateStoragesFull, ResourcePlanExhausted,
        PreconditionFailed, Failed, Cancelled
    }
}
