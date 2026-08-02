using ADB_Tool_Automation_Post_FB.Core.GameDetection;
using System;
using System.Collections.Generic;

namespace ADB_Tool_Automation_Post_FB.Core.TeamSelection
{
    public sealed class SelectFarmTeamResult
    {
        public SelectFarmTeamOutcome Outcome { get; set; }
        public bool Success { get; set; }
        public TeamNumber? SelectedTeam { get; set; }
        public TeamNumber? ActualSelectedTeam { get; set; }
        public TeamNumber? ExpectedTeam { get; set; }
        public IReadOnlyList<TeamNumber> VisibleTeams { get; set; }
        public IReadOnlyList<TeamNumber> AttemptedTeams { get; set; }
        public GameState InitialState { get; set; }
        public GameState FinalState { get; set; }
        public int TeamTapCount { get; set; }
        public int ScrollAttempts { get; set; }
        public int SelectionVerificationFrames { get; set; }
        public string FailureReason { get; set; }
        public bool CleanupAttempted { get; set; }
        public bool CleanupSucceeded { get; set; }
        public GameState StateAfterCleanup { get; set; }
        public bool TeamSelectionScreenVerified { get; set; }
        public bool SelectedStateVerified { get; set; }
        public bool ActionReadyFallbackAccepted { get; set; }
        public TimeSpan Duration { get; set; }
        public string Message { get; set; }
        public string ErrorMessage { get; set; }
        public string DiagnosticScreenshotPath { get; set; }
        public IReadOnlyList<TeamSelectionAttempt> Attempts { get; set; }
    }
}
