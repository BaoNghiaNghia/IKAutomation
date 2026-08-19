using IK_Auto_ADB.Core.ResourceSearch;
using IK_Auto_ADB.Core.TeamSelection;
using System;
using System.Collections.Generic;

namespace IK_Auto_ADB.Core.Workflows
{
    public enum MapRepositionState
    {
        None,
        OpeningContinentMap,
        TestingCoordinate,
        VerifyingTerritoryColor,
        ReturningToWorldMap,
        Completed,
        Failed
    }

    public sealed class OneShotFarmProgress
    {
        public OneShotFarmProgress()
        {
            AllowedTeams = new TeamNumber[0];
            DetectedTeams = new TeamNumber[0];
            ReadyTeams = new TeamNumber[0];
            BusyTeams = new TeamNumber[0];
            LockedTeams = new TeamNumber[0];
            EligibleReadyTeams = new TeamNumber[0];
        }

        public OneShotFarmProgressStage Stage { get; set; }
        public DateTimeOffset ReportedAt { get; set; }
        public int TeamAvailabilityChecks { get; set; }
        public IReadOnlyList<TeamNumber> AllowedTeams { get; set; }
        public IReadOnlyList<TeamNumber> DetectedTeams { get; set; }
        public IReadOnlyList<TeamNumber> ReadyTeams { get; set; }
        // These are the latest focused WorldMap roster observations.  They are
        // carried with waiting progress so the UI never has to invent a
        // "Chưa kiểm tra" state after a completed scan.
        public IReadOnlyList<TeamNumber> BusyTeams { get; set; }
        public IReadOnlyList<TeamNumber> LockedTeams { get; set; }
        public IReadOnlyList<TeamNumber> EligibleReadyTeams { get; set; }
        public DateTimeOffset? NextCheckAt { get; set; }
        public DateTimeOffset? WaitDeadline { get; set; }
        public OneShotFarmStep? CurrentStep { get; set; }
        public ResourceType? CurrentResource { get; set; }
        public int? CurrentLevel { get; set; }
        public TeamNumber? CurrentTeam { get; set; }
        public TeamNumber? CurrentExpectedTeam { get; set; }
        public TeamNumber? CurrentSelectedTeam { get; set; }
        public TeamNumber? LastDispatchedTeam { get; set; }
        public int ConfirmedRosterCount { get; set; }
        public string RosterConfidence { get; set; }
        public string RosterSource { get; set; }
        public MapRepositionState MapRepositionState { get; set; }
        public string Message { get; set; }
        public string TerritoryColorSummary { get; set; }
        public string FarmRunId { get; set; }
        public string TeamOperationRunId { get; set; }
        // Template matching identifies the known redirect variant; the UI
        // renders a normalized message from these operation fields (no OCR).
        public string ResourceToastText { get; set; }
        public string ResourceToastVariant { get; set; }
        public DateTimeOffset? ResourceToastDetectedAt { get; set; }
        public string ResourceToastState { get; set; }
    }

    public static class OneShotFarmProgressUtilities
    {
        public static TimeSpan Remaining(DateTimeOffset now, DateTimeOffset? target)
        {
            if (!target.HasValue || target.Value <= now) return TimeSpan.Zero;
            return target.Value - now;
        }

        public static bool IsCurrentRun(long callbackGeneration, long currentGeneration,
            object callbackRun, object currentRun) =>
            callbackGeneration == currentGeneration
            && callbackRun != null
            && ReferenceEquals(callbackRun, currentRun);
    }
}
