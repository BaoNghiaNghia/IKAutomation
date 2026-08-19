using IK_Auto_ADB.Core.GameDetection;
using System;
using System.Collections.Generic;

namespace IK_Auto_ADB.Core.ResourceSearch
{
    public sealed class ResourceLevelFallbackResult
    {
        public ResourceLevelFallbackOutcome Outcome { get; set; }
        public bool Success { get; set; }
        public ResourceType ResourceType { get; set; }
        public int? LocatedLevel { get; set; }
        public int? LastAttemptedLevel { get; set; }
        public string MatchedNotFoundVariant { get; set; }
        public ResourceSearchFailureReason FailureReason { get; set; }
        public IReadOnlyList<int> RequestedLevels { get; set; }
        public IReadOnlyList<ResourceLevelAttemptResult> Attempts { get; set; }
        public GameState InitialState { get; set; }
        public GameState FinalState { get; set; }
        public TimeSpan Duration { get; set; }
        public string Message { get; set; }
        public string ErrorMessage { get; set; }
        public string DiagnosticScreenshotPath { get; set; }
    }
}
