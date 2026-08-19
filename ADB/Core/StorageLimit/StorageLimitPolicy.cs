namespace IK_Auto_ADB.Core.StorageLimit
{
    public enum StorageLimitPolicy
    {
        StopAndReport,
        ConfirmAndProceed,
        CancelDispatch,
        SwitchResource,
        ConfirmAndSwitchResource,
        CancelAndSwitchResource
    }
}
