namespace ADB_Tool_Automation_Post_FB.Core.Workflows
{
    public enum ResourceFarmFallbackOutcome
    {
        MarchStarted, ResourcePlanExhausted, AllCandidateStoragesFull,
        NoEligibleTeam, SearchFailed, PopupFailed, TeamSelectionFailed,
        DispatchFailed, RecoveryFailed, Failed, Cancelled
    }
}
