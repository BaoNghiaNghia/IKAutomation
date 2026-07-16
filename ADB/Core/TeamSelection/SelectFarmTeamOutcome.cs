namespace ADB_Tool_Automation_Post_FB.Core.TeamSelection
{
    public enum SelectFarmTeamOutcome
    {
        AlreadySelected,
        TeamSelected,
        NoEligibleTeam,
        TeamSelectionNotReady,
        SelectionTimeout,
        Failed,
        Cancelled
    }
}
