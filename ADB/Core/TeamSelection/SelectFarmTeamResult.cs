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
        public IReadOnlyList<TeamNumber> AttemptedTeams { get; set; }
        public GameState InitialState { get; set; }
        public GameState FinalState { get; set; }
        public int TeamTapCount { get; set; }
        public bool TeamSelectionScreenVerified { get; set; }
        public bool SelectedStateVerified { get; set; }
        public TimeSpan Duration { get; set; }
        public string Message { get; set; }
        public string ErrorMessage { get; set; }
        public string DiagnosticScreenshotPath { get; set; }
        public IReadOnlyList<TeamSelectionAttempt> Attempts { get; set; }
    }
}
