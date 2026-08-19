namespace ADB_Tool_Automation_Post_FB.Core.Workflows
{
    public enum OneShotFarmProgressStage
    {
        CheckingTeamAvailability,
        WaitingForReadyTeam,
        ReadyTeamFound,
        PreparingFarm,
        RunningFarmStep,
        Stopping,
        Completed,
        Failed,
        Cancelled
    }
}
