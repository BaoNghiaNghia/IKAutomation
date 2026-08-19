namespace IK_Auto_ADB.Core.Workflows
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
