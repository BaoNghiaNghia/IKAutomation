namespace ADB_Tool_Automation_Post_FB.Core.TeamSelection
{
    public enum SelectFarmTeamOutcome
    {
        AlreadySelected,
        TeamSelected,
        ExpectedTeamNotAllowed,
        ExpectedTeamUnavailable,
        TargetBadgeNotFound,
        TargetTeamDisabled,
        SelectionEvidenceUncertain,
        NoEligibleTeam,
        TeamSelectionNotReady,
        SelectionTimeout,
        ExpectedTeamNotVisible,
        WrongTeamSelected,
        TeamSelectionMismatch,
        Failed,
        Cancelled
    }
}
