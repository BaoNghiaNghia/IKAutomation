namespace IK_Auto_ADB.Core.TeamSelection
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
