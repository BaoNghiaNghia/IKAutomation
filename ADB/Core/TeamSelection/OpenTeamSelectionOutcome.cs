namespace IK_Auto_ADB.Core.TeamSelection
{
    public enum OpenTeamSelectionOutcome
    {
        AlreadyOpen,
        TeamSelectionOpened,
        TeamSelectionOpenedButNotReady,
        ResourcePopupNotReady,
        GatherButtonNotAvailable,
        TransitionTimeout,
        Failed,
        Cancelled
    }
}
