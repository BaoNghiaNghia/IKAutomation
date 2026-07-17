using ADB_Tool_Automation_Post_FB.Core.ResourceSearch;
using System.Collections.Generic;
using ADB_Tool_Automation_Post_FB.Core.GameDetection;
using ADB_Tool_Automation_Post_FB.Core.TeamSelection;
using System;

namespace ADB_Tool_Automation_Post_FB.Core.Workflows
{
    public sealed class ResourceFarmFallbackResult
    {
        public ResourceFarmFallbackOutcome Outcome { get; set; }
        public bool Success { get; set; }
        public IReadOnlyList<ResourceType> RequestedResources { get; set; }
        public IReadOnlyList<ResourceType> AttemptedResources { get; set; }
        public IReadOnlyList<ResourceType> StorageFullResources { get; set; }
        public IReadOnlyList<ResourceType> LevelsExhaustedResources { get; set; }
        public ResourceType? LocatedResource { get; set; }
        public int? LocatedLevel { get; set; }
        public ResourceType? DispatchedResource { get; set; }
        public TeamNumber? DispatchedTeam { get; set; }
        public IReadOnlyList<ResourceFarmAttemptResult> Attempts { get; set; }
        public GameState InitialState { get; set; }
        public GameState FinalState { get; set; }
        public TimeSpan Duration { get; set; }
        public string Message { get; set; }
        public string ErrorMessage { get; set; }
        public int RecoveryTransitions { get; set; }
        public OneShotFarmStep LastCompletedStep { get; set; }
    }
}
