using ADB_Tool_Automation_Post_FB.Core.GameDetection;
using ADB_Tool_Automation_Post_FB.Core.Vision;
using System.Collections.Generic;

namespace ADB_Tool_Automation_Post_FB.Core.TeamSelection
{
    public enum TeamRosterEvidenceSource
    {
        Unknown,
        FreshBadges,
        FreshRowEvidence,
        CachedKnownCount,
        ExplicitSingleTeam
    }

    public enum TeamRosterClassification
    {
        FreshConfirmed,
        CachedConfirmed,
        ExplicitSingleTeam,
        Uncertain,
        Failed
    }

    public sealed class WorldMapTeamAvailabilityResult
    {
        public bool Success { get; set; }
        public bool AnyReadyTeam { get; set; }
        public IReadOnlyList<TeamNumber> AvailableTeams { get; set; }
        public IReadOnlyList<TeamNumber> ExistingTeams { get; set; }
        public IReadOnlyList<TeamNumber> ReadyTeams { get; set; }
        public IReadOnlyList<TeamNumber> BusyTeams { get; set; }
        public IReadOnlyList<TeamNumber> LockedTeams { get; set; }
        public IReadOnlyList<TeamRowObservation> RowObservations { get; set; }
        public int ConfirmedRosterCount { get; set; }
        public GameState FinalState { get; set; }
        public ImageMatchResult ReadyMatch { get; set; }
        public IReadOnlyList<ImageMatchResult> ReadyMatches { get; set; }
        public TeamRosterEvidenceSource RosterEvidenceSource { get; set; }
        public TeamRosterClassification RosterClassification { get; set; }
        public bool IsRosterUncertain { get; set; }
        public string RosterSource { get; set; }
        public string Message { get; set; }
        public string ErrorMessage { get; set; }
    }
}
