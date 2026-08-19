namespace IK_Auto_ADB.Core.StorageLimit
{
    public enum StorageLimitDialogOutcome
    {
        ConfirmedForResourceSwitch,
        ActionButtonUnavailable,
        DialogNotVerified,
        TransitionTimeout,
        RecoveryFailed,
        Cancelled,
        Failed,
        CancelledForResourceSwitch
    }
}
