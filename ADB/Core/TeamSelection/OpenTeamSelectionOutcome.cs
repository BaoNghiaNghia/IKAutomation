namespace ADB_Tool_Automation_Post_FB.Core.TeamSelection
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
