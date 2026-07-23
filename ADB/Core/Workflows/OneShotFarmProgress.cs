using ADB_Tool_Automation_Post_FB.Core.ResourceSearch;
using ADB_Tool_Automation_Post_FB.Core.TeamSelection;
using System;
using System.Collections.Generic;

namespace ADB_Tool_Automation_Post_FB.Core.Workflows
{
    public sealed class OneShotFarmProgress
    {
        public OneShotFarmProgress()
        {
            AllowedTeams = new TeamNumber[0];
            DetectedTeams = new TeamNumber[0];
            ReadyTeams = new TeamNumber[0];
            EligibleReadyTeams = new TeamNumber[0];
        }

        public OneShotFarmProgressStage Stage { get; set; }
        public DateTimeOffset ReportedAt { get; set; }
        public int TeamAvailabilityChecks { get; set; }
        public IReadOnlyList<TeamNumber> AllowedTeams { get; set; }
        public IReadOnlyList<TeamNumber> DetectedTeams { get; set; }
        public IReadOnlyList<TeamNumber> ReadyTeams { get; set; }
        public IReadOnlyList<TeamNumber> EligibleReadyTeams { get; set; }
        public DateTimeOffset? NextCheckAt { get; set; }
        public DateTimeOffset? WaitDeadline { get; set; }
        public OneShotFarmStep? CurrentStep { get; set; }
        public ResourceType? CurrentResource { get; set; }
        public int? CurrentLevel { get; set; }
        public TeamNumber? CurrentTeam { get; set; }
        public string Message { get; set; }
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
