namespace IK_Auto_ADB.Core.MarchDispatch
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
