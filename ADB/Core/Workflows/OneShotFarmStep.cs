namespace ADB_Tool_Automation_Post_FB.Core.Workflows
{
    public enum OneShotFarmStep
    {
        Preflight, EnsureWorldMap, OpenSearchPanel, ConfigureSearch, ExecuteSearch,
        VerifyResourcePopup, OpenTeamSelection, SelectTeam, DispatchTeam,
        FinalVerification, Completed
    }
}
