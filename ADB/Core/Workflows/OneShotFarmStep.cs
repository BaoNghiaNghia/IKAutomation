namespace IK_Auto_ADB.Core.Workflows
{
    public enum OneShotFarmStep
    {
        Preflight, EnsureWorldMap, OpenSearchPanel, ConfigureSearch, ExecuteSearch,
        SearchWithLevelFallback,
        ResourceFarmFallback,
        VerifyResourcePopup, OpenTeamSelection, SelectTeam, DispatchTeam,
        FinalVerification, Completed
    }
}
