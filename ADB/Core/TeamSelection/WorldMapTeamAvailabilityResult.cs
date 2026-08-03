using ADB_Tool_Automation_Post_FB.Core.GameDetection;
using ADB_Tool_Automation_Post_FB.Core.Vision;
using System;
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
        // Identifies the concrete WorldMap observation used by a single gather
        // operation.  Cached roster knowledge must never be mistaken for this.
        public Guid RosterScanId { get; set; }
        public DateTimeOffset? RosterCapturedAt { get; set; }
        public bool IsFresh { get; set; }
        public bool Success { get; set; }
        public bool AnyReadyTeam { get; set; }
        public IReadOnlyList<TeamNumber> AvailableTeams { get; set; }
        public IReadOnlyList<TeamNumber> ExistingTeams { get; set; }
        public IReadOnlyList<TeamNumber> ReadyTeams { get; set; }
        public IReadOnlyList<TeamNumber> BusyTeams { get; set; }
        public IReadOnlyList<TeamNumber> LockedTeams { get; set; }
        public IReadOnlyList<TeamRowObservation> RowObservations { get; set; }
        public IReadOnlyDictionary<TeamNumber, TeamRowObservation> TeamRows { get; set; }
        public IReadOnlyDictionary<TeamNumber, TeamRowObservation> Rows
        {
            get => TeamRows;
            set => TeamRows = value;
        }
        public int ConfirmedRosterCount { get; set; }
        public GameState FinalState { get; set; }
        public ImageMatchResult ReadyMatch { get; set; }
        public IReadOnlyList<ImageMatchResult> ReadyMatches { get; set; }
        public TeamRosterEvidenceSource RosterEvidenceSource { get; set; }
        public TeamRosterClassification RosterClassification { get; set; }
        public bool IsRosterUncertain { get; set; }
        public string RosterSource { get; set; }
        public string RosterStatus { get; set; }
        public string RosterConfidence { get; set; }
        public string Message { get; set; }
        public string ErrorMessage { get; set; }
    }
}
