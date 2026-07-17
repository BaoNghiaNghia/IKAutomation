using ADB_Tool_Automation_Post_FB.Core.GameDetection;
using ADB_Tool_Automation_Post_FB.Core.MarchDispatch;
using ADB_Tool_Automation_Post_FB.Core.Navigation;
using ADB_Tool_Automation_Post_FB.Core.ResourcePopup;
using ADB_Tool_Automation_Post_FB.Core.ResourceSearch;
using ADB_Tool_Automation_Post_FB.Core.TeamSelection;
using System;
using System.Collections.Generic;

namespace ADB_Tool_Automation_Post_FB.Core.Workflows
{
    public sealed class OneShotFarmResult
    {
        public OneShotFarmOutcome Outcome { get; set; }
        public bool Success { get; set; }
        public string DeviceName { get; set; }
        public ResourceType RequestedResource { get; set; }
        public int RequestedLevel { get; set; }
        public bool RequestedUnoccupiedOnly { get; set; }
        public TeamNumber? SelectedTeam { get; set; }
        public TeamNumber? DispatchedTeam { get; set; }
        public GameState InitialState { get; set; }
        public GameState FinalState { get; set; }
        public OneShotFarmStep LastCompletedStep { get; set; }
        public TimeSpan Duration { get; set; }
        public string Message { get; set; }
        public string ErrorMessage { get; set; }
        public string DiagnosticScreenshotPath { get; set; }
        public IReadOnlyList<OneShotFarmStepResult> Steps { get; set; }
        public NavigationResult NavigationResult { get; set; }
        public ResourceSearchConfigurationResult ConfigurationResult { get; set; }
        public ResourceSearchExecutionResult SearchResult { get; set; }
        public ResourcePopupVerificationResult PopupResult { get; set; }
        public OpenTeamSelectionResult OpenTeamResult { get; set; }
        public SelectFarmTeamResult SelectTeamResult { get; set; }
        public DispatchMarchResult DispatchResult { get; set; }
    }
}
