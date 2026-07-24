namespace ADB_Tool_Automation_Post_FB.Core.MarchDispatch
{
    public enum DispatchMarchOutcome
    {
        MarchStarted,
        AlreadyMarching,
        TeamSelectionNotReady,
        ExpectedTeamNotSelected,
        TeamAlreadyBusy,
        ActionButtonUnavailable,
        DispatchRejected,
        TransitionTimeout,
        VerificationIndeterminate,
        StorageLimitResourceSwitchRequired,
        ResourceExpiryResourceSwitchRequired,
        Failed,
        Cancelled
    }
}
