using IK_Auto_ADB.Core.ResourceSearch;
using System.Collections.Generic;
using IK_Auto_ADB.Core.GameDetection;
using IK_Auto_ADB.Core.TeamSelection;
using System;

namespace IK_Auto_ADB.Core.Workflows
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
        public string MatchedNotFoundVariant { get; set; }
        public ResourceSearchFailureReason FailureReason { get; set; }
        public ResourceType? DispatchedResource { get; set; }
        public TeamNumber? DispatchedTeam { get; set; }
        public IReadOnlyList<ResourceFarmAttemptResult> Attempts { get; set; }
        public GameState InitialState { get; set; }
        public GameState FinalState { get; set; }
        public TimeSpan Duration { get; set; }
        public string Message { get; set; }
        public string ErrorMessage { get; set; }
        public string TerritoryColorSummary { get; set; }
        public int RecoveryTransitions { get; set; }
        public OneShotFarmStep LastCompletedStep { get; set; }
    }
}
