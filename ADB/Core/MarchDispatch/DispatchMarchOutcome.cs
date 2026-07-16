namespace ADB_Tool_Automation_Post_FB.Core.MarchDispatch
{
    public enum DispatchMarchOutcome
    {
        MarchStarted,
        AlreadyMarching,
        TeamSelectionNotReady,
        ExpectedTeamNotSelected,
        ActionButtonUnavailable,
        DispatchRejected,
        TransitionTimeout,
        VerificationIndeterminate,
        Failed,
        Cancelled
    }
}
