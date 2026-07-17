namespace ADB_Tool_Automation_Post_FB.Core.Workflows
{
    public enum OneShotFarmOutcome
    {
        MarchStarted, ResourceNotFound, NoEligibleTeam, WorldMapUnavailable,
        SearchPanelUnavailable, SearchConfigurationFailed, SearchExecutionFailed,
        ResourcePopupNotReady, TeamSelectionFailed, TeamSelectionNotReady,
        TeamDispatchFailed, PreconditionFailed, Failed, Cancelled
    }
}
