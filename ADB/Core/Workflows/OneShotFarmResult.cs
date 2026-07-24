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
        public IReadOnlyList<int> AttemptedLevels { get; set; }
        public int? LocatedLevel { get; set; }
        public IReadOnlyList<ResourceType> AttemptedResources { get; set; }
        public IReadOnlyList<ResourceType> SelectedResources { get; set; }
        public IReadOnlyList<ResourceType> ShuffledResourcePriority { get; set; }
        public IReadOnlyList<MissingRuntimeTemplate> MissingRuntimeTemplates { get; set; }
        public IReadOnlyList<ResourceType> StorageFullResources { get; set; }
        public IReadOnlyList<ResourceType> LevelsExhaustedResources { get; set; }
        public ResourceType? LocatedResource { get; set; }
        public ResourceType? DispatchedResource { get; set; }
        public ResourceFarmFallbackResult ResourceFallbackResult { get; set; }
        public bool StorageLimitDialogDetected { get; set; }
        public bool StorageLimitConfirmed { get; set; }
        public bool StorageLimitCancelled { get; set; }
        public bool ResourceSwitchRequired { get; set; }
        public GameState StateAfterConfirmation { get; set; }
        public GameState StateAfterCancel { get; set; }
        public bool ReturnedToTeamSelection { get; set; }
        public bool BackSent { get; set; }
        public int BackCount { get; set; }
        public bool ReturnedToWorldMap { get; set; }
        public ResourceType? CurrentResource { get; set; }
        public ResourceType? NextResource { get; set; }
        public int RecoveryTransitions { get; set; }
        public bool RequestedUnoccupiedOnly { get; set; }
        public TeamNumber? SelectedTeam { get; set; }
        public TeamNumber? DispatchedTeam { get; set; }
        public int TeamAvailabilityChecks { get; set; }
        public bool ReadyTeamObserved { get; set; }
        public IReadOnlyList<TeamNumber> DetectedTeams { get; set; }
        public IReadOnlyList<TeamNumber> ReadyTeams { get; set; }
        public int CompletedDispatches { get; set; }
        public IReadOnlyList<ResourceType> DispatchedResources { get; set; }
        public IReadOnlyList<TeamNumber> BatchDispatchedTeams { get; set; }
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
        public ResourceLevelFallbackResult FallbackResult { get; set; }
        public ResourcePopupVerificationResult PopupResult { get; set; }
        public OpenTeamSelectionResult OpenTeamResult { get; set; }
        public SelectFarmTeamResult SelectTeamResult { get; set; }
        public DispatchMarchResult DispatchResult { get; set; }
    }
}
